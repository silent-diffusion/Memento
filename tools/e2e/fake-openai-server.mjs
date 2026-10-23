// A stand-in for an OpenAI-compatible server the user runs (Ollama, LM Studio, vLLM, llama.cpp's server), on this PC
// only (127.0.0.1), for Memento's "Your own server" end-to-end check (m14-server.mjs). It serves:
//
//   GET  /v1/models            the models it pretends to have (qwen3:8b, llama3.1:8b, gemma3:4b)
//   GET  /api/version          {"version":"0.12.3"}, as Ollama answers it (a host may use it to name the server)
//   POST /v1/chat/completions  a short, minimal answer that fits the request's JSON schema (or, without one, the shape
//                              the system prompt asks for), streamed as server-sent events when `stream` is true
//
// Every request is recorded (method, path, whether an Authorization header came, the parsed body) and, with --record,
// written to a JSON file after each one. --reject-json-schema answers a request whose response_format is json_schema
// with 400, as a server without structured outputs does, so the host's fallback (json_object or the prompt alone) is
// exercised. Nothing here calls any other host. No dependencies (Node 22+).
//
//   node tools/e2e/fake-openai-server.mjs [--port 0] [--record <file.json>] [--reject-json-schema] [--delay-ms 25]
//                                          [--require-key <key>]

import { writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { pathToFileURL } from 'node:url';

export const FAKE_MODELS = ['qwen3:8b', 'llama3.1:8b', 'gemma3:4b'];
export const FAKE_VERSION = '0.12.3';

/** The numbered transcript lines of a request ("[12] Speaker: words"), with their numbers. */
function transcriptLines(text) {
  const lines = [];
  for (const match of text.matchAll(/^\[(\d+)\]\s*([^:\n]{1,60}):\s*(.+)$/gm)) {
    lines.push({ line: Number(match[1]), speaker: match[2].trim(), words: match[3].trim() });
  }
  return lines;
}

/** The first `n` words of a line, copied exactly (a citation quote). */
const firstWords = (words, n) => words.split(/\s+/).slice(0, n).join(' ');

/** Which answer the request wants: from the schema's top-level properties, else from the shape in the system prompt. */
function answerKind(schema, system) {
  const keys = schema && typeof schema === 'object' && schema.properties ? Object.keys(schema.properties) : [];
  const has = (key) => keys.includes(key) || (keys.length === 0 && new RegExp(`"${key}"\\s*:`).test(system));
  if (has('verdicts')) return 'verdicts';
  if (has('action_items') || has('decisions')) return 'commitments';
  if (has('quotes')) return 'quotes';
  if (has('points')) return 'points';
  if (has('supported_part') || has('verdict')) return 'verdict';
  if (has('supported')) return 'binary';
  if (has('items')) return 'items';
  return 'unknown';
}

/** A minimal instance of a JSON schema: required properties only, empty arrays, the first enum value. */
function minimalOf(schema) {
  if (!schema || typeof schema !== 'object') return null;
  if (Array.isArray(schema.enum)) return schema.enum[0];
  const type = Array.isArray(schema.type) ? schema.type.find((t) => t !== 'null') ?? 'null' : schema.type;
  switch (type) {
    case 'object': {
      const out = {};
      for (const key of schema.required ?? Object.keys(schema.properties ?? {})) out[key] = minimalOf(schema.properties?.[key]);
      return out;
    }
    case 'array':
      return Array.from({ length: schema.minItems ?? 0 }, () => minimalOf(schema.items));
    case 'string':
      return '';
    case 'integer':
    case 'number':
      return 1;
    case 'boolean':
      return false;
    default:
      return null;
  }
}

/**
 * A short answer for the request: one or two points citing real transcript lines word for word, one decision and one
 * action item, every claim "supported" in a verify request. Anything it does not recognise gets the schema's minimal
 * instance, which the pipeline reads as "nothing found".
 */
export function answerFor(body) {
  const messages = Array.isArray(body?.messages) ? body.messages : [];
  const text = (role) => messages.filter((m) => m.role === role).map((m) => (typeof m.content === 'string' ? m.content : (m.content ?? []).map((p) => p.text ?? '').join(''))).join('\n');
  const system = text('system');
  const user = text('user');
  const format = body?.response_format;
  const schema = format?.type === 'json_schema' ? format.json_schema?.schema : null;
  const lines = transcriptLines(user);
  const pick = (i) => lines[i % Math.max(1, lines.length)];
  switch (answerKind(schema, system)) {
    case 'points':
      return { points: lines.slice(0, 2).map((l) => ({ text: `${l.speaker} said: ${firstWords(l.words, 12)}`, line: l.line, quote: firstWords(l.words, 8) })) };
    case 'commitments': {
      const decision = pick(2);
      const action = pick(3);
      return {
        action_items: action ? [{ task: firstWords(action.words, 10), owner: action.speaker, due: null, citation: { line: action.line, quote: firstWords(action.words, 8) } }] : [],
        decisions: decision ? [{ decision: firstWords(decision.words, 12), citation: { line: decision.line, quote: firstWords(decision.words, 8) } }] : [],
      };
    }
    case 'quotes':
      return { quotes: lines.slice(0, 1).map((l) => ({ line: l.line, quote: firstWords(l.words, 10) })) };
    case 'items':
      return { items: [] };
    case 'verdicts': {
      const count = Math.max(1, [...user.matchAll(/<item number="(\d+)">/g)].length);
      return { verdicts: Array.from({ length: count }, (_, i) => ({ item: i + 1, reason: 'The excerpt says so.', verdict: 'supported', supported_part: null })) };
    }
    case 'verdict':
      return { reason: 'The excerpt says so.', verdict: 'supported', supported_part: null };
    case 'binary':
      return { reason: 'The excerpt says so.', supported: true };
    default:
      return schema ? minimalOf(schema) : {};
  }
}

/** Splits text into small pieces, as a model's tokens arrive. */
function pieces(text, size = 6) {
  const out = [];
  for (let i = 0; i < text.length; i += size) out.push(text.slice(i, i + size));
  return out;
}

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

/**
 * Starts the fake server on 127.0.0.1 (port 0: any free one). `options` may be changed while it runs (the e2e script
 * turns `rejectJsonSchema` on for its second generation). Resolves with { server, url, requests, options, close }.
 */
export function startFakeOpenAiServer({ port = 0, record = null, rejectJsonSchema = false, delayMs = 25, requireKey = null } = {}) {
  const options = { rejectJsonSchema, delayMs, requireKey };
  const requests = [];
  const save = () => {
    if (record) writeFileSync(record, JSON.stringify(requests, null, 2));
  };
  const json = (response, status, value) => {
    response.writeHead(status, { 'content-type': 'application/json' });
    response.end(JSON.stringify(value));
  };
  const server = createServer((request, response) => {
    let raw = '';
    request.on('data', (d) => (raw += d));
    request.on('end', async () => {
      const path = (request.url ?? '/').split('?')[0];
      let body = null;
      try {
        body = raw === '' ? null : JSON.parse(raw);
      } catch {
        body = { unparsed: raw.slice(0, 2000) };
      }
      const entry = { at: new Date().toISOString(), method: request.method, path, authorization: request.headers.authorization !== undefined, body, status: 0 };
      requests.push(entry);
      const answer = (status, value) => {
        entry.status = status;
        save();
        json(response, status, value);
      };
      if (options.requireKey !== null && request.headers.authorization !== `Bearer ${options.requireKey}`) {
        answer(401, { error: { message: 'Invalid API key', type: 'invalid_request_error', code: 'invalid_api_key' } });
        return;
      }
      if (request.method === 'GET' && path === '/v1/models') {
        answer(200, { object: 'list', data: FAKE_MODELS.map((id) => ({ id, object: 'model', created: 1760000000, owned_by: 'library' })) });
        return;
      }
      if (request.method === 'GET' && path === '/api/version') {
        answer(200, { version: FAKE_VERSION });
        return;
      }
      if (request.method !== 'POST' || path !== '/v1/chat/completions') {
        answer(404, { error: { message: `${request.method} ${path} is not served here`, type: 'not_found' } });
        return;
      }
      if (!body || !Array.isArray(body.messages)) {
        answer(400, { error: { message: 'messages is required', type: 'invalid_request_error', param: 'messages' } });
        return;
      }
      if (!FAKE_MODELS.includes(body.model)) {
        answer(404, { error: { message: `model '${body.model}' not found`, type: 'not_found_error', param: 'model', code: 'model_not_found' } });
        return;
      }
      if (options.rejectJsonSchema && body.response_format?.type === 'json_schema') {
        answer(400, { error: { message: 'response_format json_schema is not supported by this server', type: 'invalid_request_error', param: 'response_format', code: null } });
        return;
      }
      const content = JSON.stringify(answerFor(body));
      entry.answer = content;
      const id = `chatcmpl-fake-${requests.length}`;
      const created = Math.floor(Date.now() / 1000);
      const promptTokens = Math.ceil(raw.length / 4);
      const completionTokens = Math.ceil(content.length / 4);
      if (body.stream !== true) {
        answer(200, {
          id,
          object: 'chat.completion',
          created,
          model: body.model,
          choices: [{ index: 0, message: { role: 'assistant', content }, finish_reason: 'stop' }],
          usage: { prompt_tokens: promptTokens, completion_tokens: completionTokens, total_tokens: promptTokens + completionTokens },
        });
        return;
      }
      entry.status = 200;
      save();
      response.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });
      const send = (value) => response.write(`data: ${JSON.stringify(value)}\n\n`);
      send({ id, object: 'chat.completion.chunk', created, model: body.model, choices: [{ index: 0, delta: { role: 'assistant', content: '' }, finish_reason: null }] });
      for (const piece of pieces(content)) {
        await sleep(options.delayMs);
        send({ id, object: 'chat.completion.chunk', created, model: body.model, choices: [{ index: 0, delta: { content: piece }, finish_reason: null }] });
      }
      send({ id, object: 'chat.completion.chunk', created, model: body.model, choices: [{ index: 0, delta: {}, finish_reason: 'stop' }] });
      if (body.stream_options?.include_usage === true) {
        send({ id, object: 'chat.completion.chunk', created, model: body.model, choices: [], usage: { prompt_tokens: promptTokens, completion_tokens: completionTokens, total_tokens: promptTokens + completionTokens } });
      }
      response.end('data: [DONE]\n\n');
    });
  });
  return new Promise((done) =>
    server.listen(port, '127.0.0.1', () =>
      done({
        server,
        url: `http://127.0.0.1:${server.address().port}`,
        requests,
        options,
        close: () =>
          new Promise((closed) => {
            server.close(() => closed());
            // Keep-alive sockets the app left open would hold close() until they time out.
            server.closeAllConnections();
          }),
      }),
    ),
  );
}

// Run on its own: prints the address and keeps serving until stopped (Ctrl+C).
if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const args = process.argv.slice(2);
  const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
  const fake = await startFakeOpenAiServer({
    port: Number(option('--port', '0')),
    record: option('--record', null),
    rejectJsonSchema: args.includes('--reject-json-schema'),
    delayMs: Number(option('--delay-ms', '25')),
    requireKey: option('--require-key', null),
  });
  console.log(`fake OpenAI-compatible server at ${fake.url} (models: ${FAKE_MODELS.join(', ')})`);
}

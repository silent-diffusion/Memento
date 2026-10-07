# Engine notes — findings from the October 2026 technical spikes

Measured on the reference machine: Windows 11 (26200), .NET 8.0.425, AMD Ryzen 7 5800HS (8C/16T), 40 GB RAM, NVIDIA RTX 3060 Laptop 6 GB (driver only, no CUDA toolkit), AMD integrated GPU. Every number below comes from a runnable console project; the conclusions are what ARCHITECTURE.md now requires. Builders should treat the gotchas as rules.

## A. Media Foundation encoding (NAudio 2.4.0)

- NAudio 3.x targets .NET 9 only; **2.4.0** is the version for .NET 8.
- Windows 11 ships `Microsoft FLAC Audio Encoder MFT`. FLAC round trip is **bit-exact** at 16 and 24 bit. 60 s encodes in ~0.35 s.
- `MediaFoundationEncoder.GetOutputMediaTypes(FLAC)` fails and a hand-built FLAC media type is rejected by `AddStream`. **Working recipe:** activate the FLAC MFT, `SetInputType(PCM)`, take `GetOutputAvailableType(0)`, build the encoder from that type. Input must be 8/16/24-bit integer PCM, 1–8 channels, 44.1–192 kHz; float32 is rejected (convert to int24 first).
- **The MF FLAC sink is not crash-safe.** It buffers the entire encode in `%TEMP%\MFP*.TMP` and writes the output file only on finalize; a killed process leaves a 0-byte file. Needs free space in `%TEMP%` equal to the output. Therefore: encode only from finished WAV tracks, check `%TEMP%` space first, keep the WAV on failure.
- MP3 (96–320 kbit/s) and AAC (16–320 kbit/s) work; decoded durations are +37 ms (MP3) and +11 ms (AAC) from priming/padding, so lossy files are never timeline-exact. Transcripts are produced from the lossless track.
- Streaming input into the encoder works (a blocking `IWaveProvider`), but is irrelevant given the above.
- The correct `MFAudioFormat_FLAC` GUID is `0000F1AC-0000-0010-8000-00AA00389B71`. The spike used `F1AC0000-…`, which is probably why looking the output type up by subtype failed; the implementation in `Memento.Audio` uses the correct GUID together with the MFT recipe.
- Fallback if ever needed: libFLAC (BSD-3) via P/Invoke. No managed FLAC encoder with an acceptable license exists.

## B. Streaming WAV with checkpoints

- Header written with zero sizes; `Checkpoint()` = `Flush(true)` → patch RIFF/data sizes → `Flush(true)`; `Repair()` sets the data size from the file length rounded down to whole frames.
- Process killed 5 s after a checkpoint: **14.949 s of 15 s recovered**; the 51 ms lost was exactly the unflushed 64 KiB `FileStream` buffer. Data up to the last checkpoint also survives power loss. Flush the managed buffer once per second to cap the loss.
- Classic RIFF stops at **4 GiB**: 3 h 06 min of float32 stereo 48 kHz, 4 h 08 min of int24, 6 h 12 min of int16. Decision: write int24 and roll over to `.partN.wav` at 3.5 GiB.

## C. WASAPI capture

- All endpoints here report WAVE_FORMAT_EXTENSIBLE float32 48 kHz stereo. Start-up latency to the first packet: 137–350 ms, different per stream.
- **Endpoint loopback (`WasapiLoopbackCapture`) delivers zero bytes while nothing is playing.** It cannot serve as a clock; insert clock-timed silence.
- NAudio does not expose QPC/device-position timestamps per packet; our own capture loop (`ProcessLoopback.cs` generalises to `IMMDevice::Activate`) does. Use it for every source.
- **Per-process loopback works** via hand-written interop: `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", …)` with `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`, then `Initialize(SHARED, LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY, 20 ms, 0, float32 48k stereo)`. Isolation is ~78 dB. `GetMixFormat`, `GetDevicePeriod`, `GetStreamLatency` return `E_NOTIMPL`: the caller picks the format. It streams zeros continuously when the target is silent (usable as a clock). `INCLUDE_TARGET_PROCESS_TREE` covers browsers and Electron child processes. The completion handler runs on an MTA thread and must be agile; never block the UI thread on it. The activation follows the target's output device automatically.
- Listing apps with audio: `AudioSessionManager` sessions per render endpoint; dedupe by PID; hide the system-sounds session (pid 0); `DisplayName`/`IconPath` are usually empty, so fall back to `FileVersionInfo.FileDescription` and `Icon.ExtractAssociatedIcon`; use `QueryFullProcessImageName` for processes whose modules cannot be enumerated; subscribe to `OnSessionCreated`.
- No drift measurable over 60 s (bound ~300 ppm); long-run drift must be measured per checkpoint from frames vs clock. The implemented capture loop later measured −0.9 ppm (mic), −3.7 ppm (system) and 0.0 ppm (app) over 60 s with zero overruns.
- Endpoint loopback stamps packets with their **presentation** time, 10–20 ms ahead of arrival. Written as they arrive, loopback tracks overshoot a stop by ~15 ms. The implementation holds packets until they are due and holds all pumps while applying a pause, resume or stop instant, so every track is cut at the same timeline position.
- The device enumerator is one shared COM object per process; creating it through a typed `[ComImport]` coclass breaks NAudio's own casts. The capture loop uses a private wrapper.
- With int24 tracks the 64 KiB `FileStream` buffer drains about every 230 ms, so a process kill loses 10–230 ms; the kill test lost 12–14 ms per track.

## D. Whisper.net 1.9.1

| Runtime | small | medium | large-v3-turbo |
|---|---|---|---|
| Vulkan (RTX 3060) | RTF 0.034 | RTF 0.069 | **RTF 0.038**, +2.1 GB VRAM, 1.2 GB RAM |
| CUDA (with 532 MB of extra cuBLAS DLLs) | 0.037 | 0.078 | 0.045 |
| CPU (8 threads) | 0.19–0.26 | 0.62 | **1.03** |
| Vulkan on the integrated AMD GPU | 0.109 | | |

- WER on a 5-minute public-domain reading: small 5.7%, medium 4.2–4.6%, turbo 3.4–3.5%.
- **Vulkan beats CUDA** here. The CUDA NuGet lacks `cublas64_13.dll`; when CUDA is the only or last runtime and fails to load, whisper.cpp **aborts the process (0xC0000409), uncatchable**. Decision: ship Vulkan + CPU only, run transcription in a worker process.
- Vulkan enumerates two devices (0 = NVIDIA, 1 = AMD iGPU, 3× slower); pick the discrete GPU explicitly. The first Vulkan run after install takes ~2.4× longer (shader cache).
- Word data: `SegmentData.Tokens[]` with per-token `Probability`, `Start`/`End` (10 ms units with `WithTokenTimestamps()`). Tokens are sub-words; a word starts at a token whose text begins with a space; filter special tokens. Word confidence = min of its tokens; threshold ~0.5 marks 1–3% of words.
- **Do not enable DTW timestamps**: in 1.9.1 they silently truncate the transcript to the first segment.
- large-v3-turbo without a prompt returns lowercase unpunctuated text; a short punctuated prompt fixes it. Both medium and prompted turbo **dropped 15 s of real speech** once. Add VAD/energy coverage checks and flag gaps.
- `dotnet publish -r win-x64` still copies linux/arm/x86 runtime folders (453 MB); strip them in MSBuild. Every runtime DLL imports MSVCP140/VCRUNTIME140/VCRUNTIME140_1 (CPU also VCOMP140): ship them app-locally. Whisper.net pulls System.Text.Json 10 transitively.
- Model files (SHA-256): large-v3-turbo 1fc70f77…bc69 (1.62 GB), medium 6c14d5ad…6208 (1.53 GB), small 1be3a9b2…a987 (488 MB), from huggingface.co/ggerganov/whisper.cpp.

## E. sherpa-onnx 1.13.8 diarization

- `OfflineSpeakerDiarization.Process(float[] 16 kHz mono)` → segments `{Start, End, Speaker, Confidence}` (confidence needs `Clustering.ComputeConfidence = 1`).
- `pyannote segmentation-3.0` (MIT, 6 MB; int8 1.5 MB untested) + **`nemo_en_titanet_small`** (CC-BY-4.0, 40 MB): RTF 0.10 on CPU (4 threads), 400–465 MB RAM. With clustering threshold **0.8** it found exactly 2 speakers in a two-reader file (confusion 0.2%) and 1 in a one-reader file. Threshold 0.5 over-splits (extra clusters are still pure). WeSpeaker resnet34-LM collapsed two speakers into one; the 3D-Speaker model is zh-cn trained. Expected-speaker-count setting maps to `NumClusters`.
- Native footprint 22.4 MB (`onnxruntime.dll` + `sherpa-onnx-c-api.dll`), statically linked CRT. The DLL is named plain `onnxruntime.dll`; avoid adding another ONNX Runtime package.
- `SpeakerEmbeddingExtractor` is available for cross-track and enrolled-voice matching later.

## F. Windows.Media.Ocr

- From the `net8.0-windows10.0.19041.0` TFM, no package. `OcrEngine.TryCreateFromUserProfileLanguages()`; only installed Windows OCR languages are available (en-US here); installing more needs Windows Settings (elevated).
- Clean 28 px text: 9/11 lines exact, CER 1.6%. Photo-like (rotated 3.5°, noise, blur): 10/11, CER 1.3%, skew reported in `TextAngle`. 12 px text: CER 8%. Upscale small images first. "Q&A" was dropped every time.
- `OcrWord.BoundingRect` exists; `OcrLine` has no rectangle (union the words). No confidence at any level, so results are always shown for review; Tesseract is the engine that offers per-word confidence.

## G. M2 implementation (transcription and speakers in the app)

Measured through the real orchestrator and `Memento.Worker.exe` (`tools/TranscriptionCheck`), 2026-10-06. **The PC was shared during every run**: another session's local-LLM benchmark held the RTX 3060 at 92–96 % utilisation and 87–88 °C, and other builds used the CPU, so these numbers are 3–5× slower than the idle spike figures in §D/§E and should be re-measured on an idle PC.

| Run | Audio | Result |
|---|---|---|
| large-v3-turbo, Vulkan (RTX 3060) | 5:00 one reader | pass 57.2 s (RTF 0.19; 42 s in a direct worker run with a warm shader cache), 79 segments, 833 words, 4.1 % below 0.5 confidence; 1 speaker |
| large-v3-turbo, Vulkan | 2:58 two readers | RTF 0.37 (first run on that file); 400 words, 4.3 % below 0.5; 2 speakers, talk time 46 % / 54 % (truth 50 / 50 by turn length) |
| small, Vulkan | 5:00 | 17.5 s worker time (RTF 0.058); 815 words, 2.7 % below 0.5 |
| small, CPU (8 threads, below-normal priority) | 5:00 | RTF 0.86; 815 words, 2.7 % below 0.5 |
| sherpa-onnx diarization, CPU 4 threads | 5:00 / 2:58 | 86–117 s / 52–71 s (RTF 0.29–0.40) |
| Model downloads through the model manager | turbo 1.62 GB | 39 s (39.5 MiB/s), SHA-256 verified; speaker models 1.5–1.8 s |
| **Re-measured with the GPU idle** (worker alone): large-v3-turbo, Vulkan | 5:00 | 13.9 s of transcription (RTF 0.046), 19.5 s for the whole job including the energy pass and model load (0.065) |
| Re-measured, quieter CPU: small, CPU, 8 threads, normal priority | 5:00 | 108.5 s (RTF 0.36; the spike's 0.19–0.26 used `en` instead of auto-detect) |
| Killed mid-pass (13-minute file, two windows) | 12:58 | 131 segments up to 9:57 kept as a partial transcript; failure offered `cpu`, `retry`; Retry on CPU continued from window 2 and completed (143 segments) |
| Real app, 2 minutes, Realtek microphone + system playing the two-reader file | 2:00 | transcript, speakers and topics in 77 s after stop; 22 segments (13 mic, 9 system); the laptop microphone also picked up the speakers, so both tracks carry the same speech and get separate speakers (by design) |

Rules learned while building it:

- **Vulkan device order is ggml's, not DXGI's.** The worker loads the model once, reads the `ggml_vulkan: N = <name>` lines from the native log and reloads on the device whose name matches the discrete GPU the host chose from DXGI; here device 0 is the NVIDIA card and 1 the AMD iGPU. DXGI reports the iGPU's shared budget (≈ 23 GB) as "free local memory", so "discrete" is decided by vendor (NVIDIA) or ≥ 2 GB dedicated memory, never by free memory.
- **sherpa-onnx confidence is a similarity score, not a probability**: about 0.5–0.8 for correctly separated readers, and −2 when a track has a single cluster. Used raw, every line read as uncertain; it is mapped linearly (0.2 → 0, 0.6 → 1, −2 → 1).
- **Whisper.net brings System.Text.Json 10 and Microsoft.Extensions.AI**, so the worker lives in its own `worker\` folder (self-contained) and never shares assemblies with the app.
- Native libraries may print to stdout; the worker keeps the original stdout handle for the protocol and points the process's stdout at stderr before loading them. The host also ignores any line that does not start with `{"type":`.
- whisper.cpp reports its own percentage per call (`WithProgressHandler`); without it a recording shorter than one 10-minute window shows 0 % until done.
- Prompted large-v3-turbo again dropped the 15-second LibriVox announcement at the start of the 5-minute sample (small kept it). The coverage threshold was 20 s then, so it was not flagged; it is 10 s since the M2 integration, and Review offers "Transcribe again with Small" at the gap.
- whisper.cpp can loop on one line ("Thank you." over and over); a run of three or more identical lines on a track keeps the first, and History lists what was dropped.
- A worker that loses the graphics card mid-job must be gone before the next one loads a model: the host lets one GPU job run at a time and waits for the previous worker to exit, the worker holds a machine-wide lock (`Local\Memento.Worker.Gpu`) while it may use the card, and every worker runs in a kill-on-close job object, so Windows ends it with Memento.
- Digital silence (loopback tracks are padded with zeros) must count toward the speech-energy noise floor, or a track whose only non-zero frames are speech has no speech at all.
- The CPU "PC is busy" measure subtracts the worker's own processor time, or a CPU transcription would pause itself.
- The repository's `models/` ignore rule also matched `src/Memento.Core/Models/`; source folders named Models are now excepted in `.gitignore`.

## Packages verified

| Package | Version | License |
|---|---|---|
| NAudio | 2.4.0 | MIT |
| Whisper.net, Whisper.net.Runtime, Whisper.net.Runtime.Vulkan | 1.9.1 | MIT |
| org.k2fsa.sherpa.onnx (+ runtime.win-x64) | 1.13.8 | Apache-2.0 |
| NtvLibs.MSVCP.{vcruntime140, vcruntime140_1, msvcp140, vcomp140}.runtime.win-x64 | 14.42.34430 | MIT packaging; Microsoft VC++ runtime redistributable terms for the DLLs |

## H. Local LLM provider (LLamaSharp 0.27 + Vulkan), October 2026 spike

Measured on the reference laptop (RTX 3060 6 GB) with another resident app intermittently holding up to 4.4 GB of VRAM; GPU numbers were taken in windows when it had released it.

- **Runtime:** LLamaSharp 0.27.0 (MIT; llama.cpp from April 2026) with `LLamaSharp.Backend.Vulkan.Windows` + `LLamaSharp.Backend.Cpu` referenced **directly** (the `Backend.Vulkan` meta-package drags in 65 MB of Linux binaries). Vulkan loads with only the GPU driver (`ggml-vulkan.dll` imports `vulkan-1.dll`). Native footprint: Vulkan 65.5 MB + CPU variants 17.6 MB; `mtmd.dll` is not needed. All DLLs import the VC++ runtime (ship app-locally, as for Whisper). Speed is at parity with the official llama.cpp Vulkan build; the CPU build is ~25% slower on prompts.
- **Devices:** llama.cpp keeps only the discrete GPU (Vulkan0); the iGPU is useless (3B at 7 t/s). `MainGpu`/`SplitMode.None` must only be set when a GPU backend is loaded, or weight loading fails on CPU.
- **Models (all Apache-2.0, Q4_K_M, SHA-256 verified):** `Qwen3.5-4B` (2.74 GB file, 2.6 GB VRAM weights; 32k context fits at f16 KV in 4.24 GB; Vulkan 1,378 pp / 35 tg t/s at 4k) is the **GPU default**. `Ministral-3-3B-Instruct-2512` (2.15 GB; CPU 45–60 pp / 6–9 tg t/s at 1–4k) is the **CPU default**. Gemma 4 E4B (5 GB, license page ambiguous) and Ministral 8B (does not fit 6 GB; spills) are out. Minimum VRAM in the catalog: 3.3 GB for Qwen at 8k, 2.8 GB for Ministral 3B at 4k.
- **Quality on a 20-minute synthetic 4-speaker meeting (map recall / precision / verify on 20 claims incl. 6 planted false / pipeline time on Vulkan):** Qwen3.5-4B 1.00 / 0.74 / 20 / 249 s; Ministral 3B 0.93 / 0.72 / 18 / 196 s (17.5 min on CPU); Gemma 4 1.00 / 0.82 / 18 / 320 s. All four models caught every planted false claim and found both undiscussed agenda items; none invented a deferred decision; none extracted items from a non-meeting control text. Typical errors: proposals and "parked" items taken as decisions; the 3B citing the wrong line; the verifier rejecting true claims over surface form ("Luis Brandt" vs "Luis", "Due: not stated"). A 1-hour meeting at 150 wpm extrapolates to about 13–17 min on Vulkan and about 70 min on CPU.
- **Pipeline decisions:** chunks of ~1,500 transcript tokens at speaker turns (render timestamps as short segment ids to save ~40% of tokens); **map with a GBNF grammar**, **reduce in code** (time-overlap + text-similarity dedupe; the LLM reduce added duplicates, disorder and 25–35% runtime), **code-based citation repair** (move a citation to the line where the quote actually occurs), **verify per claim with the owner and date checked separately and names given exactly as in the transcript**, then the deterministic grounding validator. With a cloud provider skip the local model entirely but keep dedupe, citation repair and the validator.
- **Engineering traps:** (1) `CreateContext` returns a context with a **null handle instead of throwing** when VRAM is exhausted; the first use is an uncatchable AccessViolation. Check `NativeHandle.IsInvalid`. (2) Moderate VRAM overcommit does not fail: Windows spills allocations to shared memory and speed drops 10–25×, also when another app takes VRAM mid-run. Read free VRAM before choosing layers, watch the process's dedicated vs shared GPU memory counters, and fall back to CPU or a smaller context when shared memory grows. (3) `GreedySamplingPipeline` with a grammar ran at 12 t/s; `DefaultSamplingPipeline { Temperature = 0, TopK = 1, Grammar, GrammarOptimization = Basic }` ran at 46–49 t/s with identical output. (4) A grammar that forbids newlines cut the 3B's recall from 0.93 to 0.57; allow natural whitespace. (5) A grammar does not prevent hitting the token limit; always check the stop reason and size prompt + max tokens against `n_ctx`. (6) `LLamaTemplate` is heuristic: it throws for Gemma 4, misrenders Ministral 3 and omits Qwen3.5's empty think block; format chat per model family from a verified template id in the catalog. (7) One context is not thread-safe; serialise. (8) Warm up after load (first decode runs ~8× slower while shaders compile). (9) Cancel via the token per generated token plus `llama_set_abort_callback` for prompt evaluation (~0.4 s to stop). (10) Dispose context then weights to free VRAM (3.1 GB → 8 MB); run in a separate process.

## I. M4a implementation (AI providers), October 2026

Measured with `tests/Memento.AI.Tests` (`--filter Category=Hardware`) on the reference laptop; Ministral 3B and Qwen3.5 4B Q4_K_M, f16 KV, 8 threads, short grammar-constrained extraction (215–231 prompt tokens).

| Run | Load | Warm-up | Prompt | Output | Dedicated VRAM |
|---|---|---|---|---|---|
| Ministral 3B, Vulkan, 4k | 2.9–3.1 s | 0.5 s | 2,250 tok/s | 55–59 tok/s | 2.75 GB |
| Qwen3.5 4B, Vulkan, 8k | 3.4–3.5 s | 0.8 s (17 s on the very first run: shader cache) | 1,310 tok/s | 39–43 tok/s | 3.41 GB |
| Ministral 3B, CPU build, 4k | 2.7 s | 0.6 s | 56 tok/s | 9.2 tok/s | 0 |
| Ministral 3B, Vulkan build on the CPU (0 layers), 4k | 0.8 s | 1.0 s | 27–37 tok/s | 6.3–8.3 tok/s | 0.03–0.15 GB |

- **Warm up past one micro-batch on the GPU.** After a two-token warm-up the first real prompt still ran at 8–14 tok/s (the batched matrix shaders compile on first use); a ~640-token warm-up prompt brings it to full speed. On the CPU a tiny warm-up is enough.
- **Decode prompts one micro-batch (512) per call.** With every layer offloaded, llama.cpp consults the abort callback only between graph splits, so a 2,048-token decode call made cancel during prompt reading take 1.3 s. Per-micro-batch calls with a cancellation check between them: 140–190 ms on both backends; during generation 1–2 ms. Unloading afterwards takes 0.45–0.75 s.
- **Shared GPU memory is only a spill signal for GPU loads.** A CPU run with the Vulkan build loaded pins about 270 MB of host memory that Windows counts as the process's shared GPU memory; GPU runs grow it by 25–30 MB. The spill watch (threshold 384 MB over the pre-load baseline, sampled every 0.5 s and after each load step) therefore runs only when layers are offloaded.
- **PDH before first GPU use.** `\GPU Process Memory(pid_N_*)\…` has no instance until the process allocates GPU memory: `PdhCollectQueryData` returns `PDH_NO_DATA`, which reads as zero, not as "cannot watch".
- **Both trap paths reproduced with Ministral 8B (5.2 GB) on the 6 GB card:** all layers at 16k context → `ErrorOutOfDeviceMemory` and a null context handle, reported as `ai.notEnoughVram` after 6.6 s; 28 layers at 8k → loads, shared memory grows by 0.9 GB, stopped as `ai.notEnoughVram` in 5.8 s instead of running 10–25x slower.
- **The CPU build is faster on the CPU than the Vulkan build with 0 layers** (56 vs 27–37 prompt tok/s), so the worker should load the CPU build for CPU jobs (one job per process makes that free).
- **Token estimate.** The tokenizer-free estimate (`EstimatingTokenCounter`, factor 1.25) gives 1.35x the exact Ministral count on transcript text: safe for budgets, so exact counts (the engine, or `LlamaTokenCounter` with a vocabulary-only load) only matter for packing chunks tightly.
- **Content is tokenized without special-token parsing**; only the template's own pieces are parsed for control tokens, so `[INST]` or `<|im_end|>` inside a transcript stays text.
- **Cloud providers:** raw HTTP rather than vendor SDKs (the Anthropic .NET SDK needs System.Text.Json 10 and Microsoft.Extensions.AI 10 in the app). Claude: `claude-opus-5-5`, streamed Messages API, `output_config.format` JSON schema, `output_config.effort: high`, `fallbacks: "default"` (beta `server-side-fallback-2026-07-01`); it rejects `temperature`, and thinking counts against `max_tokens`, so 16k tokens of headroom are added. OpenAI: `gpt-6-astra` on the Responses API with `store: false` and a strict `text.format` schema. Retries only for answers that say the request was not processed (429, 500, 502, 503, 504, 529, an overload event before any text), honouring `retry-after`/`retry-after-ms` up to 60 s; timeouts and dropped connections are never retried.

| Package | Version | License |
|---|---|---|
| LLamaSharp, LLamaSharp.Backend.Cpu, LLamaSharp.Backend.Vulkan.Windows | 0.27.0 | MIT |

### I.2 The local pipeline at the M4 integration (0.9.0), October 2026

Measured with `LocalPipelineHardwareTests` (Meeting minutes of the 20-minute synthetic meeting, Qwen3.5 4B on Vulkan, 16k context asked, 3,000-token chunks → 2 chunks) and `FixedVerification` (the spike's 20 claims, 14 true and 6 planted false). **No run had a quiet PC:** another session's soak and processing apps, and another resident app's llama-server and speech server, shared the RTX 3060 (18–35 % utilisation from other processes, at times 4.3 GB of its memory), so the times in the table are contended; accuracy is not affected. A later run with the card nearly free (other processes 0.7 GB and 10–26 % of it, no other model loaded) took **152 s** (map 92 s, verify 59 s, load and warm-up 4 s) with the same 28 requests and the same results: recall 0.93 (U2 missed), precision 1.00, the fixed set 20/20 with 6/6 planted claims caught, agenda items 5 and 7 "Not reached".

| | Before (8ce9c56) | After (0.9.0) |
|---|---|---|
| Recall / precision on the 14 decisions and action items | 0.79 (11/14) / 1.00 | **0.93 (13/14) / 1.00** |
| Fixed verification set (planted false caught) | 18/20 (6/6) | **20/20 (6/6)** |
| Agenda items reported "Not reached" (truth: 5, 7) | 2, 5, 7 | **5, 7** |
| Requests / model loads | 76 / 3 | **28 / 1** |
| Pipeline time (map / verify / load and warm-up) | 338 s (174 / 164 / 33) | **225 s (142 / 80 / 8)** |
| Output tokens (verify) | ~4,300 | 1,986 |
| Pipeline time, card nearly free | — | **152 s (92 / 59 / 4)** |

What moved the numbers, each measured on its own:

- **Recall was lost in the map, not the verifier.** On chunk 2 the model answered decisions first and then wrote one action item (117 output tokens), missing A5, A6 and U2. With `action_items` before `decisions` in the answer it wrote 349 tokens and found A5 and A6 (U2, "we need to tell the sales team", is still missed). Smaller chunks found everything but cost more: 2,000-token chunks (3 chunks) found U2, A5 and A6 for 1.5× the map calls and invented a weekend-coverage action; 2,500-token chunks found 13/14 with more parked items offered as decisions. An instruction to "list every action item, also small ones" changed nothing.
- **Span.** The fixed set against the truth's lines only: 18/20 ("deliver final pricing page mockups" rejected because the line says "final mockups"); with one neighbouring line either side 20/20, two either side 20/20. The pipeline's span (two either side) was already wide enough; the fixed set now uses one neighbour either side.
- **Graded verdicts.** "supported / partly / not supported" with the supported part: one request per claim 16/20 on the truth lines and 19/20 with one neighbour (the mockups claim graded "partly"); in batches of 6, 20/20; of 10, 19/20; of 20, 20/20; planted claims 6/6 in every variant. "Partly" keeps a summary point shortened to the supported part, only when the shorter text adds no word that is neither the claim's nor the excerpt's. For decisions, action items, owners, dates and agenda coverage "partly" gets a **second vote** (plain yes/no over one more line either side), which accepted the mockups claim and never a planted one.
- **The claim in the transcript's own words** was not needed: the map already copies the decision's wording ("We ship 3.2 on Thursday, November twelfth"), and every verifier miss was on the fixed set's paraphrases, which the wider span fixed.
- **Agenda coverage** from one yes/no answer per chunk missed item 2 ("Next, the offline mode backlog." answered "not discussed"). Each item's words are now matched in code against the transcript and the other passes' citations (most of the item's words, at least two), and up to three matching lines per item become candidates for the same verifier. Items 5 and 7 have no candidate, so they stay "Not reached".
- **Speed.** One model load per generation (a worker job that stays loaded and answers `prompts` lines) took loads and warm-ups from 33 s to 8 s and keeps the graphics card for the whole generation. Verification in batches of up to 6 per module family **without a reason per item**: the reason changed no verdict on the fixed set (20/20 either way; a reason capped at 80 characters dropped to 19/20) but was most of the output (4,259 → 2,216 tokens). Summary-like modules have only their length's worth of statements checked (spread over the recording, then replacements for failures), and the meeting purpose asks for one point per chunk: 76 → 28 requests. Data modules never had model requests.
- **Duplicates.** A recap's shorter copy of a person's task ("Send final pricing mockups") is merged when the owner matches at word similarity 0.25 (others keep 0.34).
- **Trap: free video memory is not the budget.** With 5.3 GB reported free, a 16k context had 0.5 GB of its KV cache moved to shared memory while another app held video memory; 8k fitted. A spill while loading or warming up now reloads once at the profile's smallest context (8k for Qwen) before failing with `ai.notEnoughVram`.
- **Not a meeting.** A three-minute reading of two short stories came out "Not discussed." in every section: the executive summary's instructions ("decisions first, then risks") and the discussion summary's ("one paragraph per agenda item") aimed at things the reading did not have, and the model answered with empty lists. Summary-like sections now write about what the excerpt says when the instructions miss; open questions and a stated purpose still stay empty. That rule was not enough on a second, more jumbled transcript of the same reading (42 lines, the two stories interleaved): Qwen answered the executive summary and the discussion summary with empty lists with the rule, with "outcomes and decisions first when there are any", with "a reading or a story has content", and the executive summary even without its instructions. Only the neutral summary task without the section's instructions found points (for both). A summary, executive summary, discussion summary or timeline that every chunk answers with nothing is therefore **read once more as a plain summary** (`.plain` requests, verified like any other); open questions, a stated purpose, a topic or a custom section may rightly be empty and are not asked again. The synthetic meeting never takes that path.

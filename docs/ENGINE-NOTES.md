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
- `pyannote segmentation-3.0` (MIT, 6 MB; int8 1.5 MB untested) + **`nemo_en_titanet_small`** (CC-BY-4.0, 40 MB): RTF 0.10 on CPU (4 threads), 400–465 MB RAM. With clustering threshold **0.8** it found exactly 2 speakers in a two-reader file (confusion 0.2%) and 1 in a one-reader file. Threshold 0.5 over-splits (extra clusters are still pure). WeSpeaker resnet34-LM collapsed two speakers into one; the 3D-Speaker model is zh-cn trained. The expected-speaker count mapped to `NumClusters` until 1.2.0; since then the host groups the voices instead (§L).
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

### I.3 Streaming the local model's tokens for Live output (1.2.0), October 2026

`LocalStreamingSpeedTests` (Hardware) runs Meeting minutes' 14 map requests over the synthetic meeting's 2 chunks (grammar-constrained, 3,096 output tokens per round, identical answers every time: temperature 0) twice on one model load of Qwen3.5 4B Q4_K_M on the RTX 3060 Laptop (Vulkan, 16k context), and reports output tokens per second over the time spent writing. *Before* is the 1.1.0 worker (a `progress` line per decoded piece, the host only counting them); *after* is this worker (coalesced lines and an `answered` line per prompt) with the requests sent through `RequestRunner` into a `GenerationOutputFeed`, as the app sends them (`MEMENTO_STREAM_LIVE=1`; `MEMENTO_WORKER_DIR` pointed the test at the saved 1.1.0 build). Release builds; runs interleaved so both sides saw the same load.

| Run (start) | Worker | Tokens/s (round 1 / 2) | Writing s per round | Worker lines per round | Events to the UI per round |
|---|---|---|---|---|---|
| 09:00, card nearly free | before | 56.2 (56.5 / 55.9) | 54.8 / 55.4 | 3,097 | — |
| 09:11, other llama.cpp server starting | after | 51.5 (53.2 / 49.9) | 58.2 / 62.1 | — | 556 / 575 |
| 10:01, card shared | before | 35.7 (29.7 / 44.6) | 104.3 / 69.4 | 3,097 | — |
| 10:05, card shared | after | 43.3 (43.9 / 42.8) | 70.6 / 72.4 | — | 563 / 563 |
| 10:08, card shared | before | 43.3 (43.7 / 42.9) | 70.8 / 72.2 | 3,097 | — |
| 10:12, card shared | after | 41.9 (42.3 / 41.5) | 73.2 / 74.6 | — | 573 / 567 |
| 10:15, card shared | after, lines only counted | 38.1 (39.2 / 37.1) | 79.0 / 83.5 | 1,577 / 1,582 | — |

- **No measurable cost.** The back-to-back pair at 10:05 and 10:08 is 43.3 tokens/s both ways; the spread between neighbouring runs of the same build (35.7–43.3 before, 38.1–43.3 after) is larger than any difference between the builds. The PC was never quiet for this measurement: another session's tests and a resident app's llama.cpp server shared the card (it read 10–43 % busy with no model of ours loaded), which is why the shared-card runs are about 25 % slower than the first one. The first pair straddles that server starting and is not a comparison.
- **Fewer, smaller lines.** At 40–55 tokens/s a piece comes every 18–25 ms, so the 40 ms window sends about two pieces per line: 3,097 → about 1,580 lines per round (with the `answered` lines). The host sends the UI about 565 events per round (5–6 a second over the writing time; at most ten a second by the feed's 100 ms window), and every answer's streamed text equals its final text (12,626 characters each round).
- **Where the cost would be.** Per piece the decode thread only appends to a buffer and reads the clock; a line is a JSON serialisation and a flushed pipe write, now at most 25 a second instead of one per token. On the host the worker's reader only queues (`GenerationOutputFeed`), so a slow UI cannot hold the worker up through a full pipe.
- Grammar-constrained answers stream their raw pieces (the JSON as it is written); the answer text is the concatenation of the pieces exactly.

## J. Reading the graphics card's memory, October 2026 (after 1.1.0)

The product owner reported Qwen3.5 4B running on the processor with Settings saying about 0.1 GB of video memory free "when the GPU wasn't even in use", also right after a restart with only Memento open. Measured on the reference laptop (RTX 3060 Laptop, 6 GB; AMD integrated GPU) with `GpuMemoryHardwareTests` (`--filter Category=Hardware`), PDH (`Get-Counter`) and `nvidia-smi --query-gpu=memory.total,memory.used,memory.free`. Under WDDM `nvidia-smi` lists the processes on the card but not their memory (`[N/A]`), so the per-process numbers come from `\GPU Process Memory(*)\Dedicated Usage`.

**Root cause: the reading was right; the card really was full.** A dictation app that starts with Windows bundles Ollama: its `ollama.exe` started `lib\ollama\llama-server.exe`, which kept a 4B model loaded (Ollama's own `ps`: 3.2 GB, 100 % GPU), and its Python speech server held another 1.2 GB. Nothing in Memento's line said so, so "0.1 GB free" read as a wrong measurement. A second, smaller fault: the Settings snapshot read at start-up (`settings.get`, which names the models in effect for the card as it was then) was not read again when Settings opened.

| State (MiB) | `nvidia-smi` used / free | PDH adapter use | Memento free | Memento's holders |
|---|---|---|---|---|
| Ollama model + speech server loaded | 5,082 / 913 | 5,082 | 898 | llama-server 3,158 · python 1,203 · dwm 374 · explorer 127 (PDH) |
| After `ollama stop <model>` | 1,924 / 4,071 | 1,934 | 4,063 | python 1,202 · dwm 374 · explorer 127 |
| Memento's worker holding a model (another session's run) | 4,477 / 1,518 | 4,490 | 1,503 | Memento 3,732 · dwm 406 · explorer 135 |
| Ollama model loaded again (speech server idle) | 3,896–3,902 / 2,093–2,099 | 3,908 | 2,085 | Ollama (llama-server.exe, started by the dictation app) 3,149 · dwm 406 · explorer 135 |
| The app, Settings › AI and privacy, Ollama loaded | 4,023 / 1,972 | | 1.9 GB of 6 GB | "Qwen3.5 4B needs 3.6 GB on the card, so it runs on the processor …" |
| Same, after `ollama stop` and **Check again** | 880 / 5,115 | | 5.0 GB of 6 GB | Qwen3.5 4B · graphics card, 16k context |

- **Agreement**: Memento's free memory is 8–15 MiB under `nvidia-smi`'s in every state, and its "used" 6–13 MiB over. DXGI reports 5,994 MiB dedicated where `nvidia-smi` says 6,144 MiB total: the driver keeps about 150 MiB, which `nvidia-smi` leaves out of both used and free. The sentences round the card to the size it is sold with ("6 GB").
- **DXGI's budget is per process** and stayed near the whole card while Ollama held it; the PDH bound (`\GPU Adapter Memory(luid_…)\Dedicated Usage`, every process together) is what makes the free figure right. A process with no D3D device (the probe, a console test) gets a budget like any other; nothing depends on which adapter Memento's window renders on.
- **Adapters and LUIDs**: the NVIDIA card is `luid_0x00000000_0x0000f961`, the AMD integrated GPU `…_0x0000e676` (495 MiB carve-out), and a third LUID with no use is the Basic Render Driver (software, left out). The PDH instance names put the high part first (`luid_0x{high}_0x{low}`), matching DXGI's `LuidHighPart`/`LuidLowPart`. The driver's adapter type (`D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERTYPE)`) flags the NVIDIA card `HybridDiscrete` (0x2313) and the AMD GPU `HybridIntegrated` (0x232B); the probe now uses those flags first, so an APU whose firmware reserves 2 GB or more is not taken for the card. The integrated GPU gets no holders.
- **Attribution cost**: one wildcard PDH read plus one Toolhelp snapshot: 10–11 ms warm, 1–2 ms when the 4-second cache answers, about 270 ms for the very first call in a process (JIT and loading PDH). The footer samples every 5 seconds, so it reads the holders at most once per tick; `status.footer` does not carry them.
- **Parent chains seen**: the bundled Ollama is `{dictation app} → ollama.exe → llama-server.exe`; Ollama's own installer gives `explorer → ollama app.exe → ollama.exe → llama-server.exe`. Naming the app that started the runtime turned "close Ollama" (which the owner had not knowingly started) into "close {the app}". The Python speech server has no file description, so it reads "python.exe (started by …)".
- **Not verified**: the exact 0.1 GB moment the owner saw (the model and the speech server together leave about 0.9 GB here; the owner's PC may have had a larger context or another program loaded), and a desktop with only an integrated GPU or an Intel Arc card.

## K. Transcript times against the audio, October 2026 (after 1.2.0)

The product owner reported: "The highlighted section of the transcription is often not synced with the audio. It's usually off by a few seconds after a minute." Review highlights the line whose `start` is the last one at or before the player's `currentTime`, so either the player's position, the mix or the line times were off. Each was measured on the reference laptop (Edge/WebView2 154, large-v3-turbo on Vulkan; the graphics card was shared with other sessions' jobs throughout, so times are contended).

**Root cause: Whisper's line times, not the clock.** large-v3-turbo with the punctuated prompt starts a line where the pause before it begins (the segment boundaries fill the silences), often on whole seconds, and after a long silence it can lose its place and repeat one sentence for minutes; the repeat filter then drops those lines and what was said there is gone. The audio path is exact:

| Checked | How | Result |
|---|---|---|
| Player position (`currentTime`) | A 5-minute linear ramp encoded by Memento's own FLAC, MP3 (44.1 kHz, 128 kbit/s) and AAC encoders, played in headless Edge 154 through `<audio>`; the audible ramp value read through Web Audio against `currentTime`, playing from 0 and after seeks to 1:00, 2:30, 4:40 | FLAC 48 and 44.1 kHz: 0–8 ms; MP3 2–25 ms; AAC up to 44 ms. No drift. |
| The app's player (WebView2, `https://library.memento/`) | The synthetic recording below, opened in the published app; each line's onset detected in the played audio against `currentTime`, 75 s from 0:00 then a seek before every later line up to 3:37 | Every line within 15 ms of its known time. The virtual host's range requests seek correctly. The same recording as a 44.1 kHz, 128 kbit/s MP3 imported through "Import audio or video" (`tools/e2e/m6-sync.mjs --audio …mp3`): every line heard 22–30 ms after its WAV time (the MP3's own encoder delay, kept by the import), and its transcript passes the same 100 ms check. |
| The mix | `TrackMixerTests.TracksAt44And48KilohertzStayOnOneTimelineForMinutes`: 3.5 minutes of a 44.1 kHz and a 48 kHz track with a burst every 30 s | Right length; every burst within 2 ms. |
| The 16 kHz path and the owner's file | The owner's 55:40 import (an MP3 decoded to a 48 kHz stereo FLAC; mix identical), speech onsets after a pause found in the decoded audio at 48 kHz and on the 16 kHz path Whisper hears, against the transcript's line starts, per 5 minutes | Both paths agree; the median offset is 0.0 ± 1 s in every 5 minutes up to 55 minutes, so nothing drifts, but the spread is −3.5 to +7 s (p10–p90). 73 % of lines start exactly where the previous one ended, 29 % on whole seconds; 386 repeated lines were dropped and 13 stretches of speech had no transcript. |

The synthetic recording (`tools/e2e/sync-fixture.ps1`): 30 made-up lines in two Windows voices, each placed after a silence of 1.5–8 s (one of 20 s, at 0:58), 4:00 long, the true start and end of every line's voice in `<wav>.json` (the first and last 10 ms above −40 dBFS). Transcribed with the 1.2.0 worker (whole 600 s window, prompted): the first nine lines started 0.24–3.23 s early (all on whole seconds), then from 1:00 to 3:50 the engine wrote "The customer survey is a very good idea." 120 times, once a second, through the real speech; the repeat filter dropped them and 21 of 30 lines were missing (and no coverage gap was reported: the filter keeps the first line of each run, one every 30 s, which left no 10 s stretch of speech without a line). `WithNoContext()` gave the same output; without the prompt there was no loop, but lines started up to 7.25 s early and silences got invented lines.

**Fix (worker).** `SpeechEnergy` keeps 10 ms frames as well and refines each region's edges to them; `SoundRegions()` uses a lower bar (−50 dBFS and twice the noise floor) so quiet speech counts as sound. Each window is then packed (`SpeechPacker`): every sound region with 0.4 s around it, silences longer than 0.8 s cut in the middle (so any pause reaches the engine as 0.8 s of the room's own quiet), and the result cut into chunks of at most 28 s between sounds (inside a sound longer than that, at its quietest 50 ms). Each chunk is one `ProcessAsync`, so the engine's own 30 s cut never falls inside a sentence (with one 116 s packed buffer it dropped "We have about 200 answers so far, which is" at its first cut). Segment and token times come back through `PackedAudio` (a start on a piece border belongs to the later piece, an end to the earlier one), and `SpeechAligner` moves a line's start that falls in silence, or in the last 0.6 s of the previous sound past its middle, to where sound next begins, and its end back to where sound stopped, within the line. With "auto" the language is detected on a window's first chunk and fixed for its other chunks (detection is an extra encoder pass per call: 647 s without this, 346 s with it on the owner's file).

| | Before (1.2.0 worker) | After |
|---|---|---|
| Synthetic, lines transcribed | 9 of 30 | 30 of 30 (one lost its first word, "Great.") |
| Synthetic, line start − voice start | −0.24 to −3.23 s (first minute only) | −0.01 to +0.01 s for 29 lines; the line without "Great." starts at its next word |
| Owner, line starts after a pause, p10–p90 per 5 min | −3.5 to +7.2 s | −0.29 to +0.89 s (median 0.00) |
| Owner, speech with no word within 1 s | 327 s of 2,333 s (14.0 %), 6 gaps of 10 s or more | 7 s (0.3 %), none |
| Owner, lines / words | 1,245 / 9,591 from the worker (939 / 6,901 after the repeat filter) | 495 / 6,804 (no repeats) |
| Owner, whole-second line times | 29 % | 3 % |
| Owner, worker time (55:40, shared card) | 326 s | 346 s |

The owner's before/after spread is measured against the same kind of energy onsets the aligner uses, so it shows that lines now start where sound starts, not that the words are right; the synthetic recording, with its true times, is the independent check (`TranscriptSyncHardwareTests`, `--filter Category=Hardware` with `MEMENTO_SYNC_WAV`, fails with the 1.2.0 worker and passes now). The player's `currentTime` needed no change, also when the storage setting has made the mix MP3 or AAC: the files Memento's own encoders write play within 25 ms (MP3) and 44 ms (AAC) of the clock in WebView2's engine, the 37 and 11 ms of encoder padding (§A) included.

- **Media Foundation's MP3 duration is short**: `MediaFoundationReader.TotalTime` said 299.354 s for a 300.030 s MP3 (the decoded frames). The import already takes the length from the decoded frames.
- **Not verified**: a recording with both microphone and system audio at mismatched device rates on real hardware (the simulated engine records at 48 kHz only; the mixer test covers 44.1 + 48 kHz); other models than large-v3-turbo with the new chunking (small was not re-measured); recordings with steady background noise louder than −50 dBFS, where the packer keeps nearly everything and the engine behaves as before.
## L. Too many speakers: grouping voices on the host, October 2026 (after 1.2.0)

The product owner reported "way too many speakers". Their two meetings (imported files, one track each, Expected speakers on Auto) had come out with **84 speakers** (1:32:14) and **35 speakers** (55:40) at clustering threshold 0.8; the 92-minute one had been corrected by hand down to 4 named people (and 63 leftover unnamed speakers), the other lists 4 participants but its corrections were lost when its speakers were identified again (which is why a speakers pass over corrected lines now keeps them as a version). Measured on private copies with `tools/TranscriptionCheck` (`diarize` runs the worker's speaker job on one file and saves its turns and voices; `evaluate` groups a saved job on a transcript's lines offline; `identify` runs the real stage), sherpa-onnx 1.13.8, pyannote 3.0 + TitaNet small, CPU, 4 threads, the PC shared with other builds. "Named speech right" is the share of the speech of the 4 people the owner named (1,110 of 1,505 lines) that lands on the right speaker, one speaker per person (matched greedily by shared speech); purity counts each speaker as its majority person.

| 1:32:14 meeting, 4 named people | Speakers (raw clusters) | With 2 %+ of the talk | Named speech right | Purity |
|---|---|---|---|---|
| Threshold 0.8, Auto (**before**: the shipped behaviour) | 84 (114) | 8 | 63.6 % | 99.7 % |
| Threshold 1.0, Auto | 28 (38) | 5 | 89.1 % | 97.7 % |
| Threshold 1.2, Auto | 5 (6) | 3 | 89.8 % | 90.1 % |
| Threshold 0.8, sherpa-onnx fixed count 4 (before, one-track count) | 4 | 3 | 89.8 % | 90.1 % |
| 0.8, host grouping to 4, little voices first (min 0 s) | 4 | 1 | 73.8 % | 73.8 % |
| **0.8, host grouping to 4 (Who spoke = 4)** | **4** | **4** | **97.8 %** | **97.8 %** |
| 0.8, host grouping to 3 / to 5 | 3 / 5 | 3 / 4 | 97.8 % / 97.5 % | 97.8 % / 98.8 % |
| **0.8, Auto, host join 0.66 (after)** | **4** | **4** | **97.8 %** | **97.8 %** |
| 0.8, Auto, host join 0.6 / 0.72 | 6 / 9 | 3 / 6 | 97.8 % / 91.0 % | 97.8 % / 98.8 % |
| 1.0, host grouping to 4 / Auto join 0.7 | 4 / 9 | 4 / 6 | 96.6 % / 91.0 % | 96.6 % / 97.3 % |

| 55:40 meeting, 4 participants listed | Speakers (raw clusters) | Talk shares |
|---|---|---|
| 0.8, Auto (**before**) | 35 (48) | 25.0, 22.6, 18.1, 11.8, 3.9, 3.1 … (6 with 2 %+, 19 under 10 s) |
| 1.0 / 1.1 / 1.2, Auto | 12 (16) / 7 (8) / 5 (5) | 55.2, 21.5, 21.3, 1.2, 0.7 at 1.2 |
| 0.8, sherpa-onnx fixed count 4 | 4 | 54.1, 22.6, 19.9, 3.4 |
| **0.8, host grouping to 4 (Who spoke = 4)** | **4** | **57.6, 21.2, 15.9, 5.4** |
| **0.8, Auto, host join 0.66 (after)** | **3** | **57.6, 21.2, 21.2** |

| Synthetic two-voice fixture (`tools/e2e/speech.ps1`, two Windows voices, 45 s) | Raw clusters |
|---|---|
| Threshold 0.5 / **0.8** | 2 / **2** (55 / 45 %; the two voices' embeddings are 0.25 alike) |
| Threshold 1.0 / 1.2 | 1 / 1 (both voices merged) |

What this decided:

- **The threshold stays 0.8.** It over-segments real meetings badly (114 voices for 4 people) but every voice is nearly pure (99.7 %), and a pure voice can be grouped later. Higher thresholds, and sherpa-onnx's own fixed cluster count, merge different people inside the diarizer (two of the four named people became one: purity 90 %), and nothing on the host can part them again; 1.0 already merges the two synthetic voices. So the diarizer never gets the count any more (`numClusters` −1) and the host groups.
- **Little voices go last.** Joining the most alike pair first, as the multi-track grouping did, compares the main voices with noise: most of the 114 voices are a few seconds of laughter, cross-talk or one word, their embeddings are unlike anything, and the real people (about 0.6 alike to each other) are joined first: 73.8 %, one speaker holding everything. Setting aside voices under 30 s (or 5 % of all speech, whichever is less, so a short recording's voices all count) and joining them last, each to the main voice it sounds most like, gives 97.8 %. With a count, every voice is joined in the end; voices are never split, so a count above the voices found leaves them as they are.
- **Auto joins main voices on one track at cosine 0.66 or more**, and folds a little voice into the main voice on its track it is at least 0.2 alike to; a less alike one stays a speaker of its own only with 10 s of speech or more (before that rule, a few voices of a second or two, unlike anyone, stayed as speakers with 0 % of the talk: 2 on the 92-minute meeting, 3 on the other once the worker gave short-turn voices an embedding). 0.62–0.69 gave the same result on both meetings; 0.6 merged the fourth (least talking) person into another and 0.72 split one person into three. Different tracks are still never joined without a count.
- Grouping 114 voices takes milliseconds, so the diarizer's output (turns and voice embeddings per track, about 250 KB for 92 minutes) is kept in `voices.json`, and identifying speakers again with another count or other names regroups without listening again (5–10 minutes for these files on the CPU).
- A speaker heard only in turns under half a second had no voice embedding and could never be joined, so a count could not be reached; such a speaker now gets an embedding from all its turns.
- **Through the real stage** (`TranscriptionCheck identify` on a private copy of the 55:40 meeting, the published worker): the first pass listened for 305.7 s of processor time and found 48 voices; Auto gives **3 speakers** (57.6, 21.2, 21.2 %), and setting Who spoke to 4 regroups from voices.json in 0.3 s into **4 speakers** (57.6, 21.2, 15.9, 5.4 %), History reading "48 voices heard, grouped into 4 speakers (4 expected, this recording)". With other builds loading the PC the pass paused for "PC is busy" four times, and a one-track pass starts its track again after each pause (speakers.partial.json keeps whole tracks only), so it took 75 minutes of wall time; not changed here.

## N. Known voices and suggested chapters, October 2026 (2.0)

Measured on private copies of the owner's two meetings (92 min, one track, 4 people named by the owner on 1,110 of 1,505 lines; 56 min, one track, 4 participants listed, no names given) and on the synthetic two-voice fixtures, with `tools/TranscriptionCheck` (`diarize` once per file, sherpa-onnx 1.13.8, pyannote 3.0 + TitaNet small, threshold 0.8, CPU: 726 s for the 92-minute file, 316 s for the 56-minute one; then `voicematch`, `voicepair` and `chapters` offline). The people are only labels here (A1–A4 by talk time, B1–B3 by the app's Auto grouping).

**Do the two meetings share people?** By voice, no: every B speaker against every A person enrolled from the owner's names is at most **0.38** alike (whole speakers; 0.13–0.38), and 72 thirty-second pieces of B against A's people at most **0.39** (median 0.37). No participant name of one meeting is said in the other's transcript. So the meetings give impostor tries, not a real cross-recording match; the genuine tries below are within one session or synthetic.

| Tries | n | Cosine with the right voice | Cosine with the best wrong voice |
|---|---|---|---|
| A, independent embeddings: each diarizer cluster of a named person (10 s or more) against that person enrolled from their *other* clusters, same session | 12 | min 0.45, median 0.76, max 0.89 | min 0.29, median 0.50, max 0.76 |
| A, 30 s pieces of each person's second half against their first half (shares cluster embeddings, so optimistic) | 55 | min 0.82, median 0.89 | median 0.36, max 0.77 |
| A, different named people, early halves | 6 pairs | — | 0.21–0.71 (the 0.71 pair: a person with 23 s of speech) |
| B pieces against A's people (different meetings, nobody shared) | 72 | — | max 0.39 |
| Synthetic: the same two Windows voices in two different made-up meetings (`speech.ps1 -Script second`) | 2 | **0.963, 0.975** | 0.25–0.28 (the other voice) |
| The same audio imported twice (the e2e) | — | 1.000 | — |

On the independent same-session tries a margin decides more than the threshold: with no margin, 4 of 12 clusters are more alike to another person than to their own other clusters (people in one room on one channel sound alike to the voice model), so a suggestion there would name the wrong person; with a margin of **0.10** none is wrong and 7 of 12 are right, and the threshold (0.50–0.65) changes nothing. Across meetings no stranger came within 0.21 of 0.60.

What this decided (`VoiceMatcher`):

- **Suggest at cosine 0.60 or more, with 0.10 over any other known voice**, one speaker per voice. 0.60 is 0.21 above the highest stranger measured across meetings and below the same-session median (0.76) and both synthetic cross-recording tries (0.96–0.98); the margin is what keeps two alike people apart. Nothing is applied by itself: a wrong suggestion costs a "Not {name}" click.
- **0.50 for a name the recording expects** (Who spoke or participants): still 0.11 above any stranger measured, and the margin still applies.
- **10 s of voiced speech** to enrol or be suggested (the 0.71 different-person pair above involved a 23-s speaker; shorter voices are noisier).
- **The signature is the mean of the newest 10 confirmations** (a capped running mean), so one odd recording moves it a tenth and old ones stop counting. Not measured: real cross-recording tries of the same person (different days, microphones, rooms); the owner's two meetings share nobody. The thresholds should be revisited with such a pair; `voicematch` takes any named transcript and saved speaker job.

**Suggested chapters** (`ChapterSuggester`, `TranscriptionCheck chapters`), on the same two meetings (they have no chapters of their own to compare with, so only shape is reported): 10 chapters for the 92-minute meeting (3.6–18.4 min long, 7 of 9 starts after a pause of 2–46 s or with a new speaker; 5 titled with a topic's label) and 6 for the 56-minute one (2.3–29.3 min; every start with a new speaker; 5 titled with a topic), in 90–100 ms each. Before tuning, a cutoff of the mean depth less half a deviation and one chapter per five minutes gave 12 and 11, with 0.3- and 1.9-minute chapters at the very end and filler titles ("Blah blah", "Bye-bye"); so dips must be deeper than average, there is about one chapter per ten minutes at most, none within half the spacing of either end, fillers and greetings never title a chapter, a verb form ("figuring") counts less unless it is a topic, and no title repeats an earlier one (ignoring a plural "s"). Keyword titles stay rough ("Lose", "Hoping"); they are dotted until accepted and can be renamed. A title written by the local model ("Improve titles") is not built in this version.

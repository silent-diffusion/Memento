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
| Vortice.Direct3D11, Vortice.DXGI, Vortice.DirectX, Vortice.MediaFoundation (§M) | 3.8.3 | MIT |
| SharpGen.Runtime, SharpGen.Runtime.COM (Vortice dependencies, §M) | 2.4.2-beta | MIT |
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

## M. Video capture for 2.0: display, window and camera capture, H.264 fragmented MP4, sync, October 2026

The 2.0 spike (ROADMAP.md, 2.0.0). Reference laptop as above, Windows 11 25H2 (26200), three displays: Display 1 (primary, 1920 × 1080, 60 Hz) and a portrait 1080 × 1920 driven by the RTX 3060, the 1920 × 1080 144 Hz panel driven by the AMD integrated GPU; every display at 100 % scaling; **no camera**. Everything was measured with `tools/VideoCheck` (commands below) on the code that became `src/Memento.Video`, and `tools/e2e/video-seek.mjs` for playback. **The PC was shared with other sessions' builds and tests the whole time: the processor stood at about 99 % load** (as in §K and §L), so frame rates, drops and seek times are contended figures, a worse case than a quiet PC. No sound was played: the sync check times the picture against a generated audio file, and a real audio clock was read from the microphone by counting frames only.

### M.1 Display and window capture (Windows.Graphics.Capture)

- **Interop without extra packages.** The Windows SDK projection of the app's own target (10.0.19041) has `GraphicsCaptureItem`, `Direct3D11CaptureFramePool` and `GraphicsCaptureSession`; the four calls it lacks are reached through COM function pointers (`WinRtInterop`): `IGraphicsCaptureItemInterop.CreateForMonitor/CreateForWindow`, `CreateDirect3D11DeviceFromDXGIDevice`, `IDirect3DDxgiInterfaceAccess.GetInterface` (frame → `ID3D11Texture2D`), and Windows 11's `IGraphicsCaptureSession3.IsBorderRequired`. Direct3D 11 and Media Foundation come from **Vortice 3.8.3** (MIT). No CsWinRT package or newer target was needed, so the app's TFM does not change.
- **The yellow border can be turned off** on this Windows without any prompt for an unpackaged desktop app (`IsBorderRequired = false` accepted; the consent store's `graphicsCaptureWithoutBorder` and `graphicsCaptureProgrammatic` read "Allow"). `IsCursorCaptureEnabled` works from 10.0.19041.
- **Frame rate.** A display delivers a picture only when something on it changes, up to its refresh rate. With an animated window on screen the pool delivered **48–52 pictures/s** on the 60 Hz and the 144 Hz display alike (the loaded processor limited the animation, not the capture); a still screen delivered 1 picture in 10 s. So Memento never relies on the delivery rate: it encodes at a constant frame rate from the latest picture (§M.3).
- **Time stamps.** `Direct3D11CaptureFrame.SystemRelativeTime` is QPC time in 100 ns units, the clock `QpcClock` and the audio tracks use. It is the moment the picture is **shown**: frames arrived 4–13 ms (average 8–11 ms) *before* their stamp. Stamping with it puts the picture where the viewer saw it.
- **Cost of capture alone** (no encoding): 0.3–0.7 % of the processor (all 16 threads = 100 %), the copy into Memento's texture 0.4–3 % of the capturing adapter's 3D engine.
- **Hybrid graphics.** DXGI says which adapter drives which display (`VideoDevice.DisplaysByAdapter`). Capturing the AMD-driven panel on an NVIDIA device works (Windows copies across) and NVENC encodes it; on the AMD device AMD's VCN encodes it. Both are kept possible; Memento creates the device on the adapter that drives the display (`AdapterForDisplay`) so nothing crosses by default.
- **Windows** (`VideoCheck window`, a repainting 640 × 400 window): **minimised**, it delivers no pictures and no event, so the source polls `IsIconic` every 0.5 s and reports "Window minimised" (0.37 s after the minimise); restored, pictures resume within 0.4 s. **Moved** from a display on the NVIDIA card to one on the AMD card, it kept delivering 48 pictures/s without interruption. **Resized** to 900 × 300, the next picture had the new size (the frame pool is recreated at the new size; the encoder letterboxes into the track's fixed size). **Closed**, the capture item's `Closed` event (or the 0.5 s poll) reported it 0.75 s later; the track ends there. A display that is unplugged raises the same `Closed` event; the track then waits for the user to switch to another display.
- **Protected content.** Windows Graphics Capture does not fail for protected content: a window that set a display affinity (`WDA_MONITOR`, `WDA_EXCLUDEFROMCAPTURE`, which players of protected video and some password managers use) is simply black or absent in the capture. Memento checks the affinity when a window is chosen and every 0.5 s while it is captured and **refuses or ends the track as "protected content"** rather than recording black (`AWindowHiddenFromCaptureIsRefusedAsProtected`). Hardware-protected video inside an ordinary browser window (PlayReady SL3000) shows black in a display capture and cannot be detected without reading pixels; not tested (no such content on this PC).
- **Multi-monitor DPI.** Capture items and frames are in physical pixels whatever the process's DPI awareness, and the enumerator reads each display's mode with `EnumDisplaySettings`, so sizes are physical. Every display here is at 100 %, so mixed scaling was **not measured**.

### M.2 Camera (Media Foundation source reader)

**Not measured: this PC has no camera.** The implementation (`CameraCaptureSource`) follows Media Foundation's documented behaviour and is covered by unit tests of its decisions; the hardware test skips without a camera. Choices: the source reader rather than `Windows.Media.Capture.MediaCapture` (same device stack, no WinRT async, shares the Direct3D device through `MF_SOURCE_READER_D3D_MANAGER`, and advanced video processing decodes MJPG and outputs NV12 on the GPU); the format is the largest up to 1080p at ≥ 24 fps, uncompressed preferred at equal size (`CameraFormatChooser`); frames are stamped with the reader's time plus the offset measured at the first frame (to verify against a flash with a real camera). Errors (`CameraErrors`): `E_ACCESSDENIED` or any privacy switch off → camera privacy, naming the switch from the capability access manager's consent store (`HKLM`/`HKCU ...\ConsentStore\webcam`, `NonPackaged` for desktop apps: `CameraPrivacy`); `ERROR_BUSY`, `MF_E_HW_MFT_FAILED_START_STREAMING`, `MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED` → in use, naming the other app from the consent store's entry whose `LastUsedTimeStop` is 0 (`CameraUsage`, what Settings' "recent activity" reads); `MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED`, `ERROR_DEVICE_NOT_CONNECTED` → disconnected, track ends. The maintainer should run `ThirtySecondsOfTheCameraBecomeAPlayableFragmentedMp4` with a camera, unplug one mid-recording, and turn "Let desktop apps access your camera" off once. (After 2.0.0-beta.1 the OBS Virtual Camera, through DirectShow, exercised the camera path from the texture on, §R, and found that the camera texture could not be read by the video processor; the source reader itself is still unmeasured.)

### M.3 Encoding: Media Foundation sink writer, H.264, fragmented MP4

- **Encoders found** (`H264EncoderProbe`): NVIDIA H.264 Encoder MFT, AMDh264Encoder, and Microsoft's software H264 Encoder MFT. Media Foundation's plain enumeration gives no adapter LUID for them, so the encoder is matched to the adapter by vendor; with the device manager set and hardware transforms enabled, the sink writer loads the encoder of the device's adapter. The hardware MFTs report no friendly name through the loaded transform (AMD's) or do (NVIDIA's); a hardware URL or vendor id on the transform tells hardware from software. Opening takes 0.3–1.4 s (NVENC), 0.6–2.7 s (AMD), 0.05–0.1 s (software).
- **Input**: NV12 surfaces from a bounded `IMFVideoSampleAllocatorEx` pool (8), filled by the GPU's video processor from the captured BGRA picture (scale, letterbox, BT.709 limited range). Gotchas: an allocated sample's buffer has a current length of 0 and the sink writer rejects it with `E_INVALIDARG` until it is set; Vortice's `GetStatistics` does not set the struct's size field (also `E_INVALIDARG`), so it is called through the vtable.
- **Settings**: High profile, peak-constrained VBR (`CODECAPI_AVEncCommonRateControlMode` = 1) with the quality's mean and twice it as peak, GOP of 2 s, no B-frames, low-latency mode. Rate control is honoured: busy content lands on the mean (table).
- **Encoder latency** (submit → the sink writer counts it encoded, measured exactly with `--exact-latency`, 1440p30): NVENC **5.7 ms** average (max 9.8), AMD **6.8 ms** (11.7), software **8.4 ms** (16.9; 1080p 4.8 ms). The writer holds each frame until the next (one frame period, 33 ms at 30 fps) to give it its real duration.
- **Cost while capturing a display with an animated window** (20 s, 30 fps unless noted; processor share of all 16 threads; GPU engine utilisation of the encoding adapter):

| Track | Encoder | Processor | GPU | Dropped |
|---|---|---|---|---|
| 1080p30 | AMD VCN (same adapter as the display) | 0.8 % | video codec 8.9 %, 3D 5.7 % | 0 / 601 |
| 1080p30 | NVENC (display on the AMD adapter) | 0.5 % | encode 7.4 %, 3D 0.5 % | 0 / 601 |
| 1080p30 | software | 4.4 % | 3D 7.0 % (conversion) | 3 / 601 |
| 1440p30 | AMD VCN | 1.0 % | video codec 14.9 %, 3D 6.7 % | 0 / 601 |
| 1440p30 | NVENC | 0.4 % | encode 14.1 % | 1 / 601 |
| 1440p30 | software | 8.6 % | 3D 8.1 % | 0 / 601 |
| 1440p60 | NVENC | 0.4 % | encode 24.3 % | 1 / 1,201 |

  The audio capture threads were not affected in any way by design: video runs on its own threads and never takes a lock the audio path uses. With the pacing thread at normal priority under the 99 % load, 1080p60 dropped 67 of 901 frames (7 %); at **above-normal** (still below the audio threads' MMCSS priority) it dropped **0 of 901** with at most 7 ms late wake-ups, so the pacing thread runs above normal.
- **Size per quality.** Screen recordings are mostly still, so a recording of a real display used 1.4–3.0 Mb/s at 1080p30 and 1.9–5.1 Mb/s at 1440p30 whatever the quality (0.6–2.3 GB per hour). The quality only caps busy pictures; the worst case (`--busy`: hundreds of random blocks every frame) gives the ceiling the Settings estimate and the low-disk banner should use:

| 1440p30 | Target | NVENC | AMD | Software | GB per hour (NVENC) |
|---|---|---|---|---|---|
| Smaller | 6 Mb/s | 6.1 | 6.0 | 5.8 | 2.7 |
| Balanced | 12 Mb/s | 11.4 | 11.6 | 11.8 | 5.1 |
| Best | 20 Mb/s | 17.0 | 14.7 | 20.7 | 7.7 |

  Balanced at 1080p30 (7.8 Mb/s target): 7.7 Mb/s, 3.5 GB/h; 1080p60 (13.1): 12.3–13.5 Mb/s; 720p30 (4.2): 4.3 Mb/s, 2.0 GB/h. The software encoder took 1.6–1.9 processor threads for busy 1440p30.
- **Fragmented MP4 as written by Media Foundation** (`MF_TRANSCODE_CONTAINERTYPE = MFTranscodeContainerType_FMPEG4`; no custom byte stream needed): `ftyp`, `moov` with `mvex/trex` first, then a `moof`+`mdat` **every 10 frames** (0.33 s at 30 fps, 0.31 s at 60), no `tfdt`, no `sidx`; a clean `Finalize` appends an `mfra` index in 5–34 ms. **The MP4 sinks store sample durations, not time stamps**: 10 s written with a 3 s gap came out 7.03 s long. The writer therefore holds each frame until the next one and sets its duration from the time stamps; the same file then measured 10.03 s with one 3.33 s frame. Without `tfdt` the file starts at 0, whatever the first time stamp, so the manifest stores the track's `startOffsetMs`.
- **Crash.** Killed with `Environment.FailFast` 12.300 s into a 1440p recording: the file held **12.067 s** (343 of 351 frames), with no torn bytes and no `mfra`. Killed at frame 36,123 of a 20-minute file: 36,120 frames kept (100 ms lost). A file cut mid-fragment by hand (a power loss before the last flush) was repaired by `VideoRecovery`: 21,681 torn bytes cut, 1,951 fragments (650 s) kept, remuxed in 1.2 s.
- **Playback and seeking in WebView2's engine** (headless Edge 155 over the DevTools protocol, the file served with range requests as the virtual host does; `tools/e2e/video-seek.mjs` reads the frame number VideoCheck's synthetic source draws as a barcode):

| File | Seeks within one frame | Seek time median / max | Notes |
|---|---|---|---|
| 20 min fragmented, clean stop (`mfra`) | 30 / 30 | 29 / 545 ms | duration 1200 s known at load, plays from 10:00 |
| **4 h fragmented, clean stop** (432,000 frames, 43,200 fragments, 902 MB) | 40 / 40 | 48 / 682 ms | opens in 2.8 s; plays from 2:00:00 |
| 20 min fragmented, process killed (no `mfra`) | 30 / 30 | 511 / 602 ms | duration known (1204 s) |
| the same, remuxed to an ordinary MP4 | 30 / 30 | 513 / 594 ms | remux of 20 min (75 MB) in 2.3 s |

  The 4-hour file was encoded faster than real time (`--fast`, 372 frames/s with NVENC at 1080p) instead of extrapolated. So **no finalize step is needed for playback** after a clean stop; after a crash the file plays and seeks as it is, and recovery only cuts a torn tail and remuxes for the export's "Original container" and other players. Seek times were measured on the loaded PC and vary with it.
- **Recovery design.** At the next start, every `videoTracks[]` entry in `recording.state.json` is repaired (`VideoRecovery.Repair`: a torn trailing fragment is cut, nothing else is touched) and remuxed (`Mp4Remuxer`, Media Foundation source reader → sink writer pass-through, no decoding) when the export or a part join needs it. Parts written by different encoders (a hardware failure) cannot be joined without re-encoding (`Mp4Remuxer` says so); the Video stage re-encodes them, at about 370–430 frames/s with NVENC at 1080p (an hour of 30 fps in 4–5 minutes).

### M.4 Synchronisation with the audio timeline

- **One clock.** Every video frame is stamped on QPC (the capture frame's `SystemRelativeTime`; a camera's reader time anchored to QPC at its first frame), slots are laid on the recording timeline from the audio session's start, recording pauses rebase it exactly as the audio writer leaves paused time out, and the frame durations in the file carry the stamps (§M.3). A video track is therefore on the QPC timeline by construction; what can still move relative to it is an audio track, whose file time is frames written at the device's own rate.
- **Measured** (`VideoCheck sync --minutes 22`, no sound played): a small window flashed white every 2 s at a scheduled QPC instant for 22 minutes while being recorded through the production path (window capture → pacer → AMD encoder, 320 × 240, 30 fps). The decoded MP4's flashes were timed against a generated WAV holding a click at each scheduled instant on the recording timeline (what an audio track with an exact clock holds). 606 of 658 flashes were found in the file (the loaded processor delayed or dropped some of the test window's own repaints; 73 of 39,556 frame slots were dropped): **video − audio = 34.0 ms in the first 5 minutes and 33.7 ms in the last 5 minutes, drift −1.7 ppm** (0.03 ms over the run), sd 13 ms, minimum 33.2 ms, outliers up to 200 ms when the window's repaint was late. The constant 33 ms is the window being drawn at the next display refresh and appearing in the next frame slot — exactly one frame at 30 fps — not a clock error.
- **A real audio clock.** Over the same 22 minutes the default microphone's frames, counted against their QPC stamps (never stored), ran **−22.8 ppm** slow: 21.6 ms behind QPC after 20 minutes. H1 measured −92.6 ppm for the same microphone over 4 hours (ROADMAP.md, H1). So the residual video-versus-audio drift is the audio device's, not the video's: up to about 0.3–1.3 s over 4 hours for a microphone track, about 0 for process loopback and −5 ppm for system loopback (§C, H1). **Recommendation**: keep video on QPC (it is right), and when the M5 audio drift correction lands (resampling a track onto QPC from the drift the writer already measures at every checkpoint) the two meet; until then Review plays the video slaved to the mix's `currentTime`, so a microphone-heavy mix can lead the picture by its drift (about 0.33 s per hour at the H1 worst case). The checkpoint drift readings should be stored in the manifest so the player can map mix time to QPC exactly.
- **Manifest.** Video tracks join `project.json` as `videoTracks[]` (schema v4), not as `tracks[]` entries with `kind: "video"`: every 1.x reader treats every `tracks[]` entry as audio (mixdown, transcription, hashes), while `videoTracks[]` has been reserved and round-tripped untouched since v1, so a 2.0 project still opens in 1.3 with its audio intact. Each entry still carries `kind: "video"` (`VideoTrack`), its parts with their start offsets, `startOffsetMs` (the file itself starts at 0), duration, frames written and dropped and an end reason; `recording.state.json` gets `videoTracks[]` of `VideoTrackCheckpoint` written at every checkpoint after the flush. The v3 → v4 migration types the entries and drops damaged ones, as v2 did for attachments.

### M.5 Picture-in-picture

No camera here, so a 720p30 synthetic picture stood in for it (two `VideoCheck synthetic --busy` encodes run at once, 20 s each):

| | NVENC encode engine (whole PC) | AMD codec engine (whole PC) | Processor | Dropped |
|---|---|---|---|---|
| Screen 1440p30 alone | 14.1 % | 15.5 % | 0.7 % | 0 |
| Screen 1440p30 + camera 720p30, two encoders at once | 18.1 % | 20.1 % | 0.7 % + 0.7 % | 0 + 0 |

A second encoder session for the camera costs about **4–5 % of the encode engine** and under 1 % of the processor; composing the inset on the GPU before one encoder would save that but costs a video-processor pass per frame (the same 3D-engine work as the conversion, 0.5–7 %), burns the inset into the only copy, and gives the camera the screen's frame rate and compression. DESIGN.md needs the two tracks anyway (Review moves the inset during playback, §18.3; export offers "Two files", §18.4). **Recommendation: record two tracks; draw the inset in the live preview and Review's player; compose only in the export's "One file" option**, which re-encodes at roughly 370–430 frames/s with NVENC at 1080p (§M.3), about 4–5 minutes per hour of 30 fps. Consumer NVIDIA drivers allow several concurrent NVENC sessions (two were used here), so screen and camera together never need the software encoder.

### M.6 Permissions and privacy

- **Screen capture.** On this Windows an unpackaged desktop app captures displays and windows without any prompt and may turn the border off (the consent store's `graphicsCaptureProgrammatic` and `graphicsCaptureWithoutBorder` both "Allow"). An access-denied when creating or starting a capture is reported as "Screen capture is blocked for Memento in Windows privacy settings" with the place to allow it (DESIGN.md §18.6), and audio continues; it could not be provoked here, so the exact Settings page name in that sentence should be checked by the maintainer on a PC where it is turned off.
- **Camera.** Windows gives no prompt to desktop apps; it applies three switches (camera access for the PC, for apps, for desktop apps). Memento reads them before opening the camera and names the one that is off; `ms-settings:privacy-webcam` opens the page. The camera light is Windows' own indicator; Memento opens a camera only while recording or while the Recording screen shows its thumbnail.
- **What Memento says** is in `VideoErrors` and `CameraErrors` (unit-tested sentences): the thing, the time, what is safe ("audio keeps recording", "everything before it is saved"), the fix.
- **Nothing leaves the PC.** No video API used here touches the network; frames, thumbnails and files stay in the project folder; the AI payload composer has no video input at all (ARCHITECTURE.md §8).

### M.7 Commands

`VideoCheck probe` (adapters, which displays each drives, encoders, displays, windows, cameras, camera privacy) · `capture --display N --seconds S [--adapter nvidia|amd] [--animate full|half|small] [--window part-of-title]` · `record` / `synthetic --size WxH --fps F --quality Q [--software] [--busy] [--fast] [--exact-latency] [--gap-at s --gap-length s]` · `crash --after s` / `synthetic --fast --crash-at-slot N` · `inspect --file f` · `recover --file f --out g` · `sync --minutes M --display N` · `window --display N --to-display M`. Playback: `node tools/e2e/video-seek.mjs file.mp4 --seeks 40 [--offset frames]`. Hardware tests: `dotnet test tests/Memento.Video.Tests --filter Category=Hardware` (skipped on CI).

### M.8 In the app: the host side (2.0)

Measured on the reference laptop with the host wired in (ARCHITECTURE.md §5.x items 10–15), the PC again shared with other sessions' builds and tests. No camera; nothing played (simulated audio).

| Check | How | Result |
|---|---|---|
| 20 s of Display 1 (1920 × 1080, 30 fps, Balanced) beside simulated audio, through the coordinator | `OrchestratorHardwareTests` (Hardware category), AMD VCN | the video track starts **633 ms** after the audio (opening the device and the encoder), 603 frames written, 14 dropped (2 %), 20.10 s, 61 fragments with the `mfra` index; its end within 1.5 s of the audio's; video stage done with the hash |
| The same through the published app | `tools/e2e/m8-video.mjs` (bridge only, 25 checks), NVENC | starts **699 ms** after the audio; 631 written, 14 dropped; 1.32 Mb/s for a mostly still desktop; Memento's process **0.8 %** of the 16-thread PC while recording one display and simulated audio; `recording.videoStatus` 20 times in 20 s; the picker thumbnail (GDI, 320 × 180 JPEG) in 81 ms |
| Playback in WebView2 through `library.memento` | the e2e: a muted `<video>` on the page, seven seeks spread over the file, the responses read over the DevTools protocol | the virtual host answers the media element's `Range` request with **206 Partial Content**; length and size right; every seek completed with the picture ready in 18–130 ms |
| Export, MP4 (encoded again, mix as AAC 192 kb/s) | hardware test and e2e | 20.7 s of 1080p30 in **8.8 s** (71 frames/s, AMD, processor-side composition) and 21.7 s in **4.6 s** in the app (NVENC); the progress moved within the file (0, 18, 27 … 99, 100 %) |
| Export, Original container (frames copied, mix as AAC) | hardware test | 603 frames in **0.6 s**; the start offset is kept (MP4 sink edit list), checked with a track that started 0.7 s in |
| Camera inset burnt in | hardware test: the recorded 1080p screen with a synthetic flat-grey 720p "camera" (no camera here) | 247 frames in 3.1 s; the inset's interior reads the camera's grey in every frame where the camera has a picture; black before the screen's first frame |
| Remove video, keep audio | the e2e | 3.65 MB freed, the audio track and the transcript stage untouched, Review's payload says removed |

What this decided or confirmed: video tracks start a little after the audio (0.6–0.7 s here), so `startOffsetMs` is never 0 in practice and the "One file" export shows black for that moment, while Review offsets the player; a pre-opened device and encoder could shorten it (not built). Composition on the processor is fast enough for exports (about a third of real time or better at 1080p30) and keeps the graphics card free for a recording that may be running; 1440p and long recordings were not measured. Not verified: a real camera (thumbnail, track, privacy and in-use messages), switching sources mid-recording on real hardware (unit tests with fakes only), a hardware encoder failing mid-recording and the re-encoded join (unit tests only), recovery after a real crash with video (unit tests with a fake repair; the spike measured `VideoRecovery` itself), and the screen-capture privacy page name.

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

## O. The live transcript while recording, October 2026 (2.0)

Design: ARCHITECTURE.md §5.9. Measured on the reference laptop (16 processor threads, RTX 3060 Laptop) while other agents' builds and test runs loaded it, so every time below is contended and on the slow side.

**First pass, without Small.** Small and Base were not on this PC at first, so the first measurements stood in for the engine:

| Check | How | Result |
|---|---|---|
| Recording four tracks with the live transcript on | `tools/e2e/m7-live.mjs` on the published build: simulated microphone (looping a speech WAV from `speech.ps1`), second microphone, system audio and meeting app; processor time of this run's Memento and its workers over 60 s | Memento 4.6 s of processor time in 61 s: 0.5 % of the PC (0.3 % in a second run). With only Turbo installed the live worker does not start (the card says to install Small or Base), so this is the capture and mix-reading side only. |
| Capture under a live engine's load | The same recording while `TranscriptionCheck live` ran the live worker at the same time with Turbo on the processor, 4 threads (the cap), back to back for 153 s | Every one of the four tracks exactly as long as the recording (61,840 ms of 61,840 ms), no gaps, finalize and the full pass normal. The worker held 24.6 % of the PC (601 s of processor time over 153 s, about 3.9 threads): the thread cap holds. |
| The live worker's windows | `TranscriptionCheck live <wav> --model-file … --threads 4` (the job the app sends, windows read by `LiveMixReader`) | Turbo on the processor: 35–48 s per 10 s window, so far too slow for live use, which is why the live transcript never uses it. The text of each window was right ("Good morning. Let us start with the library screen and the row height.", …). |

**With Small (follow-up).** `whisper-small` was then installed the way the product does it: Settings › Transcription › Install in the published build on a private data root (downloaded from Hugging Face, SHA-256 checked, 487,601,967 bytes), and kept under `artifacts/` for reuse. The same 4-track simulated recording (microphone looping the two-voice WAV, nothing played), 3 minutes, live on, on the processor:

| | As first built | With the fix below |
|---|---|---|
| One 10 s window, `TranscriptionCheck live --threads 4` | 9.4–10.5 s (97 % of the audio time) | 3.2–4.1 s (37 %) |
| Live worker, 3-minute recording (`m7-live.mjs`) | 665 s of processor time = **22.8 %** of the 16-thread PC, busy all the time | 223 s = **7.7 %** of the PC |
| Memento itself (capture, writers, mix reading) | 0.9 % | 0.7 % |
| Windows with lines | 12 of 18 (6 skipped to catch up) | 17 of 18 (none skipped; the 18th was still being heard) |
| Newest line behind the recording (median / max, sampled every 2 s) | 30 s / 46 s | 14 s / 23 s |
| Tracks against the recording | all four exactly as long | all four exactly as long |

**The fix:** Whisper's encoder always works on 30 s of audio, so a 10 s window cost as much as 30 s. The live job now sets the encoder's audio context to 640 frames (12.8 s; 1,500 is 30 s) and turns off the retries at higher temperatures (`WithAudioContextSize`, `WithTemperatureInc(0)`; the full pass is unchanged). 512 frames (10.2 s) was a little faster but made the model repeat a sentence cut at the window's end ("…at 68 pixels, and the row height should be at 68 pixels"); 640 heard the fixture word for word as the full context did.

What the draft looks like: the lines follow the speech closely ("Next, the dark theme. Do we ship it in this release or the next one?"); where a window cuts a sentence, the model sometimes completes it from its own guess ("the colors are done in the dark theme" for "the colors are done and the contrast checks…"), which the full pass after Stop gets right (42 lines, replacing the draft). The newest line runs 10–20 s behind: a window is heard only once it is complete on disk (up to 10 s plus the writers' 1 s flush), then takes about 3.5 s. The window skip (`LiveWindows`) keeps a slower PC from queueing work: the draft gets coarser, never later.

- **The mix reader** costs nothing measurable: file lengths every 500 ms, and one 10 s read of each track per window (4 × 0.5–1 MB).
- **Never saved**: the e2e run checks the project folder after Stop for a draft and finds none; `LiveTranscriptTests` check the same with the scripted worker.
- **Not verified**: Base (Small was measured; Base is only the fallback); the live transcript on the graphics card on real hardware (unit tests only: on the card when allowed and free, gives way to a full pass or a document); yielding to a full pass with the real model in the app (a recording holds the full pass back while "Pause when the PC is busy" is on, the default; unit tests cover a running pass); real WASAPI devices while live runs (the simulated engine was used, as the product owner asked for no sound); Base and a 2-thread PC.

## P. Tesseract for agenda photos and scans, October 2026 (2.0)

Design: ARCHITECTURE.md §1 (OCR row). Tesseract 5.5.1 through the **TesseractOCR 5.5.2** NuGet package (Apache-2.0; `x64\tesseract55.dll`, `x64\leptonica-1.85.0.dll`, which import only the VC++ runtime the worker already ships, the UCRT and system DLLs), with the catalog's `tesseract-eng` (tessdata_fast, LSTM, 4,113,088 bytes, SHA-256 `7d4322bd…70b2`). It runs in **Memento.Worker** as an `ocr` job that stays loaded for one import's passes, so a native failure in Tesseract or Leptonica cannot take down the app or a recording; the worker never decodes a file (the host sends 8-bit gray pixels, wrapped in an in-memory PGM for Leptonica).

**What the measurements changed** (on the synthetic image fixtures in `tests/Memento.Documents.Tests/fixtures`, `TesseractEngineTests.ComparesTesseractWithWindowsOcr`):

- **Text angle sign.** Tesseract's block `DeskewAngle` is already clockwise in radians, as Windows OCR's `TextAngle` (3.5° came back as 0.0627). Negating it, as first written, rotated the 3.5° photo to 7° and read it at 15 % CER with 8 items for 4; with the right sign, 0 %.
- **Paper evened out first** (`GrayImage.NormalizeBackground`: the 90th percentile of each 32 px tile, the brightest of its neighbours, interpolated, every pixel divided by it). The photo-like agenda placed on a white PDF page (a scanned PDF) found **no words at all**: Tesseract's global (Otsu) threshold and page layout took the grey photo on white for a picture. Leptonica's adaptive thresholds (`thresholding_method` 1 and 2) did not help; sparse-text segmentation found 103 "items" of noise in 38 s. With the background divided out it reads the scan like the photo, with automatic page segmentation and nothing else changed.
- **Taller words.** Upscaling stops at a median word box of 32 px for Tesseract (24 for Windows OCR), its best x-height; the 12 px fixture is read at 0 % CER either way.

**Comparison** (three imports of each through `AgendaImporter`, Debug worker build, RTX 3060 Laptop PC with 16 threads; the PC was shared with other agents' builds and a transcription, so the times are on the slow side: an earlier, quieter run had Tesseract's medians at 1.8 s for the clean page, 1.8–2.0 s for the photos and 1.0 s for the small text, Windows OCR's at 0.2–0.5 s):

| Input | Engine | CER | Items | Uncertain | First import | Median |
|---|---|---|---|---|---|---|
| ocr-clean.png (28 px Arial) | Windows OCR | 0.0 % | 5/5 | 0 | 1,133 ms | 739 ms |
| | Tesseract | 0.0 % | 5/5 | 0 | 6,872 ms | 4,026 ms |
| ocr-photo.jpg (warm paper, grey ink, 3.5°, noise, blur, JPEG 80) | Windows OCR | 0.0 % | 4/4 | 0 | 417 ms | 417 ms |
| | Tesseract | 1.9 % | 4/4 | 1 | 3,671 ms | 2,812 ms |
| ocr-photo.png (the same, lossless) | Windows OCR | 0.0 % | 4/4 | 0 | 395 ms | 395 ms |
| | Tesseract | 0.0 % | 4/4 | 0 | 3,405 ms | 3,232 ms |
| ocr-small.png (12 px) | Windows OCR | 0.0 % | 3/3 | 0 | 218 ms | 252 ms |
| | Tesseract | 0.0 % | 3/3 | 0 | 1,556 ms | 1,625 ms |
| ocr-clean.png as a scanned PDF (200 dpi render) | Windows OCR | 0.0 % | 5/5 | 2 | 1,956 ms | 573 ms |
| | Tesseract | 3.3 % | 5/5 | 1 | 1,806 ms | 1,773 ms |
| ocr-photo.jpg as a scanned PDF | Windows OCR | 0.0 % | 4/4 | 0 | 2,641 ms | 2,252 ms |
| | Tesseract | 2.8 % | 5/4 | 2 | 4,168 ms | 3,262 ms |

- **Where Tesseract went wrong**: list markers. "3." read as "~" (photo JPEG: "~ Volunteer rota for the summer", marked uncertain for the stray symbol), "4." as "4" and "5." as "." on the scanned clean page ("? . Close", marked), and the scanned photo's title split over two lines ("Garden Committee / agenda", the second taken for an item). Every misread item it produced was marked uncertain, which is what the review step is for; Windows OCR read every fixture exactly but marks nothing on its own (its two marks on the scanned clean page are the shared misread-character check).
- **Speed**: each Tesseract import starts the worker and loads the language data first, **655–739 ms** of it (an empty 8 × 8 image, three runs), then recognises once per pass (up to three: as decoded, upscaled, straightened). Windows OCR runs in the app with no start-up. Tesseract is 3–8× slower per import, 1–4 s for a one-page agenda on this shared PC: fine for an import the person waits for once.
- **Through the app** (`tools/e2e/m3-flow.mjs --tesseract` on the published build, private data root): Settings › Documents downloaded Tesseract English from GitHub in 1–2 s, its file matched the catalog SHA-256, choosing Tesseract wrote `documents.ocrEngine`, and the photographed agenda was read in 4 s (worker start included) into the four right items, with the caption "read with Tesseract on this PC".
- **Not verified**: languages other than English (the catalog has only `tesseract-eng`, and the engine says so for another language); real camera photos and real scans (only the synthetic fixtures); the Release worker's timing on an idle PC; a Tesseract crash in the field (the error path is tested with a scripted worker failure).

## Q. A quiet microphone beside loud system audio, October 2026 (after 2.0.0-beta.1)

The product owner reported, on 2.0.0-beta.1: with a YouTube video playing (clear system audio) and their own microphone, the microphone was much quieter and **nothing it picked up was transcribed**, while other apps transcribe the same microphone at the same level. Design: ARCHITECTURE.md §5 (Even out source levels) and §6 (Every track at its own level).

**Fixture** (`tools/e2e/levels-fixture.ps1`, from a second script of `sync-fixture.ps1`): two 4-minute 48 kHz tracks of made-up lines in two Windows voices, each with its truth. `system.wav` (stereo, 30 lines) at a −12 dBFS peak with digital silence between lines; `mic.wav` (mono, 18 other lines at other times) at a −38 dBFS peak (−36.8 at 16 kHz) over steady pink room noise at −60 dBFS RMS. On the 16 kHz path the worker hears: system speech −31.2 dBFS active level (100 ms frames up to −24.6); microphone speech −53.9 dBFS active level over a −61.6 dBFS noise floor, its 100 ms frames between −56 and −50 dBFS (p90 −55.7, max −49.8), so about 6–8 dB over its room noise. 10 of the 18 microphone lines overlap system speech by more than 30 %. Measured with `TranscriptionCheck levels <folder>` (the two WAVs as two tracks of one recording through the real transcript, speakers and topics stages, the real worker; coverage as in §K: the share of true speech with a word of that track within 1 s) on the reference laptop (RTX 3060 Laptop, Vulkan; the card shared with other sessions' jobs, so times are contended).

**Where the speech was lost.** (a) The absolute bars, alone. The full pass transcribes **each track separately** (`TranscriptStage` sends every track to the worker as its own `WorkerTrack`; only a "keep only the mix" recording is transcribed from the mix), so the loud track could not mask the quiet one there. The worker's energy pass called a 100 ms frame speech only above −40 dBFS (and 3× the noise floor) and sound only above −50 dBFS: not one microphone frame reached −49.8 dBFS, so the track had 0 s of speech regions and 0 s of sound regions, counted as **silent** ("Skipped Microphone · The track was silent the whole time") and never reached the engine. (b) Masking applies to the **live draft only**, which hears one mix of all tracks (`LiveMixReader`). (c) Whisper's own sensitivity was not the problem: with the bars fixed and no normalization, both models heard every line at −54 dBFS (word recall 98.2 % Turbo, 97.7 % Small), and still did at a −50 dBFS peak (−66 dBFS speech over −72 dBFS noise: 97.7 % both); normalization lifted word recall to 98.6 % on both fixtures.

| Fixture, model | Microphone before (1.3.0 bars) | Bars relative to the track, no normalization | After (bars + per-window normalization) | System audio, before → after |
|---|---|---|---|---|
| −38 dBFS mic, Large v3 Turbo | 0.0 % coverage, 0 of 18 lines, track skipped as silent; 0 s of speech and 0 s of sound regions (52.9 s of its speech cut) | 100.0 %, 18 of 18, recall 98.2 % | **100.0 %, 18 of 18, recall 98.6 %** | 99.9 %, 30 of 30 → 99.9 %, 30 of 30 (recall 98.9 %) |
| −38 dBFS mic, Small | 0.0 %, 0 of 18, skipped | 100.0 %, 18 of 18, recall 97.7 % | **100.0 %, 18 of 18, recall 98.6 %** | 99.9 %, 30 of 30 → 99.9 %, 30 of 30 |
| −50 dBFS mic (−72 dBFS noise), Turbo / Small | (skipped, as above) | 100.0 %, recall 97.7 % / 97.7 % | 100.0 %, recall 98.6 % / 98.6 % | unchanged |

After the fix the energy pass finds 40.0 s of speech regions (75.5 % of the microphone's true speech; the coverage check and the silent-track rule use these) and 46.3 s of sound regions, which keep 99.7 % of its speech through the silence shortener (0.1 s cut; before: all 52.9 s). Speakers: the microphone's two voices and the system audio's two voices stay four separate speakers (different tracks stay different people, as before).

**Timing (§K) unchanged.** System-audio line start against voice start: median 0.00–0.01 s, p90 0.01 s, max 1.08 s before and after (the one line at 1.08 s is the same in the 1.3.0 run). Microphone lines: median 0.04 s, p90 0.15 s, max 0.18 s (its sound regions start a little later at 7 dB SNR). `TranscriptSyncHardwareTests` (the §K synthetic recording, every line after 2:00 within 100 ms) passes with Large v3 Turbo **and with Small** (not measured with the new chunking before).

**Speed.** The per-window normalizer measures and scales a copy of the window: 110–116 ms for 240 s of audio (Debug build), well under 1 % of a window's engine time; the relative bars sort the frames once more per track (milliseconds). The full pass got longer only because the microphone track is now transcribed at all: Turbo 18.1 s for 4:00 of tracks before (one track heard), 20.7–21.5 s for 8:00 after; Small 11.5 s → 14.7–15.1 s (all on the shared card).

**What changed** (ARCHITECTURE.md §5, §6): `SpeechEnergy`'s bars follow each track's noise floor (speech ×2, sound ×1.6, was ×3 and ×2) and its speech level (`SpeechLevel`: the RMS of 100 ms frames 6 dB over the noise floor, the floor being none where a tenth or more of the audio is digital silence): the 1.3.0 bars (−40 / −50 dBFS) for speech at −20 dBFS or louder, 20 / 30 dB under the speech level below that, never under −70 / −75 dBFS. Steady noise alone at any level, digital silence and a −77 dBFS blip stay silent. `LoudnessNormalizer` brings each window's active level to −20 dBFS (−10 to +30 dB, tanh limiter above 0.9, no gain without speech) for the engine and, with one gain per track, for the diarizer, and per track per window in the live mix.

**The live draft** (Small, processor, 4 threads, `TranscriptionCheck live system.wav,mic.wav`, lines matched when a draft line has half the true line's words): before, system audio 27 of 30 and microphone 4 of 18 lines (the microphone alone: 16 of 18); after normalizing each track before the sum, 26 of 30 and 7 of 18. One engine pass hears one talker at a time, and 10 of the 18 microphone lines are spoken over the system audio, so the draft keeps mostly the 8 that are not; the full pass after Stop transcribes each track on its own and has all 18. Lifting the microphone only where it speaks (its hiss left at −60 dBFS) gave 4 of 18 and was not kept. Transcribing each track separately live would roughly double the live worker's processor time (§O: 7.7 % of the PC with Small), so it was not done.

**The mix** ("Even out source levels", on by default): each track's speech level against its own noise floor; a track more than 3 dB under the loudest is lifted to it, by at most 24 dB, never lowered. In `m9-levels.mjs` (published build, simulated engine looping the two fixture WAVs, 150 s): microphone −52.4 dBFS, system audio −31.1 dBFS (48 kHz, both channels) → microphone +21.3 dB, system 0 dB; `MediaFoundationTrackFinalizerTests`: a −37 dBFS microphone beside −15 dBFS system audio sits within 3 dB of it in `mix.flac` (22 dB under without the setting), the track's own FLAC unchanged. Rebuilding the mix (as recorded, then evened, and back) replaces `mix.flac` and `peaks.json` and the player loads the new file (`mix.flac?v=<hash>`).

**The Record screen hint**: from `recording.levels` peaks over the last 10 s, judged once a second: the source speaks (its 95th-percentile peak 10 dB over its 10th) and peaks under −30 dBFS or 20 dB under another speaking source; it stays until the speech reaches −26 dBFS and comes within 16 dB. On the fixture it read "Microphone is quiet (−40 dB). …" 4 s into the recording, under the microphone only, with no toast.

- **Not verified**: real microphones and real system audio (simulated engine and file fixtures only, as no sound may be played here); non-English speech; a microphone under about 6 dB over its room noise (the bars then find little speech, and a track with under 1 s of it is still skipped as silent); keyboard clicks or other non-speech bursts on a quiet microphone (counted as speech and lifted like it); "keep only the mix" recordings made before this change (their mix, and so their transcript, keeps the old balance; they cannot be rebuilt); a recording whose tracks were kept as multi-part WAV (the rebuild reads the first part only).

## R. DirectShow cameras: the OBS Virtual Camera, October 2026 (after 2.0.0-beta.1)

The product owner tried the OBS Virtual Camera on 2.0.0-beta.1 and Memento did not list it. OBS's virtual camera (and NDI's, Snap Camera's, Camo's on some versions, and DirectShow-only capture cards) is a **DirectShow video input filter** registered under `CLSID_VideoInputDeviceCategory`, not a Media Foundation capture device, so `MFEnumDeviceSources` never returns it: on the reference laptop (no webcam, OBS Studio 32.2.2 installed) Media Foundation lists no camera and DirectShow lists one, "OBS Virtual Camera" (`CLSID {A3FCE0F5-3493-419F-958A-ABA1250EC20B}`, no device path). Windows exposes every Media Foundation camera to DirectShow too, never the other way round.

**Design** (ARCHITECTURE.md §5.x item 1):

- **Listing** (`DirectShowDevices`, `CameraListMerger`): the system device enumerator's monikers give each device's `FriendlyName`, `DevicePath` (real devices) and `CLSID` (filters); nothing is loaded, so no camera starts. A device in both lists is kept once, from Media Foundation, matched by its device instance (the interface path without its interface class: Media Foundation and DirectShow give the same camera different class GUIDs) or, for a DirectShow entry without a path, by name. Ids: `camera:dshow:<device path>`, `camera:dshow:clsid:{…}` for a filter, `camera:dshow:name:<name>` otherwise, with `#2` for repeats; stable while Memento runs. `VideoSource.backend` is `mf` or `dshow`; the page notes "via DirectShow".
- **Interop**: hand-written `[ComImport]` interfaces for the system device enumerator, property bag, filter graph, capture graph builder, `IAMStreamConfig`, `IEnumPins`/`IPin`, `IMediaControl`/`IMediaEvent` and qedit's sample grabber, with `ISampleGrabberCB` implemented in C# (no package; DirectShow is part of Windows). Every call runs in the MTA (`ComApartment`): the OBS filter refused calls from the STA it was not created in (`InvalidCastException` on `RemoveFilter`). Two RCW traps: a device moniker is its own property bag, and the OBS filter is its own `IAMStreamConfig`, so releasing the "second" object released the first; neither is released separately now.
- **Graph** (`DirectShowGraph`): device filter → sample grabber → null renderer, no reference clock (pictures go through as they arrive). The capture pin's format comes from `IAMStreamConfig` through `CameraFormatChooser` (largest up to 1080p at ≥ 24 fps, uncompressed preferred); the grabber asks for that subtype when Memento reads it, else NV12, YUY2, RGB32, RGB24 in turn, so DirectShow inserts its MJPEG decoder for an MJPG camera. Connecting the first output pin straight to the grabber took the start from **about 1.1 s** (`ICaptureGraphBuilder2.RenderStream` with `PIN_CATEGORY_CAPTURE`) to **140–230 ms** to the first picture; `RenderStream` remains the fallback. `EC_DEVICE_LOST`, `EC_ERRORABORT` and `EC_COMPLETE` end the source as disconnected; `VFW_E_CANNOT_CONNECT`, `VFW_E_NO_ACCEPTABLE_TYPES` and `VFW_E_TYPE_NOT_ACCEPTED` say the format can't be read.
- **Frames** (`DirectShowCameraSource`): the grabber's `BufferCB` runs on DirectShow's streaming thread and only copies the picture into a `RawFrameQueue` of three buffers (one being converted, two waiting); with none free the picture is dropped and counted, so the camera never waits for Memento. A conversion thread (above normal, like the Media Foundation camera's thread) turns NV12, I420/IYUV, YV12, YUY2, UYVY, RGB24 and RGB32 (bottom-up or not) into NV12 (`RawFrameConverter`: YUV values kept, 4:2:2 chroma averaged over two rows, RGB with the BT.709 limited-range matrix) and uploads it into the source's NV12 texture, which the pacer reads as for any source. Pictures are stamped on QPC when they arrive (virtual cameras' own sample times are not trustworthy, and the graph runs without a clock); the arrival-to-texture delay is the source's latency figure. No picture for 5 s ends the source as disconnected.
- **A virtual camera's app stopping** is invisible to DirectShow: OBS's filter keeps delivering a placeholder picture at the same rate. `VirtualCameraWatch` reads the header of the shared-memory queue OBS writes (`OBSVirtualCamVideo`: write index at byte 0, state at byte 8, 2 = ready, 3 = stopping), opening it read-only for each check every 0.5 s and closing it at once so OBS can always recreate it: not ready, missing, or a write index that has not moved for 2 s (OBS closed or crashed) means stopped. A track does not start while OBS is not sending ("OBS Virtual Camera is not started in OBS, so it has no picture to record. … Start the virtual camera in OBS, then retry the camera."), and ends cleanly when it stops ("OBS Virtual Camera stopped at 0:42: OBS is no longer sending it a picture. The camera track ends there and everything before it is saved; …"; `VideoCaptureException.AtPosition` lets the source word it with the session's time). A thumbnail shows whatever the filter shows, OBS's placeholder included.
- **Thumbnails** (`CameraStills`): the same graph at up to 1280 × 720 into a two-buffer queue; each request takes the newest picture; the 3 s keep-open rule applies, and every held camera is closed when the app closes.
- **Two faults found on the way**, both in code that had never run because the reference PC has no camera: (1) the camera texture (the Media Foundation path's too) was a shader resource only, and `CreateVideoProcessorInputView` refuses it with `E_INVALIDARG`, so the first camera frame failed the writer as a "hardware encoder failure"; camera textures are now render targets like screen textures. (2) With the OBS filter still running at exit (a thumbnail held open while OBS was not sending), Memento's process did not exit for more than 30 s after its window closed; closing the thumbnail cameras when the host is disposed fixed it (exit in about 2 s).
- **Risk kept**: a DirectShow filter is third-party code loaded into Memento's process (a Media Foundation camera runs in Windows' Frame Server service), so a faulty filter can crash the app that captures the audio. It is loaded only while that camera is chosen or recorded. Running DirectShow capture in the worker process (frames through shared memory) would isolate it; not built.

**Measured** on the reference laptop (16 threads; the PC shared with another session's builds and tests, so the processor figures are contended), with a **portable copy of OBS 32.2.2** started for the test (`obs64 --portable --startvirtualcam --minimize-to-tray`, its own settings: 1280 × 720, 30 fps, NV12, a scene of two still pictures alternating every 0.5 s, no audio source), never the person's own OBS settings; the copy was deleted afterwards. Nothing was played.

| Check | How | Result |
|---|---|---|
| Listing | `VideoCheck probe`, `ObsVirtualCameraTests` | Media Foundation: none; DirectShow: OBS Virtual Camera; merged: one camera, `dshow` |
| Format | `VideoCheck capture --camera OBS` | the pin's NV12 1280 × 720 at 30 fps, read as it is (no conversion beyond the copy) |
| Start to first picture | the same, three runs | 137–226 ms (with `RenderStream`: 925–1,149 ms) |
| Delivery | 10 s capture | 29.3–29.6 pictures/s (30–33 in each second), 0–1 dropped at the hand-over, 0 unreadable; arrival to texture 1.1–2.3 ms average, 14–123 ms worst |
| Cost of the camera alone | the same | 1.1–1.3 % of the PC (copy, upload, VideoCheck), its 3D engine 0.3 % |
| 20 s recorded, VideoCheck | `record --camera OBS --size 1280x720` and `1920x1080`, AMD VCN | 601 written, 0 dropped either way; 1.5 % of the PC; the saved still is OBS's picture |
| 20 s beside simulated audio, coordinator | `TwentySecondsOfTheVirtualCameraRecordBesideTheAudio` (AMD VCN, 1080p) | starts 1.57 s after the audio, 608 frames, 36 dropped (the slots before the first frame, as for screens), the video stage done, the decoded picture not black; the test process 2.1 % of the PC |
| 20 s in the published app | `tools/e2e/m8-camera.mjs` (NVENC, 1080p) | starts 1.33 s after the audio, 645 written, 18 dropped; Memento **2.2 %** of the PC; 0.22 MB for 21.5 s (two still pictures); WebView2 plays and seeks it through `library.memento` |
| Thumbnails | the e2e (320 px, then 144 px once a second) | first 86–172 ms (opens the graph), then 16–52 ms |
| OBS stops mid-recording | the e2e with `--stop-obs-after 8` (OBS's own WM_CLOSE, `obs-close.ps1`) | `recording.videoProblem` `ended` 1.5 s after the close was asked (OBS's shutdown included), "OBS Virtual Camera stopped at 0:10: …"; track 10.1 s, `endReason` `sourceLost`, playable with its `mfra` index; the audio recorded on. `VideoCheck capture` saw the same loss when OBS closed mid-capture |
| OBS installed, not sending | the e2e with OBS closed | the thumbnail is OBS's placeholder; the camera track is refused with "not started in OBS"; the audio records |

**Not verified**: a real webcam or capture card through DirectShow (device-path ids, the privacy check, `EC_DEVICE_LOST` on unplugging, in-use errors), MJPG through DirectShow's decoder and YUY2 or RGB from a real device (OBS sends NV12; the conversions are unit-tested), other virtual cameras (NDI, Snap Camera, Camo), OBS crashing rather than closing (the stalled write index is unit-tested only), and the processor cost of converting a 1080p RGB or YUY2 camera.

Commands: `VideoCheck probe` (both lists), `VideoCheck capture --camera "OBS" --seconds 10 [--png still.png]`, `VideoCheck record --camera "OBS" --seconds 20 --out x.mp4`, `dotnet test tests/Memento.Video.Tests --filter "Category=Hardware&FullyQualifiedName~ObsVirtualCamera"` (skipped unless OBS's virtual camera runs), `node tools/e2e/m8-camera.mjs [--stop-obs-after 8 --obs-pid <pid of an OBS started for the test>]`.

## S. Choosing the cloud model, October 2026 (after 2.1)

Which model ids Settings lists, where they come from, and what is sent with each (`AiModelDefaults`, BRIDGE.md "Cloud models").

| Provider | Model | Context / answer | Effort | Server-side fallback | Source |
|---|---|---|---|---|---|
| Claude | `claude-opus-5-5` (default) | 1M / 128k | `high` (its own default is `medium`) | `"default"` | claude-api skill, model table cached 2026-10-06 |
| Claude | `claude-fable-5-1` | 1M / 128k | `high` | `"default"` | same; Anthropic's top tier, always thinks, $10/$50 per MTok against Opus 5.5's $4/$20, needs 30-day data retention (a zero-data-retention organisation gets a 400) |
| Claude | `claude-sonnet-5-5` | 1M / 128k | `high` | `"default"` (Claude API only) | same; $2/$10 |
| Claude | `claude-haiku-5-5` | 1M / 128k | `high` (its default is `medium`) | none: Claude Haiku 5.5 refusals have no server-side fallback, so `fallbacks` is not sent | same; $0.10/$0.50 up to 100k tokens |
| ChatGPT | `gpt-6-astra` (default) | 1.05M (922k in) / 128k | `high` | n/a | developers.openai.com/api/docs/models/gpt-6-astra, read 2026-10-12 |
| ChatGPT | `gpt-6.1-sol` | 1.05M (922k in) / 128k | `high` (low to max; no `none`) | n/a | developers.openai.com/api/docs/models/gpt-6.1-sol, read 2026-10-12 |
| ChatGPT | `gpt-6-luna` | 1.05M (922k in) / 128k | `high` (none to max) | n/a | developers.openai.com/api/docs/models/gpt-6-luna, read 2026-10-12 |

OpenAI's model index (developers.openai.com/api/docs/models; platform.openai.com/docs/models redirects there) lists these three as its flagship text models; the others on that page are specialised (cybersecurity, image, realtime, speech, transcription) and are not offered. All three take structured outputs on the Responses API, which is what the provider uses.

- **A model typed by hand** is sent exactly as typed, with a profile every model accepts: no effort and no `fallbacks` (Claude Haiku 4.5 rejects effort; an unknown model may reject the fallback beta), an assumed context of 200k for Claude (Claude Haiku 4.5's, the smallest current one) and 128k for ChatGPT (GPT-4o's), and an answer limit of 32,000 (Claude) or 16,384 (ChatGPT) tokens.
- **Context.** Every listed model takes about a million tokens, so the choice changes no size note in practice; the product owner's example of a 200k Haiku is Claude Haiku 4.5, not Claude Haiku 5.5. A cloud request carries at most 24,000 transcript tokens, less when a quarter of the model's context is smaller: a long transcript is split into segments rather than refused. A request's answer limit is the pass's limit plus 16,000 tokens of thinking headroom, capped at the model's answer limit.
- **A model the service does not offer.** Anthropic answers an unknown model with HTTP 404 `not_found_error` ("model: claude-x"); OpenAI with 404 and code `model_not_found` ("The model `x` does not exist or you do not have access to it."). Both, and a 400 whose message names the model as missing, are `ai.unknownModel`; a 404 without a JSON error (a proxy, a wrong address) is not. Nothing is retried and no other model is tried. Tested against the fake servers only (`CloudModelRequestTests`, the e2e's fake Messages API); no real key or request was used.

## T. Larger local models across the graphics card and RAM, October 2026 (after 2.2)

The product owner asked for "larger models like Qwen 3 27B", run with the computer's RAM beside a small graphics card, without getting in the way of what the rest of the app needs from the card or the RAM, and for mixture-of-experts models too. Measured on the reference laptop: RTX 3060 Laptop (6 GB; DXGI 5,994 MiB, 5.1 GB free when idle), Ryzen 7 5800HS (8 cores, 16 threads), **40 GB of DDR4-3200** (39.4 GB visible to Windows, read with `GlobalMemoryStatusEx`; a 1.6 GB page file, so the commit limit is 42 GB). The PC was not idle: other resident programs used 16–18 GB of RAM throughout (20–24 GB available before a load), and from 14:34 a dictation app's bundled Ollama loaded its model and a Python speech server onto the card (4.1 + 1.2 GB), leaving 0.6 GB free for a long stretch (§J describes the same app). Tools: a spike console over LLamaSharp 0.27 (one load per process, the synthetic meeting of §I.2 as a 3,000-token prompt in the Qwen3.5 template, 128 tokens generated at temperature 0, memory read from PDH and `GlobalMemoryStatusEx`), then `LargeModelPipelineHardwareTests` (the real worker, the planner's placement, Meeting minutes and the fixed verification set). Models were downloaded through Memento's own model manager into a private data root (SHA-256 checked) and removed afterwards.

### T.1 What "Qwen 3 27B" is, and the candidates

| Model | Hugging Face (GGUF / original) | Kind | Q4_K_M file | License | Measured here |
|---|---|---|---|---|---|
| **Qwen3.5-27B** | unsloth/Qwen3.5-27B-GGUF / Qwen/Qwen3.5-27B | dense, 64 blocks (48 linear-attention + 16 full-attention), hidden 5,120 | 16,740,812,704 B (15.6 GiB) | Apache-2.0 | yes |
| **Qwen3.5-35B-A3B** | unsloth/Qwen3.5-35B-A3B-GGUF / Qwen/Qwen3.5-35B-A3B | mixture of experts: 40 blocks, 256 routed experts (8 active) + 1 shared, ~3B active | 22,016,023,168 B (20.5 GiB) | Apache-2.0 | yes |
| Qwen3-32B | unsloth/Qwen3-32B-GGUF / Qwen/Qwen3-32B | dense, 64 blocks | 19,762,150,048 B (18.4 GiB) | Apache-2.0 | estimated |
| Qwen3-30B-A3B-Instruct-2507 | unsloth/Qwen3-30B-A3B-Instruct-2507-GGUF | mixture of experts, 48 blocks, 128 experts (8 active) | 18,556,686,752 B (17.3 GiB) | Apache-2.0 | estimated |
| gpt-oss-20b | unsloth/gpt-oss-20b-GGUF / openai/gpt-oss-20b | mixture of experts, 24 blocks, 32 experts (4 active), MXFP4 | 11,624,759,488 B (10.8 GiB, "Q4_K_M" is MXFP4 underneath) | Apache-2.0 | estimated |

"Qwen 3 27B" is Qwen3.5-27B: Qwen3 has no 27B (its dense models are 14B and 32B); Qwen3.5 has a 27B dense model beside the 35B-A3B mixture of experts. Both Qwen3.5 models share the Qwen3.5-4B tokenizer and chat format, so the catalog's verified `qwen3.5` template applies; their GGUF chat templates differ from the 4B's only in the default (thinking on unless `enable_thinking` is false), and Memento's rendering is exactly the `enable_thinking: false` one (an empty think block). Qwen3-32B and Qwen3-30B-A3B are the previous generation of the same two shapes and are left out; gpt-oss-20b would need a verified Harmony template and its reasoning channel handled (§H trap 6), so it is left for later. Sizes and SHA-256 from the Hugging Face API (`/api/models/{repo}/tree/main`, read 2026-10-21); licenses from each model card.

Exact weights from the GGUF tensor table (`tools` in the spike, read-only):

| Model | Blocks (non-expert) | Routed experts | Output layer | Token embeddings (stay in RAM) | KV cache (f16) |
|---|---|---|---|---|---|
| Qwen3.5-27B | 64 × 223.1 MiB | — | 994.6 MiB | 682.0 MiB | 64 KiB a token (16 attention blocks), 1 GiB at 16k |
| Qwen3.5-35B-A3B | 40 × 37.8 MiB | 40 × 464.0 MiB (18,560 MiB) | 397.9 MiB | 515.3 MiB | 20 KiB a token (10 attention blocks), 320 MiB at 16k |

### T.2 What LLamaSharp 0.27 can do

- **Partial offload**: `GpuLayerCount` (n_gpu_layers) works as in llama.cpp: the last blocks go on the card, the output layer counts as one more.
- **Experts in RAM**: `ModelParams.TensorBufferOverrides` (llama.cpp's `--override-tensor`) works on the Vulkan build with the buffer type `"CPU"`: the pattern `\.ffn_(up|down|gate)_exps` keeps every block's routed experts in RAM while attention, the shared expert and the output layer go on the card; `^blk\.(0|1|…|n-1)\.ffn_(up|down|gate)_exps` keeps only the first n blocks' (this is all `--n-cpu-moe n` does). There is no `n_cpu_moe` convenience; Memento writes the pattern. llama.cpp warns "tensor overrides to CPU are used with mmap enabled"; with mmap off the experts would be private memory (20 GB of commit on a PC with a 42 GB commit limit), so mmap stays on.
- **Flash attention**: `ModelParams.FlashAttention` (`bool?`) works on Vulkan: with every expert in RAM the card's compute buffer is 501 MiB with it and 852 MiB without, at the same speed (tg 12.4 vs 12.9), so planned loads turn it on.
- **KV placement**: the KV cache of offloaded blocks goes on the card, of the others in RAM (`NoKqvOffload` would move it all to RAM; not used). **mlock** (`UseMemoryLock`) stays off: locking 20 GB would take it from the rest of the PC for good.
- **Op offload** (`OpOffload`, on by default): prompt reading copies the experts held in RAM to the card in 512-token batches: 99–104 prompt tokens/s with it, 50 without; kept on.
- **Not exposed**: `use_extra_bufts` (weight repacking for the CPU build) is only on the native `LLamaModelParams`; `IModelParams` has no field. Memento loads a larger model in RAM through `SafeLlamaModelHandle.LoadFromFile` with it off and wraps the handle with `LLamaWeights`' non-public constructor (`LlamaWeightsLoader`; a unit test pins that the constructor is there in this build). `llama_params_fit` (llama.cpp's own fitting) is exported but plans as if RAM were unlimited and changes the global logger, so Memento plans itself.

### T.3 Qwen3.5-35B-A3B (mixture of experts), spike

One load per row; 16k context, f16 KV, 8 threads, ubatch 512, flash attention on unless noted; "RAM" is the drop in available RAM over the run; "card" the process's dedicated video memory (PDH).

| Placement | Prompt tok/s | Output tok/s | Card | RAM | Load | Note |
|---|---|---|---|---|---|---|
| Every expert in RAM, the rest on the card | 102.7 | **12.35** | 3.13 GB | 19.6 GB | 7.4 s | 8.2 GB still available afterwards |
| Experts of 36 of 40 blocks in RAM (4 on the card) | 104.3 | 12.78 | 5.11 GB | 18.9 GB | 10.4 s | the card full to 20 MB |
| Every expert in RAM, flash attention off | 94.1 | 12.86 | 3.61 GB | 18.5 GB | 7.8 s | compute buffer 852 MiB |
| Every expert in RAM, op offload off | 50.4 | 11.87 | 3.05 GB | 17.4 GB | 9.3 s | |
| Every expert in RAM, 16 threads | 99.4 | 10.78 | 3.30 GB | 18.5 GB | 10.5 s | 3.4 GB available afterwards |
| Every expert in RAM, ubatch 2048 | 65.0 | 10.22 | 4.72 GB | 19.0 GB | 11.8 s | **spilled 1.5 GB** (compute buffer 2,464 MiB) |
| Plain layer offload, 16 of 40 blocks | — | — | — | — | — | weights failed to load: 16 blocks with their experts are 8 GB |
| Plain layer offload, 7 of 40 blocks | 103.9 | **4.64** | 4.22 GB | 19.3 GB | 9.7 s | slower than RAM alone |
| RAM alone, CPU build | 25.8 | 6.38 | 0 | **22.5 GB** | 39.2 s | the CPU build repacked 11.5 GB more; 0.2 GB left available |
| RAM alone, CPU build without repacking | 27.1 | **6.11** | 0 | 18.7 GB | 5.6 s | private memory 1.6 GB instead of 12.9 GB |

- **Experts in RAM is the placement for a mixture of experts**: 12.4 tokens/s, twice RAM alone and 2.7 times plain layer offload, with 3.1 GB on the card. Each token reads only 8 of 256 experts (about 0.6 GB from RAM) while the card does attention and the shared expert. Bringing 4 blocks' experts onto the card gains 3 % and fills the card.
- **Plain layer offload is the wrong tool for a mixture of experts**: a block carries its 464 MiB of experts, so only 7 blocks fit and every token still reads the other 33 blocks' active experts from RAM through a split graph.
- **The CPU build's repacking doubles the RAM**: it keeps an interleaved copy of every Q4_K tensor beside the mapped file. For the 35B that is 11.5 GB more and left 0.2 GB available on this PC, for no speed (6.38 vs 6.11 tokens/s).
- **ubatch 2048 spills**: the compute buffer grows to 2.4 GB; 512 stays.

### T.4 Qwen3.5-27B (dense), spike

Same settings (2,000-token prompt, 64 tokens written). The card was free for the split rows; one earlier split row (10 blocks) was taken while the dictation app had loaded its model, spilled 3.5 GB to shared memory and is left out.

| Placement | Prompt tok/s | Output tok/s | Card | RAM (working set) | Load |
|---|---|---|---|---|---|
| RAM alone, CPU build without repacking, 8 threads | 3.6 | 1.41 | 0 | 16.4 GB | 6.9 s |
| RAM alone, CPU build without repacking, 16 threads | 4.5 | 1.26 | 0 | 16.4 GB | 3.2 s |
| Output layer + 9 of 64 blocks on the card | **52.5** | 1.41 | 3.76 GB | 16.2 GB | 9.3 s |
| Output layer + 13 blocks on the card | 37.9 | 1.53 | 4.68 GB | 16.2 GB | 17.1 s |
| Output layer + 16 blocks on the card | 36.3 | **1.67** | 5.34 GB | 16.2 GB | 10.6 s |

- **A dense 27B writes at RAM speed whatever is on a 6 GB card**: three quarters of its 15.6 GB stay in RAM and each token reads all of it (1.4–1.7 tokens/s, about 18 GB/s of RAM). The card's real gain is **prompt reading**: llama.cpp hands the large prompt matrices to the card (op offload), 36–52 tokens/s against 4 in RAM alone, so even a few blocks on the card cut a document from about 6 hours to about an hour. Fewer blocks on the card read prompts slightly faster here (less to synchronise).
- The repacked CPU build could not be measured for the 27B (its copy would need about 31 GB); for the 35B it changed nothing (§T.3).

### T.5 The whole document pipeline

`LargeModelPipelineHardwareTests`: Meeting minutes of the 20-minute synthetic meeting (2 chunks of 3,000 tokens at 16k context, 28–29 requests, one model load), then the spike's fixed verification set (14 true, 6 planted false claims), through the real worker with the planner's placement; the same measures as §I.2.

| Model and placement (planned → loaded) | Pipeline | Map / verify / load | Writing speed | Recall / precision (14 decisions and actions) | Fixed set | Card / RAM while writing |
|---|---|---|---|---|---|---|
| Qwen3.5 4B, all on the card (§I.2, card nearly free) | 152 s | 92 / 59 / 4 s | 40–56 tok/s | 0.93 / 1.00 | 20/20 (6/6) | 3.4 GB / — |
| **Qwen3.5 35B-A3B**, experts of 37 of 40 blocks in RAM (as planned) | **1,054 s (17.6 min)** | 716 / 333 / 36 s | 11.2 tok/s | **0.86 / 1.00** (missed U2, U3) | 19/20 (6/6) | 4.66 GB (planned 4.47) / 20.8 GB working set (planned 17.8) |
| **Qwen3.5 27B**, output layer + 12 of 64 blocks on the card (as planned) | **3,870 s (64.5 min)** | 3,029 / 837 / 34 s | 1.39 tok/s | **0.79 / 1.00** (missed U1, U2, U3) | 19/20 (6/6) | 4.75 GB (planned 4.73) / 17.5 GB working set (planned 14.7) |

- **Quality**: on this meeting the larger models are not more accurate than Qwen3.5 4B (the hardware test now asserts the measured 11 of 14, precision 1.00 and every planted claim caught). The 27B missed U1 as well. The 35B missed U2 (which the 4B misses too, §I.2) and U3, both action items said in passing, kept no wrong claim and no parked item as a decision, and the verifier caught every planted claim but rejected one true claim. The pipeline's prompts were tuned on Qwen3.5 4B (§I.2) and were not retuned for the larger models.
- **Time**: the pipeline read about 80,000 prompt tokens and wrote about 2,400 (solved from the two runs' times and speeds); the plan's "minutes of a 20-minute meeting in about …" uses those, so the 35B reads "about 18 minutes" (measured 17.6) and the 27B "about 72 minutes" (measured 64.5).
- **The plan held**: the card stayed within 0.2 GB of the plan with no spill (shared memory +0.25 GB, under the 0.38 GB threshold); RAM available never went below 5.3 GB (6.3 GB for the 27B). The working set ran 3 GB over the plan because the file pages of the weights uploaded to the card stay mapped in the worker until Windows trims them; they are clean pages Windows can take back at once, so the plan does not count them, and the next plan is raised by the measured working set (at most ×1.5).
- **End to end in the app** (`tools/e2e/m12-large-model.mjs`, a short speech recording imported (77 lines), transcribed on the processor while another program held the card, then Meeting minutes with the 35B once the card was free): Settings showed both larger models with their plan lines and the §17 sentence while the card was held ("Qwen3.5 35B-A3B needs 25 GB of memory; this PC has 23 GB free…", then a RAM-only plan when more RAM was free), the Builder's Local card read "Qwen3.5 35B-A3B · graphics card and RAM · on this PC" with the plan line, the minutes took 12 min 7 s, and How this was made and History said "Ran on the graphics card with the experts of 37 of 40 layers in RAM · 10.8 tokens/s · …". Memento itself (the app and its WebView2 processes) used 0.67–0.85 GB throughout; Whisper Large v3 Turbo on the processor took 2.1 GB in its worker.

### T.6 Placement rules and the reserves

`MemoryPlanner` (Memento.AI) decides before every load, from the card's free memory (§J: other programs' use already taken off) and the RAM available now; the first placement that fits is the plan, at 16k context, else 8k:

1. **Every layer on the card**, when the weights, the KV cache and the compute buffer fit the card's free memory less a 0.5 GB margin. (Neither larger model does on a 6 GB card.)
2. **Mixture of experts: experts in RAM.** Attention, the shared expert, the output layer, the KV cache and the compute buffer on the card (3.1 GB for the 35B at 16k); then the experts of as many of the last blocks as still fit; the experts of the first blocks in RAM.
3. **Split by blocks**: the output layer and the last blocks with their KV cache on the card, the first blocks in RAM, whenever at least one block fits beside the output layer and the compute buffer: the card then reads the prompts (§T.4: 36–52 tokens/s against 4), which is most of a document's time. For a mixture of experts this is the fallback when attention and the experts' neighbours do not fit (a transcription keeping part of the card): plain layer offload writes slower than RAM alone (4.6 vs 6.1 tokens/s) but reads prompts four times faster, so a document still takes about half as long.
4. **RAM alone** on the CPU build, without repacking.

A placement fits when its RAM fits **the RAM available less the reserves**:

| Reserve | Amount | Why |
|---|---|---|
| Windows | 2 GB | A program held RAM so that about 4, 2 and 1 GB stayed available while the 35B (experts in RAM) loaded and wrote: it still wrote at 10.6 / 12.3 / 11.3 tokens/s and a small program still started in a median 25 ms (at most 72 ms), but prompt reading fell from 103 to 86 / 82 / 76 tokens/s, available memory touched 0.3 GB while the holder let go, and the CPU build's repacked load left 0.2 GB. Nothing stalled, so 2 GB is a margin rather than a cliff: it is what keeps the model's own pages resident (prompt reading slows first) and leaves room for what other programs load mid-document, which on this PC was 5.3 GB of video memory and its server's RAM |
| Memento's own growth | 0.5 GB | The app and its WebView2 processes used 0.67–0.85 GB, sampled every 5 s through an import, a transcription and a generation: 0.18 GB of growth; Live output and a render or export add a few hundred MB more. |
| A recording being captured | 0.5 GB, 1.5 GB with video | Audio capture keeps a few MB of buffers per track and the live transcript's 10-second window; video adds the encoder, its frame pool and the preview (§M). Only while a recording runs. |
| A transcription waiting or running | Whisper's RAM: 0.5 GB of working memory, plus the model file (1.6 GB for Large v3 Turbo) when it would run on the processor | Only while the orchestrator has transcription work. |
| The card's margin | 0.5 GB beyond the plan | A 16k KV cache once moved 0.5 GB to shared memory with 5.3 GB reported free (§I.2); the 35B with four blocks' experts on the card used 5.11 GB of 5.1 GB free. |
| A waiting transcription on the card | the transcription model's minimum video memory (2.5 GB for Large v3 Turbo) | The larger model is planned without that memory, so Whisper still fits when it gets the card; when the model only fits with that memory too it is planned with the whole card and waits at the gate (one job on the card at a time, as before). |

The worker checks the RAM again right before loading (the job may have waited at the gate for minutes) against the plan's RAM plus Windows', the app's and a recording's reserves, and refuses with `ai.notEnoughMemory` rather than drive the PC into paging. A recording that starts while a larger model writes unloads it when the RAM available is below Windows' 2 GB plus the capture reserve. The plan's speed is the weights read per token from RAM (16 GB/s, the 35B in RAM alone; the 27B reads 18–21 GB/s) and from the card (150 GB/s, Qwen3.5 4B) plus 0.45 ms for each hand-over between them (two per block whose experts are in RAM, four for a split): it predicted 12.2 tokens/s for the 35B with its experts in RAM (measured 11.2 over a whole document, 12.4 in the spike), 6.0 in RAM alone (6.1), 1.3 for the 27B split (1.39) and 1.0 in RAM alone (1.3–1.4). Prompt reading is estimated as 65 GB/s of weights in RAM alone, the experts in RAM copied to the card at 3.5 GB/s per 512-token batch, a dense model's blocks in RAM at 0.9 GB/s, and the card's own at 4.5 TB/s of active weights (Qwen3.5 4B: 1,731 tokens/s). After a load the measured speed replaces it for the same placement and scales it for another; measured memory above the plan raises the next plan's estimate (never lowers it).

### T.7 Estimates for the models not measured

From the measured rates (RAM 16–19 GB/s for weights read per token, prompt reading 65 GB/s of weights in RAM alone, the card's copies) and each model's tensor sizes; not measured:

| Model | Placement on this PC | Writing | Prompt reading | RAM | Note |
|---|---|---|---|---|---|
| Qwen3-32B Q4_K_M | split, about 10 of 64 blocks | about 1.1 tok/s | about 30 tok/s | about 17 GB | the previous generation of the 27B, 18 % bigger and slower |
| Qwen3-30B-A3B-Instruct-2507 Q4_K_M | experts in RAM | about 9–11 tok/s | about 100 tok/s | about 17 GB | 128 experts of 48 blocks, 8 active: close to the 35B-A3B |
| gpt-oss-20b (MXFP4) | experts in RAM, about 8 of 24 blocks' experts on the card | about 13–18 tok/s | about 150 tok/s | about 9 GB | the lightest mixture of experts; needs a verified Harmony template and its reasoning channel handled first |

### T.8 Traps

1. **The CPU build's weight repacking doubles a large model's RAM** (11.5 GB more for the 35B, 0.2 GB left available, same speed). `use_extra_bufts` is not on `IModelParams`; the worker loads larger models in RAM with it off (§T.2).
2. **Another program can take the card in the middle of a document.** At 14:34 a dictation app's Ollama loaded 5.3 GB onto the card while the 35B had been writing for 23 minutes; the spill watch stopped it (1.2 GB moved to shared memory) and the whole generation was lost. The worker now loads the model again with the experts of more blocks, then blocks, in RAM, and answers the prompt again (at most twice), so a spill costs a reload (about 10 s) instead of the document.
3. **Plain layer offload of a mixture of experts** fails to load beyond 7 blocks on a 6 GB card and is slower than RAM alone; only the expert overrides make the card useful.
4. **ubatch 2048 spills** (the compute buffer grows to 2.4 GB); keep 512.
5. **Available RAM counts the standby list**, which holds the model file's own pages after an earlier load: a second load looks free, then takes those pages. The plan's RAM is the model's resident size, so the check stays right either way.
6. **The model's chat template defaults to thinking.** The 27B and 35B GGUF templates think unless `enable_thinking` is false; Memento's `qwen3.5` rendering is exactly the non-thinking prompt (an empty think block), so the verified template id is shared with the 4B.
7. **Commit limit**: with mmap off the experts would be 20 GB of private memory against a 42 GB commit limit (40 GB RAM, 1.6 GB page file); mmap stays on and the warning about overrides with mmap is expected.

### T.9 Downloaded and removed

Through Memento's own model manager (Hugging Face, host-checked, SHA-256 verified) into a private data root under `artifacts/`: Qwen3.5-35B-A3B-Q4_K_M.gguf (22,016,023,168 bytes, 248 s) and Qwen3.5-27B-Q4_K_M.gguf (16,740,812,704 bytes; the server ended the response at 11.8 GB after 96 minutes, Install again continued from there and finished in 76 s, the hash matched). Both were removed at the end; the catalog keeps their published SHA-256, which the downloads confirmed. Qwen3.5 4B, Ministral 3 3B and Whisper Large v3 Turbo were hard-linked into the private root from the installed models; the links were removed. The hardware tests' default models folder is the real data root, so `MEMENTO_MODELS_ROOT` / `MEMENTO_LLM_MODELS` must point at the private one (one unit run without the filter loaded the installed small models read-only before that was noticed).

## U. Lines cut where the speaker changes, October 2026 (2.6.0)

The product owner reports speaker assignments that are often wrong. One cause is in the assignment itself (`SpeakerAssigner`): a Whisper line that spans a quick exchange ("Yeah. — So what I think is…") gets one speaker, whoever talked longest in it, and the other person's words are mislabelled with no line boundary to move them across. The diarizer's turns are finer than the lines (pyannote segmentation changes speaker mid-line; `MinDurationOn` 0.3 s), and word timestamps are kept by default, so the host can cut the line where the turns change before assigning speakers (`LineSplitter`, called by the speakers stage).

The rule: each word goes to the speaker whose turns on the line's track overlap it most (a word no turn touches joins the word before it); consecutive words of one speaker form a run; a run with fewer than **2 words** (`MinWords`) or less than **0.5 s** of speech from its first word's start to its last word's end (`MinSeconds`) is too short to place and folds into the neighbouring run with more speech, shortest run first, until every run can stand as a line; the line is cut between the runs that remain. The constants follow pyannote's own minimum turn (0.3 s on, 0.5 s off): a single word straddling a turn change is as likely Whisper's timing as a real interjection, and a 0.5 s run is the shortest the diarizer itself keeps apart. The first part keeps the line's id and start, the later parts are `{id}-2`, `{id}-3`… and the last keeps the line's end; cuts fall between the words, so every part plays what it says. Only a fresh transcript is cut (its last change is the transcription and it has no speakers yet; a speakers write keeps the transcription as the last change, so the speakers themselves tell a first pass from a later one); a pass over an edited transcript, and Identify speakers again, keep every line, because highlights, chapters, known-voice matches and documents refer to lines by id, and a line the user edited is never cut (its words may no longer match its text). Settings › Speakers › Split lines where the speaker changes turns it off.

**Not measured on a real recording yet.** The owner's corrected meetings are not line-by-line ground truth (they have not been checked that closely), so this version is checked by unit tests (the word, time, confidence and id rules) and the stage test (a fresh transcript cut, an edited one and a re-identified one not). To measure it later: `tools/TranscriptionCheck diarize` on a two-voice file with word timestamps, then `evaluate` with and without the cut (a flag to add there), reporting how much speech lands on the right speaker; the synthetic two-voice fixture (`tools/e2e/speech.ps1`) has no mid-line exchanges, so a fixture with a short interjection inside a sentence is needed first.

## V. Doubtful lines by voice, October 2026 (2.6.0)

The "uncertain speaker" mark (`speakerConfidence` below 0.7: the dotted dot, the Uncertain filter, the U key) came only from sherpa-onnx's per-turn score, which says how cleanly the diarizer separated a turn. It says nothing about the step that goes wrong most (§L): a raw voice grouped into the wrong person, by a count-driven join or by a little voice folded into the nearest main voice (folded at cosine ≥ 0.2, so a laugh that sounds like nobody lands somewhere). `SpeakerAssigner` now checks every raw voice against the person it was grouped into, by voice alone.

The rule: a voice's **fit** is the cosine of its embedding to the mix of its person's *other* voices (leave-one-out; the mix is weighted by the talk time of the lines each voice won, not by the embedding's capped 60 s). When those other voices hold at least as much speech as the voice, its lines' confidence is capped at `Calibrate(fit)` (the same map as the turn score: 0.2 → 0, 0.6 → 1), so a fit under about **0.48** shows as uncertain and one of 0.6 or more changes nothing; when the voice is most of its person, nothing is lowered (a voice that is most of a person *is* the person, and the doubt belongs to the rest: a 2-second laugh folded into a 57-second speaker must not doubt the speaker, while two equal halves joined at 0.3 are both doubtful). A person with one voice, a voice without an embedding and a line the user placed (`speakerByUser`) are not touched, and a line already uncertain by its turn is not counted again. History says "41 lines marked uncertain by voice"; every fit is kept in `voices.json` (`clusters[].fit`, optional, schema still 1) and `TranscriptionCheck evaluate` prints the fit distribution per configuration, so the threshold can be tuned from saved jobs.

Why about 0.48: in §N's same-session measurements a cluster of the right person scored min 0.45 / median 0.76 against that person's other clusters, and a cluster of the wrong person 0.29–0.76 (median 0.50), so the two overlap and no cutoff is clean; a fit under about 0.5 is where the wrong joins live. Auto only joins main voices at ≥ 0.66, so on Auto the marks fall on folded little voices; with a count they also fall on the joins the count forced.

**Not measured on a real recording yet.** The owner's corrected meetings are not line-by-line ground truth, so this is checked by unit tests (the fold, the count-driven join at 0.7 and at 0.3, the equal and the 60/40 split, one voice, no embedding, a placed line, a turn already uncertain) and a stage test (the History line and the fits in voices.json). To measure it: `TranscriptionCheck diarize` once per file, then `evaluate` with a reference; the fit line beside "named speech right" shows whether the voices under 0.48 are the ones on the wrong speaker, and the cutoff moves with `SpeakerAssigner.GuessScore` / `CertainScore` if not.

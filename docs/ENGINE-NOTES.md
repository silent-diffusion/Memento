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
- Prompted large-v3-turbo again dropped the 15-second LibriVox announcement at the start of the 5-minute sample (small kept it). The gap is under the 20-second coverage threshold, so it is not flagged.
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

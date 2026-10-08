# Third-party components

Every bundled dependency, its license, and why it is used. Builders add a row when they add a package. Only permissive licenses (MIT, Apache-2.0, BSD, OFL, Ms-PL) are accepted; no GPL/AGPL.

| Component | License | Used for |
|---|---|---|
| Manrope (font) | OFL-1.1 | Interface typeface |
| JetBrains Mono (font) | OFL-1.1 | Timecodes |
| Microsoft.Web.WebView2 | BSD-3-Clause (Microsoft) | UI host |
| Preact | MIT | UI framework |
| Vite | MIT | UI build |
| @preact/signals | MIT | UI state |
| Microsoft.Extensions.Hosting (with its Microsoft.Extensions.* dependencies) | MIT | Host composition and dependency injection |
| Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions | MIT | Core service registration and logging interfaces |
| Microsoft.Windows.SDK.NET projection (from the `net8.0-windows10.0.19041.0` target) | MIT | Windows theme API (`UISettings`) |
| Serilog | Apache-2.0 | Logging |
| Serilog.Extensions.Hosting | Apache-2.0 | Serilog behind `ILogger<T>` |
| Serilog.Sinks.File | Apache-2.0 | Rolling log files |
| Serilog.Sinks.Console | Apache-2.0 | Console log output in Debug builds |
| Velopack | MIT | Installer and update hooks in the app |
| NAudio.Core, NAudio.Wasapi (2.4.0) | MIT | Device and audio-session enumeration, Media Foundation encode/decode (FLAC, MP3, AAC), WDL resampler |
| System.Drawing.Common | MIT | Application icons for the per-app audio source list (`Icon.ExtractAssociatedIcon` → PNG) |
| Microsoft.Data.Sqlite | MIT | Library index (`library.db`) |
| SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core, SQLitePCLRaw.provider.e_sqlite3 (Microsoft.Data.Sqlite dependencies) | Apache-2.0 | SQLite bindings and the bundled native `e_sqlite3` library |
| SQLite (inside `e_sqlite3`, with FTS5) | Public domain | Database engine and full-text search for the library index |
| DocumentFormat.OpenXml, DocumentFormat.OpenXml.Framework (3.5.1) | MIT | Agenda import from Word (.docx) and Excel (.xlsx) files (Memento.Documents) |
| System.IO.Packaging (8.0.1, an Open XML SDK dependency) | MIT | Reading the Office package (ZIP) parts for agenda import |
| PdfPig (0.1.16: UglyToad.PdfPig and its Core, Fonts, Tokens, Tokenization, DocumentLayoutAnalysis, Package assemblies) | Apache-2.0 | Agenda import from PDF: words with their positions and fonts |
| Adobe Core 14 font metrics (AFM data embedded in UglyToad.PdfPig.Fonts) | Adobe AFM terms: use, copy and distribute for any purpose with the copyright notices kept | Glyph widths of the standard PDF fonts; metrics data only, no font files. The product owner should confirm the notice is carried in the installer's notices. |
| Microsoft.Windows.SDK.NET projection: `Windows.Media.Ocr`, `Windows.Graphics.Imaging` (from the `net8.0-windows10.0.19041.0` target of Memento.Documents) | MIT | Windows text recognition and image decoding for agenda photos; the OCR languages are part of Windows |
| Microsoft.Windows.SDK.NET projection: `Windows.Data.Pdf` (same target, M3) | MIT | Renders the pages of a scanned PDF agenda (no text layer) for text recognition; the renderer is part of Windows |
| System.Security.Cryptography.ProtectedData (8.0.0, M3) | MIT | Windows DPAPI (current-user scope) for the AI provider keys in `secrets.bin` |

Shipped in the `worker\` folder beside the app (`Memento.Worker.exe`, M2; published self-contained there with its own copy of the .NET runtime, MIT):

| Component | License | Used for |
|---|---|---|
| Whisper.net, Whisper.net.Runtime, Whisper.net.Runtime.Vulkan (1.9.1) | MIT | Transcription; the runtimes carry whisper.cpp and ggml (MIT) for the CPU and Vulkan. No CUDA package. Only `runtimes/win-x64` and `runtimes/vulkan/win-x64` are kept. |
| Microsoft.Extensions.AI.Abstractions, System.Text.Json 10, System.Memory, System.IO.Pipelines, System.Text.Encodings.Web (Whisper.net dependencies) | MIT | Pulled in by Whisper.net; isolated in the worker folder so the app keeps its own .NET 8 assemblies |
| org.k2fsa.sherpa.onnx, org.k2fsa.sherpa.onnx.runtime.win-x64 (1.13.8) | Apache-2.0 | Speaker identification (offline diarization); `sherpa-onnx-c-api.dll` |
| ONNX Runtime (`onnxruntime.dll`, inside the sherpa-onnx runtime package) | MIT | Inference for the speaker models |
| NtvLibs.MSVCP.vcruntime140 / vcruntime140_1 / msvcp140 / vcomp140 `.runtime.win-x64` (14.42.34430) | Packaging: MIT (nietras). The DLLs: Microsoft Visual C++ 2015–2022 Runtime, under the [Microsoft Visual Studio 2022 C runtime license terms](https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/), which permit app-local redistribution | `vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll`, `vcomp140.dll`, which every Whisper.net native library imports; shipped beside the worker so no Visual C++ Redistributable install is needed. These are Microsoft redistributable files, not open-source code: the product owner should confirm this is acceptable under the "permissive only" rule (the alternative is to require the redistributable). |

Build, test and packaging tools (not shipped in the installer):

| Component | License | Used for |
|---|---|---|
| vpk (Velopack CLI, `dotnet-tools.json`) | MIT | Packing `Setup.exe` and the update feed |
| CycloneDX (.NET tool 6.2.0, `dotnet-tools.json`) | Apache-2.0 | The NuGet SBOM attached to each release (`build/sbom.ps1`) |
| @cyclonedx/cyclonedx-npm (6.0.1, run through `npx` by `build/sbom.ps1`) | Apache-2.0 | The UI's npm SBOM attached to each release |
| Microsoft.CodeAnalysis.NetAnalyzers (10.0, pinned in `Directory.Packages.props`) | MIT | The same code-analysis rules on every SDK, local and CI |
| xunit, xunit.runner.visualstudio | Apache-2.0 | .NET unit tests |
| Microsoft.NET.Test.Sdk, Microsoft.Extensions.DependencyInjection | MIT | .NET test host; DI container in tests |
| TypeScript | Apache-2.0 | UI type checking |
| Vitest, jsdom | MIT | UI unit tests |
| ESLint, @eslint/js, typescript-eslint, globals | MIT | UI linting |
| @fontsource/manrope, @fontsource/jetbrains-mono | OFL-1.1 | Source of the bundled font files (`ui/scripts/copy-fonts.mjs`) |
| @types/node | MIT | Types for the build scripts |
| lightningcss (Vite dependency) | MPL-2.0 | CSS processing during the UI build only; nothing from it is bundled |
| System.Drawing.Common (in Memento.Documents.Tests) | MIT | Renders the image fixtures for the agenda OCR tests |

Models downloaded at runtime through the model manager are listed in the in-app catalog (`src/Memento.Core/Models/catalog.json`) with their own licenses; they are not part of this repository or the installer:

| Model | License | Source |
|---|---|---|
| Whisper large-v3-turbo, medium, small, base (ggml) | MIT | huggingface.co/ggerganov/whisper.cpp |
| pyannote segmentation-3.0 (sherpa-onnx export) | MIT | huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0 |
| NeMo TitaNet small (English) | CC-BY-4.0 (attribution: NVIDIA NeMo) | github.com/k2-fsa/sherpa-onnx releases |
| 3D-Speaker ERes2Net base (optional) | Apache-2.0 | github.com/k2-fsa/sherpa-onnx releases |
| Tesseract English (tessdata_fast) | Apache-2.0 | github.com/tesseract-ocr/tessdata_fast |

## Added with M4a (AI providers)

The cloud providers (Memento.AI) use only HttpClient and System.Text.Json from .NET 8; no Anthropic or OpenAI SDK is bundled, so the app keeps its .NET 8 assemblies. The local LLM engine (Memento.AI.Local) is loaded by Memento.Worker only and ships in the `worker\` folder:

| Component | License | Used for |
|---|---|---|
| LLamaSharp (0.27.0) | MIT | .NET binding for llama.cpp: the local LLM provider |
| LLamaSharp.Backend.Vulkan.Windows (0.27.0) | MIT (llama.cpp and ggml: MIT) | `llama.dll`, `ggml*.dll` built for Vulkan (`runtimes/win-x64/native/vulkan`, 65 MB); they import only the VC++ runtime, the UCRT, `KERNEL32` and the graphics driver's `vulkan-1.dll`. Referenced directly: the `LLamaSharp.Backend.Vulkan` meta-package adds 65 MB of Linux binaries. |
| LLamaSharp.Backend.Cpu (0.27.0) | MIT (llama.cpp and ggml: MIT) | The CPU builds (`runtimes/win-x64/native/{noavx,avx,avx2,avx512}`, about 4 MB each); `ggml-cpu.dll` also imports `ADVAPI32` and `VCOMP140` (OpenMP, already shipped with the worker). `mtmd.dll` (multimodal) is in every folder but is not used; the worker publish leaves it out (M4d). |
| CommunityToolkit.HighPerformance (8.4.2) | MIT | LLamaSharp dependency |
| Microsoft.Extensions.AI.Abstractions (10.4.1), Microsoft.Extensions.Logging.Abstractions and DependencyInjection.Abstractions (10.0.5), Microsoft.Bcl.AsyncInterfaces and Microsoft.Bcl.Memory (10.0.5), System.Diagnostics.DiagnosticSource (10.0.5), System.Numerics.Tensors (10.0.5), System.Linq.AsyncEnumerable (10.0.2), System.Text.Json, System.Text.Encodings.Web and System.IO.Pipelines (10.0.4) | MIT | LLamaSharp dependencies, isolated in the worker folder like Whisper.net's |
| System.Linq.Async, System.Interactive.Async (7.0.0) | MIT | LLamaSharp dependencies |

Models the local provider downloads through the model manager (catalog entries in `src/Memento.AI/Local/local-models.json`, to be merged into Core's catalog):

| Model | License | Source |
|---|---|---|
| Qwen3.5-4B (Q4_K_M GGUF) | Apache-2.0 | huggingface.co/unsloth/Qwen3.5-4B-GGUF |
| Ministral-3-3B-Instruct-2512 (Q4_K_M GGUF) | Apache-2.0 | huggingface.co/mistralai/Ministral-3-3B-Instruct-2512-GGUF |

Test fixtures: `tests/Memento.AI.Tests/fixtures/templates/*.jinja` are the chat templates stored in those two GGUF files (Apache-2.0), kept to test the chat formatting against.

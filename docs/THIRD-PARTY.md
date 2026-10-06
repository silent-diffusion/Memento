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

Build, test and packaging tools (not shipped in the installer):

| Component | License | Used for |
|---|---|---|
| vpk (Velopack CLI, `dotnet-tools.json`) | MIT | Packing `Setup.exe` and the update feed |
| xunit, xunit.runner.visualstudio | Apache-2.0 | .NET unit tests |
| Microsoft.NET.Test.Sdk, Microsoft.Extensions.DependencyInjection | MIT | .NET test host; DI container in tests |
| TypeScript | Apache-2.0 | UI type checking |
| Vitest, jsdom | MIT | UI unit tests |
| ESLint, @eslint/js, typescript-eslint, globals | MIT | UI linting |
| @fontsource/manrope, @fontsource/jetbrains-mono | OFL-1.1 | Source of the bundled font files (`ui/scripts/copy-fonts.mjs`) |
| @types/node | MIT | Types for the build scripts |
| lightningcss (Vite dependency) | MPL-2.0 | CSS processing during the UI build only; nothing from it is bundled |

Models downloaded at runtime through the model manager are listed in the in-app catalog with their own licenses; they are not part of this repository.

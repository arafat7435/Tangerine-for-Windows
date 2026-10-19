# Tangerine for Windows

A lightweight native Windows utility for converting and editing files through a floating radial menu. Built with **C# / WPF / .NET 8**, with no Electron, embedded browser or cloud processing.

This is an independent implementation inspired by [Tangerine for Mac](https://tangerineformac.com/). It is not affiliated with or endorsed by the original publisher. The MIT license covers this project's original code; it does not grant rights to third-party branding or reference assets.

## Get started

Requires **Windows 10 version 2004 or later, x64**. Windows 11 is supported.

For just the application, choose **`Tangerine-for-Windows.exe`**. It is a portable, self-contained executable: double-click to open the toolkit, with no source checkout, installer, administrator rights or separate .NET installation needed. Its first launch extracts the bundled runtime and native libraries into your user cache. License notices are embedded and available from **Licenses and notices** in the tray menu.

The `win-x64-standalone.zip` includes the same application plus readable documentation and notices. The smaller `win-x64.zip` requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0); extract the entire ZIP before running `Tangerine.exe`.

Use the `--tray` argument to start without the toolkit window. Right-click the tray icon for Settings, Open Tangerine, licenses, or Quit.

1. Drag one or more files in Explorer or from the desktop.
2. Tap **Shift** for conversions, **Ctrl** for editing tools, or **Alt** for file actions.
3. Move onto a wheel segment and release the mouse. Releasing the key keeps the wheel open; Escape cancels.

Normal key presses, held clicks and text drags do not open the wheel. Tool panels open in the foreground. Multiple files can share settings or use individual edits, with separate progress and error reporting.

You can also use **Add files +** in the toolkit window. Conversion and editing preserve original files and choose collision-safe output names. The explicit **Move** action moves originals.

## Features

| Category | Included workflows |
| --- | --- |
| Images | JPG, PNG, WebP, HEIC, AVIF, TIFF, BMP and PDF conversion; crop, compress, adjust, annotate, redact, backgrounds, collage |
| Video | Conversion, compress, mute, trim, crop, speed, snapshots, split, join, rectangular redaction |
| Audio | Conversion, compress, normalize, trim, mono/stereo channel gains, timed bleeps, waveform visualization |
| PDF | Page images, text and document exports; merge, split, reorder, rotate, duplicate, remove pages, lossless compression |
| Documents | Word, presentation and spreadsheet conversions; EPUB, text/Markdown and subtitle workflows |
| Archives / files | ZIP, TAR, GZIP and RAR extraction; copy, paths, move, Windows Share, terminal, floating shelf |

See [the feature matrix and limits](docs/FEATURES.md) before choosing a workflow.

## Optional components

- **Audio/video:** install FFmpeg and ffprobe locally, then select their folder in Settings. The setup script downloads a pinned, checksum-verified build; FFmpeg is not in public application ZIPs.
- **Office conversions:** require the corresponding locally installed Microsoft Word, Excel or PowerPoint application. Office is not included. Core image, PDF and archive tools work without Office.
- **Wheel sound:** the existing local reference sound is preserved for private builds. Public source and release ZIPs exclude it because no redistribution permission has been established. Public builds work silently; [asset instructions](docs/ASSETS.md) explain how a local authorized build can use the existing clip.

No account or network connection is required for file processing. Building and installing optional engines require downloads.

## Build from source

Install the **.NET 8 SDK** matching [global.json](global.json), **PowerShell 7**, and Git. Clone this repository or extract the source ZIP, open PowerShell 7 in the project directory, then run:

```powershell
.\scripts\build.ps1
.\scripts\run.ps1
```

The default output is `artifacts/app`. The build uses a committed NuGet lock file. It generates a local shortcut and launcher-path file; these are ignored by Git. To choose another build drive or SDK:

```powershell
.\scripts\build.ps1 -BuildRoot D:\TangerineBuild -DotnetPath D:\dotnet\dotnet.exe
```

Use `-SelfContained` for a local runtime-inclusive folder build, or `-SingleFile` for a portable EXE with the runtime and notices embedded. Build scripts place temporary files and the SDK CLI home under the build root and honor an existing `NUGET_PACKAGES` setting.

Optional media engine installation:

```powershell
.\scripts\setup-engines.ps1 -Destination .\artifacts\app\engines
```

## Verify and package

```powershell
.\scripts\smoke-test.ps1 -AppPath .\artifacts\app\Tangerine.exe -ResultRoot .\artifacts\smoke-results
.\scripts\package-release.ps1 -BuildRoot .\artifacts\public-v0.1.0
.\scripts\package-release.ps1 -BuildRoot .\artifacts\standalone-v0.1.0 -Standalone
```

The public packaging script requires a fresh build directory, excludes the private sound and media engines, runs core smoke checks, includes dependency licenses and a dependency inventory, and writes a ZIP with a SHA-256 checksum to `dist`. `-Standalone` also writes the self-contained EXE and its checksum, and tests a copy of only that EXE in an isolated folder.

[Testing](docs/TESTING.md) documents the larger conversion, batch, native-input and panel suites. [Releasing](docs/RELEASING.md) covers source archives and manual release checks. GitHub Actions builds and checks public source on Windows; it does not publish a release automatically.

## Contribute

Read [CONTRIBUTING.md](CONTRIBUTING.md), [the architecture guide](docs/ARCHITECTURE.md), and [SECURITY.md](SECURITY.md). Bug reports should include a minimal non-private sample and reproduction steps. Dependencies keep their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

Original project code and documentation: [MIT](LICENSE). Third-party code, fonts, names and optional reference assets are governed separately. The bundled icon is generated from the project's own drawing script.

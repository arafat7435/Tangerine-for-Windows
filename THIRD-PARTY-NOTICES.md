# Third-party notices

The original project code is licensed under MIT. This does not relicense its dependencies, Microsoft software, trademarks, reference photographs, or the optional reference sound.

## Direct dependencies

| Component | Version | License / source |
| --- | --- | --- |
| Magick.NET-Q8-x64 and Magick.NET.Core | 15.0.0 | Apache-2.0; [Magick.NET](https://github.com/dlemstra/Magick.NET). The native package includes ImageMagick and multiple codecs with additional notices. |
| PDFsharp | 6.2.2 | MIT; [PDFsharp](https://github.com/empira/PDFsharp) |
| PdfPig | 0.1.16 | Apache-2.0; [PdfPig](https://github.com/UglyToad/PdfPig) |
| SharpCompress | 1.0.0 | MIT plus component notices; [SharpCompress](https://github.com/adamhathcock/sharpcompress) |
| System.Security.Cryptography.Pkcs | 8.0.2 | Microsoft .NET package; pinned to the patched version used by PDFsharp. Package license and third-party notices accompany the build. |
| bblanchon.PDFium.Win32 | 157.0.8100 | NuGet metadata declares Apache-2.0; the upstream packaging repository includes an MIT notice. Both texts are retained. PDFium uses BSD-style terms plus bundled third-party licenses; [PDFium binaries](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium/8100). |

The [licenses directory](licenses) retains upstream license and notice texts. Every build also collects license files shipped with the resolved NuGet packages and writes `dependencies.json` with exact versions and NuGet SHA-512 content hashes. The dependency lock is `Tangerine/packages.lock.json`.

## Windows and .NET

.NET and WPF are Microsoft projects distributed under MIT with additional third-party notices. Microsoft.Extensions and cryptography dependencies carry their own notices in the NuGet packages.

Microsoft.Windows.SDK.NET.Ref uses [Microsoft Windows SDK license terms](https://aka.ms/WinSDKLicenseURL); it must not be described as covered by this project's MIT license. Segoe UI and Segoe MDL2 Assets are used from Windows. Font files are not redistributed.

Microsoft Office is an optional installed prerequisite. No Office binaries or license are included. The smaller framework-dependent ZIP requires the .NET 8 Desktop Runtime installed separately. The standalone EXE/ZIP includes the .NET and Windows Desktop runtime packs, records their exact versions and NuGet hashes in `dependencies.json`, and carries their license and third-party notice files. The EXE embeds these files and exposes them through **Licenses and notices** in the tray menu.

## Optional FFmpeg

The optional setup script downloads Gyan Doshi's FFmpeg 9.0.2 essentials build, which is **GPL v3 or later**. Its pinned SHA-256 is verified before extraction. Public application ZIPs and source archives do not contain FFmpeg executables.

If distributing an engine-inclusive package separately, satisfy the applicable GPL terms, including corresponding source for the exact build and its libraries. A link to upstream alone is not a substitute for those obligations. [Build/source details](https://www.gyan.dev/ffmpeg/builds/) · [Exact binary release](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2).

## Reference sound and materials

The private local file `Tangerine/Assets/highlight.wav` is the unchanged reference clip from [Tangerine for Mac](https://tangerineformac.com/sounds/highlight.wav). Its SHA-256 is `606B2829835A38E88A62355C9D7E3F1C2E7699DB6C047CF2AE36FFF6D375E02C`. No redistribution license has been established. It is ignored by Git and is excluded from every build using `-PublicRelease`, even when the file is present locally. It is not MIT licensed by this project. See [ASSETS.md](docs/ASSETS.md).

Downloaded website source, reference screenshots/video, local audit logs and screenshots with unverified image provenance are not part of the public source package. No Mac application binary is included.

Tangerine for Windows is an independent implementation, not affiliated with or endorsed by the Tangerine for Mac publisher. Use of the project name does not grant trademark rights.

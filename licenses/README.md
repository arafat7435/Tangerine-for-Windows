# License text provenance

These upstream texts are retained verbatim and keep their original terms. They are not covered by the project's MIT license.

- `Magick.NET-NOTICE.txt` and `Magick.NET-COPYRIGHT.txt`: resolved Magick.NET 15.0.0 NuGet packages. The notice includes the ImageMagick/native codec notices.
- `pdfium-8100/`: `LICENSE`, `VERSION`, and the complete `licenses/` directory from the exact `chromium/8100` x64 release archive. Archive SHA-256: `51621BCC1D9BE9EA448F4B84B0516862B928E3968D3FCA899AF1A6D6E2848D6D`.
- `dotnet-SDK-LICENSE.txt` and `dotnet-THIRD-PARTY-NOTICES.txt`: SDK 8.0.426 distribution. The SDK's Microsoft terms are distinct from MIT-licensed .NET source projects; the SDK itself is not in the application ZIP.
- `Windows-SDK-license.rtf`: Microsoft's terms at the URL declared by Microsoft.Windows.SDK.NET.Ref. The downloaded document is RTF.
- `download-provenance.json`: source URLs and hashes for the other downloaded license texts. PDFsharp and PdfPig use their version tags; SharpCompress and PDFium packaging notices use upstream master where no matching license tag was available.
- Public builds add a `packages/` directory with the LICENSE/NOTICE/COPYRIGHT files actually shipped by every resolved NuGet package. `dependencies.json` records package versions, declarations and content hashes.
- Standalone builds also collect the exact .NET and Windows Desktop runtime pack notices and hashes. Their EXE embeds the complete notices directory and makes it available from the tray menu after extraction.

The PDFium NuGet license declaration and upstream packaging MIT notice differ; both are preserved rather than asserting that the project MIT license replaces either. Review updated component notices whenever dependencies change.

# Changelog

## 0.1.0 — 2026-10-19

Initial public-source preparation.

- Native C# / WPF radial file conversion and editing workflows.
- Image, audio/video, PDF, document, subtitle and archive support with documented prerequisites and limits.
- Single and multiple-file operations, shared or individual settings, cancellation and output preservation.
- Rounded editing panels, previews and short native animations.
- File-payload verification prevents modifier keys from opening the wheel outside a real file drag.
- Panel focus handoff brings editors to the foreground after a drop.
- MIT project license, dependency notices, locked NuGet restore, Windows CI, public packaging and source hygiene checks.
- Portable standalone EXE with bundled .NET/WPF, image/PDF libraries and accessible license notices; isolated executable checks and SHA-256 downloads.
- Pin System.Security.Cryptography.Pkcs 8.0.2 to address the published denial-of-service advisories affecting 8.0.1; audit transitive dependencies and fail restore for high/critical advisories.
- Private reference audio remains supported for local authorized builds and is excluded from public packages.

# Security

Tangerine processes local files using native image/PDF libraries, archive readers, optional FFmpeg and installed Microsoft Office. It runs as the current user, without elevation or a security sandbox. Do not treat file conversion as malware isolation.

## Reporting

For a vulnerability, use the repository's **Security → Report a vulnerability** option when private vulnerability reporting is enabled. If that option is unavailable, open an issue asking for a private contact channel without disclosing exploit details or private files. Maintainers should enable private vulnerability reporting before public launch.

Include the affected version, minimal reproduction steps, expected impact and a non-sensitive sample. Do not post credentials or malicious payloads in public issues. There is no guaranteed response-time commitment.

## Boundaries

- Inputs stay local; the processing app does not upload them.
- Office-dependent operations use local COM automation. Office and engine security updates remain the user's responsibility.
- Outputs use temporary staging and collision-safe names. Explicit Move is a destructive file operation by design.
- Archive extraction validates paths, rejects links and applies entry/size limits.
- Native desktop tests move the pointer and operate the clipboard. Run them on a controlled interactive desktop.

Only the latest development/release line is currently maintained. Dependency updates are proposed by Dependabot; they still require review and validation.

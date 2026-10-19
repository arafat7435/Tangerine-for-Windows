# Testing

## Core smoke gate

After building, run:

```powershell
.\scripts\smoke-test.ps1 -AppPath .\artifacts\app\Tangerine.exe -ResultRoot .\artifacts\smoke
```

This uses generated fixtures and checks image conversions/edits, PDF pages, subtitle timestamps, archive round trips/path traversal, cancellation and source preservation. It does not require Office, FFmpeg, audio hardware or native mouse input. It creates a WPF preview for a layout check. Add `-RequirePublic` to assert that the private sound is absent from a public binary.

`scripts/package-release.ps1 -BuildRoot <fresh-directory> -Standalone` additionally tests an isolated copy of just the EXE, with its own extraction cache and no configured shared .NET runtime. `-RequireStandalone` on the smoke script verifies the runtime assembly locations and embedded license inventory. This catches missing native libraries and accidental dependencies on adjacent publish files.

## Expanded suites

Use fresh directories for every run and read the JSON reports, not only the process exit code.

| Flag | Report | Prerequisites |
| --- | --- | --- |
| `--self-test` | `results.json` | Office and FFmpeg for the full suite; `--core-only` avoids them |
| `--audit` | `audit-results.json` | Office, FFmpeg; optional RAR fixture |
| `--conversion-matrix` | `matrix-results.json` | Office, FFmpeg; all offered routes and semantic output checks |
| `--file-actions-test` | `file-actions-results.json` | Relevant engines; optional `--native-drag` |
| `--panels-test` | `panel-results.json` | FFmpeg and an interactive desktop |
| `--batch-test` | `batch-results.json` | FFmpeg and an interactive desktop |
| `--interaction-test` | `interaction-results.json` | Interactive desktop, FFmpeg; sound cases require an authorized clip and audio device |
| `--drag-activation-test` | `drag-activation-results.json` | Interactive desktop |
| `--panel-focus-test` | `panel-focus-results.json` | Interactive desktop and FFmpeg |
| `--stress-test` | `stress-results.json` | Engines appropriate to fixtures |

Example:

```powershell
$env:TANGERINE_ENGINE_DIR = (Resolve-Path .\artifacts\app\engines).Path
.\artifacts\app\Tangerine.exe --batch-test .\artifacts\batch-check
```

`scripts/full-audit.ps1 -ResultRoot <fresh-directory> -AppPath <exe>` runs all eight comprehensive suites and fails on a failed suite. Optional `-RarFixture` and `-PanelPhoto` paths must refer to files you have permission to use. The default panel images are synthetic gradients.

Close other Tangerine instances before native tests. Keep the mouse and keyboard idle; these tests deliberately move the pointer, operate clipboard/file actions and create temporary windows. The foreground suite uses a separate maximized Windows Forms process, compares the native foreground HWND, checks Escape without an extra activation click, and verifies that user input cancels the delayed focus handoff.

GitHub CI runs the core gate. It cannot establish physical speaker audibility, Office integration, all codecs, real Explorer behavior or every DPI/monitor arrangement. Skipped prerequisite-dependent cases must remain marked as skipped.

Do not commit raw audit directories. They can contain machine paths and generated binaries. Publish a sanitized report identifying the exact tested binary when reporting release verification.

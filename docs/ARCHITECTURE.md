# Architecture

The application is a single native WPF executable targeting .NET 8 and Windows x64. There is no web runtime or server.

- `App.xaml.cs`: startup, tray lifetime, single-instance handling, modal workflows, job queue and cancellation.
- `GlobalDragHook.cs` / `FileDragProbe.cs`: low-level modifier observation plus actual OLE file-payload verification. Ordinary input is forwarded.
- `OverlayWindow.cs` / `RadialWheel.cs`: floating drop target, geometry, hit testing, selection and sound triggers.
- `MiniWindow.cs` / `WindowActivation.cs`: shared panel shell and bounded foreground handoff.
- `OptionsWindow.*.cs`: image, media and document editors. `SelectionSurface`, `RangeTimeline` and `MediaPreview` implement previews and gestures.
- `BatchWindow.cs` / `BatchRunner.cs`: compatible selections, shared/individual options and sequential progress.
- `Engine.cs` with image/media/PDF/document/archive partial classes: conversion, staging, unique filenames, rollback and cancellation.
- `FileActions.cs`: local shell actions and floating shelf.
- `*Audit.cs`, `SelfTest.cs` and conversion fixtures: executable diagnostics.

Settings and recent history live under `%LOCALAPPDATA%/TangerineWindows`. Diagnostic runs suppress normal settings/history writes. Test results belong in ignored output directories.

The work queue is serialized. FFmpeg runs only during relevant operations. Wheel audio uses prepared WinMM voices. Motion uses short event-driven WPF animations; there is no continuous idle render loop.

Keep file processing separate from UI controls. Preserve explicit ownership of native handles, callbacks, cancellation tokens, Office COM objects and temporary directories.

# Contributing

Thanks for helping improve Tangerine for Windows.

## Setup

Use Windows x64, PowerShell 7 and the SDK specified in `global.json`. Run `scripts/build.ps1`. Optional Office and FFmpeg dependencies are described in the README. Keep secrets, personal sample files and machine-specific paths out of commits.

Use focused branches and pull requests. Describe the user-visible problem, the final behavior and the tests run. Include a sanitized screenshot for visual changes. Do not bundle reference media without an explicit compatible license and provenance.

## Code and validation

- Follow `.editorconfig`. Format C# with `dotnet format whitespace Tangerine/Tangerine.csproj`.
- Preserve originals, unique output naming, cancellation and cleanup behavior when changing engines.
- Keep native drag hooks short. Create UI outside the hook callback and retain normal keyboard input.
- Run core smoke checks for processing changes. Run the relevant native, batch or panel suite when changing those paths; see [TESTING.md](docs/TESTING.md).
- Test actual output semantics, not just output-file existence. Do not turn missing prerequisites into apparent passes.
- Update feature limits and the changelog when changing supported behavior.

NuGet versions are pinned in the project and lock file. After an intentional dependency change, run `dotnet restore Tangerine/Tangerine.csproj -r win-x64 --force-evaluate`, review both diffs and update relevant notices. Normal builds restore in locked mode.

## Pull requests

Use the PR template. Small documentation fixes do not need new tests. Larger changes should include a regression check that demonstrates the original issue. Public CI is a core smoke gate, not full Office, media, audio-device or Explorer verification.

By submitting an original contribution, you agree to license it under the project's MIT license. Third-party contributions must retain their notices and compatible license terms.

# Assets and redistribution

## Existing sound

The requested original 60 ms highlight sound can remain in a **local private build** as `Tangerine/Assets/highlight.wav`. A normal build embeds that file when present. It is not changed, regenerated or downloaded automatically.

The sound's owner has not supplied redistribution permission in this project. Therefore:

- Git ignores the file.
- Source archives omit it.
- `build.ps1 -PublicRelease` excludes it even if it exists locally.
- Public builds remain functional without sound; Settings indicates that no clip is included.
- Sound-specific native tests explicitly report a skip when the asset is absent.

To make an authorized local sound-enabled build, supply the clip at that path and run the normal build script. Use a short 16-bit PCM WAV. The existing native sound audit checks the original reference clip's hash; adapt that provenance assertion if intentionally substituting a different licensed clip.

Do not commit or release the reference audio until a compatible redistribution permission is recorded. The project MIT license cannot grant someone else's audio rights.

## Icon

`Tangerine/Assets/tangerine.ico` is generated from original geometric drawing code in `scripts/generate-icon.ps1`. Its source and generated icon are covered by the project MIT license. Run the generator on Windows to regenerate it. It is not extracted from the Mac application.

## Other references

Local research material is excluded as a whole. Downloaded website HTML/JavaScript, reference video frames and screenshots, personal fixtures, and images with unclear provenance are not part of the public source package.

The three documentation images in `docs/images/` were supplied by the maintainer for the README. They show the Windows application's Convert, Tools and Actions wheels without filenames or personal paths. New documentation screenshots should use synthetic or suitably licensed inputs and avoid personal paths.

# Releasing

1. Update the project version and CHANGELOG. Review dependencies, their notices and the package lock.
2. Run `scripts/check-source.ps1` against the staged/tracked source. Review the diff for credentials, personal paths and unlicensed media.
3. Create a **fresh** build with `scripts/package-release.ps1 -BuildRoot <new-directory>`. This builds in locked mode, excludes the private reference sound, runs core smoke checks and creates a Windows ZIP plus SHA-256. Run again with a different fresh directory and `-Standalone` to produce the portable `Tangerine-for-Windows.exe`, its checksum and a runtime-inclusive ZIP.
4. Run the relevant larger suites from TESTING.md on a controlled Windows desktop. A core CI pass alone is not a complete conversion or UI audit.
5. Create the source ZIP with `scripts/package-source.ps1 -OutputPath <new-file.zip>`. It uses Git's tracked file list and excludes ignored local assets.
6. Inspect the ZIP inventories and EXE checksums. Keep MIT, third-party notices, the licenses directory and dependencies.json with folder builds. The standalone EXE embeds all of these, including runtime-package notices; its tray menu opens the extracted notices.
7. Publish the reviewed artifacts to the chosen repository release manually. The included workflow only builds/uploads workflow artifacts; it does not create public releases.

The source ZIP builds without a Git checkout. Git is needed only for the source inventory/hygiene and source-packaging commands. Only the smaller framework-dependent ZIP requires the .NET 8 Desktop Runtime. The standalone EXE includes .NET/WPF and native image/PDF libraries, and runs without installation. It extracts its bundle under the user's temporary `.net` cache; `DOTNET_BUNDLE_EXTRACT_BASE_DIR` can override this location. FFmpeg can be installed in an `engines` folder beside the EXE or selected in Settings.

The standalone packaging gate copies only the executable to a fresh directory with spaces, gives it an empty shared-runtime location, and checks that .NET/WPF load from its bundle, notices remain readable, and core conversions still work. Do not trim WPF or remove native libraries to shrink the download. The EXE is unsigned unless a maintainer signs it separately; if signing, validate the signed file and regenerate its checksum before publishing.

Do not include the original highlight sound until compatible redistribution permission is documented. Local builds can retain it as described in ASSETS.md. Do not add FFmpeg to the public ZIP without satisfying the exact build's GPL source obligations.

Before opening the repository publicly, enable private vulnerability reporting and branch protection for the CI job. Configure repository ownership, remote URL and maintainer contact under your own account. No hosting account or email address is invented in these files.

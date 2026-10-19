#requires -Version 7.0
param(
    [string]$BuildRoot = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$DotnetPath = 'dotnet',
    [string]$EngineSourceDirectory = '',
    [switch]$SelfContained,
    [switch]$SingleFile,
    [switch]$PublicRelease,
    [switch]$NoLocalLauncher
)
$ErrorActionPreference = 'Stop'
if ($SingleFile) { $SelfContained = $true }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
if ($PublicRelease -and $EngineSourceDirectory) { throw 'Public packages exclude FFmpeg. Configure the optional media engine separately.' }
if ($PublicRelease -and (Test-Path -LiteralPath (Join-Path $BuildRoot 'app'))) { throw 'Use a fresh BuildRoot for public releases to avoid packaging stale or private files.' }
New-Item -ItemType Directory -Force -Path $BuildRoot,(Join-Path $BuildRoot 'temp') | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $BuildRoot 'cli'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $BuildRoot 'packages' }
$env:TEMP = Join-Path $BuildRoot 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$publish = Join-Path $BuildRoot 'app'
$project = Join-Path $repo 'Tangerine\Tangerine.csproj'
$properties = @("-p:TangerineBuildRoot=$BuildRoot", '-p:RestoreLockedMode=true', "-p:TangerinePublicRelease=$($PublicRelease.IsPresent.ToString().ToLowerInvariant())", "-p:SelfContained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())")
if ($SingleFile) {
    $notices = Join-Path $BuildRoot 'notices'
    $properties += @('-p:PublishSingleFile=true', '-p:IncludeAllContentForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:EnableSingleFileAnalyzer=false', "-p:TangerineNoticesDirectory=$notices")
}
& $DotnetPath restore $project -r win-x64 @properties --nologo
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
if ($SingleFile) {
    New-Item -ItemType Directory -Force -Path $notices | Out-Null
    foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $notices }
    foreach ($name in @('docs','licenses')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $notices -Recurse -Force }
    & (Join-Path $PSScriptRoot 'collect-notices.ps1') -AssetsFile (Join-Path $BuildRoot 'obj\Tangerine\project.assets.json') -Destination $notices
}
& $DotnetPath publish $project -c Release -r win-x64 --no-restore @properties -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($EngineSourceDirectory) {
    $engineDestination = Join-Path $publish 'engines'
    New-Item -ItemType Directory -Force -Path $engineDestination | Out-Null
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
        Copy-Item -LiteralPath (Join-Path $EngineSourceDirectory $name) -Destination $engineDestination
    }
    $license = Join-Path (Split-Path $EngineSourceDirectory) 'LICENSE'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination (Join-Path $engineDestination 'FFmpeg-LICENSE.txt') }
}
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $publish }
foreach ($name in @('docs','licenses')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $publish -Recurse -Force }
& (Join-Path $PSScriptRoot 'collect-notices.ps1') -AssetsFile (Join-Path $BuildRoot 'obj\Tangerine\project.assets.json') -Destination $publish
$exe = Join-Path $publish 'Tangerine.exe'
if (-not $PublicRelease -and -not $NoLocalLauncher) {
Set-Content -LiteralPath (Join-Path $repo '.local-app-path') -Value $exe -Encoding UTF8
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $repo 'Tangerine.lnk'))
$shortcut.TargetPath = $exe
$shortcut.Arguments = '--tray'
$shortcut.WorkingDirectory = $publish
$shortcut.Description = 'Tangerine for Windows'
$shortcut.Save()
}
Write-Output "Ready: $exe"

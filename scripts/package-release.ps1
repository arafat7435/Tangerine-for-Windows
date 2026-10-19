#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildRoot,[string]$DotnetPath='dotnet',[string]$OutputDirectory=(Join-Path $PSScriptRoot '..\dist'),[switch]$Standalone)
$ErrorActionPreference='Stop'
$BuildRoot=[IO.Path]::GetFullPath($BuildRoot)
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $PSScriptRoot 'build.ps1') -BuildRoot $BuildRoot -DotnetPath $DotnetPath -PublicRelease -NoLocalLauncher -SingleFile:$Standalone
$app=Join-Path $BuildRoot 'app'
$testExe=Join-Path $app 'Tangerine.exe'
if($Standalone){
    # Test only the EXE, in a path with spaces, without the publish directory's files.
    $isolated=Join-Path $BuildRoot 'standalone test'
    New-Item -ItemType Directory -Path $isolated | Out-Null
    $testExe=Join-Path $isolated 'Tangerine for Windows.exe'
    Copy-Item -LiteralPath (Join-Path $app 'Tangerine.exe') -Destination $testExe
}
$oldExtraction=$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
$oldDotnetRoot=$env:DOTNET_ROOT
try {
    if($Standalone){
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $BuildRoot 'bundle-cache'
        $env:DOTNET_ROOT=Join-Path $BuildRoot 'no-shared-runtime'
    }
    & (Join-Path $PSScriptRoot 'smoke-test.ps1') -AppPath $testExe -ResultRoot (Join-Path $BuildRoot 'smoke-results') -RequirePublic -RequireStandalone:$Standalone
} finally {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=$oldExtraction
    $env:DOTNET_ROOT=$oldDotnetRoot
}
[xml]$project=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Tangerine\Tangerine.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
New-Item -ItemType Directory -Force -Path $OutputDirectory|Out-Null
$suffix=if($Standalone){'win-x64-standalone'}else{'win-x64'}
$zip=Join-Path $OutputDirectory ("Tangerine-for-Windows-$version-$suffix.zip")
if(Test-Path -LiteralPath $zip){throw 'A release ZIP already exists. Choose a new output directory.'}
if(Test-Path -LiteralPath (Join-Path $app 'engines')){throw 'Public package unexpectedly contains media engines.'}
[IO.Compression.ZipFile]::CreateFromDirectory($app,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
Set-Content -LiteralPath ($zip+'.sha256') -Value (((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($zip)) -Encoding utf8
Write-Output "Public Windows ZIP: $zip"
if($Standalone){
    $exe=Join-Path $OutputDirectory 'Tangerine-for-Windows.exe'
    if(Test-Path -LiteralPath $exe){throw 'A portable EXE already exists. Choose a new output directory.'}
    Copy-Item -LiteralPath $testExe -Destination $exe
    Set-Content -LiteralPath ($exe+'.sha256') -Value (((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($exe)) -Encoding utf8
    Write-Output "Standalone EXE: $exe (includes .NET, native libraries and license notices)"
} else { Write-Output 'Requires the .NET 8 Desktop Runtime.' }
Write-Output 'Optional reference sound and FFmpeg are excluded.'

#requires -Version 7.0
param([string]$Destination=(Join-Path $PSScriptRoot '..\artifacts\app\engines'))
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$archive=Join-Path $Destination 'ffmpeg-9.0.2.7z'
$uri='https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.7z'
$expected='4705843CCAAF54257C16AD90F3E952ECE33C17DF964ECF7BFDBB0F49C7171077'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $uri -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg archive checksum mismatch. The archive has not been extracted.' }
& tar.exe -xf $archive -C $Destination
if ($LASTEXITCODE -ne 0) { throw 'Extraction failed. Use a current Windows 10/11 tar.exe or extract the archive with 7-Zip.' }
$package=Join-Path $Destination 'ffmpeg-9.0.2-essentials_build'
Copy-Item -LiteralPath (Join-Path $package 'bin\ffmpeg.exe'),(Join-Path $package 'bin\ffprobe.exe') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $package 'LICENSE') -Destination (Join-Path $Destination 'FFmpeg-LICENSE.txt')
Write-Output "Media engine ready: $Destination"
Write-Output 'Optional GPL v3-or-later engine. Public application ZIPs exclude it; redistribution requires corresponding source and build notices.'

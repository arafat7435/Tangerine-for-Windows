#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if(Test-Path -LiteralPath $OutputPath){throw 'Choose a new output archive.'}
& (Join-Path $PSScriptRoot 'check-source.ps1')
$files=@(& git -C $repo -c core.quotepath=false ls-files)
if($LASTEXITCODE -ne 0 -or $files.Count -eq 0){throw 'Initialize and stage the public source files before packaging.'}
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath)|Out-Null
$archive=[IO.Compression.ZipFile]::Open($OutputPath,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $repo $file),('Tangerine-for-Windows/'+$file),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
$hash=(Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($OutputPath+'.sha256') -Value ($hash+'  '+[IO.Path]::GetFileName($OutputPath)) -Encoding utf8
Write-Output "Source archive: $OutputPath"

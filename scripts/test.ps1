#requires -Version 7.0
param(
    [string]$AppPath = '',
    [Parameter(Mandatory=$true)][string]$ResultRoot,
    [string]$RarFixture = '',
    [switch]$IncludeInteraction,
    [switch]$ConversionsOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $AppPath) { $local=Join-Path $repo '.local-app-path';$AppPath=if(Test-Path -LiteralPath $local){(Get-Content -LiteralPath $local -Raw).Trim()}else{Join-Path $repo 'artifacts\app\Tangerine.exe'} }
$AppPath = [IO.Path]::GetFullPath($AppPath)
$ResultRoot = [IO.Path]::GetFullPath($ResultRoot)
if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'Build the application first.' }
if (Test-Path -LiteralPath $ResultRoot) { throw 'Choose a fresh result directory.' }
New-Item -ItemType Directory -Path $ResultRoot | Out-Null
$env:TANGERINE_ENGINE_DIR = Join-Path (Split-Path $AppPath) 'engines'
if ($RarFixture) { $env:TANGERINE_RAR_FIXTURE = [IO.Path]::GetFullPath($RarFixture) }
$suites = @(@('--self-test','integration'), @('--audit','expanded'))
if ($ConversionsOnly) { $suites = ,@('--conversion-matrix','conversions') }
if ($IncludeInteraction) { $suites += ,@('--interaction-test','desktop') }
$failed = $false
foreach ($suite in $suites) {
    $folder = Join-Path $ResultRoot $suite[1]
    $start = [Diagnostics.ProcessStartInfo]::new($AppPath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add($suite[0])
    $start.ArgumentList.Add($folder)
    $process = [Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { $failed = $true }
    Write-Output ($suite[1] + ': exit ' + $process.ExitCode + ' — ' + $folder)
    $process.Dispose()
}
if ($failed) { throw 'At least one test suite failed. Inspect the JSON reports before shipping.' }

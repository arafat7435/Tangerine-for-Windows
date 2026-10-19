#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$ResultRoot,[switch]$RequirePublic,[switch]$RequireStandalone)
$ErrorActionPreference='Stop'
$AppPath=[IO.Path]::GetFullPath($AppPath)
$ResultRoot=[IO.Path]::GetFullPath($ResultRoot)
if(Test-Path -LiteralPath $ResultRoot){throw 'Use a fresh result directory.'}
$start=[Diagnostics.ProcessStartInfo]::new($AppPath)
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
foreach($argument in @('--self-test',$ResultRoot,'--core-only')){$start.ArgumentList.Add($argument)}
if($RequirePublic){$start.ArgumentList.Add('--require-public')}
if($RequireStandalone){$start.ArgumentList.Add('--require-standalone')}
$process=[Diagnostics.Process]::Start($start)
if(-not $process.WaitForExit(180000)){$process.Kill($true);$process.WaitForExit();throw 'Core smoke test timed out.'}
$exitCode=$process.ExitCode
$process.Dispose()
$report=Join-Path $ResultRoot 'results.json'
if(-not(Test-Path -LiteralPath $report)){throw 'Smoke test did not write results.json.'}
$tests=@(Get-Content -LiteralPath $report -Raw|ConvertFrom-Json)
if($exitCode -ne 0 -or $tests.Count -lt 20 -or @($tests|Where-Object passed -NE $true).Count){throw "Smoke tests failed (exit $exitCode). Inspect $report"}
Write-Output "$($tests.Count) core checks passed."

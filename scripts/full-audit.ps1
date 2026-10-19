#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ResultRoot,[string]$AppPath='',[string]$RarFixture='',[string]$PanelPhoto='',[int]$SuiteTimeoutMinutes=15)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(-not $AppPath){$local=Join-Path $repo '.local-app-path';$AppPath=if(Test-Path -LiteralPath $local){(Get-Content -LiteralPath $local -Raw).Trim()}else{Join-Path $repo 'artifacts\app\Tangerine.exe'}}
$AppPath=[IO.Path]::GetFullPath($AppPath)
if(-not(Test-Path -LiteralPath $AppPath -PathType Leaf)){throw 'Build the application first or supply -AppPath.'}
$ResultRoot=[IO.Path]::GetFullPath($ResultRoot)
if(Test-Path -LiteralPath $ResultRoot){throw 'Use a fresh audit directory.'}
New-Item -ItemType Directory -Path $ResultRoot|Out-Null
$env:TANGERINE_ENGINE_DIR=Join-Path (Split-Path $AppPath) 'engines'
if($RarFixture){$env:TANGERINE_RAR_FIXTURE=[IO.Path]::GetFullPath($RarFixture)}
if($PanelPhoto){$env:TANGERINE_PANEL_PHOTO=[IO.Path]::GetFullPath($PanelPhoto)}
$env:TEMP=Join-Path $ResultRoot 'temp';$env:TMP=$env:TEMP
New-Item -ItemType Directory -Path $env:TEMP|Out-Null
$dll=Join-Path (Split-Path $AppPath) 'Tangerine.dll'
$hash=(Get-FileHash -LiteralPath $dll).Hash
$suites=@(
    @{Name='conversions';Flag='--conversion-matrix';Report='matrix-results.json'},
    @{Name='expanded';Flag='--audit';Report='audit-results.json'},
    @{Name='file-tools';Flag='--file-actions-test';Report='file-actions-results.json'},
    @{Name='integration';Flag='--self-test';Report='results.json'},
    @{Name='panels';Flag='--panels-test';Report='panel-results.json'},
    @{Name='batch';Flag='--batch-test';Report='batch-results.json'},
    @{Name='interaction';Flag='--interaction-test';Report='interaction-results.json'},
    @{Name='stress';Flag='--stress-test';Report='stress-results.json'}
)
$results=@()
foreach($suite in $suites){
    if((Get-FileHash -LiteralPath $dll).Hash -ne $hash){throw 'Application changed during audit.'}
    $folder=Join-Path $ResultRoot $suite.Name
    $start=[Diagnostics.ProcessStartInfo]::new($AppPath);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.ArgumentList.Add($suite.Flag);$start.ArgumentList.Add($folder)
    $process=[Diagnostics.Process]::Start($start)
    @{Suite=$suite.Name;ProcessId=$process.Id;Started=(Get-Date).ToString('o');ApplicationDllSHA256=$hash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $ResultRoot 'current.json')
    $watch=[Diagnostics.Stopwatch]::StartNew();$timedOut=-not $process.WaitForExit($SuiteTimeoutMinutes*60000)
    if($timedOut){$process.Kill($true);$process.WaitForExit();$exitCode=124}else{$exitCode=$process.ExitCode};$process.Dispose()
    $report=Join-Path $folder $suite.Report
    $checks=if(Test-Path -LiteralPath $report){@(Get-Content -LiteralPath $report -Raw|ConvertFrom-Json)}else{@()}
    $result=[pscustomobject]@{Suite=$suite.Name;ExitCode=$exitCode;Checks=$checks.Count;Passed=@($checks|Where-Object passed -EQ $true).Count;Failed=@($checks|Where-Object passed -EQ $false).Count;Skipped=@($checks|Where-Object skipped -EQ $true).Count;Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2);Report=$report;ApplicationDllSHA256=$hash}
    $results+=$result;$results|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ResultRoot 'summary.json')
    $result|ConvertTo-Json -Compress
}
@{Completed=$true;ApplicationDllSHA256=$hash;FailedSuites=@($results|Where-Object ExitCode -NE 0).Count}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $ResultRoot 'complete.json')
if(@($results|Where-Object ExitCode -NE 0).Count){throw 'At least one audit suite failed. Inspect summary.json and the individual reports.'}

#requires -Version 7.0
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$files=@(& git -C $repo -c core.quotepath=false ls-files)
if($LASTEXITCODE -ne 0 -or $files.Count -eq 0){throw 'No tracked source files. Run git add after reviewing .gitignore.'}
$forbidden=@('research/','.tools/','artifacts/','dist/','.local-app-path','Tangerine/Assets/highlight.wav')
$issues=[Collections.Generic.List[string]]::new()
foreach($file in $files){
    foreach($prefix in $forbidden){if($file.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){$issues.Add("Private/generated file tracked: $file")}}
    if($file -match '\.(lnk|pfx|p12|pyc|exe|dll)$'){$issues.Add("Unexpected binary tracked: $file")}
    if([IO.Path]::GetExtension($file) -notin @('.cs','.xaml','.csproj','.props','.json','.md','.ps1','.py','.yml','.yaml','.config','.manifest')){continue}
    $content=[IO.File]::ReadAllText((Join-Path $repo $file))
    $personalRoot='[A-Za-z]:[\\/]Users[\\/]'
    $privateBuild='[A-Za-z]:[\\/]'+'Codex'+'Build[\\/]'
    $privateKey='-----BEGIN '+'(?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    $accessToken='(?:gh[pousr]_'+'[A-Za-z0-9]{30,}|AKIA'+'[A-Z0-9]{16})'
    if($content -match $personalRoot -or $content -match $privateBuild){$issues.Add("Machine-specific path found: $file")}
    if($content -match $privateKey -or $content -match $accessToken){$issues.Add("Potential credential found: $file")}
}
if($issues.Count){$issues|Write-Output;throw 'Public source check failed.'}
Write-Output "$($files.Count) tracked files passed the publication path/asset/credential checks."

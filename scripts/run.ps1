$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$location = Join-Path $repo '.local-app-path'
$exe = if (Test-Path -LiteralPath $location) { (Get-Content -LiteralPath $location -Raw).Trim() } else { Join-Path $repo 'artifacts\app\Tangerine.exe' }
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the app first with scripts/build.ps1.' }
Start-Process -FilePath $exe -ArgumentList '--tray' -WindowStyle Hidden

#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$AssetsFile,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$records = @()
$packages = @{}
foreach ($entry in $assets.libraries.PSObject.Properties) {
    if ($entry.Value.type -eq 'package') { $packages[$entry.Name] = $entry.Value }
}
# Self-contained runtime/reference packs are restore downloads, not ordinary libraries.
foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
    foreach ($download in $framework.downloadDependencies) {
        if ($download.version -notmatch '^\[([^,\]]+)(?:,\s*\1)?\]$') { throw "Runtime pack is not pinned: $($download.name) $($download.version)" }
        $version = $Matches[1]
        $key = $download.name + '/' + $version
        if (-not $packages.ContainsKey($key)) { $packages[$key] = @{ path=$key.ToLowerInvariant(); sha512=$null } }
    }
}
foreach ($key in $packages.Keys | Sort-Object) {
    $entry = $packages[$key]
    $id,$version = $key -split '/',2
    $package = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $entry.path
        if (Test-Path -LiteralPath $candidate) { $package = $candidate; break }
    }
    if (-not $package) { throw "Restored package not found: $key" }
    $specFile = Get-ChildItem -LiteralPath $package -Filter '*.nuspec' -File | Select-Object -First 1
    [xml]$spec = Get-Content -LiteralPath $specFile.FullName -Raw
    $metadata = $spec.package.metadata
    $license = if ($metadata.license) { $metadata.license.InnerText } else { [string]$metadata.licenseUrl }
    $target = Join-Path $Destination ('licenses/packages/' + $id + '/' + $version)
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $package -File | Where-Object Name -Match '(?i)license|notice|copyright') { Copy-Item -LiteralPath $file.FullName -Destination $target }
    $sha512 = $entry.sha512
    if (-not $sha512) { $sha512 = (Get-Content -LiteralPath (Join-Path $package ($id.ToLowerInvariant()+'.'+$version+'.nupkg.sha512')) -Raw).Trim() }
    $records += [ordered]@{name=$id;version=$version;license=$license;authors=[string]$metadata.authors;projectUrl=[string]$metadata.projectUrl;packageUrl="https://www.nuget.org/packages/$id/$version";sha512=$sha512}
}
ConvertTo-Json -InputObject $records -Depth 6 | Set-Content -LiteralPath (Join-Path $Destination 'dependencies.json') -Encoding utf8

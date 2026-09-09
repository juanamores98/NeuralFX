param([Parameter(Mandatory)][string]$GameRoot, [string]$RunName = 'city-baseline', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRoot ('artifacts/evidence/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$files = @('dxgi.dll','dlss5-feed.addon64','renodx-dlss5.addon64','nvngx_dlss.dll','nvngx_dlssnr.dll','dlss5-feed.cfg','ReShade.ini','ReShadePreset.ini')
$manifest = [ordered]@{
    runName=$RunName; timestampUtc=[DateTime]::UtcNow.ToString('o'); sourceCommit=(& git -C $taskRoot rev-parse HEAD)
    expectedAbi=3; expectedBridgeBuild=4; expectedIpc=4
    installed=@{}; loadedModuleHash=$null; loadedModules=@(); driverInventory=@()
    nrConfirmed=$false; uiProtected=$false; cityAccepted=$false
    note='Installed hashes describe files on disk. A loaded module name does not certify loaded memory bytes or an NR evaluation. Add the in-game exported state and controlled recordings separately.'
}
foreach ($name in $files) {
    $path = Join-Path $GameRoot $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $entry = [ordered]@{sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();length=(Get-Item -LiteralPath $path).Length}
        if ($name.EndsWith('.dll')) { $entry.signature=(Get-AuthenticodeSignature -LiteralPath $path).Status.ToString();$entry.version=(Get-Item -LiteralPath $path).VersionInfo.FileVersion }
        $manifest.installed[$name]=$entry
    }
}
Get-Process -Name Cities -ErrorAction SilentlyContinue | ForEach-Object {
    try { foreach ($module in $_.Modules) { if ($files -contains $module.ModuleName) { $manifest.loadedModules += [ordered]@{processId=$_.Id;name=$module.ModuleName} } } }
    catch { $manifest.note += ' Process module enumeration unavailable.' }
}
try { $manifest.driverInventory = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,PNPDeviceID) } catch { $manifest.note += ' Driver inventory unavailable.' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'tuple.json') -Encoding utf8
# Copy only known component logs; never collect saves, arbitrary folders or screenshots.
foreach ($name in @('dlss5-feed.log','ReShade.log')) {
    $path = Join-Path $GameRoot $name
    if (Test-Path -LiteralPath $path -PathType Leaf) { Get-Content -LiteralPath $path -Tail 2500 | Set-Content -LiteralPath (Join-Path $OutputDirectory $name) -Encoding utf8 }
}
Write-Output "Evidence saved: $OutputDirectory"

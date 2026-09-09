param([Parameter(Mandatory)][string]$GameRoot, [switch]$SrOnly, [string]$RenoDxOverride, [int]$Width = 1920, [int]$Height = 1080)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mode = if ($SrOnly) { 'sr' } else { 'nr' }
$fixture = Join-Path $root ('artifacts/graphics-' + $mode + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $GameRoot 'NeuralFX_Manifest.json') -Raw | ConvertFrom-Json
if (!$manifest.InstalledFiles -or $manifest.SchemaVersion -ne 2) { throw 'A NeuralFX v2 installation manifest is required' }
foreach ($relative in $manifest.InstalledFiles) {
    $source = [IO.Path]::GetFullPath((Join-Path $GameRoot $relative))
    $target = [IO.Path]::GetFullPath((Join-Path $fixture $relative))
    if (!$source.StartsWith([IO.Path]::GetFullPath($GameRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($fixture.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'out/NeuralFX.Smoke.exe') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'out/dlss5-feed.addon64') -Destination $fixture -Force
if ($RenoDxOverride) { Copy-Item -LiteralPath $RenoDxOverride -Destination (Join-Path $fixture 'renodx-dlss5.addon64') -Force }
if ($SrOnly) {
    # Physically remove the NR consumer from this isolated copy for the carrier control.
    $consumer = Join-Path $fixture 'renodx-dlss5.addon64'
    if (Test-Path -LiteralPath $consumer) { Move-Item -LiteralPath $consumer -Destination ($consumer + '.disabled') }
}
$tuple = [ordered]@{ mode=$mode; sourceCommit=(& git -C $root rev-parse HEAD); width=$Width; height=$Height; startedUtc=[DateTime]::UtcNow.ToString('o'); files=@{} }
Get-ChildItem -LiteralPath $fixture -File | ForEach-Object { $tuple.files[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
$tuple | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixture 'tuple.json') -Encoding utf8
$process = Start-Process -FilePath (Join-Path $fixture 'NeuralFX.Smoke.exe') -ArgumentList @($Width, $Height) -WorkingDirectory $fixture -WindowStyle Hidden -RedirectStandardOutput (Join-Path $fixture 'fixture.log') -PassThru
if (!$process.WaitForExit(55000)) { $process.Kill(); throw "Graphics fixture exceeded 55 seconds: $fixture" }
Write-Output "Graphics fixture: $fixture"
Get-Content -LiteralPath (Join-Path $fixture 'fixture.log')
Get-Content -LiteralPath (Join-Path $fixture 'dlss5-feed.log') -Tail 15
if ($process.ExitCode -ne 0) { throw "Graphics fixture exited with $($process.ExitCode)" }
$feedLog = Get-Content -LiteralPath (Join-Path $fixture 'dlss5-feed.log') -Raw
if ($feedLog -match 'stopped:|Close\(\) failed|### CRASH' -or $feedLog -notmatch 'frame 3 delivered') { throw "Pipeline failed to deliver frames: $fixture" }
if (!$SrOnly) {
    $consumerLogSignal = (Get-Content -LiteralPath (Join-Path $fixture 'ReShade.log') -Raw) -match 'inline feature 18 evaluation succeeded'
    Write-Output "Consumer historical log signal: $consumerLogSignal. Not per-frame NR confirmation or city acceptance."
}

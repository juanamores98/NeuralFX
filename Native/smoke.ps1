param([Parameter(Mandatory)][string]$GameRoot, [switch]$SrOnly, [string]$RenoDxOverride, [int]$Width = 1920, [int]$Height = 1080)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mode = if ($SrOnly) {
    # A clean carrier comparison physically excludes the NR consumer from the fixture.
    # Do not claim a key in ReShade.ini disables a proprietary backend.
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
    Write-Output "Consumer historical log signal: $consumerLogSignal. This is not per-frame NR confirmation or city acceptance."
}

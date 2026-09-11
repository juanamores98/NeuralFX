param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines',
    [string]$OutputDirectory,
    [int]$FrameWidth = 3840, [int]$FrameHeight = 2160,
    [int]$SceneWidth = 3840, [int]$SceneHeight = 1933,
    [int]$SceneX = 0, [int]$SceneY = 0
)
$ErrorActionPreference = 'Stop'
# Read-only against the game. Each run creates a new, reviewable candidate directory.
# SceneX/Y use the D3D top-left origin, not Unity's bottom-left pixelRect origin.
if ($FrameWidth -le 0 -or $FrameHeight -le 0 -or $SceneWidth -le 0 -or $SceneHeight -le 0 -or
    $SceneX -lt 0 -or $SceneY -lt 0 -or $SceneX + $SceneWidth -gt $FrameWidth -or $SceneY + $SceneHeight -gt $FrameHeight) {
    throw 'The camera rectangle must fit inside the presented frame.'
}
$gamePath = (Resolve-Path -LiteralPath $GameRoot).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/terrain-trial-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputPath.TrimEnd('\', '/') -eq $gamePath.TrimEnd('\', '/') -or
    $outputPath.StartsWith($gamePath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be outside the game directory.'
}
if (Test-Path -LiteralPath $outputPath) { throw 'Use a new output directory; existing evidence is preserved.' }
$shaderPath = Join-Path $gamePath 'reshade-shaders/Shaders/DLSS5_Feed.fx'
$shader = [IO.File]::ReadAllText($shaderPath)
$signature = 'float RawDepth(float2 uv)'
if (($shader.Split(@($signature), [StringSplitOptions]::None).Count - 1) -ne 1 -or $shader.Contains('RawDepthLegacy')) {
    throw 'Expected exactly one unpatched RawDepth function.'
}
$start = $shader.IndexOf($signature, [StringComparison]::Ordinal)
$open = $shader.IndexOf('{', $start)
$close = $shader.IndexOf('}', $open)
# The pinned function contains no nested block. Stop if the source changes.
if ($open -lt 0 -or $close -lt 0 -or $shader.Substring($open + 1, $close - $open - 1).Contains('{')) {
    throw 'RawDepth layout changed; inspect the shader before patching.'
}
$fixPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'Native/shaders/NeuralFXDepthRect.fxh'
$fix = [IO.File]::ReadAllText($fixPath)
# Standalone trial defaults; the integrated bridge overrides these in the shipped version.
$fix = $fix.Replace('NFX_DepthRectEnabled < hidden = true; > = false', 'NFX_DepthRectEnabled < hidden = true; > = true')
$fix = $fix.Replace('NFX_DepthSceneRect < hidden = true; > = float4(0, 0, 0, 0)', "NFX_DepthSceneRect < hidden = true; > = float4($SceneX, $SceneY, $SceneWidth, $SceneHeight)")
$fix = $fix.Replace('NFX_DepthFrameSize < hidden = true; > = float2(0, 0)', "NFX_DepthFrameSize < hidden = true; > = float2($FrameWidth, $FrameHeight)")
$legacy = $shader.Substring($start, $close - $start + 1).Replace($signature, 'float RawDepthLegacy(float2 uv)')
$candidate = $shader.Substring(0, $start) + $legacy + "`n" + $fix + $shader.Substring($close + 1)

New-Item -ItemType Directory -Path (Join-Path $outputPath 'original'), (Join-Path $outputPath 'candidate') | Out-Null
Copy-Item -LiteralPath $shaderPath -Destination (Join-Path $outputPath 'original/DLSS5_Feed.fx')
Copy-Item -LiteralPath (Join-Path $gamePath 'ReShade.ini'), (Join-Path $gamePath 'ReShadePreset.ini'), (Join-Path $gamePath 'dlss5-feed.cfg') -Destination (Join-Path $outputPath 'original')
[IO.File]::WriteAllText((Join-Path $outputPath 'candidate/DLSS5_Feed.fx'), $candidate)
$manifest = [ordered]@{
    status = 'Candidate only; not installed or visually validated'
    source = $shaderPath
    sourceSha256 = (Get-FileHash -LiteralPath $shaderPath).Hash
    candidateSha256 = (Get-FileHash -LiteralPath (Join-Path $outputPath 'candidate/DLSS5_Feed.fx')).Hash
    frame = @($FrameWidth, $FrameHeight)
    sceneRectTopLeft = @($SceneX, $SceneY, $SceneWidth, $SceneHeight)
    scope = 'Native Unity motion at 100%; only RawDepth changes, optical validation remains unchanged'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'manifest.json') -Encoding utf8
Write-Output "Prepared without modifying the game: $outputPath"

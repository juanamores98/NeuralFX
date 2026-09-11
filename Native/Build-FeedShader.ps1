param([Parameter(Mandatory)][string]$UpstreamRoot, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
# Always patch the pinned source, never the shader already installed in the game.
$shader = (& git -C $UpstreamRoot show HEAD:shaders/DLSS5_Feed.fx) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the pinned companion shader.' }
$signature = 'float RawDepth(float2 uv)'
if (($shader.Split(@($signature), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'RawDepth signature changed.' }
$start = $shader.IndexOf($signature, [StringComparison]::Ordinal)
$open = $shader.IndexOf('{', $start)
$close = $shader.IndexOf('}', $open)
if ($close -lt 0 -or $shader.Substring($open + 1, $close - $open - 1).Contains('{')) { throw 'RawDepth body changed.' }
$legacy = $shader.Substring($start, $close - $start + 1).Replace($signature, 'float RawDepthLegacy(float2 uv)')
$fix = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'shaders/NeuralFXDepthRect.fxh'))
[IO.File]::WriteAllText($OutputPath, $shader.Substring(0, $start) + $legacy + "`n" + $fix + $shader.Substring($close + 1))

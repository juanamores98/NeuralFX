param([string]$ToolchainPath = (Join-Path $PSScriptRoot 'toolchain'), [switch]$SmokeHarness)
$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$upstreamRoot = Join-Path $nativeRoot 'upstream'
$commit = '927d76d30e888bce497f5c5f8d496fcb696da335'
if (-not (Test-Path -LiteralPath $upstreamRoot)) {
    & git clone --depth 1 --branch v0.14.0-beta.4 https://github.com/jlrouzies-fr/DLSS5-Feeder.git $upstreamRoot
    if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
}
$actual = & git -C $upstreamRoot rev-parse HEAD
if ($actual -ne $commit) { throw 'Unexpected upstream commit; inspect before updating patches.' }
& (Join-Path $nativeRoot 'fetch-deps.ps1')
# Read the pinned original from git; do not accumulate patches across builds.
$source = (& git -C $upstreamRoot show HEAD:src/dlss5-feed.cpp) -join "`n"
function Replace-Once([string]$Text, [string]$Before, [string]$After) {
    if (($Text.Split(@($Before), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Native patch marker missing or ambiguous: $Before" }
    return $Text.Replace($Before, $After)
}
$source = Replace-Once $source '#define FEED_VERSION "0.14.0-beta.4"' ('#include "neuralfx_bridge.h"' + "`n" + '#define FEED_VERSION "0.14.0-beta.4-neuralfx.3"')
$source = Replace-Once $source '    CK("queue Signal(fence12)");' @'
    CK("queue Signal(fence12)");
    if (FAILED(neuralfx_signal) || FAILED(g.dev12->GetDeviceRemovedReason()))
    {
        FeedDisable("D3D12 submission/fence failed; no result will be copied back");
        return 0;
    }
'@
# Only EndCommands owns this exact sequence; other queue signals retain their upstream behavior.
$source = Replace-Once $source ('    g.queue->Signal(g.fence12, v);' + "`n" + '    CK("queue Signal(fence12)");') ('    const HRESULT neuralfx_signal = g.queue->Signal(g.fence12, v);' + "`n" + '    CK("queue Signal(fence12)");')
$start = $source.IndexOf('static void FeedFrame11(')
$end = $source.IndexOf("`nstatic void FeedFrame(", $start)
if ($start -lt 0 -or $end -lt 0) { throw 'D3D11 function boundary changed.' }
$body = $source.Substring($start, $end - $start)
$marker = '    g.mask_ok = mask != nullptr && kd.Width == cd.Width && kd.Height == cd.Height && kd.Format == DXGI_FORMAT_R8_UNORM;'
$nativeMvInjection = @'
    NeuralFxFrame neuralfx_frame = {};
    const bool neuralfx_valid = NeuralFxTakeFrame(cd.Width, cd.Height, neuralfx_frame);
    if (neuralfx_valid && neuralfx_frame.motion_vectors_ptr != 0)
    {
        IUnknown *unk = reinterpret_cast<IUnknown *>(neuralfx_frame.motion_vectors_ptr);
        ID3D11ShaderResourceView *srv = nullptr;
        ID3D11Resource *res = nullptr;
        if (SUCCEEDED(unk->QueryInterface(__uuidof(ID3D11ShaderResourceView), reinterpret_cast<void **>(&srv))))
        {
            srv->GetResource(&res);
            srv->Release();
        }
        else if (SUCCEEDED(unk->QueryInterface(__uuidof(ID3D11Resource), reinterpret_cast<void **>(&res))))
        {
        }
        if (res != nullptr)
        {
            D3D11_TEXTURE2D_DESC nmd = {};
            ID3D11Texture2D *native_mv = AsTexture2D(res, &nmd);
            res->Release();
            if (native_mv != nullptr && nmd.Width == cd.Width && nmd.Height == cd.Height)
            {
                SafeRelease(mv);
                mv = native_mv;
                md = nmd;
            }
            else
            {
                SafeRelease(native_mv);
            }
        }
    }
'@
$body = Replace-Once $body $marker ($marker + "`n" + $nativeMvInjection)
$body = Replace-Once $body 'ep.InReset           = reset;' 'ep.InReset           = reset || (neuralfx_valid && NeuralFxResetNeeded(neuralfx_frame));'
$body = Replace-Once $body 'ep.InJitterOffsetX   = g.sr_active ? static_cast<float>(g_cfg.jitter_sign) * g.jitter_x : 0.0f;' 'ep.InJitterOffsetX   = neuralfx_valid ? neuralfx_frame.jitter_x : (g.sr_active ? static_cast<float>(g_cfg.jitter_sign) * g.jitter_x : 0.0f);'
$body = Replace-Once $body 'ep.InJitterOffsetY   = g.sr_active ? static_cast<float>(g_cfg.jitter_sign) * g.jitter_y : 0.0f;' 'ep.InJitterOffsetY   = neuralfx_valid ? neuralfx_frame.jitter_y : (g.sr_active ? static_cast<float>(g_cfg.jitter_sign) * g.jitter_y : 0.0f);'
$body = Replace-Once $body 'ep.InMVScaleX        = g_cfg.mv_scale_x;' 'ep.InMVScaleX        = (neuralfx_valid && neuralfx_frame.mv_scale_x != 0.0f) ? neuralfx_frame.mv_scale_x : g_cfg.mv_scale_x;'
$body = Replace-Once $body 'ep.InMVScaleY        = g_cfg.mv_scale_y;' 'ep.InMVScaleY        = (neuralfx_valid && neuralfx_frame.mv_scale_y != 0.0f) ? neuralfx_frame.mv_scale_y : g_cfg.mv_scale_y;'
$body = Replace-Once $body 'AbortCommands();  // never execute a list NGX crashed while recording' ('AbortCommands();  // never execute a list NGX crashed while recording' + "`n                    if (neuralfx_valid) NeuralFxEvaluated(neuralfx_frame, false, g.width, g.height);")
# NGX success means recording succeeded. A failed Close must never be reported as a
# delivered frame, copied back, or used to acknowledge a camera-history reset.
$body = Replace-Once $body 'const UINT64 v_out = EndCommands();' @'
const UINT64 v_out = EndCommands();
                if (neuralfx_valid) NeuralFxEvaluated(neuralfx_frame, !NVSDK_NGX_FAILED(re) && v_out != 0, g.width, g.height);
                if (v_out == 0)
                {
                    FeedDisable("the D3D12 command list could not be submitted; see dlss5-feed.log");
                    g.frame_ready = false;
                    ok = false;
                }
                else
'@
$source = $source.Substring(0, $start) + $body + $source.Substring($end)
Set-Content -LiteralPath (Join-Path $upstreamRoot 'src/dlss5-feed.cpp') -Value $source -Encoding utf8
Copy-Item -LiteralPath (Join-Path $nativeRoot 'neuralfx_bridge.h') -Destination (Join-Path $upstreamRoot 'src/neuralfx_bridge.h') -Force
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
Push-Location $upstreamRoot
try {
    if ($vs) {
        $env:VCVARSALL = Join-Path $vs 'VC/Auxiliary/Build/vcvarsall.bat'
        & cmd.exe /d /c build.bat
    } else {
        $compiler = Get-ChildItem -LiteralPath (Join-Path $ToolchainPath 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if (!$compiler) { throw 'Visual Studio C++ x64 build tools or an extracted toolchain are required.' }
        $sdk = Join-Path $ToolchainPath 'microsoft.windows.sdk.cpp/c'
        $sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdk 'Include') -Directory | Sort-Object Name -Descending | Select-Object -First 1
        $runtime = Get-ChildItem -LiteralPath (Join-Path $ToolchainPath 'VC/Redist/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
        $env:PATH = (Join-Path $compiler.FullName 'bin/Hostx64/x64') + ';' + (Join-Path $sdk "bin/$($sdkVersion.Name)/x64") + ';' + (Join-Path $runtime.FullName 'x64/Microsoft.VC145.CRT') + ';' + $env:PATH
        $env:INCLUDE = (Join-Path $compiler.FullName 'include') + ';' + (Join-Path $sdkVersion.FullName 'ucrt') + ';' + (Join-Path $sdkVersion.FullName 'shared') + ';' + (Join-Path $sdkVersion.FullName 'um')
        $env:LIB = (Join-Path $compiler.FullName 'lib/x64') + ';' + (Join-Path $compiler.FullName 'lib/onecore/x64') + ';' + (Join-Path $ToolchainPath 'microsoft.windows.sdk.cpp.x64/c/um/x64') + ';' + (Join-Path $ToolchainPath 'microsoft.windows.sdk.cpp.x64/c/ucrt/x64')
        New-Item -ItemType Directory -Path 'build' -Force | Out-Null
        & rc.exe /nologo /fo build/version.res src/version.rc
        if ($LASTEXITCODE -ne 0) { throw 'Resource compilation failed.' }
        & cl.exe /nologo /LD /EHsc /O2 /MD /W3 /std:c++20 /Iexternal/reshade/include /Iexternal/ngx /Iexternal/vulkan /Iexternal/imgui /Iexternal/minhook/include /Fobuild/ /Fdbuild/ src/dlss5-feed.cpp external/minhook/src/buffer.c external/minhook/src/hook.c external/minhook/src/trampoline.c external/minhook/src/hde/hde64.c /link /OUT:build/dlss5-feed.addon64 build/version.res external/ngx/libs/nvsdk_ngx_d.lib version.lib kernel32.lib user32.lib advapi32.lib ole32.lib
    }
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    if ($vs) {
        $testSource = Join-Path $nativeRoot 'bridge_tests.cpp'
        $testCommand = 'call "' + $env:VCVARSALL + '" x64 && cl.exe /nologo /EHsc /W3 /std:c++20 /Fobuild/ /Febuild/bridge-tests.exe "' + $testSource + '"'
        & cmd.exe /d /c $testCommand
    } else {
        & cl.exe /nologo /EHsc /W3 /std:c++20 /Fobuild/ /Febuild/bridge-tests.exe (Join-Path $nativeRoot 'bridge_tests.cpp')
    }
    if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }
    & .\build\bridge-tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Native bridge tests failed.' }
    if ($SmokeHarness) {
        $smokeSource = Join-Path $nativeRoot 'smoke_host.cpp'
        if ($vs) {
            & cmd.exe /d /c ('call "' + $env:VCVARSALL + '" x64 && cl.exe /nologo /EHsc /W3 /std:c++20 /Fobuild/ /Febuild/NeuralFX.Smoke.exe "' + $smokeSource + '" user32.lib')
        } else {
            & cl.exe /nologo /EHsc /W3 /std:c++20 /Fobuild/ /Febuild/NeuralFX.Smoke.exe $smokeSource user32.lib
        }
        if ($LASTEXITCODE -ne 0) { throw 'Graphics fixture compilation failed.' }
    }
}
finally { Pop-Location }
$output = Join-Path $nativeRoot 'out'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $upstreamRoot 'build/dlss5-feed.addon64') -Destination $output -Force
if ($SmokeHarness) { Copy-Item -LiteralPath (Join-Path $upstreamRoot 'build/NeuralFX.Smoke.exe') -Destination $output -Force }
Write-Output "Native artifact: $output/dlss5-feed.addon64"

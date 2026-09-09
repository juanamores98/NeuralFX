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
$source = Replace-Once $source '#define FEED_VERSION "0.14.0-beta.4"' ('#include "neuralfx_inputs.h"' + "`n" + '#define FEED_VERSION "0.14.0-beta.4-neuralfx.4"')
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
    NeuralFxFrameV3 neuralfx_frame = {};
    const bool neuralfx_valid = NeuralFxTakeFrame(cd.Width, cd.Height, neuralfx_frame);
    if (!neuralfx_valid) {
        SafeRelease(color); SafeRelease(mv); SafeRelease(depth); SafeRelease(mask);
        return; // never evaluate a stale frame or an auxiliary Present
    }
    NeuralFxPendingOutput* neuralfx_output = NeuralFxPrepareOutput(ctx);
    if (!neuralfx_output) {
        SafeRelease(color); SafeRelease(mv); SafeRelease(depth); SafeRelease(mask);
        return; // bounded queue: leave the current scene untouched
    }
    bool neuralfx_committed = false;
    ID3D11Device* neuralfx_device = nullptr; ctx->GetDevice(&neuralfx_device);
    NeuralFxSelectedMotion neuralfx_motion;
    neuralfx_motion.Select(neuralfx_device, neuralfx_frame, mv,
        reinterpret_cast<ID3D11ShaderResourceView*>(mv_srv.handle), g_cfg.mv_scale_x, g_cfg.mv_scale_y);
    // Device identity comes from the actual rendering device, never registry order.
    IDXGIDevice* neuralfx_dxgi = nullptr;
    if (SUCCEEDED(neuralfx_device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void**>(&neuralfx_dxgi)))) {
        IDXGIAdapter* adapter = nullptr;
        if (SUCCEEDED(neuralfx_dxgi->GetAdapter(&adapter))) {
            DXGI_ADAPTER_DESC info = {}; adapter->GetDesc(&info);
            { std::lock_guard<std::mutex> lock(nfx_lock); nfx_result.adapter_low = info.AdapterLuid.LowPart; nfx_result.adapter_high = info.AdapterLuid.HighPart; }
            adapter->Release();
        }
        neuralfx_dxgi->Release();
    }
    neuralfx_device->Release();
    static uint32_t neuralfx_previous_provider = 0;
    if (neuralfx_previous_provider != neuralfx_motion.provider) { g.need_reset = true; neuralfx_previous_provider = neuralfx_motion.provider; }
'@
$body = Replace-Once $body $marker ($marker + "`n" + $nativeMvInjection)
$body = Replace-Once $body 'CopyOrResampleInputs(ctx, color, mv, depth, mask,' 'CopyOrResampleInputs(ctx, color, neuralfx_motion.texture, depth, mask,'
$body = Replace-Once $body 'reinterpret_cast<ID3D11ShaderResourceView *>(mv_srv.handle),' 'neuralfx_motion.view,'
$body = Replace-Once $body 'ep.InReset           = reset;' 'ep.InReset           = reset || NeuralFxResetNeeded(neuralfx_frame);'
$body = Replace-Once $body 'ep.InMVScaleX        = g_cfg.mv_scale_x;' 'ep.InMVScaleX        = neuralfx_motion.scale_x;'
$body = Replace-Once $body 'ep.InMVScaleY        = g_cfg.mv_scale_y;' 'ep.InMVScaleY        = neuralfx_motion.scale_y;'
$body = Replace-Once $body 'AbortCommands();  // never execute a list NGX crashed while recording' ('AbortCommands();  // never execute a list NGX crashed while recording' + "`n                    NeuralFxRecorded(neuralfx_frame, static_cast<int32_t>(ecode), false, g.width, g.height, neuralfx_motion.provider);")
$body = Replace-Once $body 'const UINT64 v_out = EndCommands();' @'
const UINT64 v_out = EndCommands();
                NeuralFxRecorded(neuralfx_frame, NVSDK_NGX_FAILED(re) ? static_cast<int32_t>(re) : (v_out ? 0 : -1), v_out != 0, g.width, g.height, neuralfx_motion.provider);
                if (v_out == 0)
                {
                    FeedDisable("D3D12 submission failed; preserving the current scene");
                    g.frame_ready = false;
                    ok = false;
                }
                else
'@
$body = Replace-Once $body ('                    g.ctx4->Wait(g.fence11, v_out);' + "`n" + '                    BlitOutputToBackbuffer(ctx, rtv11);') @'
                    const HRESULT neuralfx_wait = g.ctx4->Wait(g.fence11, v_out);
                    if (SUCCEEDED(neuralfx_wait) && SUCCEEDED(g.dev11->GetDeviceRemovedReason())) {
                        BlitOutputToBackbuffer(ctx, rtv11);
                        neuralfx_committed = true;
                    } else {
                        NeuralFxRecorded(neuralfx_frame, static_cast<int32_t>(neuralfx_wait), false, g.width, g.height, neuralfx_motion.provider);
                        FeedDisable("D3D11 result wait failed; restart the game");
                    }
'@
$body = Replace-Once $body ('    SafeRelease(color);' + "`n" + '    SafeRelease(mv);') ('    NeuralFxFinishOutput(neuralfx_output, neuralfx_frame, neuralfx_committed);' + "`n" + '    SafeRelease(color);' + "`n" + '    SafeRelease(mv);')
$source = $source.Substring(0, $start) + $body + $source.Substring($end)
$source = Replace-Once $source '    if (!g_cfg.enabled || g.disabled || g_cfg.mode == 0) return;' '    if (!NeuralFxEnabled() || !g_cfg.enabled || g.disabled || g_cfg.mode == 0) return;'
$source = Replace-Once $source 'static void DrawOverlay(reshade::api::effect_runtime *rt)' @'
// Apply only the techniques owned by NeuralFX. Foreign techniques keep their state.
static void NeuralFxBeginEffects(reshade::api::effect_runtime* rt, reshade::api::command_list*, reshade::api::resource_view, reshade::api::resource_view) {
    NeuralFxPollOutputs();
    const bool active = NeuralFxEnabled();
    static bool previous = false;
    if (active != previous) { g.need_reset = true; previous = active; }
    auto feed = rt->find_technique("DLSS5_Feed.fx", "DLSS5_Feed");
    auto cas = rt->find_technique("NeuralFX_CAS.fx", "NeuralFX_CAS");
    if (feed.handle && rt->get_technique_state(feed) != active) rt->set_technique_state(feed, active);
    if (cas.handle && rt->get_technique_state(cas) != active) rt->set_technique_state(cas, active);
}
static void DrawOverlay(reshade::api::effect_runtime *rt)
'@
$source = Replace-Once $source '        reshade::register_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' ('        reshade::register_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' + "`n" + '        reshade::register_event<reshade::addon_event::reshade_begin_effects>(NeuralFxBeginEffects);')
$source = Replace-Once $source '        reshade::unregister_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' ('        reshade::unregister_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' + "`n" + '        reshade::unregister_event<reshade::addon_event::reshade_begin_effects>(NeuralFxBeginEffects);')
Set-Content -LiteralPath (Join-Path $upstreamRoot 'src/dlss5-feed.cpp') -Value $source -Encoding utf8
Get-ChildItem -LiteralPath $nativeRoot -Filter 'neuralfx_*.h' | Copy-Item -Destination (Join-Path $upstreamRoot 'src') -Force
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

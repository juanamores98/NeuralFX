param([string]$ToolchainPath = (Join-Path $PSScriptRoot 'toolchain'), [switch]$SmokeHarness)
$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$upstreamRoot = Join-Path $nativeRoot 'upstream'
$commit = '3f624855276c4bde55145c712782477639b30e85'
if (-not (Test-Path -LiteralPath $upstreamRoot)) {
    & git clone --depth 1 --branch v0.15.1 https://github.com/jlrouzies-fr/DLSS5-Feeder.git $upstreamRoot
    if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
}
$actual = & git -C $upstreamRoot rev-parse HEAD
if ($actual -ne $commit) { throw 'Unexpected upstream commit; inspect before updating patches.' }
& (Join-Path $nativeRoot 'fetch-deps.ps1')
# Read the pinned original from git; do not accumulate patches across builds.
$source = (& git -C $upstreamRoot show HEAD:src/dlss5-feed.cpp) -join "`n"
function Replace-Once([string]$Text, [string]$Before, [string]$After) {
    # Git checkout may use CRLF while git-show above is joined with LF.
    $Text = $Text.Replace("`r`n", "`n"); $Before = $Before.Replace("`r`n", "`n"); $After = $After.Replace("`r`n", "`n")
    if (($Text.Split(@($Before), [StringSplitOptions]::None).Count - 1) -ne 1) { throw "Native patch marker missing or ambiguous: $Before" }
    return $Text.Replace($Before, $After)
}
$source = Replace-Once $source '#define FEED_VERSION "0.15.1"' ('#include "neuralfx_inputs.h"' + "`n" + 'static NeuralFxHealth neuralfx_health = { sizeof(NeuralFxHealth), 1 };' + "`n" + '#define FEED_VERSION "0.15.1-neuralfx.10"')
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
        // Un rechazo aqui era invisible: el pipeline se quedaba callado para siempre.
        static uint64_t neuralfx_gate_at = 0;
        const NeuralFxGateReport neuralfx_gate = NeuralFxGate(true);
        const uint64_t neuralfx_gate_now = NeuralFxNow();
        if (neuralfx_gate_now - neuralfx_gate_at > 5000) {
            neuralfx_gate_at = neuralfx_gate_now;
            Log("[neuralfx] sin entrada del mod en %u presents: enabled=%d mod=%ux%u escena=%ux%u frame=%u tomado=%u edad=%u ms | rechazos jitter=%u latido=%u epoca=%u no-nuevo=%u decode=%u",
                neuralfx_gate.misses, neuralfx_gate.enabled ? 1 : 0,
                neuralfx_gate.width, neuralfx_gate.height, cd.Width, cd.Height,
                neuralfx_gate.frame, neuralfx_gate.taken, neuralfx_gate.age_ms,
                neuralfx_gate.jitter, neuralfx_gate.heartbeat, neuralfx_gate.epoch,
                neuralfx_gate.stale, neuralfx_gate.decode);
            NeuralFxGateLogged();
        }
        SafeRelease(color); SafeRelease(mv); SafeRelease(depth); SafeRelease(mask);
        return; // never evaluate a stale frame or an auxiliary Present
    }
    NeuralFxPendingOutput* neuralfx_output = NeuralFxPrepareOutput(ctx);
    if (!neuralfx_output) {
        static uint64_t neuralfx_queue_at = 0;
        const uint64_t neuralfx_queue_now = NeuralFxNow();
        if (neuralfx_queue_now - neuralfx_queue_at > 5000) {
            neuralfx_queue_at = neuralfx_queue_now;
            Log("[neuralfx] cola de salidas saturada: ninguna consulta de las 8 ha terminado; se conserva la escena actual");
        }
        { std::lock_guard<std::mutex> lock(nfx_lock); NeuralFxAbandonLocked(neuralfx_frame); }
        SafeRelease(color); SafeRelease(mv); SafeRelease(depth); SafeRelease(mask);
        return; // bounded queue: leave the current scene untouched
    }
    bool neuralfx_committed = false;
    ID3D11Device* neuralfx_device = nullptr; ctx->GetDevice(&neuralfx_device);
    NeuralFxSelectedMotion neuralfx_motion;
    const bool neuralfx_motion_ok = neuralfx_motion.Select(neuralfx_device, neuralfx_frame, mv,
        reinterpret_cast<ID3D11ShaderResourceView*>(mv_srv.handle), g_cfg.mv_scale_x, g_cfg.mv_scale_y);
    // Device identity comes from the actual rendering device, never registry order.
    static ID3D11Device* neuralfx_identity_device = nullptr;
    static uint32_t neuralfx_identity_epoch = 0;
    IDXGIDevice* neuralfx_dxgi = nullptr;
    if ((neuralfx_identity_device != neuralfx_device || neuralfx_identity_epoch != neuralfx_frame.epoch) && SUCCEEDED(neuralfx_device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void**>(&neuralfx_dxgi)))) {
        IDXGIAdapter* adapter = nullptr;
        if (SUCCEEDED(neuralfx_dxgi->GetAdapter(&adapter))) {
            DXGI_ADAPTER_DESC info = {}; adapter->GetDesc(&info);
            { std::lock_guard<std::mutex> lock(nfx_lock); nfx_result.adapter_low = info.AdapterLuid.LowPart; nfx_result.adapter_high = info.AdapterLuid.HighPart; }
            adapter->Release();
        }
        neuralfx_dxgi->Release(); neuralfx_identity_device = neuralfx_device; neuralfx_identity_epoch = neuralfx_frame.epoch;
    }
    neuralfx_device->Release();
    static uint32_t neuralfx_previous_provider = 0;
    if (neuralfx_previous_provider != neuralfx_motion.provider) { g.need_reset = true; neuralfx_previous_provider = neuralfx_motion.provider; }
'@
$body = Replace-Once $body $marker ($marker + "`n" + $nativeMvInjection)
$body = Replace-Once $body '    if ((g.frames_done % 60) == 0 && CfgReload()) g.frame_ready = false;' @'
    if ((g.frames_done % 60) == 0 && CfgReload()) g.frame_ready = false;
    if (nfx_work_override) {
        g_cfg.work_resolution = nfx_work_override;
        g_cfg.work_upscale = nfx_work_override < 100 ? 1 : 0;
        g_cfg.work_sharpness = 0;
    }
'@
$body = Replace-Once $body '    bool ok = true;' '    bool ok = neuralfx_motion_ok;'
$body = Replace-Once $body 'CopyOrResampleInputs(ctx, color, mv, depth, mask,' 'CopyOrResampleInputs(ctx, color, neuralfx_motion.texture, depth, mask,'
$body = Replace-Once $body 'reinterpret_cast<ID3D11ShaderResourceView *>(mv_srv.handle),' 'neuralfx_motion.view,'
$body = Replace-Once $body 'ep.InReset           = reset;' 'ep.InReset           = reset || NeuralFxResetNeeded(neuralfx_frame);'
$body = Replace-Once $body 'ep.InMVScaleX        = g_cfg.mv_scale_x;' 'ep.InMVScaleX        = neuralfx_motion.scale_x;'
$body = Replace-Once $body 'ep.InMVScaleY        = g_cfg.mv_scale_y;' 'ep.InMVScaleY        = neuralfx_motion.scale_y;'
$body = Replace-Once $body 'AbortCommands();  // never execute a list NGX crashed while recording' ('AbortCommands();  // never execute a list NGX crashed while recording' + "`n                    NeuralFxRecorded(neuralfx_frame, static_cast<int32_t>(ecode), false, g.width, g.height, neuralfx_motion.provider);")
$body = Replace-Once $body @'
            g.ctx4->Signal(g.fence11, v_in);
            ctx->Flush();

            if (!BeginCommands()) { FeedFail("command list"); ok = false; }
'@ @'
            const HRESULT neuralfx_signal = g.ctx4->Signal(g.fence11, v_in);
            ctx->Flush();

            if (FAILED(neuralfx_signal)) {
                NeuralFxRecorded(neuralfx_frame, static_cast<int32_t>(neuralfx_signal), false, g.width, g.height, neuralfx_motion.provider);
                FeedDisable("Input fence signal failed; preserving the current scene");
                ok = false;
            }
            else if (!BeginCommands()) { FeedFail("command list"); ok = false; }
'@
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
                    const HRESULT neuralfx_device_result = g.dev11->GetDeviceRemovedReason();
                    if (SUCCEEDED(neuralfx_wait) && SUCCEEDED(neuralfx_device_result)) {
                        BlitOutputToBackbuffer(ctx, rtv11);
                        neuralfx_committed = true;
                    } else {
                        NeuralFxRecorded(neuralfx_frame, static_cast<int32_t>(FAILED(neuralfx_wait) ? neuralfx_wait : neuralfx_device_result), false, g.width, g.height, neuralfx_motion.provider);
                        FeedDisable("D3D11 result wait failed; restart the game");
                    }
'@
$body = Replace-Once $body ('    SafeRelease(color);' + "`n" + '    SafeRelease(mv);') ('    NeuralFxFinishOutput(neuralfx_output, neuralfx_frame, neuralfx_committed);' + "`n" + '    SafeRelease(color);' + "`n" + '    SafeRelease(mv);')
$source = $source.Substring(0, $start) + $body + $source.Substring($end)
$source = Replace-Once $source '    if (!g_cfg.enabled || g.disabled || g_cfg.mode == 0) return;' '    if (!NeuralFxEnabled() || !g_cfg.enabled || g.disabled || g_cfg.mode == 0) return;'
$source = Replace-Once $source '    Log("[feed] %s", g_mv_probe);' @'
    Log("[feed] %s", g_mv_probe);
    neuralfx_health.probe_frame    = static_cast<uint32_t>(g_guide_probe_capture_frame);
    neuralfx_health.mv_mean_px     = static_cast<float>(sum / total);
    neuralfx_health.mv_max_px      = static_cast<float>(maxlen);
    neuralfx_health.mv_nonzero_pct = static_cast<uint32_t>(nonzero * 100 / total);
    NeuralFxPublishHealth(neuralfx_health);
'@
$source = Replace-Once $source '    Log("[feed] %s", g_depth_probe);' @'
    Log("[feed] %s", g_depth_probe);
    neuralfx_health.probe_frame      = static_cast<uint32_t>(g_guide_probe_capture_frame);
    neuralfx_health.depth_min        = static_cast<float>(finite > 0 ? min_depth : 0.0);
    neuralfx_health.depth_max        = static_cast<float>(finite > 0 ? max_depth : 0.0);
    neuralfx_health.depth_mean       = static_cast<float>(mean);
    neuralfx_health.depth_variance   = static_cast<float>(variance);
    neuralfx_health.depth_finite_pct = static_cast<uint32_t>(finite * 100 / total);
    neuralfx_health.depth_flat_moving = flat_moving ? 1u : 0u;
    NeuralFxPublishHealth(neuralfx_health);
'@
$source = Replace-Once $source '    if (++g.timed_frames < 600) return;' @'
    if (g.timed_frames + 1 >= 600) {
        const double neuralfx_span = 1000.0 * double(exit - g.span_start) / double(g.qpf);
        const double neuralfx_n    = double(g.timed_frames + 1);
        neuralfx_health.feed_cpu_ms       = static_cast<float>(1000.0 * double(g.cpu_ticks) / double(g.qpf) / neuralfx_n);
        neuralfx_health.feed_gpu_ms       = g.ts_n > 0 ? static_cast<float>(g.ts_sum_ms / double(g.ts_n)) : 0.0f;
        neuralfx_health.frame_interval_ms = static_cast<float>(neuralfx_span / neuralfx_n);
        neuralfx_health.stalls            = static_cast<uint32_t>(g.win_stalls);
        NeuralFxPublishHealth(neuralfx_health);
    }
    if (++g.timed_frames < 600) return;
'@
$source = Replace-Once $source 'static void DrawOverlay(reshade::api::effect_runtime *rt)' @'
// Apply only the techniques owned by NeuralFX. Foreign techniques keep their state.
static void NeuralFxBeginEffects(reshade::api::effect_runtime* rt, reshade::api::command_list*, reshade::api::resource_view, reshade::api::resource_view) {
    if (rt == g.runtime) NeuralFxPollOutputs();
    const bool active = NeuralFxEnabled() && rt->get_device()->get_api() == reshade::api::device_api::d3d11;
    NeuralFxControls controls;
    bool controls_pending;
    { int result = NeuralFX_GetControls(&controls,sizeof(controls)); controls_pending = controls.revision != 0 && result == 0; }
    if (controls_pending && active && rt == g.runtime) {
        auto sharp = rt->find_uniform_variable("NeuralFX_CAS.fx", "Sharpening");
        if ((controls.mask & 2) && controls.sharpness > 0 && !sharp.handle) {
            std::lock_guard<std::mutex> lock(nfx_lock); nfx_controls_result = -1;
        } else {
            if (controls.mask & 1) nfx_work_override = controls.work_percent;
            if (controls.mask & 2) nfx_sharp_override = controls.sharpness;
            if ((controls.mask & 1) && g_cfg.work_resolution != controls.work_percent) {
                g_cfg.work_resolution = controls.work_percent; g_cfg.work_upscale = controls.work_percent < 100 ? 1 : 0;
                g_cfg.work_sharpness = 0; g.frame_ready = false; g.need_reset = true;
            }
            if ((controls.mask & 2) && sharp.handle) rt->set_uniform_value_float(sharp, &controls.sharpness, 1);
            std::lock_guard<std::mutex> lock(nfx_lock); nfx_controls_applied = controls.revision; nfx_controls_result = 1;
        }
    }
    if (active && rt == g.runtime && nfx_sharp_override > 0) {
        auto sharp = rt->find_uniform_variable("NeuralFX_CAS.fx", "Sharpening");
        if (sharp.handle) rt->set_uniform_value_float(sharp, &nfx_sharp_override, 1);
    }
    static bool previous = false;
    if (rt == g.runtime && active != previous) { g.need_reset = true; previous = active; }
    auto feed = rt->find_technique("DLSS5_Feed.fx", "DLSS5_Feed");
    auto cas = rt->find_technique("NeuralFX_CAS.fx", "NeuralFX_CAS");
    if (feed.handle && rt->get_technique_state(feed) != active) rt->set_technique_state(feed, active);
    const bool cas_active = active && rt == g.runtime && nfx_sharp_override != 0;
    if (cas.handle && rt->get_technique_state(cas) != cas_active) rt->set_technique_state(cas, cas_active);
}
static void NeuralFxFinishEffects(reshade::api::effect_runtime* rt, reshade::api::command_list* cl, reshade::api::resource_view, reshade::api::resource_view) {
    if (rt == g.runtime && rt->get_device()->get_api() == reshade::api::device_api::d3d11) {
        auto* context = reinterpret_cast<ID3D11DeviceContext*>(cl->get_native());
        if (context && context->GetType() == D3D11_DEVICE_CONTEXT_IMMEDIATE) NeuralFxRetireUnused(context);
    }
}
static void DrawOverlay(reshade::api::effect_runtime *rt)
'@
$source = Replace-Once $source '        reshade::register_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' ('        reshade::register_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' + "`n" + '        reshade::register_event<reshade::addon_event::reshade_begin_effects>(NeuralFxBeginEffects);' + "`n" + '        reshade::register_event<reshade::addon_event::reshade_finish_effects>(NeuralFxFinishEffects);' + "`n" + @'
        {
            // Desde 32.0.16.1664 el loader del driver conduce el rasgo 18 a
            // nvngx_dlssnr.dll y el evaluate revienta dentro de D3D12Core. Cerrar esa
            // ruta en este proceso devuelve el comportamiento de 1656, que es el que
            // funciona. Portado de dlss5-bridge (NIGos), MIT.
            void *neuralfx_ngx_table = nullptr;
            switch (NeuralFxCloseDriverNeuralRoute(&neuralfx_ngx_table))
            {
            case NFX_NGX_ROUTE_CLOSED:
                Log("[neuralfx] ruta del driver al rasgo 18 cerrada en este proceso: la entrada 18 de la tabla del loader en %p decia \"dlssnr\" y ahora no dice nada, como en 32.0.16.1656. El consumidor conduce el rasgo el mismo. Nada cambia en disco.", neuralfx_ngx_table);
                break;
            case NFX_NGX_ROUTE_UNTOUCHED:
                Log("[neuralfx] el loader NGX no tiene una entrada \"dlssnr\" reconocible, asi que no conduce neural rendering por su cuenta y no hay nada que cerrar.");
                break;
            case NFX_NGX_ROUTE_NO_LOADER:
                Log("[neuralfx] no hay _nvngx.dll cargado ni registrado; el driver traera el suyo mas tarde.");
                break;
            case NFX_NGX_ROUTE_LOCKED:
                Log("[neuralfx] la tabla del loader esta en %p pero no se pudo hacer escribible (%lu); el driver conserva su ruta.", neuralfx_ngx_table, GetLastError());
                break;
            }
        }
'@)
$source = Replace-Once $source '        reshade::unregister_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' ('        reshade::unregister_event<reshade::addon_event::reshade_render_technique>(OnRenderTechnique);' + "`n" + '        reshade::unregister_event<reshade::addon_event::reshade_begin_effects>(NeuralFxBeginEffects);' + "`n" + '        reshade::unregister_event<reshade::addon_event::reshade_finish_effects>(NeuralFxFinishEffects);')
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
    $motionSource = Join-Path $nativeRoot 'motion_tests.cpp'
    if ($vs) {
        & cmd.exe /d /c ('call "' + $env:VCVARSALL + '" x64 && cl.exe /nologo /EHsc /W3 /std:c++20 /Fobuild/ /Febuild/motion-tests.exe "' + $motionSource + '"')
    } else { & cl.exe /nologo /EHsc /W3 /std:c++20 /Fobuild/ /Febuild/motion-tests.exe $motionSource }
    if ($LASTEXITCODE -ne 0) { throw 'Motion fixture compilation failed.' }
    & .\build\motion-tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Motion descriptor fixture failed.' }
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

$buildManifest = [ordered]@{
    sourceCommit = (& git -C (Split-Path -Parent $nativeRoot) rev-parse HEAD)
    bridgeBuild = 5; frameAbi = 3; frameBytes = 64; resultBytes = 80; healthBytes = 64; ipc = 4
    artifactSha256 = (Get-FileHash -LiteralPath (Join-Path $output 'dlss5-feed.addon64') -Algorithm SHA256).Hash.ToLowerInvariant()
    supported = @('reset','session-control','registered-motion-experimental','work-resolution-control','cas-control','output-completion-query','pipeline-health')
    unverified = @('unity-motion-sign-and-coverage','scene-depth-camera-match','pre-ui-composition','nr-per-frame-confirmation')
    unavailable = @('prepared-camera-jitter','unity-internal-sr','nr-only-same-frame-comparison','integrated-frame-generation')
}
$buildManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'capabilities.json') -Encoding utf8

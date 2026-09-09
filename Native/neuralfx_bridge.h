// NeuralFX feeder extension. MIT-0. Metadata is latched on the render thread.
#pragma once
#include "neuralfx_contract.h"
#include <mutex>
#include <chrono>
#ifdef _WIN32
#include <windows.h>
#define NFX_EXPORT extern "C" __declspec(dllexport)
#define NFX_CALL __cdecl
#define NFX_EVENT __stdcall
#else
#define NFX_EXPORT extern "C"
#define NFX_CALL
#define NFX_EVENT
#endif
static std::mutex nfx_lock;
static uint64_t NeuralFxNow() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
}
static NeuralFxFrameV3 nfx_slots[16] = {}, nfx_render_frame = {};
static NeuralFxStatus nfx_status = {sizeof(NeuralFxStatus), 1, NFX_RESET | NFX_CONTROL | NFX_REGISTERED_MOTION};
static NeuralFxResult nfx_result = {sizeof(NeuralFxResult), 3};
static NeuralFxFrameV3 nfx_abandoned[16] = {};
// Only frames whose render event has executed may enter this retirement queue.
static void NeuralFxAbandonLocked(const NeuralFxFrameV3& frame) {
    if (!frame.motion_handle) return;
    for (auto& pending : nfx_abandoned) if (pending.motion_handle == frame.motion_handle) return;
    for (auto& pending : nfx_abandoned) if (!pending.motion_handle) { pending = frame; return; }
}
static uint32_t nfx_taken_frame = 0, nfx_last_submitted = 0, nfx_camera = 0, nfx_epoch = 0;
static uint64_t nfx_render_tick = 0, nfx_evaluate_tick = 0, nfx_heartbeat = 0;
static bool nfx_enabled = false;
static NeuralFxControls nfx_controls = {24,3,0,0,100,0.3f};
static uint32_t nfx_controls_applied = 0;
static int32_t nfx_controls_result = 0;
static int32_t nfx_work_override = 0;
static float nfx_sharp_override = -1;
static bool NeuralFxEnabled() {
    std::lock_guard<std::mutex> lock(nfx_lock);
    return nfx_enabled && NeuralFxNow() - nfx_heartbeat < 3000;
}
NFX_EXPORT int NFX_CALL NeuralFX_SetControls(const NeuralFxControls* controls, uint32_t bytes) {
    if (!controls || bytes != sizeof(NeuralFxControls)) return 0;
    NeuralFxControls c = {}; std::memcpy(&c, controls, sizeof(c));
    if (c.size != bytes || c.version != 3 || !c.revision || !c.mask || (c.mask & ~3u)) return 0;
    if ((c.mask & 1) && c.work_percent != 100 && c.work_percent != 85 && c.work_percent != 66) return 0;
    if ((c.mask & 2) && (!NeuralFxFinite(c.sharpness) || c.sharpness < 0 || c.sharpness > 1)) return 0;
    std::lock_guard<std::mutex> lock(nfx_lock);
    if (nfx_controls.revision && !NeuralFxNewer(c.revision, nfx_controls.revision)) return 0;
    // Do not silently replace an accepted command that the render thread has not applied.
    if (nfx_controls_result == 0 && nfx_controls.revision != nfx_controls_applied) return 0;
    nfx_controls = c; nfx_controls_result = 0; return 1;
}
NFX_EXPORT int NFX_CALL NeuralFX_GetControls(NeuralFxControls* controls, uint32_t bytes) {
    if (!controls || bytes != sizeof(NeuralFxControls)) return 0;
    std::lock_guard<std::mutex> lock(nfx_lock); *controls = nfx_controls;
    return nfx_controls_result; // 0 pending, 1 applied to render settings, -1 unsupported uniform
}
NFX_EXPORT int NFX_CALL NeuralFX_SetEnabled(uint32_t enabled) {
    if (enabled > 1) return 0;
    std::lock_guard<std::mutex> lock(nfx_lock);
    nfx_enabled = enabled != 0; nfx_heartbeat = NeuralFxNow();
    if (!nfx_enabled) { NeuralFxAbandonLocked(nfx_render_frame); nfx_render_frame = {}; nfx_status.result = 0; }
    return 1;
}
NFX_EXPORT int NFX_CALL NeuralFX_GetCapabilities(void* output, uint32_t bytes) {
    if (!output || bytes != sizeof(NeuralFxCapabilities)) return 0;
    NeuralFxCapabilities caps = {sizeof(caps), 3, NFX_BUILD, NFX_RESET | NFX_CONTROL | NFX_REGISTERED_MOTION, 16384, sizeof(NeuralFxFrameV3), sizeof(NeuralFxResult), 0};
    std::memcpy(output, &caps, sizeof(caps)); return 1;
}
NFX_EXPORT int NFX_CALL NeuralFX_SubmitFrameV3(const void* input, uint32_t bytes) {
    NeuralFxFrameV3 frame = {};
    if (!NeuralFxDecode(input, bytes, frame)) return 0;
    // This consumer has no prepare/fallback contract for camera jitter. Fail closed.
    if (frame.jitter_x != 0 || frame.jitter_y != 0) return 0;
    std::lock_guard<std::mutex> lock(nfx_lock);
    if (!nfx_enabled || NeuralFxNow() - nfx_heartbeat >= 3000) return 0;
    if (frame.camera && (frame.camera != nfx_camera || frame.epoch != nfx_epoch)) {
        if (nfx_epoch && !NeuralFxNewer(frame.epoch, nfx_epoch)) return 0;
        nfx_camera = frame.camera; nfx_epoch = frame.epoch;
        nfx_last_submitted = nfx_taken_frame = 0; NeuralFxAbandonLocked(nfx_render_frame); nfx_render_frame = {};
        // Keep submitted slots until their queued render event executes.
        nfx_result = {sizeof(NeuralFxResult), 3};
        nfx_status.result = 0; nfx_status.reset_serial = frame.reset_serial - 1;
    }
    if (nfx_last_submitted && !NeuralFxNewer(frame.frame, nfx_last_submitted)) return 0;
    nfx_last_submitted = frame.frame;
    nfx_slots[frame.frame % 16] = frame;
    return 1;
}
// Legacy entry point has no caller buffer length. It reads only its declared exact
// layout; new callers must use the sized export. No 48-byte copy of a V1 allocation.
NFX_EXPORT int NFX_CALL NeuralFX_SubmitFrame(const NeuralFxFrame* input) {
    if (!input) return 0;
    uint32_t header[2]; std::memcpy(header, input, 8);
    if (!((header[0] == 32 && header[1] == 1) || (header[0] == 48 && header[1] == 2))) return 0;
    return NeuralFX_SubmitFrameV3(input, header[0]);
}
static void NFX_EVENT NeuralFxRenderEvent(int event_id) {
    std::lock_guard<std::mutex> lock(nfx_lock);
    uint32_t token = static_cast<uint32_t>(event_id);
    auto& slot = nfx_slots[token % 16];
    if (slot.frame == token) {
        if ((!slot.epoch || slot.epoch == nfx_epoch) && nfx_enabled) {
            NeuralFxAbandonLocked(nfx_render_frame);
            nfx_render_frame = slot; nfx_render_tick = NeuralFxNow();
        } else NeuralFxAbandonLocked(slot);
        slot = {};
    }
}
NFX_EXPORT void* NFX_CALL NeuralFX_GetRenderEvent() { return reinterpret_cast<void*>(&NeuralFxRenderEvent); }
NFX_EXPORT int NFX_CALL NeuralFX_GetStatus(NeuralFxStatus* output) {
    if (!output || output->size != sizeof(NeuralFxStatus)) return 0;
    std::lock_guard<std::mutex> lock(nfx_lock); *output = nfx_status;
    output->age_ms = nfx_evaluate_tick ? static_cast<uint32_t>(NeuralFxNow() - nfx_evaluate_tick) : UINT32_MAX;
    return 1;
}
NFX_EXPORT int NFX_CALL NeuralFX_GetFrameResult(void* output, uint32_t bytes) {
    if (!output || bytes != sizeof(NeuralFxResult)) return 0;
    std::lock_guard<std::mutex> lock(nfx_lock); std::memcpy(output, &nfx_result, bytes); return 1;
}
static bool NeuralFxTakeFrame(uint32_t width, uint32_t height, NeuralFxFrameV3& frame) {
    std::lock_guard<std::mutex> lock(nfx_lock);
    frame = nfx_render_frame;
    bool valid = nfx_enabled && frame.frame && frame.frame != nfx_taken_frame && frame.width == width && frame.height == height && NeuralFxNow() - nfx_render_tick < 250;
    if (valid) { nfx_taken_frame = frame.frame; nfx_render_frame = {}; }
    return valid;
}
static bool NeuralFxResetNeeded(const NeuralFxFrameV3& frame) {
    std::lock_guard<std::mutex> lock(nfx_lock); return frame.reset_serial != nfx_status.reset_serial;
}
static void NeuralFxRecorded(const NeuralFxFrameV3& frame, int32_t error, bool submitted, uint32_t width, uint32_t height, uint32_t provider) {
    std::lock_guard<std::mutex> lock(nfx_lock);
    nfx_result.frame = frame.frame; nfx_result.camera = frame.camera; nfx_result.epoch = frame.epoch;
    nfx_result.recorded = error == 0 ? frame.frame : 0; nfx_result.submitted = submitted ? frame.frame : 0;
    nfx_result.error = error; nfx_result.motion_provider = provider;
    nfx_result.work_width = width; nfx_result.work_height = height;
    nfx_status.frame = frame.frame; nfx_status.width = width; nfx_status.height = height;
    nfx_status.result = error == 0 && submitted ? 1 : -1; nfx_evaluate_tick = NeuralFxNow();
    if (error == 0 && submitted) ++nfx_status.evaluations;
}
// Called only after a D3D11 event query following output blit has completed.
static void NeuralFxCompleted(const NeuralFxFrameV3& frame) {
    std::lock_guard<std::mutex> lock(nfx_lock);
    if (frame.epoch != nfx_epoch || frame.camera != nfx_camera) return;
    if (nfx_result.completed && !NeuralFxNewer(frame.frame, nfx_result.completed)) return;
    nfx_result.completed = nfx_result.output_committed = frame.frame;
    nfx_result.reset_serial = nfx_status.reset_serial = frame.reset_serial;
}

// NeuralFX extension of the pinned feeder. MIT-0.
// CPU metadata is latched by a Unity render-thread event before the ReShade evaluate.
#pragma once
#include <windows.h>
#include <cstdint>

struct NeuralFxFrame {
    uint32_t size, version, frame, reset_serial;
    uint32_t width, height;
    float jitter_x, jitter_y;
    uint64_t motion_vectors_ptr;
    float mv_scale_x, mv_scale_y;
};
struct NeuralFxStatus {
    uint32_t size, version, capabilities, frame;
    uint32_t reset_serial, evaluations, width, height;
    int32_t result;
    uint32_t age_ms;
};
static_assert(sizeof(NeuralFxFrame) == 48);
static_assert(sizeof(NeuralFxStatus) == 40);
static SRWLOCK nfx_lock = SRWLOCK_INIT;
static NeuralFxFrame nfx_slots[16] = {};
static NeuralFxFrame nfx_render_frame = {};
static NeuralFxStatus nfx_status = {sizeof(NeuralFxStatus), 1, 7}; // capability 1: reset, 2: jitter, 4: native MV
static uint32_t nfx_taken_frame = 0;
static ULONGLONG nfx_render_tick = 0;
static ULONGLONG nfx_evaluate_tick = 0;

extern "C" __declspec(dllexport) int __cdecl NeuralFX_SubmitFrame(const NeuralFxFrame* input) {
    if (!input || !input->frame || !input->width || !input->height) return 0;
    if (input->size != sizeof(NeuralFxFrame) && input->size != 32) return 0;
    if (input->version != 1 && input->version != 2) return 0;
    // Sub-pixel jitter offsets must be in reasonable pixel range [-2.0, 2.0]
    if (input->jitter_x < -2.0f || input->jitter_x > 2.0f || input->jitter_y < -2.0f || input->jitter_y > 2.0f) return 0;
    AcquireSRWLockExclusive(&nfx_lock);
    nfx_slots[input->frame % 16] = *input;
    ReleaseSRWLockExclusive(&nfx_lock);
    return 1;
}
static void __stdcall NeuralFxRenderEvent(int event_id) {
    AcquireSRWLockExclusive(&nfx_lock);
    const auto& slot = nfx_slots[static_cast<uint32_t>(event_id) % 16];
    if (slot.frame == static_cast<uint32_t>(event_id)) { nfx_render_frame = slot; nfx_render_tick = GetTickCount64(); }
    ReleaseSRWLockExclusive(&nfx_lock);
}
extern "C" __declspec(dllexport) void* __cdecl NeuralFX_GetRenderEvent() { return reinterpret_cast<void*>(&NeuralFxRenderEvent); }
extern "C" __declspec(dllexport) int __cdecl NeuralFX_GetStatus(NeuralFxStatus* output) {
    if (!output || output->size != sizeof(NeuralFxStatus)) return 0;
    AcquireSRWLockShared(&nfx_lock); *output = nfx_status;
    output->age_ms = nfx_evaluate_tick ? static_cast<uint32_t>(GetTickCount64() - nfx_evaluate_tick) : UINT32_MAX;
    ReleaseSRWLockShared(&nfx_lock); return 1;
}
static bool NeuralFxTakeFrame(UINT width, UINT height, NeuralFxFrame& frame) {
    AcquireSRWLockExclusive(&nfx_lock);
    frame = nfx_render_frame;
    bool valid = frame.frame != 0 && frame.frame != nfx_taken_frame && frame.width == width && frame.height == height && GetTickCount64() - nfx_render_tick < 2000;
    if (valid) nfx_taken_frame = frame.frame;
    ReleaseSRWLockExclusive(&nfx_lock);
    return valid;
}
static bool NeuralFxResetNeeded(const NeuralFxFrame& frame) {
    AcquireSRWLockShared(&nfx_lock); bool reset = frame.reset_serial != nfx_status.reset_serial; ReleaseSRWLockShared(&nfx_lock); return reset;
}
static void NeuralFxEvaluated(const NeuralFxFrame& frame, bool success, UINT width, UINT height) {
    AcquireSRWLockExclusive(&nfx_lock);
    nfx_status.frame = frame.frame; nfx_status.width = width; nfx_status.height = height; nfx_status.result = success ? 1 : -1;
    nfx_evaluate_tick = GetTickCount64();
    if (success) { nfx_status.reset_serial = frame.reset_serial; ++nfx_status.evaluations; }
    ReleaseSRWLockExclusive(&nfx_lock);
}

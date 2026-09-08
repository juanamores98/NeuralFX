#include "neuralfx_bridge.h"
#include <cassert>
#include <cstdio>

int main() {
    NeuralFxStatus status = {sizeof(NeuralFxStatus)};
    assert(NeuralFX_GetStatus(&status) == 1 && status.result == 0 && status.age_ms == UINT32_MAX);
    NeuralFxFrame frame = {sizeof(NeuralFxFrame), 2, 1, 7, 1920, 1080, 0, 0, 0x12345678ULL, 1920.0f, 1080.0f};
    NeuralFxFrame captured = {};
    assert(NeuralFX_SubmitFrame(&frame) == 1);
    assert(!NeuralFxTakeFrame(1920, 1080, captured)); // CPU submit alone cannot associate a render frame.
    NeuralFxRenderEvent(1);
    assert(!NeuralFxTakeFrame(1280, 720, captured));
    assert(NeuralFxTakeFrame(1920, 1080, captured));
    assert(captured.motion_vectors_ptr == 0x12345678ULL && captured.mv_scale_x == 1920.0f);
    assert(!NeuralFxTakeFrame(1920, 1080, captured));
    assert(NeuralFxResetNeeded(frame));
    NeuralFxEvaluated(frame, false, 1920, 1080);
    assert(NeuralFxResetNeeded(frame)); // Failed evaluations cannot acknowledge a reset.
    NeuralFxEvaluated(frame, true, 1920, 1080);
    assert(!NeuralFxResetNeeded(frame));
    NeuralFX_GetStatus(&status);
    assert(status.capabilities == 7);
    frame.frame = 2; frame.reset_serial = 8; frame.jitter_x = 0.25f; frame.jitter_y = -0.125f;
    assert(NeuralFX_SubmitFrame(&frame) == 1); // Accept validated sub-pixel jitter contracts.
    NeuralFxRenderEvent(2);
    assert(NeuralFxTakeFrame(1920, 1080, captured));
    assert(captured.jitter_x == 0.25f && captured.jitter_y == -0.125f);
    frame.jitter_x = 5.0f; assert(NeuralFX_SubmitFrame(&frame) == 0); // Reject out-of-range jitter.
    frame.jitter_x = 0; frame.jitter_y = 0; assert(NeuralFX_SubmitFrame(&frame) == 1);
    NeuralFxRenderEvent(2); nfx_render_tick = GetTickCount64() - 2001;
    assert(!NeuralFxTakeFrame(1920, 1080, captured));
    std::puts("Native bridge tests passed (ABI, render event, dimensions, reset acknowledgement, freshness, jitter and native MV support).");
}

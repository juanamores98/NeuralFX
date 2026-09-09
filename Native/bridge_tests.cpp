#include "neuralfx_bridge.h"
#include <cassert>
#include <cstdio>
#include <limits>
#include <memory>
#include <initializer_list>
int main() {
    NeuralFxCapabilities caps = {}; assert(NeuralFX_GetCapabilities(&caps, sizeof(caps)) == 1);
    assert(caps.frame_bytes == 64 && !(caps.supported & 2));
    NeuralFxFrameV3 decoded = {}, f = {64, 3, 1, 7, 1920, 1080, 0, 0, 42, 1, 0, 0, 1, 1, NFX_MAGIC, 0};
    assert(!NeuralFxDecode(nullptr, 0, decoded));
    for (uint32_t size = 0; size < 64; ++size) assert(!NeuralFxDecode(&f, size, decoded));
    auto legacy = std::make_unique<NeuralFxFrameV1>(NeuralFxFrameV1{32, 1, 1, 7, 1920, 1080, 0, 0});
    assert(NeuralFxDecode(legacy.get(), 32, decoded)); // exact heap allocation, ASan covers the real decoder
    NeuralFX_SetEnabled(1);
    assert(NeuralFX_SubmitFrame(reinterpret_cast<NeuralFxFrame*>(legacy.get())) == 1);
    NeuralFxFrame v2 = {48,2,2,7,1920,1080,0,0,0x12345678,1920,1080};
    assert(NeuralFxDecode(&v2,48,decoded) && !decoded.motion_handle && decoded.mv_scale_x == 1);
    v2.version=1; assert(!NeuralFxDecode(&v2,48,decoded)); v2.version=2;
    legacy->version=2; assert(!NeuralFxDecode(legacy.get(),32,decoded));
    for (float invalid : {std::numeric_limits<float>::quiet_NaN(),std::numeric_limits<float>::infinity(),-std::numeric_limits<float>::infinity()}) {
        f.jitter_x=invalid; assert(!NeuralFxDecode(&f,64,decoded)); f.jitter_x=0;
        f.mv_scale_y=invalid; assert(!NeuralFxDecode(&f,64,decoded)); f.mv_scale_y=1;
    }
    f.flags=1; assert(!NeuralFxDecode(&f,64,decoded)); f.flags=0;
    f.width=UINT32_MAX; assert(!NeuralFxDecode(&f,64,decoded)); f.width=1920;
    assert(NeuralFX_SubmitFrameV3(&f,64)==1);
    assert(!NeuralFxTakeFrame(1920,1080,decoded));
    NeuralFxRenderEvent(1); assert(!NeuralFxTakeFrame(1280,720,decoded));
    assert(NeuralFxTakeFrame(1920,1080,decoded)); assert(!NeuralFxTakeFrame(1920,1080,decoded));
    assert(!NeuralFX_SubmitFrameV3(&f,64)); // replay
    assert(NeuralFxResetNeeded(f));
    NeuralFxRecorded(f,0,true,1920,1080,1); assert(NeuralFxResetNeeded(f)); // submission is not completion
    NeuralFxCompleted(f); assert(!NeuralFxResetNeeded(f));
    f.frame=2; f.epoch=2; f.reset_serial=8; assert(NeuralFX_SubmitFrameV3(&f,64));
    NeuralFxFrameV3 old=f; old.epoch=1; old.frame=3; assert(!NeuralFX_SubmitFrameV3(&old,64));
    NeuralFxCompleted(old); assert(NeuralFxResetNeeded(f));
    f.frame=3; f.jitter_x=.25f; assert(!NeuralFX_SubmitFrameV3(&f,64)); f.jitter_x=0;
    NeuralFX_SetEnabled(0); assert(!NeuralFX_SubmitFrameV3(&f,64));
    assert(NeuralFxNewer(1,UINT32_MAX) && !NeuralFxNewer(UINT32_MAX,1));
    std::puts("Bridge contract passed: exact V1 allocation, V2/V3, malformed buffers, finite values, replay, epochs, completion, safe jitter and Off.");
}

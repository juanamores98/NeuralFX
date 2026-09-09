// NeuralFX internal wire contract. MIT-0. No NVIDIA API identifiers.
#pragma once
#include <cstdint>
#include <cstring>
#include <cmath>
#include <cstddef>
#pragma pack(push, 4)
struct NeuralFxFrameV1 {
    uint32_t size, version, frame, reset_serial, width, height;
    float jitter_x, jitter_y;
};
struct NeuralFxFrame {
    uint32_t size, version, frame, reset_serial, width, height;
    float jitter_x, jitter_y;
    uint64_t motion_vectors_ptr;
    float mv_scale_x, mv_scale_y;
};
struct NeuralFxFrameV3 {
    uint32_t size, version, frame, reset_serial, width, height;
    float jitter_x, jitter_y;
    uint32_t camera, epoch, flags, motion_handle;
    float mv_scale_x, mv_scale_y;
    uint32_t magic, reserved;
};
struct NeuralFxStatus {
    uint32_t size, version, capabilities, frame, reset_serial, evaluations, width, height;
    int32_t result;
    uint32_t age_ms;
};
struct NeuralFxCapabilities {
    uint32_t size, version, build, supported, max_dimension, frame_bytes, result_bytes, reserved;
};
struct NeuralFxResult {
    uint32_t size, version, frame, camera, epoch, recorded, submitted, completed, output_committed;
    uint32_t reset_serial, motion_provider, bypass_reason, nr_confirmed, ui_isolated;
    int32_t error;
    uint32_t work_width, work_height, adapter_low;
    int32_t adapter_high;
    uint32_t reserved;
};
#pragma pack(pop)
static_assert(sizeof(NeuralFxFrameV1) == 32);
static_assert(sizeof(NeuralFxFrame) == 48);
static_assert(sizeof(NeuralFxFrameV3) == 64);
static_assert(sizeof(NeuralFxStatus) == 40);
static_assert(sizeof(NeuralFxResult) == 80);
static_assert(offsetof(NeuralFxFrame, motion_vectors_ptr) == 32);
static constexpr uint32_t NFX_MAGIC = 0x4e465833;
static constexpr uint32_t NFX_BUILD = 4;
// No jitter/pre-UI/NR-confirmation capability until their acceptance gates pass.
static constexpr uint32_t NFX_RESET = 1, NFX_CONTROL = 8, NFX_REGISTERED_MOTION = 16;
static bool NeuralFxNewer(uint32_t a, uint32_t b) { return a != b && uint32_t(a - b) < 0x80000000u; }
static bool NeuralFxFinite(float f) { return std::isfinite(f); }
static bool NeuralFxDecode(const void* input, uint32_t bytes, NeuralFxFrameV3& out) {
    if (!input || bytes < 8) return false;
    uint32_t head[2]; std::memcpy(head, input, sizeof(head));
    NeuralFxFrameV3 decoded = {};
    if (head[0] != bytes) return false;
    if (head[1] == 1 && bytes == sizeof(NeuralFxFrameV1)) {
        NeuralFxFrameV1 old = {}; std::memcpy(&old, input, sizeof(old));
        std::memcpy(&decoded, &old, sizeof(old));
    } else if (head[1] == 2 && bytes == sizeof(NeuralFxFrame)) {
        NeuralFxFrame old = {}; std::memcpy(&old, input, sizeof(old));
        // Legacy free pointers are never dereferenced or promoted to registered resources.
        if (!NeuralFxFinite(old.mv_scale_x) || !NeuralFxFinite(old.mv_scale_y) || old.mv_scale_x < 0 || old.mv_scale_y < 0) return false;
        std::memcpy(&decoded, &old, sizeof(NeuralFxFrameV1));
    } else if (head[1] == 3 && bytes == sizeof(decoded)) {
        std::memcpy(&decoded, input, sizeof(decoded));
        if (decoded.magic != NFX_MAGIC || !decoded.camera || !decoded.epoch || decoded.flags || decoded.reserved) return false;
        if (!NeuralFxFinite(decoded.mv_scale_x) || !NeuralFxFinite(decoded.mv_scale_y)) return false;
        if (decoded.motion_handle && (decoded.mv_scale_x <= 0 || decoded.mv_scale_y <= 0)) return false;
    } else return false;
    if (!decoded.frame || !decoded.width || !decoded.height || decoded.width > 16384 || decoded.height > 16384) return false;
    if (!NeuralFxFinite(decoded.jitter_x) || !NeuralFxFinite(decoded.jitter_y)) return false;
    if (std::fabs(decoded.jitter_x) > 2 || std::fabs(decoded.jitter_y) > 2) return false;
    decoded.size = sizeof(decoded); decoded.version = 3;
    if (!decoded.motion_handle) { decoded.mv_scale_x = 1; decoded.mv_scale_y = 1; }
    out = decoded; return true;
}

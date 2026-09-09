// Registered in-process D3D11 resources; never accepted through Hub IPC. MIT-0.
#pragma once
#include <d3d11.h>
#include "neuralfx_bridge.h"
struct NeuralFxMotionSlot {
    uint32_t handle = 0, camera = 0, epoch = 0;
    ID3D11Texture2D* texture = nullptr;
    ID3D11ShaderResourceView* view = nullptr;
    bool reserved = false, retiring = false;
};
static std::mutex nfx_inputs_lock;
static NeuralFxMotionSlot nfx_motion_slots[16];
static uint32_t nfx_next_handle = 0;
static void NeuralFxReleaseSlot(NeuralFxMotionSlot& slot) {
    if (slot.view) slot.view->Release();
    if (slot.texture) slot.texture->Release();
    slot = {};
}
NFX_EXPORT uint32_t NFX_CALL NeuralFX_RegisterMotion(void* pointer, uint32_t camera, uint32_t epoch) {
    if (!pointer || !camera || !epoch) return 0;
    // Only the trusted mod registers its own live RenderTexture during creation.
    auto* texture = static_cast<ID3D11Texture2D*>(pointer);
    D3D11_TEXTURE2D_DESC desc; texture->GetDesc(&desc);
    if (desc.Format != DXGI_FORMAT_R16G16_FLOAT || desc.SampleDesc.Count != 1 || desc.MipLevels != 1 || desc.ArraySize != 1 || !(desc.BindFlags & D3D11_BIND_SHADER_RESOURCE)) return 0;
    ID3D11Device* device = nullptr; texture->GetDevice(&device);
    ID3D11ShaderResourceView* view = nullptr;
    HRESULT hr = device->CreateShaderResourceView(texture, nullptr, &view); device->Release();
    if (FAILED(hr)) return 0;
    std::lock_guard<std::mutex> lock(nfx_inputs_lock);
    for (auto& slot : nfx_motion_slots) if (!slot.handle) {
        if (++nfx_next_handle == 0) ++nfx_next_handle;
        texture->AddRef(); slot.handle = nfx_next_handle; slot.camera = camera; slot.epoch = epoch;
        slot.texture = texture; slot.view = view; return slot.handle;
    }
    view->Release(); return 0;
}
NFX_EXPORT int NFX_CALL NeuralFX_ReserveMotion(uint32_t handle) {
    std::lock_guard<std::mutex> lock(nfx_inputs_lock);
    for (auto& slot : nfx_motion_slots) if (slot.handle == handle && handle && !slot.reserved && !slot.retiring) { slot.reserved = true; return 1; }
    return 0;
}
static void NeuralFxMotionComplete(uint32_t handle) {
    std::lock_guard<std::mutex> lock(nfx_inputs_lock);
    for (auto& slot : nfx_motion_slots) if (handle && slot.handle == handle) {
        slot.reserved = false; if (slot.retiring) NeuralFxReleaseSlot(slot); return;
    }
}
NFX_EXPORT void NFX_CALL NeuralFX_ReleaseMotion(uint32_t handle) {
    std::lock_guard<std::mutex> lock(nfx_inputs_lock);
    for (auto& slot : nfx_motion_slots) if (handle && slot.handle == handle) {
        slot.retiring = true; if (!slot.reserved) NeuralFxReleaseSlot(slot); return;
    }
}
// A single descriptor owns the resource, view and constants for both copy and resample.
struct NeuralFxSelectedMotion {
    ID3D11Texture2D* texture = nullptr;
    ID3D11ShaderResourceView* view = nullptr;
    D3D11_TEXTURE2D_DESC desc = {};
    float scale_x = 1, scale_y = 1;
    uint32_t provider = 1; // 1 optical, 2 registered Unity (experimental coverage/sign)
    ~NeuralFxSelectedMotion() { if (view) view->Release(); if (texture) texture->Release(); }
    bool Select(ID3D11Device* device, const NeuralFxFrameV3& frame, ID3D11Texture2D* optical, ID3D11ShaderResourceView* optical_view, float optical_x, float optical_y) {
        texture = optical; texture->AddRef(); view = optical_view; view->AddRef(); texture->GetDesc(&desc);
        scale_x = optical_x; scale_y = optical_y;
        std::lock_guard<std::mutex> lock(nfx_inputs_lock);
        for (auto& slot : nfx_motion_slots) if (frame.motion_handle && slot.handle == frame.motion_handle && slot.reserved && slot.camera == frame.camera && slot.epoch == frame.epoch) {
            D3D11_TEXTURE2D_DESC candidate; slot.texture->GetDesc(&candidate);
            ID3D11Device* owner = nullptr; slot.texture->GetDevice(&owner); bool same = owner == device; owner->Release();
            ID3D11Resource* viewed = nullptr; slot.view->GetResource(&viewed); same = same && viewed == slot.texture; viewed->Release();
            if (!same || candidate.Width != frame.width || candidate.Height != frame.height) break;
            texture->Release(); view->Release(); texture = slot.texture; view = slot.view; texture->AddRef(); view->AddRef();
            desc = candidate; scale_x = frame.mv_scale_x; scale_y = frame.mv_scale_y; provider = 2; break;
        }
        return NeuralFxFinite(scale_x) && NeuralFxFinite(scale_y);
    }
};
// Nonblocking completion queries. The slot and reset stay pending until the final blit
// completes on the game's queue. Saturation bypasses rather than overwriting pending work.
struct NeuralFxPendingOutput { ID3D11Query* query = nullptr; ID3D11DeviceContext* context = nullptr; NeuralFxFrameV3 frame = {}; bool committed = false; };
static NeuralFxPendingOutput nfx_outputs[8];
static void NeuralFxPollOutputs() {
    for (auto& item : nfx_outputs) if (item.query) {
        BOOL done = FALSE;
        HRESULT hr = item.context->GetData(item.query, &done, sizeof(done), D3D11_ASYNC_GETDATA_DONOTFLUSH);
        if ((hr == S_OK && done) || FAILED(hr)) {
            if (hr == S_OK && item.committed) NeuralFxCompleted(item.frame);
            NeuralFxMotionComplete(item.frame.motion_handle);
            item.query->Release(); item.context->Release(); item = {};
        }
    }
}
static NeuralFxPendingOutput* NeuralFxPrepareOutput(ID3D11DeviceContext* context) {
    NeuralFxPollOutputs();
    for (auto& item : nfx_outputs) if (!item.query) {
        ID3D11Device* device = nullptr; context->GetDevice(&device);
        D3D11_QUERY_DESC desc = {D3D11_QUERY_EVENT, 0};
        HRESULT hr = device->CreateQuery(&desc, &item.query); device->Release();
        if (FAILED(hr)) return nullptr;
        context->AddRef(); item.context = context; return &item;
    }
    return nullptr;
}
static void NeuralFxFinishOutput(NeuralFxPendingOutput* item, const NeuralFxFrameV3& frame, bool committed) {
    if (!item) return;
    item->frame = frame; item->committed = committed; item->context->End(item->query);
}

// Registered in-process D3D11 resources; never accepted through Hub IPC. MIT-0.
#pragma once
#include "neuralfx_ngx.h"
#include <d3d11.h>
#include "neuralfx_bridge.h"
#include "neuralfx_registration_report.h"
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
static std::mutex nfx_report_lock;
static NeuralFxRegistrationReport nfx_report = {sizeof(NeuralFxRegistrationReport), 1};
// El informe guarda el ÚLTIMO intento, y además cuenta cuántas veces ocurrió cada motivo. Lo
// primero sirve para diagnosticar; lo segundo, para no confundir un tropiezo aislado con un
// rechazo sistemático, que es una distinción que el registro de sesión no podía hacer.
static void NeuralFxPublishRegistration(uint32_t stage, uint32_t reason, HRESULT hr,
    uint32_t camera, uint32_t epoch, const D3D11_TEXTURE2D_DESC* desc, bool from_view, uint32_t used) {
    std::lock_guard<std::mutex> lock(nfx_report_lock);
    NeuralFxRegistrationReport next = {};
    next.size = sizeof(next); next.version = 1;
    next.request_serial = nfx_report.request_serial + 1;
    next.stage = stage; next.reason = reason; next.hresult = static_cast<int32_t>(hr);
    next.camera = camera; next.epoch = epoch; next.from_view = from_view ? 1u : 0u;
    if (desc) {
        next.width = desc->Width; next.height = desc->Height; next.format = desc->Format;
        next.mip_levels = desc->MipLevels; next.array_size = desc->ArraySize;
        next.sample_count = desc->SampleDesc.Count; next.sample_quality = desc->SampleDesc.Quality;
        next.bind_flags = desc->BindFlags; next.misc_flags = desc->MiscFlags;
        next.usage = desc->Usage; next.cpu_access = desc->CPUAccessFlags;
    }
    next.slots_used = used; next.slots_total = static_cast<uint32_t>(sizeof(nfx_motion_slots) / sizeof(nfx_motion_slots[0]));
    next.accepted = nfx_report.accepted + (reason == NFX_REG_OK ? 1u : 0u);
    next.rejected = nfx_report.rejected + (reason == NFX_REG_OK ? 0u : 1u);
    for (uint32_t i = 0; i < NFX_REG_REASON_COUNT; ++i) next.counts[i] = nfx_report.counts[i];
    if (reason < NFX_REG_REASON_COUNT) ++next.counts[reason];
    nfx_report = next;
}
/// <summary>Último intento de registro, con su descriptor real. Nunca bloquea al render.</summary>
NFX_EXPORT int NFX_CALL NeuralFX_GetRegistrationReport(NeuralFxRegistrationReport* out, uint32_t bytes) {
    if (!out || bytes != sizeof(NeuralFxRegistrationReport)) return 0;
    std::lock_guard<std::mutex> lock(nfx_report_lock);
    *out = nfx_report; return 1;
}
static uint32_t NeuralFxSlotsUsed() {
    uint32_t used = 0;
    for (const auto& slot : nfx_motion_slots) if (slot.handle) ++used;
    return used;
}
NFX_EXPORT uint32_t NFX_CALL NeuralFX_RegisterMotion(void* pointer, uint32_t camera, uint32_t epoch) {
    if (!pointer) { NeuralFxPublishRegistration(NFX_REG_STAGE_INPUT, NFX_REG_NULL_POINTER, S_OK, camera, epoch, nullptr, false, 0); return 0; }
    if (!camera || !epoch) { NeuralFxPublishRegistration(NFX_REG_STAGE_INPUT, NFX_REG_BAD_IDENTITY, S_OK, camera, epoch, nullptr, false, 0); return 0; }
    // Only the trusted mod registers its own live RenderTexture during creation.
    //
    // Se pregunta por la interfaz en vez de suponerla. La documentación de Unity 5.6 dice que
    // GetNativeTexturePtr entrega un ID3D11Resource en D3D11; un static_cast a ID3D11Texture2D
    // y un GetDesc sobre otra cosa sería comportamiento indefinido, y el motivo del rechazo
    // quedaría además indistinguible. Si el objeto resulta ser una vista, se pide su recurso:
    // es un contrato adicional declarado aquí, no una afirmación sobre lo que Unity entrega.
    auto* unknown = static_cast<IUnknown*>(pointer);
    ID3D11Texture2D* texture = nullptr; bool from_view = false;
    HRESULT hr = unknown->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&texture));
    if (FAILED(hr) || !texture) {
        ID3D11View* view_object = nullptr;
        if (SUCCEEDED(unknown->QueryInterface(__uuidof(ID3D11View), reinterpret_cast<void**>(&view_object))) && view_object) {
            ID3D11Resource* resource = nullptr; view_object->GetResource(&resource); view_object->Release();
            if (resource) {
                HRESULT from = resource->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&texture));
                resource->Release();
                if (SUCCEEDED(from) && texture) { from_view = true; hr = S_OK; }
            }
        }
    }
    if (!texture) { NeuralFxPublishRegistration(NFX_REG_STAGE_INTERFACE, NFX_REG_QUERY_INTERFACE, hr, camera, epoch, nullptr, false, NeuralFxSlotsUsed()); return 0; }
    D3D11_TEXTURE2D_DESC desc; texture->GetDesc(&desc);
    // El descriptor se publica antes de juzgarlo: si se rechaza, el informe ya lleva lo que se
    // vio, que es justamente el dato que faltaba.
    uint32_t reason = NFX_REG_OK;
    if (!desc.Width || !desc.Height) reason = NFX_REG_DIMENSION;
    else if (desc.Format != DXGI_FORMAT_R16G16_FLOAT) reason = NFX_REG_FORMAT;
    else if (desc.SampleDesc.Count != 1) reason = NFX_REG_SAMPLES;
    else if (desc.MipLevels != 1) reason = NFX_REG_MIPS;
    else if (desc.ArraySize != 1) reason = NFX_REG_ARRAY;
    else if (!(desc.BindFlags & D3D11_BIND_SHADER_RESOURCE)) reason = NFX_REG_BIND_SRV;
    if (reason != NFX_REG_OK) {
        NeuralFxPublishRegistration(NFX_REG_STAGE_DESCRIPTOR, reason, S_OK, camera, epoch, &desc, from_view, NeuralFxSlotsUsed());
        texture->Release(); return 0;
    }
    ID3D11Device* device = nullptr; texture->GetDevice(&device);
    ID3D11ShaderResourceView* view = nullptr;
    HRESULT created = device->CreateShaderResourceView(texture, nullptr, &view); device->Release();
    if (FAILED(created) || !view) {
        NeuralFxPublishRegistration(NFX_REG_STAGE_VIEW, NFX_REG_VIEW, created, camera, epoch, &desc, from_view, NeuralFxSlotsUsed());
        texture->Release(); return 0;
    }
    std::lock_guard<std::mutex> lock(nfx_inputs_lock);
    for (auto& slot : nfx_motion_slots) if (!slot.handle) {
        if (++nfx_next_handle == 0) ++nfx_next_handle;
        slot.handle = nfx_next_handle; slot.camera = camera; slot.epoch = epoch;
        // La referencia de QueryInterface es la del hueco: no se suelta aquí.
        slot.texture = texture; slot.view = view;
        NeuralFxPublishRegistration(NFX_REG_STAGE_DONE, NFX_REG_OK, S_OK, camera, epoch, &desc, from_view, NeuralFxSlotsUsed());
        return slot.handle;
    }
    NeuralFxPublishRegistration(NFX_REG_STAGE_SLOT, NFX_REG_SLOTS, S_OK, camera, epoch, &desc, from_view, NeuralFxSlotsUsed());
    view->Release(); texture->Release(); return 0;
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
NFX_EXPORT void NFX_CALL NeuralFX_CancelMotion(uint32_t handle) { NeuralFxMotionComplete(handle); }
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

static void NeuralFxRetireUnused(ID3D11DeviceContext* context) {
    ID3D11Device* device = nullptr; context->GetDevice(&device);
    NeuralFxFrameV3 candidates[17] = {};
    {
        std::lock_guard<std::mutex> lock(nfx_lock);
        for (size_t i=0; i<16; ++i) candidates[i] = nfx_abandoned[i];
        candidates[16] = nfx_render_frame;
    }
    for (const auto& frame : candidates) if (frame.motion_handle) {
        bool same = false;
        {
            std::lock_guard<std::mutex> lock(nfx_inputs_lock);
            for (auto& slot : nfx_motion_slots) if (slot.handle == frame.motion_handle) {
                ID3D11Device* owner = nullptr; slot.texture->GetDevice(&owner); same = owner == device; owner->Release(); break;
            }
        }
        if (!same) continue;
        auto* query = NeuralFxPrepareOutput(context); if (!query) break;
        NeuralFxFinishOutput(query,frame,false);
        std::lock_guard<std::mutex> lock(nfx_lock);
        for (auto& pending : nfx_abandoned) if (pending.motion_handle == frame.motion_handle) pending = {};
        if (nfx_render_frame.motion_handle == frame.motion_handle) nfx_render_frame = {};
    }
    device->Release();
}

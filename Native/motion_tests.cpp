// D3D11 WARP fixture: no NVIDIA runtime, no city. Exercises the real selected descriptor.
#include "neuralfx_inputs.h"
#include <d3dcompiler.h>
#include <vector>
#include <cassert>
#include <cstdio>
#pragma comment(lib,"d3d11.lib")
#pragma comment(lib,"dxgi.lib")
#pragma comment(lib,"d3dcompiler.lib")
static ID3D11Texture2D* Texture(ID3D11Device* device, UINT width, UINT height, uint16_t x, uint16_t y) {
    D3D11_TEXTURE2D_DESC d={};d.Width=width;d.Height=height;d.MipLevels=1;d.ArraySize=1;d.Format=DXGI_FORMAT_R16G16_FLOAT;d.SampleDesc.Count=1;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    std::vector<uint16_t> pixels(width*height*2);for(size_t i=0;i<pixels.size();i+=2){pixels[i]=x;pixels[i+1]=y;}
    D3D11_SUBRESOURCE_DATA data={pixels.data(),width*4,0};ID3D11Texture2D* texture=nullptr;
    assert(SUCCEEDED(device->CreateTexture2D(&d,&data,&texture)));return texture;
}
static void Probe(ID3D11Device* device,ID3D11DeviceContext* context,const NeuralFxFrameV3& frame,ID3D11Texture2D* optical,ID3D11ShaderResourceView* opticalView,int percentage,bool expectNative) {
    NeuralFxSelectedMotion selected;assert(selected.Select(device,frame,optical,opticalView,1,1));
    assert(selected.provider==(expectNative?2u:1u));
    assert(selected.scale_x==(expectNative?257.f:1.f));
    ID3D11Resource* viewed=nullptr;selected.view->GetResource(&viewed);assert(viewed==selected.texture);viewed->Release();
    UINT width=257*percentage/100,height=129*percentage/100;
    D3D11_TEXTURE2D_DESC desc={};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_R16G16_FLOAT;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_UNORDERED_ACCESS;
    ID3D11Texture2D* output=nullptr;assert(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&output)));
    if(percentage==100) context->CopyResource(output,selected.texture);
    else {
        // The resample consumes the selected SRV; scales are validated separately.
        const char* source="Texture2D<float2> src:register(t0); RWTexture2D<float2> dst:register(u0); [numthreads(8,8,1)] void main(uint3 id:SV_DispatchThreadID){uint w,h;dst.GetDimensions(w,h);if(id.x<w&&id.y<h)dst[id.xy]=src.Load(int3(id.xy,0));}";
        ID3DBlob* code=nullptr;ID3DBlob* errors=nullptr;assert(SUCCEEDED(D3DCompile(source,strlen(source),nullptr,nullptr,nullptr,"main","cs_5_0",0,0,&code,&errors)));
        ID3D11ComputeShader* shader=nullptr;assert(SUCCEEDED(device->CreateComputeShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader)));code->Release();if(errors)errors->Release();
        ID3D11UnorderedAccessView* uav=nullptr;assert(SUCCEEDED(device->CreateUnorderedAccessView(output,nullptr,&uav)));
        context->CSSetShader(shader,nullptr,0);context->CSSetShaderResources(0,1,&selected.view);context->CSSetUnorderedAccessViews(0,1,&uav,nullptr);context->Dispatch((width+7)/8,(height+7)/8,1);
        ID3D11ShaderResourceView* none=nullptr;ID3D11UnorderedAccessView* noUav=nullptr;context->CSSetShaderResources(0,1,&none);context->CSSetUnorderedAccessViews(0,1,&noUav,nullptr);shader->Release();uav->Release();
    }
    desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging=nullptr;assert(SUCCEEDED(device->CreateTexture2D(&desc,nullptr,&staging)));context->CopyResource(staging,output);
    D3D11_MAPPED_SUBRESOURCE map={};assert(SUCCEEDED(context->Map(staging,0,D3D11_MAP_READ,0,&map)));auto* pixel=static_cast<uint16_t*>(map.pData);
    assert(pixel[0]==(expectNative?0x3c00:0)&&pixel[1]==(expectNative?0:0x4000));context->Unmap(staging,0);staging->Release();output->Release();
}
// Recurso a medida para los casos negativos: cada uno mueve un solo campo del descriptor, de
// modo que el motivo publicado no pueda deberse a otra cosa.
static ID3D11Texture2D* Shaped(ID3D11Device* device, DXGI_FORMAT format, UINT mips, UINT slices, UINT bind) {
    D3D11_TEXTURE2D_DESC d={};d.Width=64;d.Height=64;d.MipLevels=mips;d.ArraySize=slices;d.Format=format;d.SampleDesc.Count=1;d.BindFlags=bind;
    if(mips!=1) d.BindFlags|=D3D11_BIND_RENDER_TARGET, d.MiscFlags=D3D11_RESOURCE_MISC_GENERATE_MIPS;
    ID3D11Texture2D* texture=nullptr;assert(SUCCEEDED(device->CreateTexture2D(&d,nullptr,&texture)));return texture;
}
static NeuralFxRegistrationReport Report() {
    NeuralFxRegistrationReport report={};
    assert(NeuralFX_GetRegistrationReport(&report,sizeof(report))==1);
    assert(report.size==sizeof(report)&&report.version==4);
    return report;
}
// Recorre el registro real, no un doble que siempre acepta. Un cero debe llevar SIEMPRE un
// motivo único y el descriptor que se llegó a leer.
static void Registration(ID3D11Device* device) {
    assert(NeuralFX_GetRegistrationReport(nullptr,sizeof(NeuralFxRegistrationReport))==0);
    NeuralFxRegistrationReport probe={};assert(NeuralFX_GetRegistrationReport(&probe,4)==0); // tamaño ajeno rechazado

    assert(NeuralFX_RegisterMotion(nullptr,1,1)==0);
    assert(Report().reason==NFX_REG_NULL_POINTER&&Report().stage==NFX_REG_STAGE_INPUT);
    auto* good=Shaped(device,DXGI_FORMAT_R16G16_FLOAT,1,1,D3D11_BIND_SHADER_RESOURCE);
    assert(NeuralFX_RegisterMotion(good,0,1)==0&&Report().reason==NFX_REG_BAD_IDENTITY);
    assert(NeuralFX_RegisterMotion(good,1,0)==0&&Report().reason==NFX_REG_BAD_IDENTITY);

    // Un objeto COM que no es textura ni vista: se rechaza con motivo propio en vez de leer
    // un descriptor inventado desde una vtable equivocada.
    D3D11_BUFFER_DESC bd={};bd.ByteWidth=256;bd.BindFlags=D3D11_BIND_SHADER_RESOURCE;bd.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;bd.StructureByteStride=16;
    ID3D11Buffer* buffer=nullptr;assert(SUCCEEDED(device->CreateBuffer(&bd,nullptr,&buffer)));
    assert(NeuralFX_RegisterMotion(buffer,1,1)==0);
    assert(Report().reason==NFX_REG_QUERY_INTERFACE&&Report().stage==NFX_REG_STAGE_INTERFACE&&Report().width==0);
    buffer->Release();

    struct { DXGI_FORMAT format; UINT mips, slices, bind; uint32_t reason; } cases[] = {
        {DXGI_FORMAT_R8G8B8A8_UNORM,1,1,D3D11_BIND_SHADER_RESOURCE,NFX_REG_FORMAT},
        // Misma anchura de canal, otra interpretación: se rechaza. El formato se acepta por
        // familia, jamás por bytes por píxel.
        {DXGI_FORMAT_R16G16_UNORM,1,1,D3D11_BIND_SHADER_RESOURCE,NFX_REG_FORMAT},
        {DXGI_FORMAT_R16G16_SINT,1,1,D3D11_BIND_SHADER_RESOURCE,NFX_REG_FORMAT},
        {DXGI_FORMAT_R16G16_FLOAT,2,1,D3D11_BIND_SHADER_RESOURCE,NFX_REG_MIPS},
        {DXGI_FORMAT_R16G16_FLOAT,1,2,D3D11_BIND_SHADER_RESOURCE,NFX_REG_ARRAY},
        {DXGI_FORMAT_R16G16_FLOAT,1,1,D3D11_BIND_RENDER_TARGET,NFX_REG_BIND_SRV},
    };
    for (const auto& item : cases) {
        auto* texture=Shaped(device,item.format,item.mips,item.slices,item.bind);
        assert(NeuralFX_RegisterMotion(texture,7,3)==0);
        auto report=Report();
        assert(report.reason==item.reason&&report.stage==NFX_REG_STAGE_DESCRIPTOR);
        assert(report.width==64&&report.height==64&&report.camera==7&&report.epoch==3); // descriptor real, no supuesto
        texture->Release();
    }

    // Caso nominal y su informe completo.
    uint32_t handle=NeuralFX_RegisterMotion(good,7,3);assert(handle);
    auto ok=Report();
    assert(ok.reason==NFX_REG_OK&&ok.stage==NFX_REG_STAGE_DONE&&ok.hresult==0&&ok.from_view==0);
    assert(ok.format==DXGI_FORMAT_R16G16_FLOAT&&ok.mip_levels==1&&ok.array_size==1&&ok.sample_count==1);
    assert(ok.bind_flags&D3D11_BIND_SHADER_RESOURCE);
    assert(ok.slots_used==1&&ok.slots_total==16&&ok.accepted==1);
    NeuralFX_ReleaseMotion(handle);

    // El caso que CS1 entrega de verdad, medido el 11-sep-2026: Unity crea la RenderTexture
    // RGHalf como R16G16_TYPELESS (33) y el puente exigia R16G16_FLOAT (34). Se acepta la
    // familia y la vista se pide tipada; con nullptr, sobre un tipeless, fallaria.
    auto* opaque=Shaped(device,DXGI_FORMAT_R16G16_TYPELESS,1,1,D3D11_BIND_SHADER_RESOURCE);
    handle=NeuralFX_RegisterMotion(opaque,7,3);assert(handle);
    auto blind=Report();
    assert(blind.reason==NFX_REG_OK&&blind.stage==NFX_REG_STAGE_DONE&&blind.format==DXGI_FORMAT_R16G16_TYPELESS);
    NeuralFX_ReleaseMotion(handle);opaque->Release();

    // Contrato adicional declarado: si llega una vista, se pide su recurso. Esto NO afirma que
    // Unity entregue vistas; acredita que el registro no se rompe si las recibe.
    ID3D11ShaderResourceView* view=nullptr;assert(SUCCEEDED(device->CreateShaderResourceView(good,nullptr,&view)));
    handle=NeuralFX_RegisterMotion(view,7,3);assert(handle&&Report().from_view==1&&Report().reason==NFX_REG_OK);
    NeuralFX_ReleaseMotion(handle);view->Release();

    // Huecos agotados: motivo propio, y la tabla no crece.
    uint32_t held[16]={};
    for(int i=0;i<16;i++){held[i]=NeuralFX_RegisterMotion(good,7,3);assert(held[i]);}
    assert(NeuralFX_RegisterMotion(good,7,3)==0);
    auto full=Report();assert(full.reason==NFX_REG_SLOTS&&full.stage==NFX_REG_STAGE_SLOT&&full.slots_used==16);
    for(int i=0;i<16;i++) NeuralFX_ReleaseMotion(held[i]);
    assert(Report().counts[NFX_REG_OK]>=17&&Report().rejected>=8);

    // Los turnos son estado vivo, no historia: una reserva concedida sube el contador y deja
    // uno en vuelo; la devolucion lo baja. Sin esto, un turno que nunca vuelve es invisible.
    handle=NeuralFX_RegisterMotion(good,7,3);assert(handle);
    auto before=Report();
    assert(NeuralFX_ReserveMotion(handle));
    auto busy=Report();
    assert(busy.reserved_now==before.reserved_now+1&&busy.reserve_ok==before.reserve_ok+1);
    assert(!NeuralFX_ReserveMotion(handle)); // el mismo turno no se concede dos veces
    assert(Report().reserve_denied==before.reserve_denied+1);
    NeuralFX_CancelMotion(handle);
    auto freed=Report();
    assert(freed.reserved_now==before.reserved_now&&freed.completed==before.completed+1);
    NeuralFX_ReleaseMotion(handle);

    // El selector tambien rechazaba por cinco condiciones con un solo silencio. Cada una debe
    // dar su motivo, y el nominal debe contarse como servido.
    auto* sized=Texture(device,257,129,0x3c00,0);
    ID3D11ShaderResourceView* opticalView=nullptr;assert(SUCCEEDED(device->CreateShaderResourceView(sized,nullptr,&opticalView)));
    uint32_t live=NeuralFX_RegisterMotion(sized,42,1);assert(live&&NeuralFX_ReserveMotion(live));
    NeuralFxFrameV3 shaped={64,3,1,1,257,129,0,0,42,1,0,live,257,129,NFX_MAGIC,0};
    struct { const char* what; uint32_t handle, camera, epoch, width, height, reason; } picks[] = {
        {"sin handle",0,42,1,257,129,NFX_SEL_NO_HANDLE},
        {"handle ajeno",0xBEEF,42,1,257,129,NFX_SEL_UNKNOWN_HANDLE},
        {"otra camara",live,43,1,257,129,NFX_SEL_CAMERA},
        {"otra epoca",live,42,2,257,129,NFX_SEL_EPOCH},
        {"otra extension",live,42,1,256,129,NFX_SEL_EXTENT},
        {"nominal",live,42,1,257,129,NFX_SEL_USED},
    };
    for (const auto& pick : picks) {
        NeuralFxFrameV3 attempt=shaped;
        attempt.motion_handle=pick.handle;attempt.camera=pick.camera;attempt.epoch=pick.epoch;
        attempt.width=pick.width;attempt.height=pick.height;
        { NeuralFxSelectedMotion chosen;assert(chosen.Select(device,attempt,sized,opticalView,1,1));
          assert(chosen.provider==(pick.reason==NFX_SEL_USED?2u:1u)); }
        auto seen=Report();
        assert(seen.select_reason==pick.reason);
        if(pick.reason==NFX_SEL_EXTENT) assert(seen.select_slot_width==257&&seen.select_frame_width==256);
    }
    assert(Report().select_native>=1&&Report().select_fallback>=4);
    // Identidad de dispositivo: el mismo puntero da coincidencia 1 y anota su LUID. Un segundo
    // dispositivo WARP de verdad no coincide, y entonces el motivo SI es el dispositivo.
    assert(Report().select_device_match==1);
    ID3D11Device* other=nullptr;ID3D11DeviceContext* otherContext=nullptr;
    assert(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&other,nullptr,&otherContext)));
    { NeuralFxSelectedMotion foreign;assert(foreign.Select(other,shaped,sized,opticalView,1,1));assert(foreign.provider==1u); }
    auto crossed=Report();
    assert(crossed.select_reason==NFX_SEL_DEVICE&&crossed.select_device_match==0);
    otherContext->Release();other->Release();
    NeuralFX_ReleaseMotion(live);opticalView->Release();sized->Release();
    good->Release();
}
int main(){
    ID3D11Device* device=nullptr;ID3D11DeviceContext* context=nullptr;assert(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)));
    Registration(device);
    auto* optical=Texture(device,257,129,0,0x4000);auto* native=Texture(device,257,129,0x3c00,0);ID3D11ShaderResourceView* opticalView=nullptr;assert(SUCCEEDED(device->CreateShaderResourceView(optical,nullptr,&opticalView)));
    uint32_t handle=NeuralFX_RegisterMotion(native,42,1);assert(handle&&NeuralFX_ReserveMotion(handle));assert(!NeuralFX_ReserveMotion(handle));
    NeuralFxFrameV3 frame={64,3,1,1,257,129,0,0,42,1,0,handle,257,129,NFX_MAGIC,0};
    for(int percent:{100,85,66}){
        Probe(device,context,frame,optical,opticalView,percent,true);
        frame.motion_handle=0;Probe(device,context,frame,optical,opticalView,percent,false);frame.motion_handle=handle;
        frame.epoch=2;Probe(device,context,frame,optical,opticalView,percent,false);frame.epoch=1;
    }
    NeuralFX_ReleaseMotion(handle);assert(!NeuralFX_ReserveMotion(handle));NeuralFX_CancelMotion(handle);assert(!NeuralFX_ReserveMotion(handle));
    // A disabled camera may have copied a slot which the feeder never consumed.
    handle=NeuralFX_RegisterMotion(native,42,2);assert(handle&&NeuralFX_ReserveMotion(handle));
    frame.epoch=2;frame.frame=10;frame.motion_handle=handle;
    NeuralFX_SetEnabled(1);assert(NeuralFX_SubmitFrameV3(&frame,64));NeuralFxRenderEvent(10);
    NeuralFX_SetEnabled(0);NeuralFX_ReleaseMotion(handle);NeuralFxRetireUnused(context);
    context->Flush();
    bool retired=false;
    for(int attempt=0;attempt<5000&&!retired;attempt++) {
        NeuralFxPollOutputs();retired=true;
        for(const auto& slot:nfx_motion_slots)if(slot.handle==handle)retired=false;
        if(!retired)Sleep(1);
    }
    assert(retired); // own COM retention ended only after the event query
    opticalView->Release();optical->Release();native->Release();context->Release();device->Release();
    std::puts("D3D11 WARP: registro nominal, once motivos de rechazo con descriptor real, recurso desde vista, huecos agotados, seleccion de recurso/SRV/escala, respaldo optico, epocas, reserva acotada y 100/85/66 por ciento. No es una captura NGX ni de CS1.");
}

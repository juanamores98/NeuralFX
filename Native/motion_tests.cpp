// D3D11 WARP fixture: no NVIDIA runtime, no city. Exercises the real selected descriptor.
#include "neuralfx_inputs.h"
#include <d3dcompiler.h>
#include <vector>
#include <cassert>
#include <cstdio>
#pragma comment(lib,"d3d11.lib")
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
int main(){
    ID3D11Device* device=nullptr;ID3D11DeviceContext* context=nullptr;assert(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)));
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
    std::puts("D3D11 WARP: registered resource/SRV/scale selection, optical fallback, epochs, bounded reservation, 100/85/66 percent and odd extents passed. Not an NGX/CS1 capture.");
}

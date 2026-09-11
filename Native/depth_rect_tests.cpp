// Exercises the candidate RawDepth body on D3D11 WARP, not a reimplementation.
#include <d3d11.h>
#include <d3dcompiler.h>
#include <cassert>
#include <cstdio>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")

static void Check(ID3D11Device* device, ID3D11DeviceContext* context, const std::string& body,
    UINT width, UINT height, bool reversed, bool flipped, bool customTransform, bool enabled = true) {
    // Nonzero origin tests the general mapping; odd dimensions expose half-texel errors.
    const UINT frameWidth = 97, frameHeight = 67, sceneWidth = 89, sceneHeight = 59, x = 3, y = 5;
    std::string source =
        "static const float4 NFX_DepthSceneRect=float4(3,5,89,59);\nstatic const float2 NFX_DepthFrameSize=float2(97,67);\n"
        "#define BUFFER_WIDTH 97\n#define BUFFER_HEIGHT 67\n#define BUFFER_SCREEN_SIZE float2(97,67)\n"
        "#define RESHADE_DEPTH_INPUT_X_SCALE 1.0\n#define RESHADE_DEPTH_INPUT_X_OFFSET 0.0\n"
        "#define RESHADE_DEPTH_INPUT_Y_OFFSET 0.0\n#define RESHADE_DEPTH_INPUT_X_PIXEL_OFFSET 0\n"
        "#define RESHADE_DEPTH_INPUT_Y_PIXEL_OFFSET 0\n";
    source += "#define RESHADE_DEPTH_INPUT_IS_REVERSED " + std::to_string(reversed) + "\n";
    source += "static const bool NFX_DepthRectEnabled = " + std::to_string(enabled) + ";\n";
    source += "#define RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN " + std::to_string(flipped) + "\n";
    source += std::string("#define RESHADE_DEPTH_INPUT_Y_SCALE ") + (customTransform ? "0.8\n" : "1.0\n");
    // ReShade intrinsics/sampler syntax adapted to D3D11; RawDepth itself is unchanged.
    source += "Texture2D<float> depth:register(t0); SamplerState pointDepth:register(s0);\n"
        "namespace ReShade {static const int DepthBuffer=0;}\nstatic const int NeuralFX_PointDepth=0;\n"
        "float2 tex2Dsize(int s){uint w,h;depth.GetDimensions(w,h);return float2(w,h);}\n"
        "float4 tex2Dlod(int s,float4 uv){return depth.SampleLevel(pointDepth,uv.xy,0).xxxx;}\n"
        "float RawDepthLegacy(float2 uv){return -7.0;}\n" + body +
        "\nRWTexture2D<float> output:register(u0); [numthreads(8,8,1)] void main(uint3 p:SV_DispatchThreadID)"
        "{if(p.x<97&&p.y<67)output[p.xy]=RawDepth((p.xy+0.5)/float2(97,67));}";
    ID3DBlob *code = nullptr, *errors = nullptr;
    HRESULT compiled = D3DCompile(source.data(), source.size(), nullptr, nullptr, nullptr, "main", "cs_5_0", 0, 0, &code, &errors);
    if (errors) { std::fputs(static_cast<const char*>(errors->GetBufferPointer()), stderr); errors->Release(); }
    assert(SUCCEEDED(compiled));
    ID3D11ComputeShader* shader = nullptr;
    assert(SUCCEEDED(device->CreateComputeShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &shader)));
    code->Release();
    std::vector<float> pixels(width * height);
    for (UINT i = 0; i < pixels.size(); ++i) pixels[i] = float(i + 1) / float(pixels.size() + 1);
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width; desc.Height = height; desc.MipLevels = desc.ArraySize = desc.SampleDesc.Count = 1;
    desc.Format = DXGI_FORMAT_R32_FLOAT; desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA initial = {pixels.data(), width * sizeof(float), 0};
    ID3D11Texture2D *input = nullptr, *output = nullptr, *staging = nullptr;
    assert(SUCCEEDED(device->CreateTexture2D(&desc, &initial, &input)));
    ID3D11ShaderResourceView* srv = nullptr;
    assert(SUCCEEDED(device->CreateShaderResourceView(input, nullptr, &srv)));
    desc.Width = frameWidth; desc.Height = frameHeight; desc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    assert(SUCCEEDED(device->CreateTexture2D(&desc, nullptr, &output)));
    ID3D11UnorderedAccessView* uav = nullptr;
    assert(SUCCEEDED(device->CreateUnorderedAccessView(output, nullptr, &uav)));
    D3D11_SAMPLER_DESC sd = {}; sd.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sd.MaxLOD = D3D11_FLOAT32_MAX;
    ID3D11SamplerState* sampler = nullptr;
    assert(SUCCEEDED(device->CreateSamplerState(&sd, &sampler)));
    context->CSSetShader(shader, nullptr, 0); context->CSSetShaderResources(0, 1, &srv);
    context->CSSetSamplers(0, 1, &sampler); context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
    context->Dispatch((frameWidth + 7) / 8, (frameHeight + 7) / 8, 1);
    context->ClearState();
    desc.BindFlags = 0; desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    assert(SUCCEEDED(device->CreateTexture2D(&desc, nullptr, &staging)));
    context->CopyResource(staging, output);
    D3D11_MAPPED_SUBRESOURCE mapped = {};
    assert(SUCCEEDED(context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped)));
    for (UINT row = 0; row < frameHeight; ++row) for (UINT col = 0; col < frameWidth; ++col) {
        float expected = reversed ? 0.f : 1.f;
        if (!enabled || width != sceneWidth || height != sceneHeight || customTransform) expected = -7.f;
        else if (col >= x && col < x + sceneWidth && row >= y && row < y + sceneHeight) {
            UINT localY = flipped ? sceneHeight - 1 - (row - y) : row - y;
            expected = pixels[localY * width + col - x];
        }
        const auto* values = reinterpret_cast<const float*>(static_cast<const char*>(mapped.pData) + row * mapped.RowPitch);
        assert(values[col] == expected);
    }
    context->Unmap(staging, 0);
    sampler->Release(); staging->Release(); uav->Release(); output->Release(); srv->Release(); input->Release(); shader->Release();
}
int main(int argc, char** argv) {
    assert(argc == 2); std::ifstream file(argv[1]); assert(file.good());
    std::string text((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    auto start = text.find("float RawDepth(float2 uv)"); assert(start != std::string::npos);
    const std::string body = text.substr(start);
    ID3D11Device* device = nullptr; ID3D11DeviceContext* context = nullptr;
    assert(SUCCEEDED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &context)));
    for (bool reversed : {false, true}) for (bool flipped : {false, true}) Check(device, context, body, 89, 59, reversed, flipped, false);
    Check(device, context, body, 97, 67, true, false, false); // Full-frame depth is left alone.
    Check(device, context, body, 32, 32, true, false, false); // Unrelated resource is left alone.
    Check(device, context, body, 89, 59, true, false, true);  // No double application of user correction.
    Check(device, context, body, 89, 59, true, false, false, false); // Off preserves every legacy pixel.
    context->Release(); device->Release();
    std::puts("Depth rect: 8 WARP cases passed, every pixel checked including Off. No CS1/NGX or ReShade FX compiler validation.");
}

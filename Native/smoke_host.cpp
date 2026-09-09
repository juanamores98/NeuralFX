// D3D11 integration fixture: exercises the installed ReShade/NGX pipeline without a city/save.
// Run from a private fixture containing the pipeline files. Not part of the mod package.
#include <windows.h>
#include <d3d11.h>
#include <cstdio>
#include <cmath>
#include <cstdlib>
#include <cstdint>
#include "neuralfx_contract.h"
#pragma comment(lib, "d3d11.lib")

int main(int argc, char** argv) {
    const UINT width = argc > 1 ? static_cast<UINT>(std::atoi(argv[1])) : 1920;
    const UINT height = argc > 2 ? static_cast<UINT>(std::atoi(argv[2])) : 1080;
    if (!width || !height || width > 7680 || height > 4320) return 4;
    WNDCLASSW wc = {}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"NeuralFXSmoke";
    RegisterClassW(&wc);
    HWND window = CreateWindowW(wc.lpszClassName, L"NeuralFX graphics fixture", WS_OVERLAPPEDWINDOW, 0, 0, width, height, nullptr, nullptr, wc.hInstance, nullptr);
    DXGI_SWAP_CHAIN_DESC desc = {}; desc.BufferDesc.Width = width; desc.BufferDesc.Height = height;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount = 2; desc.OutputWindow = window;
    desc.Windowed = TRUE; desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    ID3D11Device* device = nullptr; ID3D11DeviceContext* context = nullptr; IDXGISwapChain* swapchain = nullptr;
    HRESULT hr = D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &desc, &swapchain, &device, nullptr, &context);
    if (FAILED(hr)) { std::printf("CreateDevice failed: %08lX\n", hr); return 1; }
    ID3D11Texture2D* buffer = nullptr; ID3D11RenderTargetView* target = nullptr;
    swapchain->GetBuffer(0, IID_PPV_ARGS(&buffer));
    hr = device->CreateRenderTargetView(buffer, nullptr, &target); buffer->Release();
    if (FAILED(hr)) return 2;
    ULONGLONG deadline = GetTickCount64() + 30000;
    unsigned frame = 0;
    using Frame = NeuralFxFrameV3;
    struct Status { uint32_t size, version, capabilities, frame, reset, evaluations, width, height; int32_t result; uint32_t age; };
    auto addon = GetModuleHandleW(L"dlss5-feed.addon64");
    auto submit = reinterpret_cast<int(__cdecl*)(const Frame*, uint32_t)>(GetProcAddress(addon, "NeuralFX_SubmitFrameV3"));
    auto enable = reinterpret_cast<int(__cdecl*)(uint32_t)>(GetProcAddress(addon, "NeuralFX_SetEnabled"));
    auto getEvent = reinterpret_cast<void*(__cdecl*)()>(GetProcAddress(addon, "NeuralFX_GetRenderEvent"));
    auto getStatus = reinterpret_cast<int(__cdecl*)(Status*)>(GetProcAddress(addon, "NeuralFX_GetStatus"));
    if (!submit || !getEvent || !getStatus || !enable) return 5;
    auto renderEvent = reinterpret_cast<void(__stdcall*)(int)>(getEvent());
    while (GetTickCount64() < deadline) {
        MSG msg; while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
        float color[4] = {0.2f + 0.1f * std::sin(frame * 0.01f), 0.3f, 0.4f, 1.0f};
        context->OMSetRenderTargets(1, &target, nullptr); context->ClearRenderTargetView(target, color);
        Frame metadata = {sizeof(Frame), 3, frame + 1, frame < 120 ? 7u : 8u, width, height, 0, 0, 1, 1, 0, 0, 1, 1, NFX_MAGIC, 0};
        enable(1);
        if (!submit(&metadata, sizeof(metadata))) return 6;
        renderEvent(frame + 1);
        hr = swapchain->Present(0, 0); if (FAILED(hr)) break;
        ++frame; Sleep(8);
    }
    std::printf("Presented %u frames, result %08lX, device %08lX\n", frame, hr, device->GetDeviceRemovedReason());
    Status status = {sizeof(Status)}; getStatus(&status);
    std::printf("Bridge evaluations=%u reset=%u result=%d work=%ux%u age=%u\n", status.evaluations, status.reset, status.result, status.width, status.height, status.age);
    target->Release(); swapchain->Release(); context->Release(); device->Release(); DestroyWindow(window);
    return FAILED(hr) || status.result != 1 || status.reset != 8 || status.evaluations < 120 ? 3 : 0;
}

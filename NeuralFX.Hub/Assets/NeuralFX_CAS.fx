// NeuralFX CAS port, based on AMD FidelityFX CAS (MIT).
// Copyright (c) 2017-2020 Advanced Micro Devices, Inc. All rights reserved.
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#include "ReShade.fxh"
uniform float Sharpening < ui_type = "slider"; ui_min = 0.0; ui_max = 1.0; ui_label = "Nitidez"; > = 0.3;
float4 CasPass(float4 position : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
    float2 px = float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT);
    float3 a = tex2D(ReShade::BackBuffer, uv + px * float2(-1, -1)).rgb;
    float3 b = tex2D(ReShade::BackBuffer, uv + px * float2( 0, -1)).rgb;
    float3 c = tex2D(ReShade::BackBuffer, uv + px * float2( 1, -1)).rgb;
    float3 d = tex2D(ReShade::BackBuffer, uv + px * float2(-1,  0)).rgb;
    float4 center = tex2D(ReShade::BackBuffer, uv);
    float3 e = center.rgb;
    float3 f = tex2D(ReShade::BackBuffer, uv + px * float2( 1,  0)).rgb;
    float3 g = tex2D(ReShade::BackBuffer, uv + px * float2(-1,  1)).rgb;
    float3 h = tex2D(ReShade::BackBuffer, uv + px * float2( 0,  1)).rgb;
    float3 i = tex2D(ReShade::BackBuffer, uv + px * float2( 1,  1)).rgb;
    // AMD's gamma-2 approximation for SDR input, with inverse conversion on output.
    a *= a; b *= b; c *= c; d *= d; e *= e; f *= f; g *= g; h *= h; i *= i;
    float3 mn = min(min(min(d, e), min(f, b)), h);
    mn += min(mn, min(min(a, c), min(g, i)));
    float3 mx = max(max(max(d, e), max(f, b)), h);
    mx += max(mx, max(max(a, c), max(g, i)));
    float3 amplitude = sqrt(saturate(min(mn, 2.0 - mx) / max(mx, 1e-5)));
    float3 weight = amplitude * (-1.0 / lerp(8.0, 5.0, saturate(Sharpening)));
    return float4(sqrt(saturate(((b + d + f + h) * weight + e) / (1.0 + 4.0 * weight))), center.a);
}
technique NeuralFX_CAS
{
    pass { VertexShader = PostProcessVS; PixelShader = CasPass; }
}

// Trial for a camera-sized Generic Depth resource. Included after RawDepthLegacy.
// The bridge updates these uniforms before effects using the rendered motion slot.
// Matching dimensions do not establish camera identity: verify silhouettes in-game.
uniform bool NFX_DepthRectEnabled < hidden = true; > = false;
uniform float4 NFX_DepthSceneRect < hidden = true; > = float4(0, 0, 0, 0);
uniform float2 NFX_DepthFrameSize < hidden = true; > = float2(0, 0);
sampler NeuralFX_PointDepth
{
    Texture = ReShade::DepthBufferTex;
    MinFilter = POINT; MagFilter = POINT; MipFilter = POINT;
    AddressU = Clamp; AddressV = Clamp;
};

float RawDepth(float2 uv)
{
    const float2 sourceSize = tex2Dsize(ReShade::DepthBuffer);
    const float2 sceneSize = NFX_DepthSceneRect.zw;
    // Preserve existing user transforms and full-frame buffers. Never infer a crop
    // from an arbitrary smaller depth texture (e.g. a shadow map).
    const bool identityTransform =
        RESHADE_DEPTH_INPUT_X_SCALE == 1.0 && RESHADE_DEPTH_INPUT_Y_SCALE == 1.0 &&
        RESHADE_DEPTH_INPUT_X_OFFSET == 0.0 && RESHADE_DEPTH_INPUT_Y_OFFSET == 0.0 &&
        RESHADE_DEPTH_INPUT_X_PIXEL_OFFSET == 0 && RESHADE_DEPTH_INPUT_Y_PIXEL_OFFSET == 0;
    float depth = RawDepthLegacy(uv);
    if (NFX_DepthRectEnabled && all(BUFFER_SCREEN_SIZE == NFX_DepthFrameSize) && all(sceneSize > 0.0) &&
        all(sourceSize == sceneSize) && identityTransform)
    {
        const float2 pixel = floor(uv * BUFFER_SCREEN_SIZE) + 0.5;
        const float2 local = pixel - NFX_DepthSceneRect.xy;
        if (any(local < 0.0) || any(local >= sceneSize))
        {
#if RESHADE_DEPTH_INPUT_IS_REVERSED
            depth = 0.0;
#else
            depth = 1.0;
#endif
        }
        else
        {
            float2 depthUV = local / sceneSize;
#if RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN
            depthUV.y = 1.0 - depthUV.y;
#endif
            depth = tex2Dlod(NeuralFX_PointDepth, float4(depthUV, 0.0, 0.0)).x;
        }
    }
    return depth;
}

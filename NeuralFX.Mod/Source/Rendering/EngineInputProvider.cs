using UnityEngine;
using UnityEngine.Rendering;
namespace NeuralFX.Rendering
{
    // Bounded, owned GPU copies. Native pointers are obtained only on allocation.
    // Coverage/sign remain experimental until the city fixture is accepted.
    internal sealed class EngineInputProvider
    {
        private readonly RenderTexture[] _textures = new RenderTexture[3];
        private readonly uint[] _handles = new uint[3];
        private NativeBridge _bridge;
        private int _width, _height;
        private uint _epoch;
        public bool Prepare(NativeBridge bridge, uint camera, uint epoch, int width, int height)
        {
            if (_epoch == epoch && width == _width && height == _height) return true;
            Release(); _bridge = bridge;
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf)) return false;
            for (int i = 0; i < 3; i++)
            {
                var texture = new RenderTexture(width, height, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
                texture.name = "NeuralFX motion " + i; texture.useMipMap = false; texture.filterMode = FilterMode.Point;
                _textures[i] = texture;
                if (!texture.Create()) { Release(); return false; }
                _handles[i] = bridge.RegisterMotion(texture.GetNativeTexturePtr(), camera, epoch);
                if (_handles[i] == 0) { Release(); return false; }
            }
            _width = width; _height = height; _epoch = epoch; return true;
        }
        public uint Record(CommandBuffer commands)
        {
            for (int i = 0; i < 3; ++i) if (_handles[i] != 0 && _bridge.ReserveMotion(_handles[i]))
            {
                // Executed AfterEverything, after this camera produced its inputs.
                commands.Blit(BuiltinRenderTextureType.MotionVectors, new RenderTargetIdentifier(_textures[i]));
                return _handles[i];
            }
            return 0; // GPU behind / unavailable: select the complete optical descriptor.
        }
        public void Release()
        {
            for (int i = 0; i < 3; ++i) {
                if (_bridge != null) _bridge.ReleaseMotion(_handles[i]); _handles[i] = 0;
                if (_textures[i] != null) { Object.Destroy(_textures[i]); _textures[i] = null; }
            }
            _epoch = 0;
        }
    }
}

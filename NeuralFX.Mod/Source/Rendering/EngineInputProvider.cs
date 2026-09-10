using UnityEngine;
using UnityEngine.Rendering;
namespace NeuralFX.Rendering
{
    // Bounded, owned GPU copies. Native pointers are obtained only on allocation.
    // Coverage/sign remain experimental until the city fixture is accepted.
    //
    // <b>Por qué las texturas miden lo que mide la pantalla y no la cámara.</b> El selector
    // nativo exige que el tamaño de la textura registrada coincida con el del frame anunciado
    // (neuralfx_inputs.h: candidate.Width/Height contra frame.width/height). En CS1 no
    // coinciden: la cámara dibuja en un rect de 0,895 de alto dentro de la pantalla, así que
    // reservarlas al tamaño de cámara dejaba esta ruta descartada en silencio en cada frame y
    // el pase temporal se quedaba siempre con el movimiento óptico estimado.
    //
    // <b>Por qué una copia por región y no un blit.</b> Un blit estiraría los vectores de 1933
    // a 2160 filas: cada píxel del color leería el movimiento de otra fila, hasta 227 más
    // abajo. La copia coloca el bloque de la cámara en el hueco que de verdad ocupa y deja el
    // resto —la franja de la barra inferior, que no tiene escena— en cero.
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
            // Sin copia por región no hay forma de colocar el bloque en su sitio, y estirarlo
            // sería peor que no mandarlo: se vuelve al descriptor óptico completo.
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0) return false;
            for (int i = 0; i < 3; i++)
            {
                var texture = new RenderTexture(width, height, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
                texture.name = "NeuralFX motion " + i; texture.useMipMap = false; texture.filterMode = FilterMode.Point;
                _textures[i] = texture;
                if (!texture.Create()) { Release(); return false; }
                // La franja sin escena no se escribe nunca más: se pone a cero una sola vez, al
                // reservar. Un contenido indefinido ahí sería movimiento inventado.
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = texture; GL.Clear(false, true, Color.clear); RenderTexture.active = previous;
                _handles[i] = bridge.RegisterMotion(texture.GetNativeTexturePtr(), camera, epoch);
                if (_handles[i] == 0) { Release(); return false; }
            }
            _width = width; _height = height; _epoch = epoch; return true;
        }
        /// <summary>
        /// Copia los vectores de la cámara al hueco que ocupa dentro del frame anunciado.
        /// El origen es el bloque completo de la cámara; el destino, su posición en pantalla
        /// con el origen abajo a la izquierda, que es como Unity da <c>pixelRect</c>.
        /// </summary>
        public uint Record(CommandBuffer commands, int sourceWidth, int sourceHeight, int x, int y)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0) return 0;
            if (x < 0 || y < 0 || x + sourceWidth > _width || y + sourceHeight > _height) return 0;
            for (int i = 0; i < 3; ++i) if (_handles[i] != 0 && _bridge.ReserveMotion(_handles[i]))
            {
                // Executed AfterEverything, after this camera produced its inputs.
                commands.CopyTexture(BuiltinRenderTextureType.MotionVectors, 0, 0, 0, 0, sourceWidth, sourceHeight,
                    new RenderTargetIdentifier(_textures[i]), 0, 0, x, y);
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

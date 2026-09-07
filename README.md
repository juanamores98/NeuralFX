# NeuralFX (v2)

Diagnóstico de hardware, inyección atómica de DLSS 5 / ReShade y rollback limpio (zero-trace) para **Cities: Skylines 1** (Unity 5.6.7f1 / DirectX 11 / x64).  
Desarrollo original de **juanamores98** — suite de escalado y renderizado neural de segunda generación (v2).

---

## ¿Qué es NeuralFX?

`NeuralFX` es una solución integral dividida en dos componentes especializados que cooperan de forma limpia:

1. **`NeuralFX.Hub` (Companion App de Escritorio — .NET 8 WPF):**
   - **Diagnóstico Estricto de Hardware:** Lee directamente del Registro de Windows (`HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}`) el valor QWORD de 64 bits de VRAM, detectando con exactitud los **16.3 GB de VRAM reales** en tarjetas como la RTX 5080 y eliminando por completo el desbordamiento a 4095 MB presente en WMI.
   - **Validación de Drivers:** Analiza la versión interna del controlador de pantalla (ej. `32.0.16.1686` $\rightarrow$ `616.86`) asegurando que cumpla el estándar requerido `>= 570.xx`.
   - **Detección de Arquitectura:** Reconoce arquitecturas modernas de NVIDIA (Blackwell / Ada Lovelace) y determina compatibilidad con Reconstrucción Neural (DLSS 5).
   - **Gestor Criptográfico de Dependencias:** Elimina enlaces efímeros o caducos de Discord CDN. Implementa validación SHA-256 e importador guiado para binarios propietarios de NVIDIA (`nvngx_dlss.dll` y `nvngx_dlssnr.dll`).
   - **Inyección y Rollback Atómico (Zero-Trace):** Registra cada archivo y hash en `NeuralFX_Manifest.json`. Si el usuario decide desinstalar, el motor de rollback elimina todos los binarios, vacía logs en caliente (`ReShade.log`, `dlss5-feed.log`), restaura backups de DLLs previas y deja la instalación de Cities: Skylines 100% en estado vanilla original.

2. **`NeuralFX.Mod` (Mod In-Game — Unity 5.6 / .NET 3.5):**
   - **Activación de Buffer de Profundidad y Vectores de Movimiento:** Activa permanentemente en la cámara principal `Camera.main.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;`, permitiendo que el puente nativo DX11 de DLSS 5 reciba la información temporal sin alterar los shaders vanilla del juego.
   - **Detección de Módulos Nativos:** Verifica mediante llamadas Win32 `kernel32.dll!GetModuleHandle` si `dxgi.dll` y `dlss5-feed.addon64` están inyectados y operando en el espacio de memoria del proceso `Cities.exe`.
   - **HUD Flotante de Telemetría (`Ctrl + Alt + N`):** Muestra resolución en tiempo real, tasa de refresco, framerate y tiempo de frame.
   - **Detector de Colisión Temporal:** Analiza los componentes de la cámara y notifica de inmediato si otro mod gráfico tiene activo un TAA o SMAA legacy que genere artefactos de doble filtrado o ghosting con DLSS 5.
   - **Atajo para ReShade:** Recordatorio in-game de la tecla `Home` para abrir el menú de ReShade y calibrar la nitidez adaptativa AMD FidelityFX CAS.

---

## Requisitos del Sistema

- **Juego:** Cities: Skylines 1 (versión 1.17+ / Unity 5.6.7f1 / x64 / DirectX 11).
- **GPU:** NVIDIA GeForce RTX Serie 5000 (Blackwell) recomendada para Reconstrucción Neural DLSS 5 completa, o RTX Serie 4000/3000/2000 para DLSS Super Resolution estándar.
- **VRAM:** 6 GB o superior (8 GB+ recomendado para resoluciones 1440p / 4K con mods intensivos).
- **Driver NVIDIA:** Versión 570.00 o superior instalada.
- **Sistema Operativo:** Windows 10 / Windows 11 (64-bit).

---

## Estructura del Desplegable

El mod se distribuye y publica desde la carpeta estándar de Addons de Cities: Skylines:

```text
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\NeuralFX\
├── NeuralFX.dll                  # Mod in-game para Cities: Skylines 1
├── Iniciar-NeuralFX-Hub.bat      # Lanzador directo de la aplicación de escritorio
├── README.md                     # Documentación completa y guía de uso
├── LICENSE                       # Licencia MIT-0
├── NOTICE                        # Reconocimientos y marcas
├── deployment-manifest.json      # Checksums criptográficos SHA-256 del paquete
└── Hub\                          # Aplicación de escritorio NeuralFX Hub
    ├── NeuralFX.Hub.exe          # Ejecutable principal del Hub
    ├── NeuralFX.Hub.dll
    ├── NeuralFX.Hub.runtimeconfig.json
    ├── NeuralFX.Hub.deps.json
    └── System.Management.dll
```

---

## Uso Rápido

1. **Lanzar el Hub:**
   - Haz doble clic en `Iniciar-NeuralFX-Hub.bat` (o ejecuta `Hub\NeuralFX.Hub.exe`).
   - Verifica que el diagnóstico marque en verde tu GPU, VRAM y versión de driver.
2. **Importar binarios de NVIDIA:**
   - Si no tienes los archivos propietarios en tu carpeta de Descargas, usa el botón **Importar DLL...** para añadir `nvngx_dlss.dll` o `nvngx_dlssnr.dll`.
3. **Instalar el Pipeline:**
   - Pulsa **Instalar / Inyectar Pipeline DLSS 5**. El instalador generará la configuración y el manifiesto.
4. **En el Juego:**
   - Inicia Cities: Skylines y activa **NeuralFX** en el Gestor de Contenido.
   - Pulsa `Ctrl + Alt + N` para ver el HUD de telemetría y comprobar que los vectores de movimiento y hooks están activos.
   - Pulsa `Home` para configurar el overlay de ReShade o ajustar la nitidez del filtro AMD CAS.
5. **Rollback 100% Limpio:**
   - Si deseas revertir a vanilla, abre el Hub y pulsa **Desinstalar y Rollback Limpio (Zero-Trace)**.

---

## Compilación desde Código Fuente

Requiere .NET SDK 8 / 10 instalado:

```bash
# Restaurar y compilar la solución completa en modo Release
dotnet build NeuralFX.slnx -c Release

# Ejecutar las pruebas automatizadas
dotnet test NeuralFX.Tests\NeuralFX.Tests.csproj

# Publicar el ejecutable del Hub
dotnet publish NeuralFX.Hub\NeuralFX.Hub.csproj -c Release -r win-x64 --self-contained false -o Publish\
```

---

## Licencia

[MIT No Attribution (MIT-0)](LICENSE) © 2026 **juanamores98**.  
Uso, copia, modificación, distribución y sublicencia libre sin restricciones.

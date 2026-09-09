# Arquitectura y decisiones

## Contrato vigente: bridge build 4 / ABI 3 / IPC 4

Consulta [la consolidación](CONSOLIDACION-2026-09.md) para los layouts y limitaciones. Un token de cámara enlaza la copia GPU de MV experimental y el consumo único del feeder. La ruta óptica conserva su textura, SRV y escalas. Los resets se confirman tras una query posterior al blit; esa señal no confirma NR ni Present.

No se habilita jitter: falta preparación completa y contingencia. `AfterEverything` es la fase de captura experimental, no una garantía pre-UI. El Hub no recibe punteros GPU. El esquema siguiente describe la infraestructura conservada; NR permanece sin confirmar en el estado efectivo.

## Flujo conservado

```text
CS1 → frame final D3D11 → flujo óptico LumeniteFX
    → DLSS5_Feed.fx: color, profundidad, movimiento, máscara
    → feeder: recursos compartidos D3D11 ↔ D3D12
    → NGX DLAA → RenoDX NR → copia al backbuffer → NeuralFX_CAS

OnPreCull → metadatos CPU
CameraEvent.AfterEverything → evento de render → reset para NGX
Mod ↔ MMF de 4 KB ↔ Hub
```

El puente conserva el reset solicitado hasta aceptar una evaluación y enviar su lista de comandos sin fallo de cierre/señalización. Su estado no certifica la calidad visual ni la finalización de todos los trabajos posteriores de GPU.

## Contratos

- Feeder fijado a `927d76d30e888bce497f5c5f8d496fcb696da335`, con parches reproducibles en `Native/build.ps1`.
- ABI nativa 1: frame de 32 bytes, estado de 40 bytes, capability 1 = reset sincronizado. Se verifican tamaños y versión. El evento toma únicamente el frame que coincide con su slot.
- MMF v3: telemetría desde offset 8, comandos desde 520; cada región tiene un escritor y un seqlock. La sesión invalida comandos de escenas anteriores. No transporta recursos GPU.
- Mod a 10 Hz; lectura del Hub a 30 Hz, actualización de texto cuando cambia el frame. Datos con más de dos segundos se rechazan.
- Cámara descubierta una vez por segundo, módulos cada tres y escáner AA cada cinco. Sin consultas de archivos de pipeline en el bucle de frames del mod.
- Instalador: hashes, rutas confinadas, mutex, snapshots y diario durables antes de mutar. Recupera también tras terminación abrupta del escritor.

## Correcciones a las auditorías

`JitterCancellation` y `PerformanceProfile` no son claves del feeder fijado. Inyectar Halton sin acordar movimiento, signo, escala, profundidad y evaluación puede empeorar la imagen. El puente rechaza jitter no nulo.

`ScalableBufferManager` no existe en esta versión de Unity. `work_resolution` reduce el trabajo del addon, no la resolución de la cámara. Ajustarlo por FPS no elimina la carga de simulación de CS1.

El setter nativo de `Camera.depthTextureMode` no tiene cuerpo IL para un parche Harmony convencional. Los flags se reaplican en Update/OnPreCull cuando la preferencia lo solicita. El escáner AA informa coincidencias por nombre.

Colossal UI reduce trabajo del mod, pero el HUD sigue en el frame final procesado por ReShade. NR y Ray Reconstruction son productos distintos aunque se renombren sus DLL.

## Siguiente fase de render

La API fijada de ReShade ofrece `effect_runtime::render_effects`: ejecuta efectos antes de Present y evita repetirlos en ese Present. Permite estudiar aislamiento de UI con un punto de entrada explícito.

Antes de activarlo en CS1 hay que comprobar el render target al terminar la cámara 3D, el orden de cámaras/UI, el estado D3D11 que debe restaurarse y los cambios de resolución/escena. El evento actual sincroniza resets; todavía no invoca esa ruta. No hay controles que simulen la capacidad pendiente.

MV nativos y jitter requieren obtener texturas vivas de Unity en render, verificar cobertura, signo/unidades y exclusión de jitter, y preservar la proyección de culling y otros mods. Su aceptación necesita comparaciones de movimiento y HUD, además de éxito NGX.

## Referencias

- [Feeder fijado](https://github.com/jlrouzies-fr/DLSS5-Feeder/tree/927d76d30e888bce497f5c5f8d496fcb696da335)
- [API ReShade 6.8.0](https://github.com/crosire/reshade/blob/v6.8.0/include/reshade_api.hpp)
- [FidelityFX CAS](https://github.com/GPUOpen-Effects/FidelityFX-CAS)
- [LumeniteFX fijado](https://github.com/umar-afzaal/LumeniteFX/tree/f8cbbb4eccfcb7adf0d74bb358ba349272e3c1e9)

Los ensamblados de Unity/CS1 y headers locales del feeder fueron la referencia para los contratos implementados. Un nombre de GPU o presencia de DLL no prueba inferencia.

# Consolidación NeuralFX, septiembre de 2026

Esta entrega implementa la base de seguridad, captura experimental y control del superplan. **No cierra el superplan completo ni certifica DLSS 5 en una ciudad.** Separación pre-UI, confirmación NR por frame, SR interno y comparación NR del mismo frame aún necesitan implementación e integración. No basta con pasar las pruebas de esta entrega para anunciarlas.

## Qué cambia

- ABI 3 de 64 bytes, resultados de 80 bytes y bridge build 4. El decodificador real acepta layouts antiguos exactos sin leer 48 bytes de un cliente de 32. Rechaza números no finitos, pares cruzados, flags desconocidos, replay y epochs anteriores. Los punteros libres de V2 se descartan; no se desreferencian.
- Negociación explícita de exports/capacidades. Un mod nuevo rechaza un bridge antiguo. Actualiza mod, Hub y addon juntos; una DLL copiada sobre el disco no actualiza la cargada.
- Jitter desarmado y rechazado por el consumidor. No cambia proyección, nonJitteredProjectionMatrix ni calidad global cuando no hay ruta preparada. `ProjectionLease` restaura matrices personalizadas y respeta escrituras posteriores, para la futura ruta temporal.
- Configuración schema 3: migra preferencias antiguas sin activar experimentos; conserva preferencias importadas y muestra errores de guardado. No intenta adivinar un LOD vanilla. Un archivo de schema posterior se protege de escritura.
- Captura MV experimental: tres RenderTextures RGHalf propios, punteros adquiridos al crear recursos, copia GPU mediante command buffer al final de la cámara, identificadores registrados, cámara/epoch y tokens únicos. El addon retiene COM references y espera queries antes de reutilizar. Si se agotan los slots, usa el descriptor óptico completo. Recursos rechazados/no consumidos se retiran al finalizar efectos en su dispositivo.
- `NeuralFxSelectedMotion` reúne textura, SRV, escala y proveedor. Tanto copia 100% como muestreo reducido usan la selección. La escala de Unity nunca se aplica a un recurso óptico rechazado. Esta corrección no demuestra todavía signo ni cobertura de los shaders de CS1.
- Resultado NGX grabado/enviado separado del final de una query D3D11 posterior al blit. Solo esa finalización confirma el reset. No hay señal NR por frame en el consumidor actual; `NrConfirmed` permanece apagado. Una query tras el blit tampoco certifica que ninguna etapa posterior sobrescriba el target.
- Off detiene el feeder propio y CAS. El addon requiere heartbeat del mod y metadata consumible; no se alimenta en el menú ni en presents auxiliares sin un nuevo token. Lumenite permanece disponible porque puede tener otros consumidores.
- Controles de sesión: trabajo NR 100/85/66 y nitidez apagada/0,15/0,30 desde Hub o juego. Son comandos escalares al hilo de render, sin tocar DLL ni reinstalar. Se informa pendiente/aplicado/rechazado. Si CAS está compilado sin uniform editable (p. ej. Performance Mode), puede rechazar la nitidez; debe reactivarse la edición del shader en ReShade. No se cambia la política global de compilación ajena.
- Un estado común diferencia módulos cargados, portador operativo, salida GPU y NR. IPC 4 añade sesión, caducidad, revisiones y resultado de comando. Los recursos GPU nunca cruzan al Hub.
- El diagnóstico del Hub no copia el mod a Addons/Mods. La actualización conjunta utiliza el instalador de paquete con backup. Restauración y desinstalación vuelven a ser acciones separadas y revisables; ninguna borra recursivamente la carpeta del mod ni promete vanilla.
- Hub con Inicio, Instalación, Diagnóstico, log desplegable, tamaños mínimos reducidos y acciones principales apiladas. Vista previa de archivos/claves antes de instalar y rechazo si los archivos cambiaron después de revisarla.
- Preset anclado: conserva técnicas ajenas, CAS ajeno y su orden; rechaza claves INI ambiguas y el cambio silencioso de un preset activo distinto. No presenta trabajo NR como DLSS SR del motor.
- Authenticode obligatorio para SR y NR, además de hash y producto. No selecciona binarios modificados según regex de GPU. Los runtimes NVIDIA requieren importación autorizada; sus URL comunitarias quedan como procedencia histórica, no certificación oficial de distribución. Importar DLL valida hash de archivo, nunca el del ZIP.
- LUID tomado del dispositivo de render; registro del Hub marcado como inventario preliminar. RTX desconocida ya no se clasifica como Blackwell.

## Estado de los cortes del plan

| Corte | Estado de esta entrega | Puerta pendiente |
|---|---|---|
| 00 Baseline | Snapshot de fuentes y manifiesto generado por build | Hashes de tu instalación y sesión |
| 01 Seguridad/cámara | Implementado; pruebas de lógica | Probar Off/cargas con DLL reales |
| 02 ABI | Implementado; ASan/UBSan y pruebas Windows | Combinación instalada real |
| 03 MV | Descriptor y fallback implementados; fixture WARP | Sonda de la entrada NGX real en CS1 |
| 04 Captura | MV propio experimental | Color/depth/MV pre-UI de la misma cámara; cobertura de objetos |
| 05 Temporal | Restauración y resets implementados; jitter bloqueado | Prepare por frame, convenciones y contingencia espacial |
| 06 Composición | Traza de cámaras y exportación implementadas | Hook pre-UI, estado D3D11 completo, prueba de identidad y HUD |
| 07 Confirmación | Portador y finalización GPU separados | API/instrumentación verificable del consumidor NR |
| 08 SR interno | No implementado | Depende de 04–07 y consulta SDK de modo/resoluciones |
| 09 Confianza | Implementado | Importaciones con archivos autorizados en tu Windows |
| 10 Estado/IPC | Implementado | Sesión real y cambios durante pérdida de dispositivo |
| 11 UI juego | Controles y opciones actualizados | Compilación con DLL reales; foco/escala/UUI en ciudad |
| 12 Hub | Implementado y compilado en CI | Navegación WPF a 720p, 150/200% y múltiples monitores |
| 13 Alternativas | Spike documental verificable, abajo | Obtener backend/API autorizados y ejecutar benchmark GPU |
| 14 Aceptación | Herramientas y escenarios preparados | Ejecución y aceptación del usuario en su PC |

## Resultado de los spikes de backend

El árbol público de `NVIDIA-RTX/Streamline` consultado en esta sesión tiene SHA `2122257e0fce486f91b385aa63b9a09b0a34b363`. Contiene headers/guías de SR, FG y RR (`sl_dlss.h`, `sl_dlss_g.h`, `sl_dlss_d.h`); no se encontró un header/contrato NR utilizable en ese inventario. Eso no prueba que no exista un SDK NR por otro canal. Se activa el criterio de abandono del spike del superplan: no inventar feature IDs ni usar la API RR como NR. Referencia: https://github.com/NVIDIA-RTX/Streamline/tree/2122257e0fce486f91b385aa63b9a09b0a34b363/include

El README del feeder fijado en `927d76d30e888bce497f5c5f8d496fcb696da335` dice que `renodx-dlss` de ShortFuse se distribuye por Discord y sustituye al feeder. No se encontró un repositorio GitHub en la ruta probada; no se descargó ni sustituyó el backend por un binario de origen desconocido. El consumidor actual `renodx-dlss5` es un componente diferente. Referencia: https://github.com/jlrouzies-fr/DLSS5-Feeder/blob/927d76d30e888bce497f5c5f8d496fcb696da335/README.md

El header ReShade incluido en ese upstream expone `render_effects` para adelantar efectos, pero también advierte que modifica estado de la command list. Llamarlo sin identificar target/cámara de CS1 ni preservar todo el estado no cumple la puerta pre-UI. La entrega registra cámaras/eventos para construir ese adaptador con evidencia. No adelanta todos los efectos ajenos.

## Compilar en tu PC

En PowerShell desde el repositorio actualizado, con Visual Studio C++ x64, Windows SDK y .NET SDK:

```powershell
./tools/Build-NeuralFX.ps1 -ManagedDLLPath 'C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed' -Package
```

Ajusta la ruta si Steam está en otro disco. Genera addon, mod, Hub, pruebas y paquete; **no instala en el juego**. Luego, con juego y Hub cerrados, ejecuta `Install-NeuralFX.ps1` del paquete generado. El Hub vive fuera de Mods. No combines el mod nuevo con el addon `.3`.

`Native/build.ps1 -SmokeHarness` también genera el host gráfico. `Native/smoke.ps1 -GameRoot <carpeta-del-juego> -SrOnly` copia componentes a un fixture aislado y excluye físicamente el consumidor NR. Sin `-SrOnly` conserva el consumidor y registra la señal histórica del log sin convertirla en confirmación por frame. El host usa ABI 3 y guarda el tuple. No altera la carpeta del juego.

## Primera prueba de ciudad

1. Conserva un backup de tu instalación y sus DLL antes de instalar. Mantén los otros cuatro FX sin cambios.
2. Empieza con trabajo NR 100%, flujo óptico, sin jitter ni experimento Unity. Comprueba que Off conserva la imagen actual y no deja CAS propio. Verifica que el menú no acumula evaluaciones.
3. Carga una ciudad fija. Prueba 100 → 85 → 66 → 100, apagar/encender y cambio de nitidez; compara dimensiones de cámara/trabajo/salida y confirmación de comando. La cámara debe conservar su resolución.
4. Solicita reset: el serial completado debe avanzar después de la finalización GPU. Un fallo o un comando rechazado no debe marcarlo completo.
5. Solo después activa captura MV experimental. Prueba edificio quieto, paneo, tráfico, trenes, ciudadanos, vegetación, agua y transparencias. Ante artefactos, vuelve a óptico. No actives jitter mediante XML: el bridge lo rechazará.
6. Activa la traza de cámaras en opciones, abre ventanas y modales, y exporta estado/traza. Esa evidencia permite decidir la fase pre-UI; esta build todavía procesa el HUD final.
7. Repite ciudad A → menú → ciudad B, Alt-Tab, resize y reinicio. No uses FG durante esta comparación inicial.
8. Recoge la evidencia:

```powershell
./tools/Collect-NeuralFXEvidence.ps1 -GameRoot 'D:\Steam\steamapps\common\Cities_Skylines' -RunName 'city-optical-100'
```

El script recoge hashes y colas de logs conocidos; no copia partidas ni capturas. Revisa los logs antes de compartirlos porque terceros pueden escribir rutas en ellos. Añade el diagnóstico exportado desde opciones y vídeos de los escenarios.

## Qué demostraron las pruebas automatizadas

- ASan/UBSan ejecutan **el decoder y almacenamiento del bridge real**, no solo una reproducción de sus expresiones.
- Pruebas Hub/IPC ejercitan caché/importación, firma mediante resultado de política, manipulación de INI, rollback, archivos cambiados después del preview, seqlock y procesos independientes. La prueba de política de firma no sustituye a verificar DLL NVIDIA reales.
- Todo el código administrado del mod se compila con dobles Unity/Colossal/ICities en C# 7.3; se ejercitan layouts, migración, mil frames sin consumidor, restauración personalizada de matrices/flags y conflicto de escritura, epochs, permiso de jitter, construcción del panel/opciones y foco. No certifica APIs del ensamblado propietario de CS1.
- El fixture WARP ejerce el descriptor D3D11 real con patrones distintos, copia y muestreo de SRV a 100/85/66 y extents impares. **No ejecuta NGX, la función completa de resample del feeder ni el juego.** La sonda final entregada a NGX sigue pendiente.
- CI compila el addon completo contra upstream/SDK fijados y el Hub WPF. No ejecuta NVIDIA NR, no navega WPF ni renderiza el mod en el juego.

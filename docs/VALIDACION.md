# Evidencia actual e histórica

La consolidación de septiembre introduce bridge build 4 / ABI 3 / IPC 4. Sus resultados y límites se registran en [CONSOLIDACION-2026-09.md](CONSOLIDACION-2026-09.md). No se ejecutó CS1 ni una GPU NVIDIA en esta entrega.

**Todo lo que sigue es el registro histórico del repositorio. No corresponde a los binarios nuevos ni confirma sus entradas temporales.** Se conserva para comparar la combinación de control.

## Evidencia de validación — 8 de septiembre de 2026

Equipo local: Windows x64, RTX 5080, controlador 616.86. CS1 informa 1.21.1-f9. Los resultados corresponden a estas pruebas y este equipo.

## Automatización

La suite administrada contiene 64 casos aprobados: instalación/rollback/desinstalación, fallos tras escrituras y eliminaciones, rutas ambiguas, caché/manifiesto y migración, identidad de runtimes, configuración, bibliotecas Steam, logs UTF-8 incrementales, seqlock y comandos.

Los nueve casos del flujo del Hub comprueban la diferencia entre preparar e instalar, archivos sin registro, cambios y ausencias tras instalar, los cuatro archivos de configuración, vista previa frente a rollback real, migración antigua, backups dañados, importar otra copia y restaurar sin caché. La comprobación visual del Hub 2.0.1 usa renders de sus controles WPF a tamaño normal y mínimo (incluidos avisos de éxito/error y la revisión de restauración). Los estados de prueba se simulan; el estado actual de disco se lee por separado. La captura de Windows falló dos veces con «foreground window did not report a process id»; no se completó una prueba manual de navegación, teclado o lector de pantalla. No se abrió CS1 para esta entrega del Hub.

Tres casos usan procesos reales: uno termina al instalador después de modificar dos archivos; otro termina al desinstalador tras su primera eliminación, recupera y completa un segundo intento; el tercero verifica coherencia y confirmación de comandos por MMF. Durante la prueba de terminación se reprodujo un mutex todavía ocupado durante menos de 1 ms después de señalarse la salida del proceso. La adquisición ahora permite una espera máxima de 250 ms; sigue rechazando un escritor activo, comprobado en la suite. Las pruebas nativas previas comprueban ABI, evento de render, dimensiones, persistencia del reset, edad y rechazo de jitter.

## Desinstalación del Hub 2.0.2

Los 14 casos nuevos de desinstalación cubren el catálogo completo antes/después de uso simulado, reinstalación, residuos privados, componentes antiguos, manifiestos ausentes/dañados/no fiables, archivos ajenos, juego abierto, vista previa desactualizada, archivo bloqueado, fallo inyectado tras cada eliminación, repetición sin cambios y redirecciones de carpetas o archivos de recuperación. Se comparan hashes y directorios completos con el estado inicial; se verifican todas las copias archivadas fuera del juego.

Prueba adicional con la caché real: `artifacts/uninstall-review/report.json`. Instala los **19 archivos reales**, reinstala otro preset, añade residuos de sesión simulados y desinstala. Resultado: **27 archivos y 10 carpetas retirados**, archivos y directorios originales exactamente conservados, todos los SHA-256 del archivo externo verificados y segundo intento sin cambios. No se ejecutó el juego ni el pipeline gráfico en esta prueba.

Después de la limpieza manual comunicada por el usuario, se leyó `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines`: **0 archivos y 0 carpetas pendientes** del inventario de desinstalación. Esta entrega no reinstala el pipeline real.

La nueva confirmación y el estado limpio se revisaron con renders de los controles WPF a tamaño normal y mínimo (`artifacts/ui-review/07` a `09`). La confirmación usa el plan de la carpeta aislada; la pantalla limpia usa el estado real leído del disco. No son capturas de navegación manual.

ReShade 6.8.0 exige que exista `IntermediateCachePath`; de lo contrario usa su caché temporal compartida. El instalador crea `.neuralfx-runtime/ReShade` dentro de la transacción y registra los directorios nuevos. También configura las capturas bajo `.neuralfx-runtime/Capturas`. Se verificó este comportamiento en el [código oficial de ReShade 6.8.0](https://raw.githubusercontent.com/crosire/reshade/v6.8.0/source/runtime.cpp), en la carga de configuración y guardado de capturas. La limpieza no borra cachés globales del controlador ni archivos que el usuario haya trasladado a rutas externas.

## Pipeline aislado

`Native/smoke.ps1` copia los componentes a un fixture privado, crea una superficie D3D11 y carga el pipeline. No abre CS1 ni usa partidas.

| Combinación | Resultado observado |
|---|---|
| RenoDX 4.70 sin NR, 1280×720 | 1.749 presents; DLAA y transporte sin pérdida de dispositivo |
| RenoDX 4.70 con NR, 1280×720 | Excepción en creación/evaluación, seguida de fallo del consumidor |
| RenoDX 4.55 con NR, 1920×1080 | Excepción en la ruta NR |
| RenoDX 3.3.4 con NR, 1920×1080 | 1.741 presents; evaluación NR registrada y salida normal |
| RenoDX 3.3.4, puente neuralfx.2, 3840×2160 | 1.305 presents; 1.175 evaluaciones; reset 8 confirmado |
| RenoDX 3.3.4, puente neuralfx.3, 3840×2160 | 1.490 presents; 1.360 evaluaciones; reset 8; resultado 1; dispositivo sin error |

Último fixture: `artifacts/graphics-nr-20260908-105746`. El feeder reportó unos 17 ms de GPU por frame a 4K. Los FPS incluyen la cadencia del fixture; no representan el rendimiento de una ciudad.

## Arranques de CS1

Se corrigió `NoReloadOnInit=1`, heredado del Hub antiguo. Se confirmó la compilación de `DLSS5_Feed.fx`, `lumenite_Kernel.fx` y `NeuralFX_CAS.fx`. Tras sacar el Hub de Mods, CS1 cargó solamente `NeuralFX.dll` dentro de esa carpeta.

Con RenoDX 4.70, NR registró éxito pero `ID3D12GraphicsCommandList::Close` falló con `0x80070057`. El feeder upstream aún registraba un frame entregado tras ese fallo. La extensión ahora omite copia y confirmación de reset, comprueba la señalización de la cola y desactiva la ruta fallida.

Con 3.3.4, el arranque posterior entregó más de 17.000 frames en el menú. No se completó una validación dentro de una ciudad. Profundidad, movimiento, calidad visual, panel y recarga de escenas quedan para la prueba manual.

## Runtimes importados

Los archivos aportados son versión 310.8.0 y tienen firma NVIDIA válida.

| Archivo | SHA-256 |
|---|---|
| nvngx_dlss.dll | c85f971ce023c9f3492fc7455f0b01a24ba18ea39636407a846902c4360b0b7e |
| nvngx_dlssnr.dll | e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e |

La instalación antigua tenía Ray Reconstruction renombrado como NR. El importador nuevo comprueba el producto firmado y rechaza esa sustitución.

## Pendientes

No se ha demostrado ausencia de ghosting, depth correcto de una ciudad, compatibilidad con todos los mods ni UI intacta. No se implementaron jitter de cámara, consumo de MV nativos ni DRS de Unity. La consulta de publicaciones no instala versiones nuevas sin un catálogo compatible y verificado.

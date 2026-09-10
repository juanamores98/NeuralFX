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

## Medida en ciudad — 10 de septiembre de 2026

Cuatro sesiones reales en la ciudad del usuario, con la telemetría de sesión y la traza de cámaras encendidas. Equipo: RTX 5080, controlador 616.92, CS1 a 3840×2160 en pantalla completa, antialiasing del juego desactivado. Registros en `%LOCALAPPDATA%\NeuralFX\Diagnostics`.

### La cámara no ocupa el backbuffer, y es su rect

```text
Main Camera id=65224 orden=-2 3840x1933 rect=0,0.105 1x0.895 destino=pantalla 3840x2160
```

Misma cámara, dibujando directa a pantalla: no es otra cámara ni una textura intermedia. El rect `(0, 0.105, 1, 0.895)` deja la escena en las 1933 filas de arriba y la franja inferior —227 píxeles, donde va la barra de herramientas, que es opaca— sin escena. `Underground View` comparte el mismo rect, así que es sistémico. NeuralFX no lo escribe: no hay una sola asignación a `Camera.rect` en el mod.

Es constante dentro de la ciudad. Los `3840x2160` de los registros son la salida al menú principal y transitorios de segundos, no bloques de juego: en la sesión de las 15:16, 30 de 266 muestras, y caen entre 15:36 y 15:38, al cerrar. **Una lectura anterior que los describía como alternancia dentro de la partida era incorrecta.**

El frame anunciado sigue llevando el tamaño de pantalla, que es lo que ReShade ve en Present. Lo que cambia es que las texturas registradas ya no se reservan al tamaño de cámara.

### Los vectores de movimiento de Unity nunca han entrado

En **todas** las entradas de **todos** los registros, `flags` no tiene el bit 256 (`NativeMotion`) y `movimiento=1`: el pase temporal siempre ha corrido con flujo óptico estimado de la imagen, no con vectores reales. Con desplazamientos medidos de 5 a 201 px, eso basta para explicar el arrastre al mover la cámara.

Dos obstáculos, en este orden:

1. El selector nativo exige que la textura registrada mida lo mismo que el frame anunciado (`Native/neuralfx_inputs.h`). Se reservaba a tamaño de cámara: 3840×1933 contra 3840×2160. **Corregido** — se reserva al tamaño del frame y los vectores se colocan por copia de región en el hueco que la cámara ocupa. Un blit los habría estirado un 11,7 % en vertical.
2. **Abierto.** `NeuralFX_RegisterMotion` rechaza la primera textura, con puntero nativo válido y `antiAliasing=1`. La reserva nunca llega al selector, así que el punto 1 era el segundo obstáculo y no el primero. Ese registro devuelve 0 por siete motivos distintos —formato, muestras, mips, array, bind flags, creación de la vista, agotamiento de huecos— sin decir cuál. **Siguiente paso: instrumentar esos siete retornos y reconstruir el addon**; desde el lado administrado no se puede acotar más.

Mientras tanto la ruta se queda en el descriptor óptico, que es el comportamiento de siempre.

### La profundidad se queda plana casi la mitad del tiempo

En la sesión de las 17:12, de 29 sondas distintas **13 dan `profundidad 0..0 var 0` con `finito 100%`**: el búfer llega entero a cero, no con basura. Las seis primeras (frames 600 a 3600) son seguidas, y después alterna en bloques. Once de las trece coinciden con movimiento de cámara de 5 a 43 px y quedan marcadas `PLANA-EN-MOVIMIENTO`.

Sin profundidad el pase neural no tiene con qué resolver las desoclusiones, que es justo lo que se ve mal al mover. La configuración de `generic_depth` ya la fija el instalador (`FilterFormat=5`, `DepthCopyBeforeClears=1`, `UseAspectRatioHeuristics=0`, `DrawStatsHeuristic=2`) y aun así la elección se mueve. Queda abierto, y con el rect ya conocido hay una hipótesis que antes no se podía formular: los candidatos de 3840×2160 conviven con una cámara que solo escribe 1933 filas, y `Underground View` aporta otro destino del mismo tamaño.

### Lo que sí está sano

`backend=0x00000000` en las 91 entradas. 18 resets solicitados y 18 completados. Media de 39,1 fps con máximo de 50,6, y el coste del feeder entre 15 y 17 ms de GPU. El usuario informa que el arrastre al mover la cámara ya no se aprecia con facilidad.

## Pendientes

No se ha demostrado ausencia de ghosting, compatibilidad con todos los mods ni UI intacta. La profundidad de una ciudad **sí se midió, y no es correcta de forma sostenida**: ver la sección del 10 de septiembre. No se implementaron jitter de cámara ni DRS de Unity; el consumo de MV nativos está escrito, pero el puente rechaza la textura y la ruta no llega a ejercitarse. La consulta de publicaciones no instala versiones nuevas sin un catálogo compatible y verificado.

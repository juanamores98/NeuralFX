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

`backend=0x00000000` en las 91 entradas. 18 resets solicitados y 18 completados. Media de 39,1 fps con máximo de 50,6, y el coste del feeder entre 15 y 17 ms de GPU. El usuario informa que el arrastre al mover la cámara ya no se aprecia con facilidad, pero **esa mejora no es atribuible a ningún cambio de esta entrega**: el pipeline corrió idéntico en las cuatro sesiones (`movimiento=1` en todas), y la sesión que se percibió mejor es precisamente la de peor profundidad. Se anota como percepción, no como resultado.

### Cierre del desarrollo

Se intentó la comprobación manual del selector de profundidad desde el menú de ReShade —abrir el overlay en ciudad, mirar la lista de candidatos de `generic_depth` y forzar uno— y **no se pudo completar**. La hipótesis de la sección anterior queda por tanto **sin probar**: no está confirmada ni descartada, y nadie debe darla por cerrada.

Lo verificado sobre la instalación, que sigue siendo válido como punto de partida:

- ReShade 6.8 lleva `generic_depth` incorporado en `dxgi.dll`; no hay `generic_depth.addon64` suelto en la carpeta del juego. Los add-ons instalados son `dlss5-feed.addon64` y `renodx-dlss5.addon64`.
- Las seis claves que ese runtime lee de `[DEPTH]` son `DepthCopyAtClearIndex`, `DepthCopyBeforeClears`, `DisableINTZ`, `DrawStatsHeuristic`, `FilterFormat` y `UseAspectRatioHeuristics`. El instalador escribe cuatro; **`DepthCopyAtClearIndex` y `DisableINTZ` no se fijan**, de modo que la elección del búfer queda a la heurística de estadísticas de dibujo en cada fotograma.
- No existe clave que persista un búfer forzado: la selección del overlay es por sesión, porque el identificador del recurso no es estable entre arranques. Cualquier arreglo tiene que pasar por esas seis claves.

Para el registro de vectores de movimiento, la vía propuesta y no ejecutada es una sola recompilación nativa que haga dos cosas: pedir el tipo del puntero con `QueryInterface` en vez del `static_cast<ID3D11Texture2D*>` ciego de `Native/neuralfx_inputs.h` —Unity puede entregar ahí la vista y no el recurso, lo que explicaría un rechazo constante sin caída— y contar por separado los ocho motivos de rechazo. Ninguna de las dos se llegó a escribir.

## Informe de registro nativo — 11 de septiembre de 2026

`NeuralFX_RegisterMotion` devolvía 0 por once condiciones distintas sin decir cuál, y el descriptor real de la textura solo lo puede leer C++. Tres sesiones de ciudad terminaron sin identificar la causa. Ahora el registro publica **etapa, motivo único, HRESULT y el descriptor que llegó a leer**, y el mod lo transcribe al registro de sesión durante una partida normal, sin ningún paso del usuario.

`Native/neuralfx_registration_report.h` define el informe con ancho fijo (144 B, `static_assert` en C++ y prueba de forma en C#). Motivos: puntero nulo, identidad inválida, sin `ID3D11Texture2D`, formato, multisample, mips, array, sin permiso de lectura, fallo de la vista, huecos agotados y extensión. **Ninguno comparte valor con otro**: agrupar causas en un solo cero fue exactamente el defecto.

El `static_cast<ID3D11Texture2D*>` pasa a `QueryInterface`. La documentación de Unity 5.6 dice que `GetNativeTexturePtr` entrega un `ID3D11Resource` en D3D11; el cast ciego y el `GetDesc` posterior eran comportamiento indefinido si el objeto fuera otro, y además dejaban el motivo indistinguible. Si lo que llega es una vista, se pide su recurso: **es un contrato adicional declarado aquí, no una afirmación sobre lo que Unity entrega**, y así consta en las pruebas.

### Correcciones de mensajes que afirmaban causas no demostradas

| Antes | Ahora |
|---|---|
| «Guías estiradas 10,5 %» por restar dos alturas | «Viewport de escena 3840×1933 dentro de una salida 3840×2160. Correspondencia de guías: sin verificar.» |
| «Profundidad PLANA: ReShade está en el buffer equivocado» | «Profundidad plana en la última muestra, con la escena en movimiento. Origen sin identificar.» |

Un viewport parcial no demuestra que la imagen se estire: haría falta observar el recurso de origen y la operación de copia o muestreo. Una muestra plana no distingue entre búfer equivocado, etapa de captura, copia incompleta, región leída o conversión.

### Verificación ejecutada

| Nivel | Resultado |
|---|---|
| `Native/build.ps1` — contrato del puente | Correcto |
| `Native/build.ps1` — fixture D3D11 WARP | Registro nominal, once rechazos con descriptor real, recurso desde vista y huecos agotados |
| `Native/smoke.ps1` en carpeta sintética 3840×2160 | 1.714 presents, resultado `00000000`, 1.548 evaluaciones, sin error de dispositivo |
| `dotnet test` | 79 casos, sin regresiones |
| `tools/package.ps1 -Deploy` | Instalado con copia de seguridad; sin tocar partidas |

El fixture WARP acredita **registro y transporte**, no NGX, ni Unity, ni la captura de CS1. La profundidad plana y los vectores en cero del fixture sintético son su escena vacía, no un defecto.

Versión del feeder sincronizada a `0.15.1-neuralfx.7` en `Native/build.ps1` y en el catálogo del Hub, que se habían separado.

### La medida: formato 33

La primera partida con el addon instrumentado dejó escrito el motivo en la primera entrada:

```text
movimiento=1 (Unity: reserva rechazada: el puente rechazó la textura 0: formato 33 ...)
```

**33 es `DXGI_FORMAT_R16G16_TYPELESS`** (verificado contra `dxgiformat.h` del SDK 10.0.26100.0, no de memoria). Unity crea la `RenderTexture` `RGHalf` como recurso **tipeless** y pone encima las vistas tipadas. El puente exigía exactamente `R16G16_FLOAT`, que es **34**, y rechazaba el recurso correcto. Eso, y no otra cosa, es por lo que la ruta de vectores de Unity nunca llegó a entrar en ninguna sesión.

Dos cambios, y hacían falta los dos:

1. El formato se acepta **por familia**: `R16G16_FLOAT` o `R16G16_TYPELESS`. Nunca por bytes por píxel — `R16G16_UNORM` y `R16G16_SINT` miden lo mismo y significan otra cosa; ambos siguen rechazados, y hay prueba de cada uno.
2. La vista se pide **siempre con descriptor explícito** `R16G16_FLOAT`. Sobre un recurso tipeless un `nullptr` falla, porque no hay interpretación que deducir; sobre uno tipado deja el contrato escrito en vez de heredado.

El mensaje del mod decía «se esperaba R16G16_FLOAT (10)». **10 es `R16G16B16A16_FLOAT`**: la constante estaba mal escrita en el texto, no en la comprobación. Corregido.

Feeder a `0.15.1-neuralfx.8`, en `Native/build.ps1` y en el catálogo a la vez.

### El registro entra; ahora se atasca un paso después

Con la familia tipeless aceptada, el registro deja de fallar. La sesión siguiente dice:

```text
movimiento=1 (Unity: sin turno libre)
```

`Record` reserva un turno por fotograma de entre tres, y el turno se devuelve cuando la GPU termina su trabajo. Si las devoluciones no ocurren, los tres se agotan y la ruta vuelve al descriptor óptico — otra vez en silencio. Y el mensaje mezclaba **dos** causas distintas: que la región no quepa en el destino, y que no quede ningún turno.

Mismo remedio que con el registro, antes de proponer un arreglo:

- Los dos motivos se separan en el mod. El primero dice ahora qué región no cabía y dónde.
- El informe nativo sube a **versión 2** (160 B) con estado **vivo** de turnos: en vuelo ahora, concedidos, negados y devueltos. Se leen al consultar, no al registrar, porque lo que hay que saber es si las devoluciones avanzan **ahora**.

Con eso, la siguiente sesión distingue sin ambigüedad entre «se conceden y nunca vuelven» —fuga en la vida útil— y «se conceden y vuelven, pero tres turnos no bastan para la latencia de la GPU» —profundidad de la reserva—. Son arreglos distintos y el segundo **no** se resuelve agrandando la tabla sin más.

Feeder a `0.15.1-neuralfx.9`.

### La copia se emite y los turnos vuelven; falla el selector

Con los dos motivos separados y el estado vivo de turnos, la sesión siguiente contesta las dos preguntas a la vez:

```text
movimiento=1 (Unity: enviado 1920x967 en 0,113)                        56 muestras
movimiento=1 (Unity: sin enviar: turnos 3 en vuelo de 3 registrados
              · concedidos 1549 · negados 1639 · devueltos 1546)        1 muestra
```

- **No hay fuga de vida útil.** 1.546 devoluciones de 1.549 concesiones. La hipótesis de que los turnos no volvían queda descartada.
- **La copia se emite.** El mod entrega el handle en 56 de 58 muestras.
- **Y aun así `movimiento=1`.** El selector del addon recibe el handle y lo descarta.

`NeuralFxSelectedMotion::Select` comprobaba cinco condiciones **dentro del filtro del bucle** —handle, reserva, cámara, época— y otras tres dentro. Si algo no encajaba, el bucle no entraba y no quedaba constancia de cuál. Es el mismo defecto que ya costó tres sesiones en el registro y una en la reserva, en un tercer sitio.

Ahora busca por handle y comprueba cada condición por separado, con un motivo propio: sin handle, handle desconocido, hueco sin reservar, otra cámara, otra época, otro dispositivo D3D11, vista que no apunta a su textura, o extensión distinta —y en ese caso publica las dos extensiones para poder compararlas—. Cuenta además cuántos frames se sirvieron con vectores de Unity y cuántos cayeron al óptico.

Informe a **versión 3** (192 B). El fixture WARP ejercita las seis rutas del selector, incluida la nominal.

**Los turnos denegados son reales pero secundarios:** 1.639 negados frente a 1.549 concedidos indica que tres huecos no cubren la latencia de la GPU. No se toca todavía: cambiar la profundidad de la reserva a la vez que el selector confundiría la próxima medida. Se decidirá con el dato, no antes.

Feeder a `0.15.1-neuralfx.10`.

### El selector descarta por dispositivo

```text
[seleccion: el recurso vive en otro dispositivo D3D11 · nativos 0 · al optico 2660]
```

37 de 38 muestras, sin una sola excepción. La comparación era `owner == device` sobre punteros crudos, y **eso no prueba nada**: ReShade envuelve `ID3D11Device`, de modo que el contexto del add-on puede entregar la envoltura mientras el recurso declara el dispositivo real. Dos punteros distintos pueden ser el mismo aparato.

La identidad pasa a establecerse por el objeto DXGI, que la envoltura reenvía al real, y se anota **cómo** se estableció (mismo puntero, identidad DXGI, o ninguna) junto al **LUID del adaptador de los dos**. Si esto era una envoltura, la ruta entra. Si no lo era, el registro dirá si los dos LUID coinciden —dos dispositivos sobre la misma GPU, que exigiría recursos compartidos— o difieren.

No se afirma cuál de los dos es: el informe lo dirá. Lo que sí se corrige sin esperar es la comprobación, que era inválida en cualquier caso.

Informe a **versión 4** (212 B). El fixture crea un segundo dispositivo WARP real y comprueba que ahí el motivo **sí** es el dispositivo, con coincidencia 0.

Feeder a `0.15.1-neuralfx.11`.

### Dos dispositivos, y una comprobacion que tampoco zanjaba nada

```text
[seleccion: otro dispositivo D3D11: adaptador del recurso 0:11EBF,
 del consumidor 0:11EBF · nativos 0 · al optico 2660]
```

La identidad DXGI tampoco coincidió. Pero **el LUID solo descarta que sean GPU distintas**: no separa una envoltura de ReShade de un segundo dispositivo real, y el mensaje que se escribió para esta medida afirmaba lo segundo. Era una deducción, otra vez.

La pregunta correcta no es de identidad sino de **capacidad**: ¿puede ese dispositivo leer ese recurso? Eso se pregunta pidiéndole que cree su propia vista, una sola vez por hueco. Si puede, se usa esa vista y la ruta entra sin importar cuántos punteros haya de por medio. Si no puede, el `HRESULT` lo dice y entonces sí está demostrado que son dispositivos distintos de verdad.

El fixture crea un segundo dispositivo WARP real y comprueba que ahí la prueba **falla**: la comprobación nueva no se volvió permisiva.

Informe a **versión 5** (216 B), con el `HRESULT` de la prueba. Feeder a `0.15.1-neuralfx.12`.

### El despliegue deja de pedir un clic

`tools/package.ps1 -Deploy` instalaba mod y Hub, pero el addon nativo de la carpeta del juego lo escribía solo el botón **Aplicar preset / reinstalar**. Tres entregas seguidas terminaron pidiéndole eso al usuario. `tools/NeuralFX.PipelineInstall` lo hace ahora por el **mismo motor transaccional** que el botón —no copiando archivos, que dejaría el manifiesto mintiendo—, respetando el ejecutable y el preset guardados, negándose si CS1 está abierto, y **verificando por hash lo que quedó escrito**. `-Deploy` lo invoca solo si el addon instalado no coincide con el construido.

### Lo que este corte no resuelve

Que el registro acepte el recurso **acredita transporte, no calidad de los vectores**. Sigue sin verificarse la geometría de origen y destino de la copia: se presupone que la textura de `BuiltinRenderTextureType.MotionVectors` mide lo que la cámara, y si midiera lo que la pantalla la copia quedaría desplazada. Tampoco están verificadas las unidades ni el signo del vector, que son magnitudes distintas de su colocación. Nada de esto se ha tocado aquí, y la próxima sesión solo puede demostrar que la ruta entra, no que sus datos sean correctos.

Tampoco se ha tocado la profundidad —plana en 13 de 29 sondas, sin origen identificado— ni el aislamiento de la interfaz.

## Pendientes

No se ha demostrado ausencia de ghosting, compatibilidad con todos los mods ni UI intacta. La profundidad de una ciudad **sí se midió, y no es correcta de forma sostenida**: ver la sección del 10 de septiembre. No se implementaron jitter de cámara ni DRS de Unity; el consumo de MV nativos está escrito, pero el puente rechaza la textura y la ruta no llega a ejercitarse. La consulta de publicaciones no instala versiones nuevas sin un catálogo compatible y verificado.

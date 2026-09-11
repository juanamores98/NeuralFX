# Punto de continuación — 11 de septiembre de 2026

La preferencia vigente exige desplegar siempre los cambios de desarrollo y completar la desinstalación/reinstalación con el juego y el Hub cerrados. Está autorizada su clausura normal para entregar. Véase [AGENTS.md](AGENTS.md). La entrega 2.1.1 está instalada y verificada; `artifacts/releases/ENTREGA-ACTUAL.txt` identifica el paquete.

El candidato de terreno no corrigió el defecto. El usuario comprobó una mejora clara y estabilización más rápida al desactivar todas las capacidades experimentales, eligió usarlas apagadas y pidió cerrar con commit y push. Conservar `ExperimentalOptIn=false`; no continuar la investigación salvo nueva petición. Resultado en [TERRENO-2026-09-11.md](docs/TERRENO-2026-09-11.md).

# Historial — 8 de septiembre de 2026, Hub 2.0.2

## Instrucción histórica del usuario, sustituida por AGENTS.md

El usuario retomó el desarrollo y autorizó todas las recomendaciones verificadas. Después indicó que **él hará las pruebas dentro del juego**. No abrir, cerrar ni manejar CS1/Windows para probarlo por cuenta propia. Si hace falta una captura o acción, explicar qué se necesita observar. Se puede seguir trabajando en código, archivos, registros y pruebas automatizadas sin control de escritorio. No hay automatización programada.

## Entrega actual — Hub 2.0.2

- El usuario limpió manualmente ReShade/DLSS/NeuralFX del directorio de CS1 y pidió asegurar la desinstalación completa de todo lo aplicado allí. Se implementó **Desinstalar del juego…**, separada de **Restaurar estado anterior…**. La segunda puede recuperar ReShade/DLSS antiguos; la primera los retira.
- Hub **2.0.2 instalado**, permanece cerrado. Mod **2.0.0**, puente **neuralfx.3** sin cambios gráficos nuevos. **64/64 pruebas aprobadas**, Release sin advertencias ni errores. No se abrió ni controló CS1.
- **Estado real vigente:** `C:/Program Files (x86)/Steam/steamapps/common/Cities_Skylines` limpio del inventario NeuralFX, **0 rutas conocidas presentes**, incluido `reshade-shaders` y los directorios privados. Verificado solo mediante lectura después del despliegue. No reinstalar el pipeline para probar: el usuario hace las pruebas en juego.
- Paquete vigente: `artifacts/releases/NeuralFX-2.0.2-20260908-191608.zip`; SHA-256 `f3c41f0474fae0485b06984cf40512d7359b8ef5014e995aad0f281ef7a60d69`. **28 archivos** coinciden en paquete y despliegue; también el manifiesto, **29 entradas ZIP verificadas**. Evidencia: `artifacts/uninstall-review/deployment-verification.json`.
- Backup de paquete: `%LOCALAPPDATA%/NeuralFX/Backups/Packages/20260908-191613-cc36d7f1704249b9b857c44bb58aae56`. El Hub sigue fuera de Mods; solo `NeuralFX.dll` se encuentra bajo Mods/NeuralFX.
- Nuevos servicios: `PipelineFootprint.cs` (catálogo + destinos antiguos + residuos conocidos), `UninstallService.cs` (plan con hashes, revisión de cambios, archivado externo, eliminación transaccional y verificación final). No usa comodines de DLL/shaders ni rutas de manifiestos antiguos. Puede limpiar sin manifiesto o con uno dañado. Conserva recursos ajenos y carpetas compartidas que los contengan.
- Copias de desinstalación bajo `%LOCALAPPDATA%/NeuralFX/Backups/Uninstall/<fecha-id>`, fuera del juego. Retira también archivos modificados/preexistentes en los destinos conocidos, tras archivarlos. Hub, mod administrado, descargas, partidas y archivos externos quedan fuera de esta acción sobre la carpeta del juego. Archivos trasladados por el usuario y cachés globales del controlador no se enumeran ni borran.
- Caché y capturas nuevas de ReShade configuradas bajo `.neuralfx-runtime/ReShade` y `.neuralfx-runtime/Capturas`. `FileTransaction.EnsureDirectory` crea/registra el directorio de caché antes de terminar la instalación: ReShade 6.8 vuelve a caché global si no existe. Ver fuente y alcance en `docs/VALIDACION.md`.
- La suite añade 14 casos de desinstalación y uno de terminación de su proceso. Un fallo de mutex tras terminar al instalador se reprodujo: primer intento inmediato ocupado, adquirido 0,3267 ms después. Espera máxima de 250 ms en `InstallationLease`; la prueba de escritor concurrente sigue rechazándolo.
- Ciclo aislado adicional con **19 archivos reales de caché**, reinstalación y residuos de sesión simulados: **27 archivos/10 carpetas retirados**, inventario original exacto y cada copia externa con SHA verificado. Segundo intento sin cambios. Evidencia y helper en `artifacts/uninstall-review/`, ignorados. No ejecutó gráficos ni Cities.exe.
- Renders WPF de estado real limpio y confirmación simulada a tamaño normal/mínimo: `artifacts/ui-review/07-clean-game-minimum.png`, `08-uninstall-preview.png`, `09-uninstall-preview-minimum.png`. Sin abrir ventanas de escritorio ni completar navegación/accesibilidad manual. Guía: `docs/FLUJO-HUB.md`.
- Sin commit ni push. Preservar todo el trabajo previo y los paquetes antiguos; el marcador `artifacts/releases/ENTREGA-ACTUAL.txt` apunta a 2.0.2.

## Entrega previa — Hub 2.0.1 (histórico, sustituido por 2.0.2)

- Última petición: aclarar qué está instalado, qué solo está preparado, qué restaura el desinstalador, y mejorar señalización, avisos y flujo. Implementado en WPF sin cambios nuevos en render nativo ni mod.
- Hub **2.0.1** instalado y abierto; mod permanece en **2.0.0**. **49 pruebas administradas aprobadas**, compilación sin advertencias. No se abrió CS1.
- Paquete final: `artifacts/releases/NeuralFX-2.0.1-20260908-175112.zip`; SHA-256 `0b4cace2c02e6590f147bb7161d886b8e9fd6360082ba49ddd7f01ec2417c797`. Sus **28 archivos** coinciden con sus hashes en el paquete y en el despliegue.
- Backup final: `%LOCALAPPDATA%/NeuralFX/Backups/Packages/20260908-175116-91ec1bda711b41b3b1f06ab0dfd7e6ff`. Mismas rutas de Hub/mod indicadas abajo; el Hub siempre fuera de Mods.
- **Estado actual del juego:** contiene el manifiesto antiguo **1.0.0**, originalmente fechado a las 07:08 UTC. Ya no contiene el registro v2 descrito en la entrega previa. Se observó este cambio leyendo disco; no atribuirlo a una acción concreta del usuario. Esta tarea no reinstaló ni restauró el pipeline real. El Hub ofrece **Actualizar instalación antigua** y muestra diez componentes disponibles. El usuario decide cuándo aplicar esa actualización.
- El catálogo y la caché están preparados para puente `0.14.0-beta.4-neuralfx.3` y RenoDX 3.3.4. No confundir esos componentes con lo que está actualmente aplicado al juego.
- Instalación abre por defecto y separa copias disponibles, componentes verificados y sesión. Hay estados ausente/incompleto/modificado/configurado/sin registro y copia preparada distinta. Se revalida caché al recuperar foco. Acciones deshabilitadas tienen explicación y apariencia/foco visibles.
- Avisos persistentes de progreso y resultado, sin confundir descarga/importación con instalación ni ocultar errores. El resultado final se muestra tras refrescar el diagnóstico; se impide cerrar durante una operación.
- Restauración con vista previa de destino y archivos a recuperar/retirar/preservar, incluidos archivos ya ausentes. Conserva Hub/mod/caché/partidas/archivos ajenos. La migración vuelve al estado antes de migrar, no a un juego limpio. Registro o backups inválidos bloquean la acción. Pruebas comparan la vista previa con rollback real.
- Nuevos archivos: `Services/InstallationStatus.cs`, `InstallationGuidance.cs`, `RollbackPreview.cs`, `RestoreWindow.xaml/.cs`, `InstallationFlowTests.cs`, `docs/FLUJO-HUB.md`.
- Evidencia: `artifacts/ui-review/REVIEW.md`, PNG y `deployment-verification.json`. Renders de controles WPF reales con estados actuales/simulados; no son capturas de navegación. Computer Use falló dos veces con «foreground window did not report a process id» y se detuvo. Interacción manual y accesibilidad completas pendientes. El Hub final responde (último PID observado 35808).
- Los paquetes intermedios `174808` y `174941` quedaron conservados: la revisión automática bloqueó el comando de limpieza con «blocked by policy». El paquete vigente es únicamente `175112`; no reintentar la eliminación ni usar los intermedios. Esta limitación no afecta la instalación final.
- Trabajo local sin commit ni push. Conservar todos los cambios anteriores. Antes de la siguiente prueba en ciudad, comprobar si el usuario ya actualizó el pipeline antiguo.

## Entrega previa — 2.0.0 (histórico, sustituido por el estado anterior)

- Cambios locales sin commit ni push. Conservar el trabajo y la protección previa del usuario en los handlers de controles durante la inicialización del Hub.
- Hub/mod 2.0.0 compilados y desplegados; **40 pruebas administradas aprobadas**, incluidas recuperación tras terminar un proceso escritor y MMF entre procesos reales. Pruebas nativas ABI/render/reset/frescura/jitter también aprobadas.
- Paquete: `artifacts/releases/NeuralFX-2.0.0-20260908-170454.zip`.
- SHA-256 del ZIP: `f245062c495fc5c1859cf014b5075df3416a1f6f5fe883e0fa807006e2f40f20`.
- El instalador publicado `Install-NeuralFX.ps1` fue ejecutado correctamente desde el paquete. Los 27 archivos de distribución y despliegue coinciden con sus hashes.
- Mod: `%LOCALAPPDATA%/Colossal Order/Cities_Skylines/Addons/Mods/NeuralFX/NeuralFX.dll`.
- Hub: `%LOCALAPPDATA%/NeuralFX/Hub/NeuralFX.Hub.exe`. **Nunca volver a colocar el Hub dentro de Mods**: CS1 escanea todas las DLL recursivamente. Se migraron Hub y backups antiguos fuera de Mods; allí queda una sola DLL, NeuralFX.dll.
- Backups de paquete: `%LOCALAPPDATA%/NeuralFX/Backups/Packages/`. Los más recientes son 20260908-170502-e7fb27ca3ce748318bf7bf4acb3c6344 y 20260908-170644-ca0b19d8a83d4aeaa2ed48cda11073de.
- El pipeline en el juego está actualizado al puente **0.14.0-beta.4-neuralfx.3** y RenoDX **3.3.4**. La última instalación real se completó a las 11:05 aproximadamente. No se abrió el juego en el último turno.

## Hallazgos gráficos

RenoDX 4.70 aceptaba la llamada NR a 4K, pero la lista D3D12 fallaba al cerrar (0x80070057). En el fixture a 720p provocó excepción; 4.55 también falló a 1080p. **El catálogo queda fijado a 3.3.4**, no actualizar por el número de versión sin volver a probar.

La prueba aislada final, `artifacts/graphics-nr-20260908-105746`, completó 1.490 presents, 1.360 evaluaciones a 3840×2160, reset 8 confirmado y dispositivo sin error. Usa una superficie sintética; no valida calidad ni profundidad/movimiento de una ciudad. Antes, CS1 con 3.3.4 entregó más de 17.000 frames en el menú. No hay validación completada en una ciudad.

Corregido el falso «frame entregado» del upstream tras fallo de Close. No se copia resultado ni se confirma reset en esa situación. Ahora también se comprueba el HRESULT de señalización y el estado del dispositivo. El estado significa NGX aceptado y comandos enviados; no prueba por sí solo la imagen NR.

## Componentes e identidad

- Juego: `C:/Program Files (x86)/Steam/steamapps/common/Cities_Skylines`.
- Runtimes aportados: `C:/Users/amore/Downloads/DLSS310.8.0-Streamline2.13`.
- SR 310.8.0 firmado: `c85f971ce023c9f3492fc7455f0b01a24ba18ea39636407a846902c4360b0b7e`.
- NR 310.8.0 firmado: `e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e`.
- La instalación antigua usó RR renombrado como NR. Se corrigió. El importador verifica producto firmado, además de nombre y PE x64.
- Caché v2: `%LOCALAPPDATA%/NeuralFX/Cache/v2`. Catálogo en `NeuralFX.Hub/Assets/components.json`: ReShade 6.8, companion beta.4, RenoDX 3.3.4, LumeniteFX fijado y cabeceras oficiales.
- El Hub incrusta el feeder modificado; no los runtimes NVIDIA ni LumeniteFX. Publicar sin feeder falla.

## Implementación

Transacciones con snapshots/diario durables, mutex, recuperación idempotente y rollback que conserva recursos ajenos y ediciones posteriores. Migración legacy restaura el estado pre-migración, no promete vanilla. Se rechazan rutas ambiguas, destinos duplicados y manifiestos/cachés inválidos sin derribar el Hub.

Configuración: claves reales del feeder; elimina duplicados reconocidos incluso dentro de secciones legacy. NoReloadOnInit=0 y preset de inicio correcto. Presets 100/85/66% ajustan coste del addon, no resolución de Unity. CAS adaptativo a resolución de salida con gamma 2 SDR.

MMF v3: dos seqlocks, sesión única por propietario, comandos con sesión/revisión/ack, caducidad de dos segundos. Mod 10 Hz, Hub 30 Hz. Cámara cacheada, panel Colossal sin OnGUI, buffers reaplicados en Update/OnPreCull, cuts por posición/rotación/FOV/velocidad/resolución. Logs del Hub incrementales y acotados; watcher de integridad; consulta de releases filtrada por producto.

**No implementado:** consumo nativo de MV de Unity, jitter de cámara, DRS real, aislamiento del HUD antes de efectos. No activar ni fingir estas capacidades. La API ReShade render_effects permite estudiar el punto previo a UI y suprime el pase duplicado en Present, pero hace falta verificar el render target/orden de cámaras/estado D3D11 de CS1. La base actual usa flujo óptico y solo exporta reset sincronizado.

## Siguiente trabajo

1. El usuario puede abrir el Hub 2.0.2 y decidir cuándo instalar el pipeline sobre su juego limpio. Después esperar su prueba manual según `docs/PRUEBAS-CS1.md`: panel, resets/telemetría, movimiento, UI, salida/recarga de ciudad. Leer logs antes del siguiente arranque; no manejar el juego sin acordar la acción.
2. Arreglar resultados y solo después continuar fases de MV/jitter/aislamiento UI con criterios gráficos explícitos. No dar el desarrollo completo por terminado.
3. Documentación actual: README.md, NOTICE, docs/VALIDACION.md, docs/ARQUITECTURA.md. Actualizar evidencia al obtener nueva validación.
4. Antes de otra entrega: `Native/build.ps1`, `dotnet test NeuralFX.Tests -c Release`, `tools/package.ps1 -Deploy`. El script de paquete ya despliega a través del mismo instalador publicado. El manifiesto estático antiguo del repositorio se eliminó; se genera por paquete.

## Herramientas

MSVC/SDK extraídos oficialmente en `Native/toolchain` (ignorado). VS instalado carecía de C++; el build usa el toolchain local o VS cuando tiene C++. No se instaló Graphics Tools (requiere elevación); se descargó un Agility SDK firmado a Native/toolchain para investigar, pero no se usa ni se distribuye.

`Native/build.ps1 -SmokeHarness` y `Native/smoke.ps1 -GameRoot '<ruta>' -Width 3840 -Height 2160` ejecutan el fixture sin CS1. `artifacts/SmokeRunner` permite preparar caché, probar instalación/rollback real en carpeta temporal y modo install con proceso cerrado. Los tests de proceso están en NeuralFX.ProcessFixture y no se distribuyen.

Computer Use fue detenido con Escape en un turno anterior. El usuario prefiere ahora probar el juego personalmente. No reutilizar ventanas/handles sin observación nueva si se acuerda una captura futura. No se accedió al servidor Discord ni se enviaron mensajes.

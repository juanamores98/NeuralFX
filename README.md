# NeuralFX — consolidación 2.1.0 · Cities: Skylines 1

Hub de instalación y diagnóstico, mod de telemetría y puente nativo para ReShade → DLSS5-Feeder → RenoDX. Proyecto de **juanamores98**.

Esta versión consolida ABI, seguridad de cámara, movimiento, controles y confianza. **No completa aún la integración DLSS 5 del superplan.** El portador NGX y su salida GPU se miden por separado de NR; pre-UI, SR interno y confirmación NR por frame están pendientes. El jitter está bloqueado.

Lee primero la [entrega, estado de los 15 cortes y guía para tu PC](docs/CONSOLIDACION-2026-09.md). La [evidencia anterior](docs/VALIDACION.md) corresponde a otros binarios; no valida automáticamente bridge build 4 / ABI 3. Actualiza mod, Hub y addon juntos.

## Instalar y abrir

Requiere Windows x64, CS1 con DirectX 11, .NET 8 Desktop Runtime x64 y runtimes NVIDIA compatibles. El equipo comprobado usa RTX 5080 y controlador 616.86; el nombre de la GPU no garantiza compatibilidad en otros equipos.

1. Cierra CS1 y NeuralFX Hub. Extrae el ZIP en una carpeta normal.
2. Ejecuta `Install-NeuralFX.ps1` desde esa carpeta con PowerShell. Comprueba hashes, instala ambos componentes y conserva los archivos anteriores. No copies el paquete completo dentro de Mods.
3. Abre `Iniciar-NeuralFX-Hub.bat`. Comprueba la ruta de `Cities.exe`; puedes seleccionarla manualmente.
4. Descarga los componentes públicos desde el Hub e importa **por separado** `nvngx_dlss.dll` y `nvngx_dlssnr.dll`. Se verifican formato x64, firma NVIDIA y producto firmado. `nvngx_dlssd.dll` es Ray Reconstruction y no sustituye NR.
5. En **Instalación**, selecciona un preset y pulsa **Instalar en el juego**, **Actualizar instalación antigua** o **Aplicar preset / reinstalar**, según el estado detectado. Activa NeuralFX en el Gestor de contenido del juego.

La pantalla inicial separa componentes disponibles, componentes instalados y estado de la sesión. **Copia lista** significa que existe una copia verificada para instalar; no significa que esté aplicada al juego. **Instalado** exige archivos registrados y verificados. También se indican archivos incompletos, modificados, sin registro y copias preparadas diferentes de las instaladas. Los avisos de operación quedan visibles e incluyen el siguiente paso; los errores y descargas parciales no se presentan como éxitos. Consulta [la guía del Hub](docs/FLUJO-HUB.md).

Ubicaciones instaladas:

```text
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\NeuralFX\NeuralFX.dll
%LOCALAPPDATA%\NeuralFX\Hub\NeuralFX.Hub.exe
%LOCALAPPDATA%\NeuralFX\Cache\v2\
%LOCALAPPDATA%\NeuralFX\Backups\Packages\
```

El Hub reside fuera de Mods porque CS1 intenta cargar recursivamente sus DLL de escritorio como ensamblados del juego. El instalador también traslada `Hub` y `.neuralfx-previous` de paquetes antiguos a los backups privados.

## Qué hace esta versión

- Instala paquetes con versiones y SHA-256 fijados, companion FX, flujo óptico y CAS. Usa RenoDX **3.3.4**: las pruebas locales de 4.55 y 4.70 fallaron.
- Conserva diario transaccional, backups y manifiesto de propiedad. Una operación interrumpida se recupera en el siguiente intento.
- Detecta bibliotecas Steam adicionales, conserva la ruta manual y comprueba cambios en los archivos instalados.
- Publica FPS, tiempo de frame, resoluciones y estado por memoria compartida. Los comandos del Hub permiten abrir el panel, reaplicar buffers y solicitar un reset.
- Envía el reset de cámara al hilo de render y a NGX. Rechaza jitter distinto de cero hasta disponer de una integración temporal completa.
- Usa un panel Colossal arrastrable (`Ctrl + Alt + N`), sin `OnGUI`. ReShade se abre con `Home`, salvo que hayas personalizado su atajo.

La ruta predeterminada consume **movimiento estimado por LumeniteFX**. La opción experimental copia vectores Unity a recursos propios registrados; su signo y cobertura de objetos necesitan validación en CS1. El panel nativo tampoco excluye la UI del procesamiento neural: esa fase sigue pendiente. No hay jitter de cámara ni resolución dinámica del render de Unity.

## Presets

| Preset | Trabajo del feeder por dimensión | CAS final |
|---|---:|---:|
| Nativo | 100% | 0,30 |
| Equilibrado de coste | 85% | 0,30 |
| Coste reducido | 66% | 0,30 |
| Fotografía | 100% | 0,15 |

Los porcentajes reducen el trabajo del addon. Unity sigue renderizando a la misma resolución. En los presets reducidos, FSR 1 expande el resultado del feeder y CAS trabaja a resolución de salida. No equivalen a Quality/Balanced/Performance de DLSS nativo ni prometen aumentar los FPS de una ciudad limitada por CPU.

## Desinstalar los componentes del juego

Con CS1 cerrado, pulsa **Desinstalar del juego…**. La vista previa enumera los archivos y carpetas que se retirarán. El Hub guarda primero una copia verificada en `%LOCALAPPDATA%\NeuralFX\Backups\Uninstall\`, elimina los componentes y comprueba que no queden residuos de su inventario. Solo entonces muestra **Desinstalación completa verificada**.

Incluye ReShade, runtimes DLSS, addons, shaders, texturas, configuración, manifiesto, logs/volcados del feeder y carpetas privadas de caché, capturas y recuperación. También reconoce los destinos del Hub antiguo, aunque falte el manifiesto o esté dañado. **No recupera instalaciones antiguas de ReShade/DLSS.** Los archivos ajenos y carpetas compartidas con otros recursos se conservan. El Hub, el mod en Addons/Mods, las descargas y las partidas permanecen fuera de esta desinstalación del directorio del juego.

Las instalaciones nuevas guardan caché y capturas de ReShade en `.neuralfx-runtime`, dentro del juego, para poder retirarlas junto con el pipeline. Consulta el [alcance y flujo completos](docs/FLUJO-HUB.md).

## Restaurar el estado anterior

Con CS1 cerrado, usa **Restaurar estado anterior…**. Antes de confirmar verás el destino de la restauración, la carpeta del juego y las listas de archivos que se recuperan, retiran y guardan aparte. Se verifican las copias antes de ofrecer la acción y al ejecutarla. Se conservan logs, archivos ajenos y copias de archivos modificados desde la instalación en `.neuralfx-preserved`, dentro de la carpeta del juego. Para retirar estos residuos y los componentes gráficos, usa **Desinstalar del juego…**.

Al migrar una instalación del Hub antiguo, el retorno es el estado existente antes de migrarla, incluido su manifiesto. No se promete una instalación vanilla cuando faltan backups originales fiables.

Esta acción restaura **el pipeline del juego**. **No desinstala el Hub ni el mod NeuralFX y no borra las descargas o las partidas.** Si vuelve el manifiesto antiguo, el Hub lo identifica como instalación antigua, permite actualizarla y no ofrece volver a restaurar con un registro que no puede verificar.

## Compilar y verificar

Requiere .NET SDK 8, ensamblados administrados de CS1, MSVC C++ x64 y Windows SDK. Puedes indicar `ManagedDLLPath` como propiedad de MSBuild si Steam está en otra ubicación.

Compilar `NeuralFX.Mod` copia `NeuralFX.dll` a `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\NeuralFX\`, como el resto de mods del repositorio. Solo esa DLL y solo esa carpeta: CS1 escanea recursivamente todas las DLL de `Mods` e intenta cargarlas como ensamblados del juego, así que el Hub de escritorio nunca puede vivir ahí — y tampoco el paquete completo, que solo debe extraerse en una carpeta normal.

Compilar el Hub o ejecutar las pruebas **no** despliega nada. Para evitarlo también al compilar el mod usa `-p:DeployMod=false`, o `-NoDeploy` en `Build-NeuralFX.ps1`; `-p:ModDeployDir=…` cambia el destino. Con el juego abierto la copia falla con un aviso, sin romper la compilación.

Esto **no** sustituye a la entrega real. Desplegar solo el mod puede dejarlo por delante del puente instalado; si los contratos no coinciden el mod entra en bypass y lo indica en su estado. Para actualizar mod, Hub y addon nativo juntos, con hashes y copias de seguridad, sigue siendo `tools/package.ps1 -Deploy`.

```powershell
./tools/Build-NeuralFX.ps1 -ManagedDLLPath 'D:\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed' -Package
```

`Native/build.ps1` obtiene fuentes fijadas, comprueba los parches y ejecuta las pruebas nativas. `tools/package.ps1` compila, prueba, publica y genera un ZIP con `deployment-manifest.json` calculado sobre sus archivos reales. `-Deploy` lo instala con backups. El manifiesto de distribución se genera por paquete; no se conserva una copia estática en el código.

La prueba gráfica automatizada usa una carpeta temporal, sin abrir CS1 ni tocar partidas:

```powershell
./Native/build.ps1 -SmokeHarness
./Native/smoke.ps1 -GameRoot 'C:\ruta\Cities_Skylines' -Width 3840 -Height 2160
```

Necesita una instalación v2 para obtener los componentes. Comprueba transporte, evaluación y reset; su escena sintética no valida la calidad de una ciudad. Consulta [validación](docs/VALIDACION.md) y [arquitectura](docs/ARQUITECTURA.md).

## Licencias

Código propio: [MIT-0](LICENSE). Feeder, ReShade, CAS, MinHook y demás componentes conservan sus licencias; consulta [NOTICE](NOTICE) y `licenses/`. Los runtimes NVIDIA se importan localmente. LumeniteFX se descarga del repositorio del autor y su código no se incluye en el paquete NeuralFX.

# Qué está instalado y qué hará el Hub

El Hub 2.1.0 separa **Inicio**, **Instalación** y **Diagnóstico**. La instalación presenta tres pasos:

1. **Preparar componentes:** descargar o importar copias verificadas. No modifica el juego. Los recursos incluidos en el Hub también cuentan como disponibles.
2. **Instalar en CS1:** copiar y registrar los archivos del pipeline. La acción disponible indica si es una primera instalación, actualización antigua, reparación o aplicación de un preset.
3. **Comprobar en juego:** abrir una ciudad, activar el mod y consultar **Estado en juego**. Juego cerrado, mod conectado y NGX confirmado son estados diferentes. La calidad visual se comprueba jugando.

## Leer los estados

| Estado en el juego | Significado |
|---|---|
| Instalado | Todos los archivos del componente coinciden con el registro de instalación. |
| Instalado · ajustado | Los archivos están presentes; la configuración se cambió después de instalar. |
| Instalado · otra copia | El componente instalado está verificado, pero la copia preparada es distinta. Hay que aplicarla para actualizarlo. |
| No instalado | No están los archivos esperados. Tenerlos descargados no cambia este estado. |
| Incompleto | Falta parte de un componente, incluida cualquiera de sus dependencias. |
| Modificado | Un archivo distinto de la configuración difiere de lo instalado. |
| Sin registro | Hay archivos presentes, pero no se pueden atribuir y verificar como instalación de este Hub. |
| Sin verificar | El diagnóstico no terminó o el registro/copias no es verificable. |

La columna **Copia para instalar** es independiente: puede haber una instalación válida aunque se haya perdido la descarga. En ese caso se puede restaurar con los backups, pero hay que recuperar los componentes antes de reinstalar. Al pasar el cursor sobre el estado en el juego se muestran las rutas y el estado de cada archivo.

En **Entorno y rutas** se indica desde dónde está abierto el Hub, si existe el archivo del mod y dónde se guardan las copias locales. Detectar `NeuralFX.dll` no demuestra que el mod esté activado en el Gestor de contenido.

## Resultado de una operación

El aviso superior permanece hasta la siguiente operación o hasta cerrar el Hub. Muestra actividad durante la operación y después indica el resultado: copia preparada, instalación verificada, importación rechazada, descarga parcial o restauración completada. **Ver registro** abre el detalle. Las acciones que requieren componentes ausentes, escritura o el juego cerrado se deshabilitan, y el texto del siguiente paso explica por qué.

## Desinstalar los componentes de NeuralFX del juego

Usa **Desinstalar del juego…** para retirar el pipeline y sus residuos. **Restaurar estado anterior…** tiene otro resultado: puede recuperar un ReShade/DLSS previo. Ambos botones muestran su alcance antes de confirmar.

1. Cierra CS1. El Hub revisa el inventario actual; si quedó una operación interrumpida, primero recupera su diario.
2. Revisa la lista de archivos y carpetas y la ruta del juego. Puedes cancelar la retirada.
3. Confirma **Desinstalar del juego**. Se guardan copias con SHA-256 verificado fuera del juego, en `%LOCALAPPDATA%\NeuralFX\Backups\Uninstall\<fecha-id>\`.
4. Se retiran los archivos, se eliminan las carpetas que quedaron vacías y se repite el inventario. El aviso solo indica **Desinstalación completa verificada** si no quedan residuos conocidos. Incluye la ruta de recuperación.

El inventario abarca los 19 archivos del catálogo actual: `dxgi.dll`, `nvngx_dlss.dll`, `nvngx_dlssnr.dll`, los dos addons, los shaders/cabeceras/texturas de NeuralFX y LumeniteFX, y sus configuraciones. También retira `nvngx_dlssd.dll` y `reshade-shaders/Shaders/CAS.fx` del Hub antiguo, `NeuralFX_Manifest.json`, `ReShade.log`, `dlss5-feed.log`, `dlss5-feed-crash.dmp`, y el contenido de `.neuralfx-backups`, `.neuralfx-preserved` y `.neuralfx-runtime`. El diario transaccional se elimina al terminar correctamente.

Los archivos modificados o preexistentes que ocupen esos destinos se retiran también, después de archivarlos. No se recupera el ReShade/DLSS antiguo. No se confía en rutas de un manifiesto dañado para borrar otros archivos ni se borran DLL/shaders por extensión. Las carpetas compartidas que contengan recursos ajenos se conservan; esos recursos no se presentan como residuos de NeuralFX.

Si un archivo está bloqueado, cambió después de la vista previa o una ruta redirige mediante un enlace/junction, se informa del problema y no se declara éxito. Un fallo durante las eliminaciones recupera los archivos desde el diario; si se termina el proceso, esa recuperación ocurre al volver a intentarlo. Repetir la desinstalación sobre un juego limpio no elimina nada ni crea otra copia.

La acción está limitada a la carpeta de instalación de CS1. Permanecen el Hub, `NeuralFX.dll` en Addons/Mods, las descargas, las copias externas, las partidas y los archivos ajenos. Las instalaciones 2.0.2 configuran la caché de shaders y las capturas de ReShade bajo `.neuralfx-runtime`. Los archivos que el usuario haya trasladado o configurado en otras rutas y las cachés globales del controlador no pertenecen a este inventario.

## Restaurar archivos anteriores

**Restaurar estado anterior…** abre una vista previa de solo lectura. Permite cancelar sin modificar archivos. Para ejecutarla se pulsa **Restaurar estos archivos**.

- Recupera desde backups los archivos que existían antes de la primera instalación registrada. Reinstalar no reemplaza ese punto original.
- Retira los archivos añadidos por ese pipeline.
- Conserva una copia de archivos modificados desde la instalación en `.neuralfx-preserved` antes de reemplazarlos o retirarlos.
- Conserva el Hub, el mod NeuralFX, las descargas, partidas, logs y archivos ajenos.

Si se migró desde el Hub antiguo, el destino es **la instalación antigua existente antes de migrarla**, incluido su manifiesto. Puede seguir habiendo ReShade, runtimes y otros componentes después de restaurar. No es una desinstalación completa de NeuralFX ni una promesa de devolver el juego a una instalación limpia de Steam.

Sin registro verificable o con backups ausentes/alterados, la restauración no se ofrece. El Hub informa del problema sin borrar archivos para intentar adivinar su origen.

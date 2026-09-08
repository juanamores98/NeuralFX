# Prueba manual de CS1

El usuario realiza estas acciones. Codex puede revisar los registros locales después; no hace falta controlar Windows para leerlos. Si se necesita una captura, se explica antes qué observar.

## Primera pasada: unos cinco minutos tras cargar

1. Deja el preset **Nativo** para la primera comparación. Inicia CS1 en DirectX 11 y carga una ciudad de prueba. Evita guardar cambios en una partida importante durante la validación.
2. Pulsa **Ctrl + Alt + N**. El panel debe abrirse, permitir arrastrarlo y cerrarse. Debe mostrar resolución y FPS que cambian. También puedes abrirlo desde las opciones de NeuralFX.
3. Pulsa **Reiniciar historial**. Los resets solicitados deben subir y el confirmado debe alcanzarlos cuando el feeder evalúe de nuevo. En el Hub, **Estado en juego** debe mostrar datos recientes y confirmar los comandos.
4. Mueve y gira la cámara; prueba un salto a un edificio, pausa y cambio de velocidad. Observa estelas, parpadeo, oscurecimiento, cambios de color y legibilidad de menús. Abre ReShade con **Home**: el orden esperado es `Lumenite_Kernel`, `DLSS5_Feed`, `NeuralFX_CAS`. Compara activado/desactivado desde el overlay.
5. Vuelve al menú y carga otra ciudad. Durante la descarga el Hub debe dejar de mostrar datos vivos; al cargar debe reconectarse sin duplicar el panel ni reproducir un comando anterior.

Si `Home` no abre ReShade, comunica ese resultado; no hace falta reinstalar todo para investigarlo.

## Qué comunicar

- Si abrió el panel y si el reset llegó a confirmarse.
- Si aparecen estelas, texto deformado, pantalla negra o cambios de color; en qué acción ocurren.
- Resolución y FPS aproximados con el efecto activado y desactivado.
- Si la segunda carga reconecta la telemetría.

Registros junto a `Cities.exe`: `ReShade.log`, `dlss5-feed.log` y `Cities_Data/output_log.txt`. Conviene revisarlos antes de otro arranque, que puede reemplazarlos.

## Interpretación

«Archivos verificados» confirma integridad. «Evaluación NGX confirmada» indica que la llamada fue aceptada y su lista de comandos enviada; no certifica por sí sola la imagen ni profundidad/movimiento. La profundidad plana es esperable en el menú o fixture sintético, pero necesita investigación si persiste dentro de una ciudad 3D.

El tratamiento de UI todavía requiere desarrollo antes de garantizar texto intacto. La ruta trabaja sobre el frame final, con flujo óptico, sin jitter de cámara ni MV nativos conectados.

Después se puede probar **Equilibrado de coste**, cerrando el juego al aplicar el preset. Cambia un filtro o mod por comparación para poder atribuir el resultado.

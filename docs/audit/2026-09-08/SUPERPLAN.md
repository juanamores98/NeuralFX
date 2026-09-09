# NeuralFX: auditoría y superplan de consolidación de DLSS 5 en Cities: Skylines 1

**Fecha de revisión:** 8 de septiembre de 2026.  
**Repositorio revisado:** `juanamores98/NeuralFX`.  
**Revisión congelada:** `a063036271f2b84d0abeca31153400940190dbf6`.  
**Documento contrastado:** informe de Gemini adjunto, `Markdown(20260908-232655).md pegado`, 211 líneas lógicas.  
**Entregable:** diagnóstico estático, especificación de implementación y protocolo de aceptación. No es una instalación, un commit ni una certificación de funcionamiento en CS1.

## 0. Dictamen y límites

**NeuralFX es un prototipo con infraestructura aprovechable y evidencia documentada de inferencia NR en un entorno gráfico aislado. Todavía no acredita una integración DLSS 5 satisfactoria, estable y visualmente validada dentro de una ciudad. La última ampliación de jitter y vectores nativos contiene defectos concretos que deben corregirse antes de presentarla como terminada.**

No recomiendo abandonar el proyecto ni reemplazarlo por otro gestor de instalaciones. Recomiendo conservar instalación transaccional, backups, hashes, separación del Hub respecto a Mods, telemetría y transporte gráfico; corregir la integración temporal y adelantar el procesamiento a un punto anterior al HUD. La alternativa de motor NR se decide mediante un experimento controlado, no porque un paquete tenga una versión más alta.

La documentación de validación registra una RTX 5080 y driver 616.86. Esa identificación pertenece al equipo de la prueba documentada; esta auditoría no ha inspeccionado físicamente el equipo del usuario. No se ejecutaron CS1, WPF ni una GPU NVIDIA en este entorno. Sí se reprodujeron aisladamente el defecto de copia del ABI y la aceptación de NaN, usando un pequeño programa C++ portátil derivado de las expresiones del contrato. No equivale a compilar ni ejecutar la suite de NeuralFX.

El documento distingue cuatro niveles:

- **C:** comprobado leyendo el código de la revisión congelada.
- **D:** declarado por documentos, registros o el informe adjunto, no repetido aquí.
- **I:** inferencia técnica a partir del código; requiere prueba de integración para observar su efecto visual.
- **P:** propuesta de esta auditoría; no se presenta como capacidad existente.

Las referencias `[Rxx]` apuntan al código leído; `[Exx]`, a fuentes externas primarias; `[G]`, al adjunto de Gemini. La sección final contiene las referencias.

## 1. Qué significa éxito en este proyecto

El objetivo sigue siendo **DLSS 5 Neural Rendering real en CS1**, no sustituirlo por un simple afilado de imagen ni declarar que DLAA es DLSS 5.

Separar cinco capacidades tanto en código como en la interfaz:

| Capacidad | Función | Prueba necesaria |
|---|---|---|
| DLAA | Reconstrucción temporal a resolución nativa para antialiasing | Evaluación del componente correspondiente y mejora A/B en movimiento |
| DLSS Super Resolution | Reconstrucción desde render interno menor que la salida | Resolución 3D realmente menor; consulta de modo admitido; recursos y jitter correctos |
| DLSS 5 Neural Rendering, NR | Etapa neural de apariencia e iluminación | Ejecución NR diferenciada del portador DLAA y salida utilizada por el juego |
| Frame Generation | Cuadros visuales adicionales | Contadores separados de cuadros renderizados/presentados/generados y análisis de latencia |
| CAS u otro afilado | Postproceso espacial | Control de intensidad y ausencia de doble afilado |

NVIDIA presenta NR como una función independiente y opcional. No es correcto deducir éxito de NR de la sola presencia de `nvngx_dlss.dll`, de la creación de un recurso NGX genérico o del aumento de cuadros presentados. NVIDIA documenta integración mediante Streamline, pero los requisitos concretos del plugin NR que se utilice deben verificarse en sus headers, distribución y licencia; no se inventará un identificador numérico de feature ni un contrato a partir de una DLL encontrada. [E01, E02]

### Escala de evidencia exigida

`Paquete verificado → Módulo cargado → Backend inicializado → Entrada válida → Evaluación registrada → Trabajo GPU completado → Salida incorporada → Calidad aprobada en ciudad`.

Son estados distintos. Por ejemplo, un `Evaluate` que devuelve éxito antes de que falle el cierre de una lista de comandos no alcanza “salida incorporada”. Y una salida incorporada con vectores incorrectos no alcanza “calidad aprobada”.

## 2. Estado real encontrado y contradicción de versiones

### 2.1. Lo aprovechable

La base anterior ya diferencia descarga, instalación y sesión; mantiene snapshots y diario transaccional; verifica contenido mediante hashes; separa Hub y mod; maneja IPC con sesión y seqlocks; y tiene un evento nativo sincronizado con render. No hay razón para reemplazar esas piezas indiscriminadamente. [R01, R02, R03]

### 2.2. Qué acredita la validación almacenada

La documentación registra, entre otros resultados:

- RenoDX 3.3.4, puente `.3`, 3840×2160: 1.490 presents, 1.360 evaluaciones, reset 8, resultado 1 y dispositivo sin error.
- Aproximadamente 17 ms de GPU por frame del feeder en ese fixture a 4K. Es el coste registrado de esa ruta, no una medición aislada de DLAA ni el rendimiento de una ciudad.
- Más de 17.000 cuadros procesados en el menú con 3.3.4.
- Fallos en la ruta NR con otras combinaciones, incluyendo RenoDX 4.55/4.70.
- Pruebas administradas principalmente de instalación, recuperación, configuración e IPC; no una validación de todos los materiales y cámaras de CS1. [R02]

Estos datos son resultados **declarados por el repositorio**. El número de presents no sustituye a un vídeo comparativo de una ciudad ni prueba que la DLL instalada hoy sea la misma.

### 2.3. Qué cambió en la última revisión

README y arquitectura todavía describen ABI 1 de 32 bytes, jitter rechazado y movimiento óptico. Sin embargo, `TemporalCamera.cs`, `NativeBridge.cs`, `neuralfx_bridge.h` y `build.ps1` ya introducen ABI 2 de 48 bytes, jitter y un puntero de vectores Unity. Los tres ajustes nuevos vienen activados por defecto. El catálogo además descarga SR 310.9.1, mientras la evidencia histórica enumera SR 310.8.0. [R01–R08, R14]

**Consecuencia:** no se debe afirmar ni “no hay jitter implementado” ni “las pruebas históricas validan este jitter”. La situación correcta es: implementación reciente, documentación rezagada y aceptación gráfica pendiente para esa combinación exacta.

## 3. Validación del informe de Gemini

| Afirmación o propuesta del adjunto | Dictamen | Incorporación al plan |
|---|---|---|
| Jitter Halton, movimiento del motor y reset son una dirección adecuada | Correcta como dirección; incompleta como prueba | Conservar, sujetos a contrato de proveedor, fases, cámara, unidades y validación GPU |
| NeuralFX ya proporciona una integración nativa temporal satisfactoria | No acreditada | Corregir defectos enumerados antes de habilitar por defecto |
| Interfaz desacoplada ya implementada | Contradice el propio siguiente paso del informe y no aparece en la ruta revisada | Convertirla en fase prioritaria de implementación |
| Máscara reactiva/BiasCurrentColorMask protege el HUD | Confunde guía temporal con exclusión de composición | Usar escena sin HUD y composición posterior; distinguir una máscara NR específica si la API la admite |
| DLAA elimina completamente aliasing y borrosidad | Garantía no demostrada | Medir cables, vegetación, LOD y vehículos en movimiento |
| DLAA cuesta menos de 1,2 ms y supera siempre 4× SSAA | Sin benchmark de ese caso en el adjunto | Eliminar cifras universales y medir por componente |
| Calidad 67% reduce el tiempo de render ~40% | No inferible para esta implementación | Medir GPU; los presets actuales reducen trabajo del addon, no render Unity |
| Fórmula `log2(render/display) - 1 ≈ -0,58` con ratio 2/3 | Error aritmético | `log2(2/3)≈-0,585`; restar 1 da `≈-1,585`. Aplicar solamente la fórmula del SDK elegido |
| `AnisotropicFiltering.ForceEnable` significa 16× en todas las texturas | No lo garantiza el ajuste mostrado | Separar política anisotrópica, nivel de texturas y medición; no etiquetar 16× sin verificar |
| LOD bias 2–3 es una optimización general | Puede aumentar coste geométrico; tampoco se prueba que gobierne todo LOD personalizado de CS1 | Fuera del núcleo DLSS; opción explícita con propietario y restauración |
| Tonemapping Linear, sombras 2.500 m/4.096, texturas 4K son necesarios | No son requisitos de DLSS 5 | No imponerlos ni entrar en conflicto con los otros FX |
| DLDSR 2,25× + DLAA siempre es superior y sin artefactos | No demostrado; incrementa coste y puede superar límites del backend | Experimento opcional posterior, nunca configuración inicial |
| Umbrales de población fijan FPS y utilización para cualquier PC | Generalización sin método ni datos aportados | Perfilar ciudades y equipos; no usar población como detector de cuello de botella |
| LSFG duplica FPS sin inconvenientes perceptibles | Alternativa válida, garantía injustificada | Benchmark opcional de FG, sin presentarlo como mayor velocidad de simulación |
| El puntero MV deja listo cualquier proxy de Frame Generation | Incorrecto como contrato de integración | Evaluar presentación, recursos, tiempos, HUD, compatibilidad API y sincronización por separado |
| Shader sin errores y DrawText presente demuestran que DLSS funciona | Demuestran preparación/compilación, no calidad ni ejecución NR completa | Mantener esas correcciones, añadir niveles de evidencia |
| Apariencia comparable a Unreal Engine 5 | No constituye un criterio verificable | Sustituir por objetivos visuales y de estabilidad concretos |

[G] contiene esas afirmaciones en sus pilares 1–5 y conclusión. Los errores no invalidan todo el esfuerzo: se conservan la búsqueda de entradas del motor, la separación de UI, el reset y la distinción conceptual entre fluidez presentada y CPU. Se retiran garantías absolutas, nombres ambiguos y presets invasivos.

## 4. Hallazgos técnicos priorizados

### NFX-001 — Jitter sin consumidor preparado (P0, C/I)

**Localización:** `NeuralFX.Mod/Source/Rendering/TemporalCamera.cs`, `OnPreCull`; `Source/Config/ModSettings.cs`.

La matriz se modifica antes de comprobar `Bridge.Connected`. La preferencia de jitter es `true`. La misma modificación puede sobrevivir como decisión para un frame cuando `Submit` es rechazado o NGX aún no está listo. Restaurar la matriz después de dibujar no reconstruye la imagen ya renderizada con jitter. [R04, R05, R08]

**Reproducción:** cargar el mod con la DLL del feeder ausente o con el backend desactivado, dejar jitter activo y observar la secuencia de matrices/imagen de la cámara.

**Cambio:** crear un plan de frame antes del culling. Permitir jitter solo si el backend y los recursos de esa cámara están armados para aceptarlo. Cuando no hay consumidor, salida idéntica al juego sin intervención temporal. En un fallo ocurrido después de renderizar un frame con jitter, usar una salida de contingencia explícita y desarmar el siguiente frame; no afirmar que cambiar una variable deshace el frame actual.

### NFX-002 — Escala de MV nativos aplicada al respaldo óptico (P0, C/I)

**Localización:** `TemporalCamera.cs`, construcción de `NativeFrame`; `Native/build.ps1`, parches de `InMVScaleX/Y`.

El frame lleva siempre ancho/alto como escala. El evaluador los utiliza si los metadatos son válidos, aunque el puntero sea cero, la preferencia nativa esté apagada o el recurso nativo no se haya aceptado. La textura óptica queda así asociada a unidades ajenas. [R04, R06]

**Cambio:** seleccionar una estructura indivisible de proveedor que incluya textura, SRV, dimensiones, unidades, escala y frame. Solo tras validar todo el paquete se usa. No tomar escalas del proveedor rechazado. Convertir preferentemente a un formato canónico de movimiento en píxeles y utilizar escala NGX 1 en ese adaptador; Streamline necesita su propia conversión de constantes.

### NFX-003 — Textura nativa y SRV óptico no coinciden (P0, C/I)

El parche sustituye la variable `mv`, pero la llamada a `CopyOrResampleInputs` sigue pasando la vista `mv_srv` original de ReShade. En el camino 100% el upstream copia la textura; cuando la resolución de trabajo cambia, su shader muestrea la SRV recibida. El resultado puede usar diferentes proveedores dependiendo del preset, con escalas incorrectas. [R06, R17, E07]

**Cambio:** crear o retener una SRV sobre el recurso efectivamente elegido, o sobre una textura propia convertida. Comprobar con `GetResource` que vista y recurso corresponden. Probar 100%, 85% y 66%, además de tamaño no múltiplo de dos.

### NFX-004 — Compatibilidad ABI antigua lee fuera del buffer (P0, C y reproducción aislada)

`NeuralFX_SubmitFrame` acepta tamaño 32 o 48 y versión 1 o 2, pero siempre asigna `*input` como una estructura de 48 bytes. Un cliente antiguo de 32 bytes provoca una lectura adicional de 16 bytes. Admite además combinaciones cruzadas de tamaño/versión y NaN en jitter por usar únicamente comparaciones de límites. [R07]

El adjunto `repro/abi_repro.cpp` reproduce solamente esas expresiones y layouts. AddressSanitizer detectó `READ of size 48` fuera de una reserva de 32 bytes. La prueba separada aceptó NaN y ambos pares cruzados. No se usa ese programa como evidencia GPU.

**Cambio:** decodificador versionado y tamaños exactos. Separar tipos V1/V2/V3, inicializar a cero y copiar solo los bytes del tipo admitido. Nuevo export con tamaño de buffer explícito. Rechazar no finitos, dimensiones inválidas y versiones incompatibles. No usar `reinterpret_cast` de un mensaje corto seguido de copia del tipo largo.

### NFX-005 — Puntero de textura sin garantía de frame ni vida útil (P0/P1, C/I)

La textura global se consulta en `OnPreCull`, antes de producir el frame actual, y el puntero se usa después. Ni su cámara productora ni su generación de recursos están identificadas. Hacer `QueryInterface` después no subsana un puntero que ya era inválido. Unity advierte también que obtener el puntero puede sincronizar el hilo de render, por lo que hacerlo cada frame es un riesgo de coste. [R04, R06, E04]

**Cambio:** adquirir recursos propios duraderos al crear/recrear cámara o resolución; copiar/convertir las entradas cuando ya hayan sido producidas; asociarlas a un slot y fence; conservarlas hasta el consumo. No reconstruir la propiedad del frame mediante una caducidad general de dos segundos.

### NFX-006 — Restauración incompleta de estado ajeno (P1, C/I)

Se fuerza anisotropía y un mínimo de LOD global sin snapshot/restauración al apagar. En `OnDisable` se llama `ResetProjectionMatrix`, pero no se restituye el valor personalizado capturado. También se escribe `nonJitteredProjectionMatrix` incluso sin consumidor. [R04]

**Cambio:** registrar qué se adquirió y qué se escribió; restaurar solo lo propio y solo cuando no se haya transferido el control. Separar modos de cámara, jitter y preferencias gráficas globales. No bajar o sobreescribir ajustes de terceros por una restauración indiscriminada.

### NFX-007 — “NGX correcto” no identifica NR ni salida presentada (P1, C)

La confirmación del puente sucede después de enviar la lista, antes de comprobar la copia de vuelta y sin un identificador separado de NR. El panel ya advierte sobre calidad visual, pero la telemetría no diferencia SR/DLAA del consumidor NR. [R06, R09]

**Cambio:** reportar etapas independientes, último error HRESULT/NGX, proveedor y versión; añadir confirmación del consumidor cuando sea observable. Sin una API de NR verificable, mostrar `Portador NGX operativo / NR sin confirmar`, nunca elevar un contador genérico.

### NFX-008 — La UI no está aislada (P1, C/D)

El evento actual entrega metadatos. No captura una escena sin HUD ni invoca el procesamiento NR antes del dibujo de UI. Un panel Colossal evita IMGUI, pero sus píxeles todavía pueden estar en el backbuffer final procesado por ReShade. [R03, R04, R10]

**Cambio:** fase dedicada pre-UI, con una traza de cámaras/pases y pruebas de texto opaco y paneles semitransparentes. Una máscara de rechazo temporal no reemplaza composición de UI.

### NFX-009 — Presets mezclan coste con calidad y no reducen el render Unity (P1, C)

`PipelineConfiguration.Create` modifica `work_resolution`, usa expansión espacial cuando baja del 100% y no crea un render target 3D menor para Unity. El XAML presenta 85%/66% como calidad o rendimiento. [R11, R12]

**Cambio:** nombrar la opción “Resolución de trabajo NR” mientras esa sea su función. Reservar “DLSS SR Quality” para un backend que realmente controle el tamaño de render y el modo SDK.

### NFX-010 — Reordenamiento silencioso de efectos ajenos (P1, C)

El generador coloca todos los efectos ajenos antes de `Lumenite → Feed → CAS`, reconstruyendo también `TechniqueSorting`. Esto modifica la composición del usuario, no solamente los archivos propios. [R11]

**Cambio:** un plan de cambios con diff, inserción anclada y preservación del orden relativo ajeno. Conflictos con efectos pre/post se presentan explícitamente. El respaldo existente no debe utilizarse para justificar alterar todo el preset sin necesidad.

### NFX-011 — La política de confianza NR se debilitó (P0/P1, C)

`BinaryIdentity.ValidateNvidia` calcula `WinVerifyTrust`, pero exige éxito únicamente para SR. Para NR tolera el fallo si puede extraer un certificado con nombre NVIDIA y un ProductName determinado. El catálogo selecciona variantes comunitarias según una clasificación de GPU. Esto no acredita integridad Authenticode del binario modificado. [R13–R16, E08]

**Cambio:** canal estable con firma válida y fuente/distribución autorizadas. Canal experimental separado, opt-in, con procedencia, licencia, hash y advertencia exacta; no llamarlo “firmado por NVIDIA verificado” si la firma no valida. No rebajar seguridad global para hacer pasar el importador. En la RTX 5080 documentada no hay justificación técnica aportada para escoger automáticamente una variante parcheada de otra arquitectura.

Hay además un defecto funcional de importación: cuando `CanAutoDownload=true`, `ImportBytes` compara los bytes de cualquier archivo importado contra el SHA del paquete ZIP. Una DLL legítima importada directamente no coincide con el hash del ZIP. Separar hash de archivo, hash de contenedor y política de entrada. [R15]

### NFX-012 — Detección de GPU y UI no expresan capacidades reales (P1/P2, C)

El Hub toma el primer adaptador NVIDIA del registro; una RTX no reconocida se clasifica como Blackwell. Ese nombre no identifica el dispositivo que renderiza el juego ni prueba soporte de NR. En UI hay anchos fijos, ventana mínima 950×760 y paneles en coordenadas de `Screen` en vez de área UI; deben verificarse en resoluciones pequeñas y DPI alto. [R10, R12, R16]

**Cambio:** identidad del adaptador por LUID del dispositivo activo y capacidades por función, no por regex. Las mejoras visuales se validarán en Windows; los riesgos de layout aquí se derivan del código, no de capturas de navegación reales.

## 5. Arquitectura objetivo: una fuente de verdad y un responsable por etapa

### 5.1. Separación mínima, sin construir otro framework general

Conservar tres productos: mod administrado compatible con el runtime de CS1, biblioteca/addon nativo y Hub WPF. Añadir contratos pequeños, no dependencias obligatorias sobre los otros FX.

```text
Preferencias persistidas / comandos confirmados
                  ↓
         Coordinador de sesión
                  ↓
       Plan de frame por cámara
                  ↓
Captura 3D → normalización de depth/MV/color → backend de reconstrucción
                  ↓
           backend NR elegido
                  ↓
       afilado propio opcional
                  ↓
          composición UI nativa
                  ↓
              presentación
                  ↓
         telemetría de resultado
```

El orden exacto de tonemapping, reconstrucción y NR se fija por el contrato de cada backend, no por este esquema abstracto. Para la ruta actual se puede empezar con color de escena ya tonemapeado, antes del HUD; una futura ruta SR HDR puede requerir un punto anterior y gestionar exposición explícita. No convertir SDR a HDR mediante una etiqueta falsa. [E03]

Separar estado **deseado**, estado **preparado** y estado **efectivo**. La UI consulta el efectivo y muestra el deseado como pendiente mientras llega una confirmación. Un checkbox no equivale a una evaluación satisfactoria.

### 5.2. Límites con los otros FX

NeuralFX será propietario de jitter, reconstrucción, recursos temporales propios, transporte NR, exclusión de UI de esa ruta y diagnóstico. No impondrá exposición, gamma, LUT, color de iluminación, niebla, clima, hora, sombras o LOD global.

Los otros agentes siguen trabajando en SceneFX/LumenFX/AtmosphereFX/ClassicLightFX. Esta implementación no debe hacer commits allí. Cuando esos módulos cambien parámetros visuales, NeuralFX podrá recibir una notificación opcional de cambio de historia mediante un contrato acordado; si no existe, usará detección prudente de cambios grandes sin invadir sus modelos.

Un único cambio de exposición no debe causar un reset completo cada frame. Cambios suaves de día/noche se resuelven con exposición coherente y temporales; saltos grandes, cambio de LUT o teletransporte sí pueden solicitar reset según el backend. Registrar la razón, agrupar eventos en el mismo frame y comprobar que no haya tormentas de resets.

### 5.3. Archivos a tocar y archivos propuestos

Los nombres marcados **nuevo** son una organización propuesta, no clases ya existentes:

| Componente | Archivo | Trabajo |
|---|---|---|
| Cámara | `NeuralFX.Mod/Source/Rendering/TemporalCamera.cs` | Dejar de resolver todo en PreCull; aplicar únicamente un plan aprobado |
| Puente | `.../Rendering/NativeBridge.cs` | Negociación, ABI versionada, estados y envío sin recursos GPU por MMF |
| Coordinación | `.../Rendering/FrameCoordinator.cs` **nuevo** | Epoch, frame tokens, disponibilidad y cambios de modo |
| Proyección | `.../Rendering/ProjectionLease.cs` **nuevo** | Captura/restauración acotada por cámara y propietario |
| Recursos | `.../Rendering/EngineInputProvider.cs` **nuevo** | Texturas propias, captura después de producción, formato y lifecycle |
| Orden | `.../Rendering/RenderStageProbe.cs` **nuevo**, solo diagnóstico | Traza de cámaras/eventos y validación pre-UI |
| Composición | `.../Rendering/UiCompositionController.cs` **nuevo** | Punto pre-UI y ruta alternativa de composición |
| Configuración | `.../Config/ModSettings.cs` | Schema nuevo, migración segura, separar modo y valor |
| Nativo | `Native/neuralfx_bridge.h` | Decodificación compatible; después dividir implementación de almacenamiento del contrato público |
| Nativo | `Native/neuralfx_inputs.*` **nuevo** | Proveedor elegido, conversión y retención de recursos |
| Nativo | `Native/neuralfx_backend.*` **nuevo** | Adaptador del backend seleccionado y prueba de capacidades |
| Build | `Native/build.ps1` | Mantener patches reproducibles; modificar también SRV y estados, no solo puntero |
| Pruebas | `Native/bridge_tests.cpp` | Casos adversariales reales del contrato |
| Hub | `Services/BinaryIdentity.cs`, `DependencyManagerService.cs` | Confianza, importación y variantes explícitas |
| Hub | `Services/HardwareDiagnosticsService.cs` | Identidad efectiva y capacidades por función |
| Hub | `Services/PipelineConfiguration.cs` | Presets veraces y orden preservado |
| Hub | `MainWindow.xaml`, `.xaml.cs` | Vista basada en estado; separar acciones de instalación y sesión |
| UI juego | `UI/TelemetryPanel.cs`, `Options/OptionsPanel.cs` | Controles cotidianos y diagnósticos consistentes |
| Evidencia | `docs/VALIDACION.md`, `ARQUITECTURA.md`, `README.md` | Regenerar manifest de capacidades y resultados de la revisión actual |

## 6. Implementación paso a paso del núcleo

### 6.1. PR-00: congelar baseline y sanear el significado del producto

**Objetivo:** que cada diagnóstico se refiera a una combinación reproducible.

1. Registrar `git rev-parse HEAD`, cambios locales, hash del mod instalado, bridge, Hub, SR, NR, consumidor, ReShade y shaders. No sobrescribir trabajo sin commit de otro agente.
2. Crear un documento `docs/audit/2026-09-08/baseline.json` con hash de archivos, ABI, driver, adapter LUID, CS1/Unity/API y modos activos. No incluir datos personales de rutas sin consentimiento; usar rutas relativas y alias.
3. Mantener separados `sourceCommit`, `buildId`, `installedHash`, `loadedModuleHash`, `evidenceRunId`. Una DLL en disco puede diferir de la ya cargada.
4. Corregir README sobre jitter/MV y auto-descargas. No borrar evidencia histórica: moverla a un bloque con su revisión y tuple de versiones.
5. Añadir un manifiesto de capacidades generado por build. El mismo identificador debe aparecer en Hub, bridge y mod.
6. Identificar un baseline de recuperación conocido, aunque sea “transporte NR sin integración temporal nueva”. No etiquetarlo como paridad visual aprobada.

**Salida:** baseline trazable, ningún archivo del juego modificado. **Aceptación:** cada fila de evidencia identifica exactamente el binario y la configuración probada.

### 6.2. PR-01: modo seguro, restauración y migración

**Cambios de configuración propuestos:** schema 3 o superior, con campos explícitos `PipelineEnabled`, `TemporalMode`, `MotionProviderPreference`, `UiIsolationPreference`, `ExperimentalOptIn`. No se exige que estos nombres coincidan con un SDK.

- Al migrar preferencias antiguas sin consentimiento experimental, dejar **jitter y selección nativa automática desarmados** hasta completar las comprobaciones. Conservar el valor previo como preferencia importada, no ejecutarlo sin evaluar compatibilidad.
- Quitar `EnhanceTextureClarity` del camino DLSS habitual. Conservar una migración que explique el ajuste anterior y permita devolverlo de forma segura cuando exista snapshot fiable.
- Si no existe snapshot del LOD global antiguo, no inventar un “valor vanilla”; informar y permitir restaurar desde una referencia del usuario/juego.
- El modo global apagado detiene la intervención por cámara, el feeder propio y el afilado propio. No apaga ReShade entero ni borra el preset ajeno.
- No cambiar proyección, `nonJitteredProjectionMatrix` ni calidad global cuando el backend esté desconectado.

**ProjectionLease:** guardar proyección original, proyección sin jitter previa y, solo si se cambia, matriz de culling. Guardar identidad y epoch de cámara. Aplicar cambios exclusivamente dentro del intervalo de render. Restaurar en PostRender y en cleanup de fallback usando el valor capturado, no `ResetProjectionMatrix` como sustituto de un valor personalizado. Si otro mod escribió después, usar comparación con la última matriz propia y el contrato de propiedad; registrar conflicto en vez de pisarlo.

**Depth flags:** hacer OR sobre los requeridos; no restablecer a un valor global que quite flags solicitados por otros efectos. Registrar flags añadidos y retirarlos solo con evidencia de propiedad exclusiva; cuando no sea posible demostrarla, es preferible dejar un flag compartido solicitado que romper otro efecto, informando del límite.

**Aceptación:** con feeder ausente, pipeline Off o handshake rechazado, las matrices permanecen iguales durante 1.000 frames de fixture. Apagar después de usarlo devuelve el control. Probar cierre durante PreCull, recreación de cámara y cambio de resolución.

### 6.3. PR-02: ABI segura y negociada

Diseño propuesto para una nueva exportación interna:

```cpp
// Nombres internos propuestos, NO una API de NVIDIA.
int NeuralFX_GetCapabilities(void* output, uint32_t outputBytes);
int NeuralFX_SubmitFrameV3(const void* input, uint32_t inputBytes);
int NeuralFX_GetFrameResult(void* output, uint32_t outputBytes);
```

No publicar structs con `bool`, `size_t`, enums de ancho implícito o punteros dependientes de arquitectura dentro de mensajes persistidos. Usar enteros de tamaño fijo, layout documentado y pruebas de offsets en C# y C++. Las direcciones GPU solo existen dentro del proceso nativo; no viajarán por el canal Hub/MMF.

**Decodificador:** comprobar primero que hay bytes para una cabecera mínima; copiarla a una estructura local; verificar magic, versión y tamaño exacto; después copiar el tipo correspondiente. Un mensaje V1 real de 32 bytes se convierte a un modelo interno con puntero cero y sin capacidades temporales. V2 exige exactamente 48. Rechazar pares cruzados. V3 no reinterpreta memoria V1/V2 como si fuera su tipo completo.

**Validaciones:** floats finitos, dimensiones positivas dentro del límite de textura y backend consultado, flags reservados a cero, frame token válido, jitter en rango admitido, escala finita positiva cuando el contrato la requiera, identidad de dispositivo y epoch actual. El ABI no puede probar que cualquier dirección arbitraria del proceso apunte a memoria válida: por eso los recursos deben registrarse por un mecanismo propio, no aceptar direcciones libres desde el Hub.

**Compatibilidad:** cambiar el identificador `.3` del bridge al introducir el ABI nuevo. Un C# nuevo con bridge viejo debe negociar una ruta no temporal o rechazarla limpiamente; nunca activar jitter porque encontró tres exports por nombre.

**Pruebas:** 0 bytes, cabecera truncada, V1-32, V2-48, tamaños cruzados, V3 correcto, NaN, Inf, dimensión negativa reinterpretada, flags desconocidos, reset serial wrap, token obsoleto, cliente viejo/bridge nuevo y viceversa. Ejecutar ASan o instrumentación equivalente en la build nativa que lo permita.

### 6.4. PR-03: proveedor MV indivisible y fallback coherente

Crear internamente un descriptor semejante a:

```text
SelectedMotionInput
  Resource / SRV retenidos
  CameraId + FrameId + ResourceEpoch
  Width + Height + Format + SampleCount
  Direction: PreviousMinusCurrent / CurrentMinusPrevious
  Units: UV / Pixels
  ContainsProjectionJitter: sí/no
  ValidCoverage y motivo de rechazo
  BackendScale, calculada DESPUÉS de seleccionar
```

No exponer estos enum como interfaz pública de terceros; son normalización interna.

**Secuencia obligatoria:**

1. Resolver candidato nativo del frame y cámara solicitados.
2. Validar identidad de dispositivo, dimensiones, formato, muestra única y vida útil.
3. Convertir, si hace falta, a textura propia RG16F y SRV coherente.
4. Si cualquier paso falla, descartar candidato y sus escalas. Elegir óptico completo o bypass según política y capacidades.
5. Generar constantes del backend desde el proveedor finalmente elegido.
6. Tanto la copia 100% como el resample 85%/66% consumen ese mismo descriptor.
7. Al cambiar proveedor o unidades, solicitar reset y reiniciar la historia de ese dominio.

**Convención interna sugerida:** `motionPx = (uvPrevious - uvCurrent) * inputSize`. Es una decisión del contrato propuesto, no una afirmación de que Unity entregue ya ese signo. El manual Unity 5.6 describe UV desde el frame anterior al actual; verificar el signo real del shader/matriz usado en CS1 y convertir una sola vez. Para NGX configurar la escala acorde al buffer ya convertido; para Streamline normalizar sus constantes según su guía, no copiar mecánicamente `InMVScaleX/Y`. [E03, E05]

**Prueba decisiva de SRV:** alimentar el candidato nativo con movimiento conocido `(1,0)` y el óptico con `(0,2)` en un fixture. Leer una pequeña sonda diferida de la textura final entregada, tanto a 100% como a 66%/85%. Tiene que reflejar el proveedor seleccionado, con la escala calculada para esa resolución. Esta prueba descubre la rama que las comprobaciones actuales de puntero no cubren.

**UI:** mostrar `Movimiento: Unity verificado`, `Óptico` o `No válido`, y debajo el motivo cuando haya fallback. Nunca `Nativo` simplemente porque una preferencia esté activada.

### 6.5. PR-04: captura de entradas del mismo frame

No resolver el puntero global en PreCull como garantía del recurso que será producido después.

**Implementación inicial recomendada:**

1. En el descubrimiento de cámara, identificar su rendering path y trazar los eventos disponibles en la build real de Unity. No asumir que toda instalación reproduce el pipeline estándar.
2. Crear un pequeño anillo de texturas propias por cámara/resolución. Usar formatos admitidos después de consultar el dispositivo. Adquirir sus punteros nativos en creación/recreación, no en cada frame.
3. Insertar un command buffer después de que estén disponibles profundidad y vectores del frame actual, con copias/conversiones GPU a esas texturas propias. Si el evento exacto no garantiza ambos, usar etapas separadas y publicar el paquete solo cuando coincidan sus tokens.
4. Emitir el evento nativo después de las copias. El evento ve recursos propios identificados por handle de registro, frame y epoch.
5. Añadir una fence o consulta de finalización por slot y no reutilizarlo hasta que el consumidor deje de necesitarlo. El tamaño del anillo se calcula y limita; no crecer sin fin ante GPU atrasada.
6. El recurso de Unity puede cambiar de identidad al redimensionar o cambiar escena. Subir epoch, invalidar la historia y diferir destrucción hasta finalizar el trabajo pendiente.
7. Durante desarrollo, añadir sentinelas o pequeñas sondas de debug que acrediten que cámara, profundidad, movimiento y color pertenecen al mismo frame. Retirar readbacks costosos del modo normal.

**Cobertura de movimiento:** ensayar por separado cámara sobre edificios estáticos, vehículos, ciudadanos, trenes, árboles animados, agua y efectos transparentes. Solicitar el flag Unity no garantiza la cobertura de todos los shaders personalizados de CS1. Si una clase de objeto no tiene datos útiles, implementar una ruta de movimiento específica o una máscara de invalidez/fallback; no presentar movimiento de cámara reconstruido desde depth como movimiento de los vehículos.

La reconstrucción por matrices de cámara y profundidad es una alternativa útil para el mundo estático. Guardar view-projection actual/anterior sin jitter, reconstruir posición y reproyectar. Debe declararse `CameraOnly` cuando no cubra objetos móviles. Para estos, valorar un pase dedicado basado en matrices anteriores y geometría visible; medir coste antes de convertirlo en obligatorio.

### 6.6. PR-05: integración temporal correcta

Solo empezar esta fase con PR-03 y PR-04 aceptadas.

**Plan por frame:** `FrameId`, cámara, input/output extents, fase jitter, amplitud, epoch de historia, método de culling y backend aceptante. Avanzar la secuencia por cuadros realmente renderizados de esa cámara; decidir explícitamente qué ocurre ante un frame saltado. No asociar ciegamente la fase a `Time.frameCount & 15` para todas las cámaras y consumidores.

**Jitter:** convertir desplazamiento en píxeles a la proyección adecuada al render target. Confirmar orientación vertical, matriz CPU/GPU y signo del contrato. Mantener culling estable o ampliado de manera justificada para que un desplazamiento subpíxel no haga desaparecer geometría del borde. No alterar proyección UI. Los vectores deben excluir o incluir jitter exactamente como declara el adaptador, nunca dos veces.

**Resolución de trabajo:** si Unity sigue renderizando a un tamaño y el feeder reduce entradas, expresar el jitter en píxeles del tamaño que el backend interpreta. No pasar el jitter de un buffer a otro sin conversión. Evitar jitter sintético del resample del feeder cuando ya existe jitter de cámara. Debe haber un solo generador temporal activo.

**Reset:** camera cut, cambio de cámara, FOV grande, resolución/formato/proveedor, cambio de backend o epoch y invalidación de exposición según contrato. Conservar reset pendiente hasta una evaluación exitosa de la historia correcta. Separar `requested`, `recorded`, `submitted` y `completed`. En apagado no invocar un consumidor ya destruido.

**Salida ante fallo:** mantener una copia del color de entrada de este frame, no mostrar una textura antigua como si fuera actual. Si el frame está desplazado por jitter y no se puede reconstruir, aplicar una resolución espacial de contingencia explícita y apagar jitter en el siguiente plan. Evaluar sus bordes en pruebas; no prometer equivalencia perfecta con un frame originalmente sin jitter. En fallo de dispositivo, no esperar indefinidamente ni seguir enviando NGX.

**Aceptación:** cámara quieta, paneo lento/rápido, zoom y teletransporte sin doble jitter; ausencia de deriva persistente; ningún frame reusa metadata vieja; captura A/B con misma escena y contadores de evaluación por cámara.

### 6.7. PR-06: separar la UI de la inferencia

Esta fase es prioritaria para usabilidad, pero su activación depende de la traza de render, no del nombre del evento `AfterEverything`.

**Ruta A, preferida para conservar la infraestructura actual:** procesar efectos propios en el punto pre-UI mediante el runtime ReShade. Su API 6.8 contiene `render_effects` y documenta que previene la repetición normal antes de Present. Esto habilita una estrategia; no demuestra que el punto actual de CS1 sea el correcto. [E06]

Pasos:

1. Instrumentar cámaras activas, depth/order, target texture, eventos, cambios de RTV y momento de dibujo del HUD. Incluir ventanas del juego y de mods, info views, cursor, tooltips, paneles semitransparentes y marcadores del mundo.
2. Elegir un target de escena completamente dibujada y todavía sin la UI que se quiere proteger. No identificarlo solamente como `Camera.main` o solamente como `UIView.uiCamera`.
3. Ejecutar una prueba de identidad: copia entrada→salida sin DLSS y sin NR. Verificar que no cambia color, gamma, formato ni profundidad y que la UI sigue intacta.
4. Registrar el callback en el hilo/contexto correcto. Relacionar el runtime ReShade con el HWND/swapchain/dispositivo del juego, no con una ventana auxiliar o Present generado.
5. Guardar y restaurar todo el estado D3D11 tocado. No llamar al procesamiento mientras su input siga enlazado como output incompatible. Crear SRV/RTV coherentes con el formato y sRGB del contrato.
6. Ejecutar una vez la cadena propia y marcarla como consumida para ese frame. Detectar reentrada, render de cámara secundaria y un segundo intento en Present.
7. Después dibujar UI a resolución de salida, sin jitter ni CAS propio sobre sus píxeles. Los efectos ajenos que deban permanecer después requieren una política explícita; no adelantar todos indiscriminadamente.
8. Si el target o runtime no cumplen, volver a bypass seguro y mostrar `UI isolation unavailable`, no encender un indicador verde por haber encontrado una cámara.

**Ruta B:** render de escena a textura propia y UI compuesta posteriormente. Se usa si no hay un punto pre-UI estable para la ruta A. No duplicar el mundo renderizándolo dos veces por defecto. Conservar clear flags, viewport y aspectos especiales de las cámaras. Si UI se captura aparte, conservar alfa y composición premultiplicada o recta de forma coherente; no hacer una resta de screenshots para deducir alfa.

**Máscaras:** `pInBiasCurrentColorMask` es una entrada temporal y no una operación “estos píxeles no pasan por la red”. Si el backend NR elegido expone una máscara de control artístico, documentarla separadamente, con sus unidades y semántica. Aun así, una UI realmente compuesta después tiene una garantía de arquitectura superior para texto. [R17, E01]

**Prueba de aceptación:** comparar regiones opacas de UI con NR Off/On en una escena congelada; deben conservar los mismos píxeles salvo cambios legítimos de animación. Para fondos semitransparentes comparar el resultado esperado de composición sobre cada escena de fondo, no exigir igualdad del fondo que legítimamente cambió con NR. Abrir/cerrar modal y mover cursor sin afectar la historia de la ciudad.

### 6.8. PR-07: confirmar NR, no solamente su portador

Construir una interfaz interna pequeña de backend:

```text
Probe(device, versions) -> Supported / Unsupported / Unknown + reason
Prepare(inputContract, outputContract) -> prepared generation
Evaluate(frameInputs, settings, history) -> recorded result
ReadCompletedResult(frameToken) -> stage results + timings
SetEnabled / RequestReset / Release
```

Son operaciones semánticas propuestas. Su implementación llama a las APIs realmente presentes y licenciadas; no deben aparecer botones que escriban claves inventadas.

Para el feeder actual, distinguir transporte, evaluación DLAA y consumidor RenoDX. Si el consumidor no publica un resultado fiable por frame, añadir un adaptador verificable mediante su API/log versionado cuando exista, o usar un backend alternativo que sí lo permita. Un log de arranque “loaded” no basta. Los logs son diagnóstico complementario; no escanear archivos grandes en cada frame.

Registrar `NR unknown` cuando solo está confirmado el portador. El usuario puede experimentar bajo esa etiqueta, pero la build no supera la puerta “NR demostrado” hasta disponer de evidencia del componente o instrumentación inequívoca, más una captura donde su salida llegue al target correcto.

El reset de una historia no se confirma por el éxito de otra. Exponer último frame enviado/completado, código de fallo y razón de bypass. No mezclar contadores de sesiones anteriores.

## 7. Alternativas de implementación y experimento de selección

No elegir una alternativa solo por su nombre o por funcionar en otro juego. Todas deben pasar la misma escena, entradas, resolución, backend de color y pruebas de HUD. No cargar dos propietarios del procesamiento neural a la vez.

### Ruta A — Consolidar Feeder + consumidor actual

**Prioridad inmediata.** Es la única combinación de este proyecto con evidencia documentada de NR a 4K, aunque no en ciudad. Conservar el consumer 3.3.4 como control experimental y ensayar después el tuple actual con SR 310.9.1. No asumir que el número 3.3.4 demuestra superioridad universal: es el baseline de esa prueba. [R02, R14]

La ventaja es reutilizar transporte y controles propios. Su límite es el acoplamiento a un consumidor que intercepta NGX y cuya confirmación puede no estar suficientemente expuesta. Corregir primero ABI, proveedor de entradas, sincronización y UI. Si sus límites impiden verificar NR o funcionar antes del HUD, documentar la restricción y pasar a una alternativa con el mismo contrato de entradas.

### Ruta B — Integración NR mediante SDK/Streamline verificado

**Objetivo de mayor control a medio plazo**, no promesa de que basta con agregar una DLL. NVIDIA documenta la vía Streamline para NR. La guía general requiere inicialización muy temprana, antes de las APIs gráficas, y consultas por adaptador. Un mod C# cargado cuando el dispositivo Unity ya existe no satisface automáticamente ese requisito. [E01, E03]

**Spike obligatorio y acotado:**

1. Obtener el SDK/plugin NR y sus condiciones por un canal autorizado. Registrar versión, headers y documentos disponibles.
2. Construir fuera de CS1 un host mínimo con el dispositivo/API que realmente admite ese plugin. Verificar entrada, salida, controles, formatos y necesidad de un application/project identifier legítimo. No suplantar la identidad de otro juego ni renombrar RR como NR.
3. Examinar inicialización manual admitida o un bootstrap nativo temprano para CS1. Evitar instalar un segundo proxy `dxgi.dll` encima de ReShade. Resolver la coexistencia explícitamente; si hace falta reemplazar el mecanismo de bootstrap, hacerlo en la rama experimental con instalación reversible.
4. Si NR requiere una sesión D3D12 para el adaptador elegido, conservar transporte D3D11↔D3D12 en el mismo adaptador. Esto no convierte Unity a DX12 ni exige portar todo el motor.
5. Conectar la captura pre-UI propia al backend, usando la API real del SDK. Reutilizar frames/epoch/recursos y no el hook del consumidor anterior.
6. Medir control, estabilidad y coste frente a Ruta A. Promover únicamente cuando supere sus puertas de aceptación.

**Criterio de abandono de ese spike:** faltar headers/licencia/API admitida, exigir un contrato no reproducible o fallar inicialización segura. En ese caso no se finge la integración ni se deja al proyecto detenido: continúa la Ruta A corregida o se prueba Ruta C.

### Ruta C — `renodx-dlss` de ShortFuse como backend alternativo

El README actual del feeder señala que `renodx-dlss` puede sustituirlo para juegos x64 DX9/11/12 y advierte que no debe coexistir con el feeder. **No es el mismo archivo que `renodx-dlss5` del tuple actual.** Esa indicación es del mantenedor del feeder, no una validación de CS1 realizada aquí. [E09]

**Experimento:** instalar en carpeta de prueba/restauración independiente, un solo backend, sin activar jitter de NeuralFX hasta comprobar cómo ese backend recibe entradas y proyección. Registrar si permite escena pre-UI, vectores Unity, profundidad, controles de NR y resultados por frame. Si solo procesa el backbuffer final con entradas sintéticas, puede ser una alternativa rápida de apariencia, pero no cumple por sí sola el objetivo de integración temporal y UI protegida.

La decisión no debe basarse solo en menos archivos. Una alternativa más pequeña que no acepte entradas correctas puede ser peor que el puente propio corregido.

### Ruta D — DLAA/SR nativo con NGX D3D11 y NR separado

Puede aislar y mejorar antialiasing/reconstrucción, pero **un evaluate NGX D3D11 de DLAA no activa por sí solo un consumidor que intercepte NGX D3D12**. Sería una arquitectura de dos etapas, con NR directo o un transporte compatible posterior. Mantener una sola reconstrucción temporal; no hacer DLAA en Unity y repetir otro DLAA con distinto jitter por accidente.

Estudiarla solo si el coste/control de la reconstrucción portadora actual es una limitación medida. No denominarla “DLSS 5 completo” hasta conectar y verificar NR.

### Frame Generation: experimento independiente

Para el equipo RTX 5080 documentado, probar primero **NVIDIA Smooth Motion**, como alternativa del driver para juegos compatibles DX11, y compararlo contra LSFG cuando esté disponible. NVIDIA documenta esa función para RTX 40/50; no garantiza que toda combinación con mods, overlays y versión del feeder funcione perfectamente. Empezar con FG apagado y activarlo después de validar el resultado NR. [E10]

Medir FPS renderizados, FPS presentados, latencia, uso/tiempo GPU y artefactos del HUD. No acumular Smooth Motion, LSFG y otra generación de cuadros. Si hay dos presentaciones por frame, NeuralFX debe ejecutar NR una sola vez por frame real, no por cada Present auxiliar.

OptiScaler tampoco se añade encima del feeder como una mejora acumulativa. El propio feeder advierte conflicto sobre la misma ruta de escalado. Tratarlo como una ruta alternativa con sus requisitos de entradas e integración. Un puntero MV no sustituye el manejo de presentación, UI y pacing. [E09]

### Lo que no recomiendo como primera respuesta

No portar CS1 entero a otra versión de Unity, no forzar DX12 para “hacerlo moderno”, no exigir una segunda GPU y no usar DLDSR+NR a 6K como punto de partida. Ninguna de esas operaciones corrige automáticamente el frame incorrecto, las unidades de MV o la composición de UI.

DLDSR y supersampling pueden permanecer como experimentos de calidad después del baseline. En un monitor 4K, un factor de área 2,25 multiplica por 1,5 ambas dimensiones, llegando a 5.760×3.240; son 2,25 veces los píxeles de entrada. La compatibilidad y el coste de NR a ese tamaño deben consultarse/medirse, no extrapolarse de la prueba 4K.

## 8. Interfaz in-game: controles reales, no una consola disfrazada

### 8.1. Diseño funcional propuesto

Una ventana nativa redimensionable con encabezado de estado, área principal corta y un bloque avanzado plegable. No obligar a abrir Hub o ReShade para las acciones normales. Conservar el acceso avanzado de ReShade para experimentación, sin convertirlo en requisito para saber si NR está activo.

**Encabezado:** `NeuralFX`, versión de sesión, botón global **Desactivar efectos propios** y estado textual. Verde solamente para el nivel realmente demostrado; el color no será la única señal.

**Área principal:**

| Control | Comportamiento exacto |
|---|---|
| Renderizado neural DLSS 5 | Toggle del backend NR confirmado. Deshabilitado con explicación si la API no permite control o faltan dependencias |
| Antialiasing/reconstrucción | Lista de modos efectivamente disponibles: Juego, DLAA, DLSS SR por modos consultados. No mostrar opciones operativas falsas |
| Resolución 3D | Valor real de la cámara; editable únicamente con DRS/SR nativo implementado |
| Resolución de trabajo NR | Valor real del addon/backend, con aviso de que no necesariamente reduce trabajo Unity |
| Conservar apariencia del juego | Preset de parámetros NR documentados por ese backend, sin tocar gamma/LUT de los otros FX |
| Intensidad de estructura/tono | Solo si el backend expone esos controles y sus límites se verificaron. No inventar valores o rangos universales |
| Nitidez | Un único afilado propio, apagable, posterior a reconstrucción y anterior a UI |
| Comparar A/B | Comparar la entrada y salida del mismo frame en el mismo punto del pipeline; no alternar escenas separadas |
| Restablecer historial | Acción explícita con confirmación solicitada/completada y razón |

Un perfil `Conservar apariencia` puede priorizar intensidad tonal nula cuando ese backend la defina así, y una intensidad estructural calibrada. No garantiza preservar exactamente todos los colores si el consumidor usa otra semántica. Los nombres “Natural”, “Fotografía” o “Cinemático” serán presets reales con parámetros versionados, no pestañas nuevas.

**Pie de diagnóstico compacto:** fuente MV efectiva, UI protegida sí/no/desconocido, tamaño input→work→output, última evaluación NR confirmada y tiempo GPU cuando esté medido. Un indicador `No medido` es mejor que un cero inventado.

### 8.2. Estados concretos y mensajes

- `Desactivado por el usuario`: no debe quedar jitter ni imposición de LOD.
- `Preparado; esperando ciudad`: hardware/dependencias verificadas, sin declarar ejecución.
- `Capturando entradas`: probando el contrato de esa cámara.
- `DLAA operativo; NR sin confirmar`: el portador funciona, consumidor no confirmado.
- `NR activo; UI no aislada`: modo experimental claramente visible.
- `NR activo; UI protegida`: solo después de validar la ruta pre-UI.
- `Respaldo óptico`: funciona otra fuente, no equivale a MV nativos.
- `Bypass por error`: mostrar componente, código y acción útil, preservando el control del juego.

Estos textos deben derivarse de un mismo reducer/modelo de estado compartido entre interfaz nativa y Hub. No mantener lógica contradictoria en tres pantallas.

### 8.3. Implementación de controles

Reutilizar `TelemetryPanel` como entrada, pero separar `SessionViewState` de los widgets. Los handlers emiten comandos al coordinador, no escriben INI ni manipulan dispositivos gráficos desde UI. Actualizar el texto solo cuando cambie su valor significativo; métricas a frecuencia moderada y gráficos con buffer limitado.

Usar dimensiones del `UIView` y su escala para reposicionar. En cada apertura y cambio de resolución, comprobar límites; recordar posición normalizada o convertida al espacio UI. No usar `Screen.width` como si fuera la anchura del panel escalado.

Añadir scroll real en tamaños reducidos, cierre accesible, foco de teclado, tooltips con explicación, entrada numérica que tolere formato de cultura local y validación sin destruir el texto a mitad de edición. Los atajos no deben activarse al escribir en campos, buscar assets o renombrar una ciudad; permitir reasignarlos y detectar conflicto.

Mantener idioma coherente. Sustituir símbolos dependientes de fuentes por sprites disponibles o texto cuando su renderizado no esté verificado. No afirmar que todos los Unicode fallan: se prueban en la fuente y escala reales.

### 8.4. Comparación A/B implementable

Guardar textura pre-NR y post-NR del mismo frame a igual resolución y codificación de color. En modo partido, combinar antes de UI usando una coordenada normalizada y aplicar la misma composición UI después. Para evitar interpretar jitter o una exposición diferente como mejora NR, no reconstruir A con otra cámara.

Dos comparaciones diferentes: `NR Off/On con la misma reconstrucción` y `Reconstrucción Off/On con NR Off`. No cambiar simultáneamente NR, CAS, LOD, LUT y resolución en una demostración.

Captura de evidencia guarda ambas imágenes, configuración efectiva, frame/cámara, hashes, timestamp, provider y medidas. Las capturas son opt-in y locales; no incluir partidas ni archivos personales en un paquete diagnóstico sin autorización.

## 9. Menú de opciones de CS1

El menú no necesita duplicar el panel de render. Su función principal es preferencias persistentes y preparación fuera de una ciudad.

**Grupo Estado:** instalación/carga/sesión separadas, GPU/API cuando estén disponibles y acción para abrir detalle. Actualizar al entrar en el menú y ante cambios; no dejar el diagnóstico congelado desde `Build`.

**Grupo Inicio:** modo al cargar ciudad, última configuración o perfil explícito, comportamiento seguro si falta backend. Por defecto no procesar el menú principal para sumar evaluaciones irrelevantes al objetivo; registrar “sin ciudad” claramente.

**Grupo Interfaz:** idioma, escala/tamaño, posición restablecible, UUI y atajo. Aplicar cambios inmediatamente cuando sea seguro, con mensaje cuando requieran reabrir el panel.

**Grupo Herramientas:** abrir Hub, abrir panel cuando exista sesión, exportar diagnóstico y restablecer preferencias propias. Si falta el ejecutable del Hub, mostrar su estado y una acción explicativa; no limitarse a un aviso en log.

**Avanzado experimental:** proveedor preferido, visualización de depth/MV, trazas y acceso a pruebas. Activar la preferencia no marca la capacidad como efectiva. Un backend incompatible impide la activación y explica exactamente el motivo.

**Regla:** los errores de guardado deben aparecer junto al ajuste con reintento; mantener estado pendiente hasta éxito. Separar almacenamiento de preferencias del historial GPU. No borrar shaders ni cambiar la instalación desde una opción corriente del juego.

## 10. Hub: conservar la solidez, reducir ambigüedad

### 10.1. Navegación propuesta

Tres destinos principales: **Inicio**, **Instalación**, **Diagnóstico**. Configuración de sesión se muestra en Inicio cuando hay ciudad. Componentes y logs quedan dentro de las pantallas correspondientes, no como una obligación para todo uso.

**Inicio** responde a tres preguntas: qué está instalado, qué funciona ahora y qué falta. Mostrar una acción principal contextual: preparar componentes, revisar instalación, iniciar prueba de ciudad o abrir controles de sesión. No usar un único semáforo para archivos, hardware y NR.

**Instalación** conserva la tabla avanzada y toda la maquinaria transaccional. El flujo es `Seleccionar juego → Elegir backend/tuple → Revisar plan → Aplicar → Verificar → Probar en juego`. Descargar no significa instalar y ambas operaciones se muestran separadas.

**Diagnóstico** concentra capacidades reales, identidad de adapter, ABI, errores por componente, integridad, versión de cada pieza, historial de pruebas y exportación. Los logs ofrecen filtro por componente/severidad y búsqueda sin perder la posición de lectura al llegar nuevas líneas.

### 10.2. Compatibilidad por función

Sustituir el booleano global `SupportsDLSS` por una matriz:

```text
AdapterId / LUID, VendorId, DeviceId, driver
SR: Supported / Unsupported / Unknown
DLAA: ...
NR: ...
NativeMVInput: ...
PreUI: ...
FrameGeneration: External / Integrated / Unavailable / Unknown
```

La prueba autoritativa se hace en el dispositivo que renderiza la sesión. La detección del Hub con el juego cerrado es un diagnóstico preliminar. En portátiles/múltiples GPU, diferenciar adaptador de pantalla, adaptador de render y adaptador de cómputo. No clasificar una RTX desconocida como Blackwell.

Memoria de vídeo: separar capacidad instalada de presupuesto y consumo actual; preferir consultas del adaptador/dispositivo. La información del registro puede servir como fallback marcado, no como certificación.

### 10.3. Perfiles y cambios de sesión

Renombrar presets actuales como `NR trabajo 100%`, `NR trabajo 85%`, `NR trabajo 66%`, con dimensiones reales y efecto esperado sobre coste del addon. No llamarlos porcentaje de calidad. El usuario que seleccione 85% debe ver la resolución de cámara sin cambios si esa es la realidad.

Separar `BackendProfile` —versiones y mecanismo— de `VisualProfile` —parámetros—. Cambiar nitidez no reinstala dependencias. Cambiar backend requiere juego cerrado, revisión del plan y rollback. Durante juego solo se admiten comandos declarados seguros por el backend y con confirmación.

No inferir que aplicar un INI en disco cambia una DLL ya cargada. Mostrar `Requiere reiniciar juego` cuando corresponda. Los cambios pendientes deben conservarse o cancelarse explícitamente.

### 10.4. Descarga, procedencia y confianza

Mantener hashes de paquete y de cada archivo como dos campos diferentes. Para ZIP, validar hash del contenedor y de sus archivos; para DLL importada directamente, validar archivo contra su política, identidad y firma, sin compararla con hash del ZIP.

Catalogar: autor del componente, distribuidor, URL de origen, licencia, versión, archivo esperado, SHA, tipo de firma, resultado de validación, arquitecturas realmente ensayadas, ABI, incompatibilidades y rango de driver probado. Un espejo comunitario no se convierte en web oficial de NVIDIA porque el archivo contenga su nombre.

Canal estable: exigir Authenticode válido para **ambos** runtimes NVIDIA cuando esa sea su política de confianza. Una firma ausente, inválida o con digest incorrecto no se normaliza a éxito. Gestionar separadamente una validación no concluyente por condiciones del sistema/red y explicar el resultado, sin confundirlo con un archivo íntegro.

Un canal experimental no debe activarse por regex de GPU. Necesita consentimiento informado y condiciones de distribución verificadas. No auto-descargar variantes alteradas sin distinguirlas. Los hashes limitan qué archivos entran, pero no sustituyen la autenticidad ni el permiso de redistribución.

### 10.5. Integridad del preset ReShade y recuperación

Antes de modificar un INI, generar un diff legible de claves y técnicas. Conservar técnicas desconocidas y su orden relativo. Detectar duplicados y decidir su semántica según el parser real en pruebas; no mezclar lectura “primera clave” y ejecución “última clave” sin advertir.

Gestionar un bloque lógico de técnicas propias con anclas, no reescribir la cadena completa. No mantener Lumenite calculando movimiento inútil si está seleccionado un proveedor nativo estable y la ruta ya no lo necesita; tampoco retirarlo antes de disponer de un fallback permitido.

Reutilizar snapshots, diarios y confinamiento de rutas existentes. Verificar juego cerrado antes de cambiar DLL, evitar rutas con redirecciones no autorizadas y mantener vista previa/confirmación para desinstalación. La UI debe explicar qué se restaura y qué queda; no prometer limpiar caches del driver o archivos externos que no son propiedad de NeuralFX.

### 10.6. Tamaño, accesibilidad y rendimiento WPF

Refactorizar el layout de acciones horizontales para que se apile cuando falte espacio. Probar 1.280×720, 1.920×1.080 a 150% y 200%, texto ampliado y monitores con DPI distinto. La ventana mínima actual de 950×760 DIP no puede asumirse utilizable en todos esos escenarios.

Mantener foco visible, orden Tab coherente, `AutomationProperties.Name` en controles relevantes, soporte de alto contraste donde corresponda y texto además de colores. No construir otro tema de controles si WPF puede resolverlo con estilos que mantengan sus estados de teclado y accesibilidad.

I/O, descarga y verificación fuera del hilo UI con cancelación y progreso coherente; serializar mutaciones del disco y actualizar UI en Dispatcher. Limitar logs y telemetría con buffers circulares. Las pruebas WPF que renderizan un control no sustituyen navegación manual, teclado y lector de pantalla.

## 11. Super Resolution real, rendimiento y alcance de optimización

### 11.1. PR-08: resolución 3D desacoplada, solo después de proteger UI

Esto no se implementa cambiando únicamente `work_resolution` ni multiplicando una dimensión de metadata.

1. Consultar las resoluciones óptimas y los límites del modo SR del backend elegido. No fijar “67%” como sustituto universal de esa consulta. [E03]
2. Crear target de escena al tamaño de entrada y target de reconstrucción al tamaño de salida. Ajustar viewport y aspectos correspondientes, sin cambiar la escala de UI.
3. Comprobar todos los consumidores de color, depth, motion vectors y tamaños de píxel de esa cámara: efectos de cámara, transparencias, reflejos, partículas, lluvia, selección de herramientas e info views. Los shaders que asuman `Screen.width` en vez del target requieren adaptación o compatibilidad explícita.
4. Renderizar la escena una sola vez a resolución reducida; entregar sus buffers reales, no reetiquetar un frame nativo ya terminado.
5. Reconstruir antes de los efectos que deban funcionar en salida y componer UI al final. Medir dónde debe quedar NR según su contrato de tamaño/color y evitar reconstrucciones redundantes.
6. Recrear recursos mediante epoch y cambio preparado: no liberar la versión anterior mientras haya GPU pendiente. Mantener bypass con imagen actual si falla la preparación.
7. Aplicar mip bias únicamente donde exista una estrategia segura de propiedad y de restauración. Distinguir sesgo de mipmap de `QualitySettings.lodBias`, que no es la misma función.
8. Medir primero tamaños fijos. La resolución dinámica automática se añade después, con histéresis y métricas GPU, no en el primer PR.

**Puerta de aceptación:** el frame capture debe mostrar geometría 3D a menor tamaño, MV/depth correctos a ese tamaño y HUD al de salida. Registrar ahorro GPU real frente al render nativo. En una ciudad limitada por CPU, puede mejorar calidad/coste sin aumentar FPS; ese resultado sigue siendo útil, pero debe explicarse.

### 11.2. Medición por etapas

Separar tiempo de frame del juego, tiempo de CPU del mod, GPU de captura/conversión, reconstrucción, NR, transporte/esperas y composición. La media calculada desde `Time.unscaledDeltaTime` es intervalo de frame del juego, no duración de GPU de NR. [R09]

Añadir queries/timestamps apropiadas en cada dispositivo/cola. No restar timestamps de dispositivos con relojes distintos como si fueran la misma escala. Publicar solo muestras completadas; descartar intervalos inválidos/disjoint. Usar lecturas diferidas y buffers acotados, nunca bloquear cada frame para obtener una cifra del overlay.

Reportar p50/p95/p99, frames omitidos, invalidaciones, resets, proveedor efectivo y memoria. Documentar si FG está activo. Una media FPS puede ocultar tirones y repetir cuadros antiguos.

**Presupuesto configurable:** calcular a partir de una meta, por ejemplo 60 Hz implica 16,67 ms por intervalo y 30 Hz 33,33 ms. No sumar automáticamente todos los tiempos CPU/GPU como si nunca se solaparan; usar una traza para identificar el camino crítico. El coste histórico ~17 ms del fixture no acredita ni descarta por sí solo la meta en otra configuración.

### 11.3. Control adaptativo de coste NR

Solo si hay un tiempo GPU fiable del dominio relevante. Elegir entre resoluciones de trabajo permitidas, con límites mínimo/máximo, ventana estable e histéresis. No reconstruir recursos por cada variación de un slider ni por cada frame lento de CPU.

Preparar el cambio, reiniciar historia una vez y confirmar el nuevo estado. Limitar oscilaciones y mostrar las dimensiones efectivas. Si la calidad se degrada por debajo del límite aceptado, el usuario debe poder mantener resolución y aceptar menor FPS; el sistema no sacrifica silenciosamente imagen para sostener un número.

Eliminar tareas per-frame innecesarias: `GetNativeTexturePtr`, reflejos/inspección de ensamblados, parseo de XML/INI, consultas de registro, alocaciones de textos y copias GPU redundantes. Cada eliminación se justifica con perfil, no con una promesa porcentual.

## 12. Telemetría, recuperación y contrato Hub↔juego

Conservar el canal con seqlock y sesión existente. Versionarlo sin reutilizar offsets con significado distinto. Añadir mensajes cortos de capacidades y estado; no enviar texturas ni punteros GPU a un proceso externo.

Campos propuestos, agrupados para limitar tamaño:

- Identidad: protocolVersion, sessionId, processId, buildId, adapterLuid, deviceEpoch, cameraId.
- Frame: renderedFrameId, preparedFrameId, evaluatedFrameId, completedFrameId, outputCommittedFrameId.
- Capacidades: bits admitidos y bits efectivos separados.
- Resoluciones: escena, MV/depth, trabajo NR y salida.
- Proveedor: preferido, elegido, unidades normalizadas, fallback reason.
- Historia: requestedResetSerial, completedResetSerial, resetReason.
- Backend: tipo, versión, estado SR/DLAA, estado NR, códigos de error originales.
- UI: punto de composición y estado de protección verificado.
- Métricas: validez explícita, tiempos diferidos, frame pacing, memoria y contadores.

No es obligatorio introducir todos en una estructura gigante. Usar una cabecera estable y bloques versionados dentro de la capacidad del MMF, con tamaños acotados y compatibilidad de lector. Las razones textuales pueden viajar como códigos que ambos extremos traducen.

Los comandos incluyen ID, revisión, sesión esperada y tiempo de expiración. Responder `accepted`, `applied` o `rejected` con motivo. `ResetHistory` recibido no equivale a reset GPU completado. `TogglePanel` sí puede confirmarse desde UI al ejecutarse.

Recuperación: desactivar trabajo propio ante fallo, preservar diagnósticos y permitir volver al modo conocido tras una nueva preparación. Ante pérdida de dispositivo o un fallo que deja un consumidor externo en estado desconocido, no intentar llamadas reiteradas sobre locks/contextos posiblemente inválidos; exigir reinicio cuando no se pueda garantizar recuperación. No ocultar errores para mantener un indicador verde.

## 13. Plan de pruebas: qué exactamente debe demostrar cada build

### 13.1. Pruebas de contrato y lógica, automatizables sin juego

| ID | Caso | Resultado esperado |
|---|---|---|
| ABI01 | Buffer V1 de exactamente 32 bytes | Conversión segura; ninguna lectura fuera del buffer |
| ABI02 | V2 de 48 bytes | Campos y offsets C#/C++ coinciden |
| ABI03 | Versiones/tamaños cruzados | Rechazo sin cambiar estado |
| ABI04 | NaN/Inf en jitter y escala | Rechazo y código específico |
| ABI05 | Frame/epoch antiguos | No consumir ni confirmar reset |
| TMP01 | Puente ausente o no preparado | No modificar proyección |
| TMP02 | Desactivar entre PreCull/PostRender | Restauración del valor personalizado capturado |
| TMP03 | Error tras jitter | Bypass definido; siguiente frame sin jitter |
| MV01 | Preferencia nativa Off | Recursos y constantes ópticos coherentes |
| MV02 | Puntero nativo cero/descartado | No heredar escala nativa |
| MV03 | Cambio de proveedor | Reset único y nuevo descriptor completo |
| CFG01 | XML antiguo sin campos nuevos | Migración segura sin habilitación experimental silenciosa |
| CFG02 | Fallo de escritura | Estado pendiente y reintento; no falsa confirmación |
| SEC01 | NR con Authenticode inválido | No aprobado como binario oficial íntegro |
| SEC02 | Importación directa de DLL válida | Validación de archivo, no contra hash ZIP |
| SEC03 | ZIP ambiguo, traversal, symlink/ruta redirigida | Rechazo manteniendo confinamiento existente |
| INS01 | Interrupción en instalación/rollback | Recuperación acorde al diario |
| INI01 | Técnicas ajenas | Orden relativo y preferencias preservados |
| IPC01 | Comando de otra sesión/caducado | Ignorado con motivo verificable |

Los mocks no acreditan GPU, integración Unity ni UI. Conservar explícitamente esa limitación en resultados.

### 13.2. Fixture gráfico con entradas conocidas

Añadir al host gráfico existente un patrón de líneas finas, un objeto que se desplaza independientemente de cámara, zonas transparentes, un HUD opaco y otro semitransparente. Guardar semillas, posiciones y matriz de cámara para repetirlo.

**Métricas/observaciones:**

- Coordenadas de reproyección conocidas frente a MV entregados: signo, escala y jitter. Una tolerancia subpíxel, por ejemplo 0,1 píxel para la sonda analítica, es una meta propuesta; justificar el umbral según cuantización del formato.
- Recursos nativos y ópticos con patrones distintos para demostrar cuál se consume a 100/85/66%.
- Reset solicitado durante NGX fallido: no confirmar hasta el resultado correcto del mismo dominio.
- Color ramp y grises: comprobar gamma/sRGB/HDR sin cambios involuntarios.
- Redimensionar, recrear dispositivo, perder/recrear target, backend Off/On y dos cámaras por frame.
- Si FG causa presents auxiliares, el contador de evaluaciones sigue los cuadros reales y no duplica trabajo.
- Prueba de identidad del camino pre-UI antes de activar DLAA o NR.

Guardar capturas input/output, descriptor completo y resultados de API. Usar readback de debug pequeño y diferido; no convertirlo en coste permanente.

### 13.3. Matriz dentro de CS1

| Escenario | Lo que se verifica |
|---|---|
| Ciudad pequeña, cámara quieta | Calidad de bordes, ruido temporal y bypass neutro |
| Ciudad grande con simulación activa | Diferenciar limitación CPU de coste NR y pacing |
| Paneo sobre cables, rieles y vallas | Estabilidad subpíxel y pérdida de detalle |
| Vehículos y ciudadanos cercanos | Vectores por objeto, estelas y disoclusiones |
| Vegetación y viento | Movimiento/deformación no cubierto y fallback |
| Agua, lluvia y transparencias | Profundidad inválida, reflejos y mezcla temporal |
| Día, noche, amanecer, atardecer | Exposición, estabilidad y conservación del perfil visual |
| Cambiar LUT/look de otro FX | Reset acotado; no sobrescribir su configuración |
| Abrir panel de construcción, búsqueda y modal | UI protegida, foco de teclado y atajos |
| Info views, selección y tooltips | Cobertura real del conjunto de UI y overlays |
| Pausa, reanudar, teletransporte y FOV | Historia coherente, sin saltos acumulativos |
| Ciudad A → menú → ciudad B → reinicio | Reaplicación, recursos, preferencias y epoch |
| Borderless, Alt-Tab, cambio de resolución | Recreación y restauración seguras |
| NeuralFX solo y con los cuatro FX | Un único propietario por dominio y ninguna invasión |

Mantener iguales mapa, assets, tema, LUT, cámara, hora y clima en cada A/B. Primero NR Off/On con DLAA constante; después antialiasing, y solo al final FG. No validar cinco cambios a la vez.

**Criterio de éxito visual propuesto:** no deformaciones persistentes visibles en cables/geometría o texto; no ghosting claramente peor que el baseline aceptado; mejoras de apariencia deseadas por el usuario y estabilidad en movimiento. Métricas de imagen ayudan, pero un valor SSIM alto contra el frame original no es por sí mismo el objetivo de NR, que modifica apariencia. Guardar secuencias, no solamente screenshots favorables.

### 13.4. Pruebas de producto y sesiones prolongadas

Ensayar los tamaños/DPI de la sección 10, teclado y UUI en juego. Cambiar modos varias veces, desconectar Hub y volverlo a abrir, importar un preset inválido, provocar disco sin permiso y usar una ruta de juego distinta. Confirmar que errores no bloquean la simulación.

Una sesión prolongada de aceptación debe incluir varios ciclos de carga y al menos una secuencia sostenida de uso normal. Definir duración y escenario en el protocolo local —por ejemplo una hora como objetivo inicial, no como promesa de estabilidad ilimitada— y guardar evolución de memoria, GC, tiempo de frame y fallos. Una sesión de menú larga no sustituye esa prueba.

## 14. Secuencia de entregas y dependencias

| PR | Entrega | Depende de | Puerta de salida |
|---|---|---|---|
| 00 | Baseline, tuple y documentación actualizados | Nada | Fuente/instalación/evidencia identificables |
| 01 | Jitter seguro, Off y restauración | 00 | Cámara intacta cuando no hay consumidor |
| 02 | ABI, negociación y validación | 00 | ASan/contratos/migración compatibles |
| 03 | Selección MV coherente y SRV correcto | 02 | Misma fuente efectiva a 100/85/66% |
| 04 | Captura de frame y recursos propios | 02–03 | Color/depth/MV de cámara y epoch correctos |
| 05 | Jitter, escalas y resets integrados | 01–04 | Fixture temporal aprobado |
| 06 | Escena sin HUD y composición posterior | 04 | UI protegida con prueba de identidad |
| 07 | Confirmación NR por backend | 02, 04, 06 | NR y salida incorporada demostrados |
| 08 | SR con render Unity desacoplado | 05–07 | Render 3D realmente menor; UI nativa |
| 09 | Seguridad/importación/catálogo | 00; puede ir en paralelo | Firma/hash/procedencia y rutas correctas |
| 10 | Estado común y comandos IPC | 02, 07 | Hub y juego coinciden en lo efectivo |
| 11 | Panel in-game y opciones | 10 | Controles funcionales, foco y escalas |
| 12 | Hub simplificado | 09–10 | Flujos de instalación y sesión verificables |
| 13 | Benchmark de backends alternativos | 04, 06 y entorno aislado | Decisión con evidencia, no por versión |
| 14 | Rendimiento y aceptación en ciudad | Entregas aplicables | Matriz real aprobada; límites publicados |

PR-09 no espera a terminar la integración gráfica. No publicar una build automática mientras el canal estable pueda confundir binarios alterados con firma íntegra. Los cambios de UI pueden diseñarse en paralelo, pero no cerrar controles sobre estados del backend todavía inventados.

**No abrir catorce PR vacíos.** Son cortes de trabajo/aceptación; combinar cortes pequeños cuando no se pierda trazabilidad. Mantener un solo cambio de riesgo gráfico principal por build ensayada.

## 15. Criterios de aceptación final

NeuralFX podrá denominarse éxito para el escenario del usuario cuando se cumplan conjuntamente:

1. Existe evidencia de NR —no solo DLAA— procesando una ciudad con el tuple instalado documentado.
2. La salida confirmada corresponde al frame actual y llega al target que se presenta.
3. Jitter, movimiento y profundidad tienen convenciones y vida útil comprobadas; fallback no combina fuentes y escalas.
4. La UI permanece fuera del procesamiento neural/afilado propio con ventanas, transparencias e info views verificadas.
5. Apagar, cambiar ciudad y reiniciar son reversibles; no quedan LOD, matrices o flags propios imponiéndose sin consentimiento.
6. Los controles muestran estado efectivo y errores; no hace falta adivinar entre Hub, menú y ReShade.
7. Calidad y coste están medidos sobre los escenarios elegidos, sin garantizar FPS universales.
8. La instalación y recuperación conservan archivos ajenos y la política de licencia/procedencia elegida.

Una build puede aprobar parcialmente: por ejemplo, `NR a resolución nativa, sin SR interno` es un resultado válido si está explicado y cumple lo demás. No hay que postergar todo hasta tener cada función de la familia DLSS. Pero tampoco se anuncia SR real o UI protegida cuando solo hay presets del addon.

## 16. Prompt de ejecución para el agente implementador

> Trabaja exclusivamente en NeuralFX. El usuario ya tiene otro agente modificando SceneFX, LumenFX, AtmosphereFX y ClassicLightFX: no los edites ni dupliques sus responsabilidades. Toma este documento como plan propuesto; verifica primero el HEAD y los cambios locales porque la auditoría se congeló en a063036271f2b84d0abeca31153400940190dbf6.
>
> Objetivo: DLSS 5 Neural Rendering real en una ciudad de CS1, con entradas correctas, UI protegida, controles honestos y recuperación segura. No sustituyas el objetivo por CAS o DLAA solamente; tampoco llames NR a un contador NGX genérico.
>
> Empieza con baseline, seguridad de cámara, ABI y proveedor MV. Escribe primero las pruebas que reproduzcan: lectura 48 sobre cliente 32, NaN aceptado, escalas nativas con textura óptica, SRV antiguo en reducción de trabajo, jitter sin consumidor y falta de restauración. Conserva la infraestructura útil y no hagas una reescritura general.
>
> No copies código GPL-3 ni componentes de licencia no verificada. No desactives validación Authenticode del canal estable para aceptar una DLL. Identifica autor, distribuidor, permisos, hashes y API de cada backend. No renombres RR como NR ni uses IDs de otro juego para aparentar compatibilidad.
>
> No inventes métodos de NVIDIA o del consumidor. Las interfaces de este documento son contratos internos propuestos. Cuando una API necesaria no esté acreditada, realiza un spike con headers/runtime autorizado y registra el límite. Continúa por la alternativa verificable sin declarar la función implementada.
>
> No modifiques la carpeta del juego ni sustituyas DLL cargadas sin autorización específica y respaldo. Compilar o abrir un PR no autoriza desplegar. Las pruebas con dobles se reportan como lógica, los fixtures como gráficos aislados y las pruebas de ciudad como integración real.
>
> Para cada corte entrega: archivos modificados, bug reproducido, diff, comandos ejecutados, tests y resultados, tuple de versiones y capturas/logs cuando corresponda, limitaciones y siguiente corte. No uses “100%”, “sin artefactos” o “terminado” sin el criterio y evidencia exactos.
>
> Mantén un HANDOFF.md corto con HEAD, cambios pendientes, último escenario probado y siguiente paso. No conviertas notas narrativas de una sesión anterior en pruebas actuales. El objetivo no es acumular controles: es hacer que cada control corresponda a un resultado real.

## 17. Referencias y procedencia de las conclusiones

### Código y documentos del repositorio

Base de todas las rutas R01–R16, R18 y R19:  
`https://github.com/juanamores98/NeuralFX/blob/a063036271f2b84d0abeca31153400940190dbf6/`

- **R01:** `README.md`.
- **R02:** `docs/VALIDACION.md`.
- **R03:** `docs/ARQUITECTURA.md`.
- **R04:** `NeuralFX.Mod/Source/Rendering/TemporalCamera.cs`.
- **R05:** `NeuralFX.Mod/Source/Rendering/NativeBridge.cs`.
- **R06:** `Native/build.ps1`.
- **R07:** `Native/neuralfx_bridge.h`.
- **R08:** `NeuralFX.Mod/Source/Config/ModSettings.cs`.
- **R09:** `NeuralFX.Mod/NeuralFXManager.cs`.
- **R10:** `NeuralFX.Mod/Source/UI/TelemetryPanel.cs`.
- **R11:** `NeuralFX.Hub/Services/PipelineConfiguration.cs`.
- **R12:** `NeuralFX.Hub/MainWindow.xaml`.
- **R13:** `NeuralFX.Hub/Services/BinaryIdentity.cs`.
- **R14:** `NeuralFX.Hub/Assets/components.json`.
- **R15:** `NeuralFX.Hub/Services/DependencyManagerService.cs`.
- **R16:** `NeuralFX.Hub/Services/HardwareDiagnosticsService.cs`.
- **R17:** upstream fijado, `FeedFrame11`, https://github.com/jlrouzies-fr/DLSS5-Feeder/blob/927d76d30e888bce497f5c5f8d496fcb696da335/src/dlss5-feed.cpp .
- **R18:** `Native/bridge_tests.cpp`.
- **R19:** `NeuralFX.Mod/Source/Options/OptionsPanel.cs`.

### Fuentes externas primarias consultadas

- **E01:** NVIDIA, anuncio oficial de DLSS 5 y su carácter independiente, publicado el 1 de septiembre de 2026: https://www.nvidia.com/en-eu/geforce/news/dlss-5-3d-guided-neural-rendering/ .
- **E02:** NVIDIA Research, definición de la etapa generativa, 1 de septiembre de 2026: https://research.nvidia.com/labs/adlr/DLSS5/ . Se consultó la página, no se afirma haber analizado el PDF del paper.
- **E03:** NVIDIA Streamline, guía DLSS, versión 2.14.1 indicada en la página consultada: https://raw.githubusercontent.com/NVIDIA-RTX/Streamline/main/docs/ProgrammingGuideDLSS.md . No se utiliza esta guía SR como contrato inventado del plugin NR.
- **E04:** Unity 5.6, `Texture.GetNativeTexturePtr`, tipo de recurso y sincronización: https://docs.unity.cn/560/Documentation/ScriptReference/Texture.GetNativeTexturePtr.html .
- **E05:** Unity 5.6, Camera Depth Texture, formato/unidades y generación de vectores: https://docs.unity.cn/560/Documentation/Manual/SL-CameraDepthTexture.html .
- **E06:** ReShade 6.8.0, API `effect_runtime::render_effects`: https://raw.githubusercontent.com/crosire/reshade/v6.8.0/include/reshade_api.hpp .
- **E07:** Código upstream fijado, `CopyOrResampleInputs` y expansión FSR1: https://raw.githubusercontent.com/jlrouzies-fr/DLSS5-Feeder/927d76d30e888bce497f5c5f8d496fcb696da335/src/dlss5-feed.cpp .
- **E08:** Microsoft, semántica de `WinVerifyTrust`: https://learn.microsoft.com/en-us/windows/win32/api/wintrust/nf-wintrust-winverifytrust .
- **E09:** README del autor de DLSS5-Feeder, alternativas y exclusividad de backends, consultado el 8 de septiembre: https://raw.githubusercontent.com/jlrouzies-fr/DLSS5-Feeder/main/README.md . `main` puede cambiar; su contenido no sustituye a una prueba CS1 de esas alternativas.
- **E10:** NVIDIA Support, Smooth Motion para juegos compatibles y RTX 40/50: https://nvidia.custhelp.com/app/answers/detail/a_id/5621/~/enabling-smooth-motion-in-nvidia-app .
- **E11:** NVIDIA Streamline, guía de Frame Generation y recursos de integración: https://raw.githubusercontent.com/NVIDIA-RTX/Streamline/main/docs/ProgrammingGuideDLSS_G.md . No se presume soporte de cualquier combinación DX11 por el hecho de que el framework general soporte esa API.

**G:** documento de Gemini adjunto. Se conserva su terminología para identificar afirmaciones; cuando una cifra o garantía no estaba sustentada, se marca expresamente y no se rellena con una supuesta medición.

## 18. Reproducción aislada adjunta

`repro/abi_repro.cpp` conserva los layouts de 32/48 bytes y las expresiones relevantes de aceptación/copia, omitiendo locks Windows y almacenamiento circular que no cambian el tamaño de copia. Se compiló con Clang C++17 y AddressSanitizer en Linux. `finite-result.txt` contiene aceptación de NaN y pares cruzados. `legacy-asan.txt` registra la lectura fuera de una reserva de 32 bytes.

Ejemplo para repetir en un entorno con Clang y ASan:

```sh
clang++ -std=c++17 -O0 -g -fsanitize=address -fno-omit-frame-pointer abi_repro.cpp -o abi_repro
ASAN_OPTIONS=detect_leaks=0 ./abi_repro finite
ASAN_OPTIONS=detect_leaks=0 ./abi_repro legacy
```

El segundo comando debe fallar en la versión vulnerable reproducida. Este programa es un test deliberado, **no una DLL para instalar**. No contiene dependencias NVIDIA, archivos del juego ni binarios redistribuidos. La corrección debe ensayarse después sobre el decodificador real del repositorio y su cliente administrado.

---

**Decisión recomendada:** corregir primero seguridad temporal y contrato de entrada; después UI pre-NR/poscomposición, confirmación NR y controles reales; comparar backends en una rama aislada. Mantener el objetivo de DLSS 5 y medirlo en ciudad. No perder otra ronda de desarrollo ampliando estilos, LOD o sombras que no resuelven los defectos del pipeline.

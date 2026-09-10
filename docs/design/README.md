# Fuentes del rediseño de interfaz

Cada `*.dc.html` es un artboard y `canvas.json` los coloca en el lienzo. Son la
especificación de la que salieron `NeuralFX.Hub/MainWindow.xaml`,
`NeuralFX.Mod/Source/UI/TelemetryPanel.cs` y
`NeuralFX.Mod/Source/Options/OptionsPanel.cs`.

`Tokens.dc.html` fija los valores: un acento (`#4EC9B0` en el Hub, `#7FD8C0` en
ciudad), dos superficies, tipografía Segoe UI con Consolas para rutas y hashes, y
el vocabulario de estado, que sale siempre de `Shared/SessionViewState.cs` — ningún
estado afirma imagen neural sin `NrConfirmed`.

El lienzo publicado se regenera desde estos archivos; el HTML sembrado no se versiona.

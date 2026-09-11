$ErrorActionPreference = 'Stop'
# The user authorizes normal shutdown at the end of a development session.
foreach ($processName in @('Cities', 'NeuralFX.Hub')) {
    foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
        try {
            if ($process.HasExited) { continue }
            Write-Output "Cerrando $processName para desplegar la última versión..."
            if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(30000)) {
                throw "$processName no completó el cierre normal. Se conserva la instalación; revisa su ventana antes de reintentar."
            }
        } finally { $process.Dispose() }
    }
}
Write-Output 'Cities: Skylines y NeuralFX Hub están cerrados.'

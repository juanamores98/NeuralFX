using System.Collections.Generic;
using System.Linq;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services;

internal sealed record InstallationGuidance(int Ready, int Installed, int Total, bool CanInstall, bool CanRestore, string InstallLabel, string Next)
{
    public static InstallationGuidance Create(bool gameFound, bool writable, bool running, IntegrityReport? report, IReadOnlyCollection<DependencyItem> items)
    {
        int total = items.Count(x => x.IsRequired), ready = items.Count(x => x.IsRequired && x.IsReady);
        int installed = items.Count(x => x.IsRequired && x.GameState is ComponentGameState.Installed or ComponentGameState.Configured or ComponentGameState.DifferentCopy);
        bool different = items.Any(x => x.GameState == ComponentGameState.DifferentCopy);
        bool complete = total > 0 && ready == total;
        bool allowed = gameFound && writable && !running;
        string label = report?.CanMigrate == true ? "Actualizar instalación antigua" : report?.NeedsRepair == true ? "Reparar instalación" : different ? "Aplicar componentes preparados" : report?.Managed == true ? "Aplicar preset / reinstalar" : "Instalar en el juego";
        string next = !gameFound ? "Elige Cities.exe en Entorno y rutas para comprobar o instalar el pipeline."
            : report == null ? "Comprobando archivos y copias de restauración…"
            : !report.Valid && !report.CanMigrate ? "No se puede verificar el registro o sus copias. Revisa el detalle en Estado en juego antes de modificar la instalación."
            : running ? "El juego está abierto. Puedes consultar su estado; ciérralo antes de instalar, cambiar el preset o restaurar."
            : !writable ? "La carpeta del juego no permite escritura. Revisa sus permisos antes de instalar o restaurar."
            : !complete ? (installed == total ? "La instalación está completa. " : "") + $"Faltan {total - ready} componentes disponibles para instalar o reparar. Descarga los públicos e importa los runtimes NVIDIA."
            : report.CanMigrate ? "Puedes actualizar. Se guardará la instalación antigua como punto de restauración; sus componentes no se eliminarán al volver atrás."
            : report.NeedsRepair ? "Hay archivos ausentes, modificados o fuera del registro. Repara usando las copias disponibles."
            : different ? "Hay componentes preparados que difieren de los instalados. Pulsa Aplicar componentes preparados para copiarlos al juego."
            : report.Managed ? "Instalación completa. Abre una ciudad y comprueba Estado en juego. Si cambias el preset, aplícalo antes de abrir CS1."
            : "Todo está disponible. Elige un preset y pulsa Instalar en el juego; descargar o importar por sí solo no instala nada.";
        return new(ready, installed, total, allowed && complete && report != null && (report.Valid || report.CanMigrate),
            allowed && report?.Restoration != null, label, next);
    }
}

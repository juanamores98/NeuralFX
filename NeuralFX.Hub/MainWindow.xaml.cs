using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;
using NeuralFX.Protocol;

namespace NeuralFX.Hub
{
    public partial class MainWindow : Window
    {
        private readonly HardwareDiagnosticsService _diagnosticsService = new();
        private readonly DependencyManagerService _dependencyManager = new();
        private readonly InstallationEngineService _installEngine;
        private readonly UninstallService _uninstaller = new();
        private readonly HubPreferences _preferences = HubPreferences.Load();
        private readonly IntegrityMonitor _integrity = new();
        private readonly Dictionary<string, IncrementalLogReader> _logReaders = new();
        private readonly StringBuilder _hubLogs = new();
        private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly DispatcherTimer _backgroundTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private HardwareInfo _hardwareInfo = new();
        private List<DependencyItem> _dependencies = new();
        private TelemetryChannel? _channel;
        private TelemetryFrame _lastFrame;
        private int _pid, _revision;
        private long _scannedRevision;
        private long _cacheRevision = 1, _scannedCacheRevision;
        private string? _watchedRoot;
        private bool _busy, _polling, _diagnosing, _closed;
        private IntegrityReport? _report;
        private string _operationLastMessage = "";
        private static string ModDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Colossal Order", "Cities_Skylines", "Addons", "Mods", "NeuralFX");
        private sealed record OperationResult(bool? Success, string Title, string Detail);
        private string? GameDirectory => _hardwareInfo.GameFound ? Path.GetDirectoryName(_hardwareInfo.GameExePath) : null;
        private static readonly string[] PresetNames = { "Nativo", "Equilibrado", "Coste reducido", "Fotografía" };
        public string HubVersion { get; } = "Hub " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "");

        public MainWindow()
        {
            InitializeComponent();
            _installEngine = new(_dependencyManager);
            MainTabs.DataContext = this;
            PresetSelector.SelectedIndex = Enum.IsDefined(_preferences.Preset) ? (int)_preferences.Preset : 0;
            _liveTimer.Tick += (_, _) => ReadTelemetry();
            _backgroundTimer.Tick += async (_, _) => await PollBackgroundAsync();
            Loaded += async (_, _) =>
            {
                LogHub("NeuralFX Hub iniciado · Almacén local: " + _dependencyManager.CacheDirectory);
                await RunFullDiagnosticsAsync();
                _liveTimer.Start(); _backgroundTimer.Start();
                await PollBackgroundAsync();
                await CheckUpdatesAsync();
            };
            Activated += (_, _) => { _integrity.Invalidate(); _cacheRevision++; };
            Closing += (_, e) =>
            {
                if (!_busy) return;
                e.Cancel = true;
                SetNotice("Operación en curso", "Espera a que finalice la operación actual antes de cerrar el Hub.");
            };
            Closed += (_, _) => { _closed = true; _liveTimer.Stop(); _backgroundTimer.Stop(); _integrity.Dispose(); _channel?.Dispose(); };
        }

        private async Task RunFullDiagnosticsAsync()
        {
            if (_diagnosing || _closed) return;
            _diagnosing = true;
            try
            {
                _hardwareInfo = await Task.Run(() => _diagnosticsService.RunDiagnostics(_preferences.GameExecutable));
                TxtGpuName.Text = _hardwareInfo.GpuName;
                TxtArchitecture.Text = "Inventario preliminar del registro. El LUID del dispositivo del juego aparece en Estado; SR, DLAA y NR requieren consultas independientes.";
                TxtBadgeGpu.Text = _hardwareInfo.DetectedArchitecture switch
                {
                    GpuArchitecture.Blackwell => "NVIDIA RTX BLACKWELL",
                    GpuArchitecture.AdaLovelace => "NVIDIA RTX ADA LOVELACE",
                    GpuArchitecture.AmpereTuring => "NVIDIA RTX AMPERE / TURING",
                    _ => _hardwareInfo.IsNvidia ? "NVIDIA RTX / GTX" : "GPU NO NVIDIA"
                };
                TxtVram.Text = _hardwareInfo.VramBytes == 0 ? "Sin lectura disponible" : _hardwareInfo.VramDisplay;
                TxtBadgeVram.Text = "";
                TxtDriverVer.Text = _hardwareInfo.ParsedDriverVersion;
                TxtDriverRaw.Text = "Versión del sistema: " + _hardwareInfo.RawDriverVersion;
                TxtBadgeDriver.Text = "";
                TxtGameExe.Text = _hardwareInfo.GameFound ? _hardwareInfo.GameExePath : _preferences.GameExecutable ?? "No localizado automáticamente. Usa 'Elegir Cities.exe'.";
                TxtWritePerm.Text = !_hardwareInfo.GameFound ? "N/A" : _hardwareInfo.CanWriteGameDir ? "Concedido" : "Bloqueado · requiere permisos de administrador";
                TxtAppLocations.Text = "• Hub: " + AppContext.BaseDirectory + "\n• Mod de Unity: " +
                    (File.Exists(Path.Combine(ModDirectory, "NeuralFX.dll")) ? "Detectado en Addons/Mods/NeuralFX (Activar en Gestor de contenido de CS1)\n" : "Pendiente de instalación (Se despliega automáticamente al pulsar Instalar)\n") +
                    "• Almacén local de runtimes: " + _dependencyManager.CacheDirectory;
                UpdateProcessState();
                await RefreshDependenciesAsync();
                if (GameDirectory is string root && _watchedRoot != root)
                {
                    _watchedRoot = root;
                    _report = null; _scannedRevision = 0;
                    _integrity.Watch(root, _dependencies.SelectMany(x => _dependencyManager.GetPackageFiles(x).Values)
                        .Concat(new[] { "dlss5-feed.cfg", "ReShade.ini", "ReShadePreset.ini", "reshade-shaders/Shaders/NeuralFX_CAS.fx" }));
                }
                _integrity.Invalidate();
            }
            catch (Exception ex) { LogHub("Diagnóstico: " + ex.Message); SetNotice("No se pudo completar el diagnóstico", ex.Message, false); }
            finally { _diagnosing = false; }
        }

        private async Task RefreshDependenciesAsync()
        {
            string? root = GameDirectory;
            long revision = _cacheRevision;
            var dependencies = await Task.Run(() => _dependencyManager.GetInitialDependencies(root, _hardwareInfo));
            if (_closed || root != GameDirectory) return;
            _dependencies = dependencies; _scannedCacheRevision = revision;
            ListDependencies.ItemsSource = _dependencies;
        }

        private void UpdateProcessState()
        {
            TxtGameProcess.Text = _hardwareInfo.IsGameRunning ? "En ejecución · ciérralo para modificar archivos" : "Cerrado · listo para instalar o desinstalar";
            TxtGameProcess.Foreground = (Brush)FindResource(_hardwareInfo.IsGameRunning ? "Danger" : "Success");
        }

        private async Task PollBackgroundAsync()
        {
            if (_polling || _closed || _busy || _diagnosing) return;
            _polling = true;
            try
            {
                string? root = GameDirectory;
                if (_cacheRevision != _scannedCacheRevision)
                { await RefreshDependenciesAsync(); _integrity.Invalidate(); }
                int pid = await Task.Run(() => FindGameProcess(_hardwareInfo.GameExePath));
                if (_closed || root != GameDirectory) return;
                _hardwareInfo.IsGameRunning = pid != 0; UpdateProcessState();
                if (pid != _pid || _channel == null)
                {
                    _channel?.Dispose(); _channel = pid > 0 ? TelemetryChannel.Open(pid, false) : null; _pid = pid; _lastFrame = default;
                }
                long revision = _integrity.Revision;
                if (root != null && revision != _scannedRevision)
                {
                    string[] paths = _dependencies.SelectMany(x => _dependencyManager.GetPackageFiles(x).Values).ToArray();
                    var report = await Task.Run(() => IntegrityMonitor.Scan(root, paths));
                    if (_closed || root != GameDirectory) return;
                    _report = report;
                    _scannedRevision = revision;
                    foreach (var item in _dependencies) InstallationStatus.Apply(item, _dependencyManager.GetPackageFiles(item).Values, report);
                }
                if (root == null) _report = null;
                UpdateInstallationView();
                string? logPath = root == null ? null : RadioLogReShade.IsChecked == true ? Path.Combine(root, "ReShade.log") : RadioLogFeeder.IsChecked == true ? Path.Combine(root, "dlss5-feed.log") : null;
                if (logPath != null)
                {
                    if (!_logReaders.TryGetValue(logPath, out var reader)) _logReaders[logPath] = reader = new();
                    string content = await Task.Run(() => reader.Read(logPath));
                    if (!_closed && (RadioLogReShade.IsChecked == true && Path.GetFileName(logPath) == "ReShade.log" || RadioLogFeeder.IsChecked == true && Path.GetFileName(logPath) == "dlss5-feed.log")) ShowLog(content);
                }
            }
            catch (Exception ex) { LogHub("Monitor: " + ex.Message); SetNotice("No se pudo actualizar el estado", ex.Message, false); }
            finally { _polling = false; }
        }

        private void UpdateInstallationView()
        {
            if (GameDirectory is not string root)
            {
                TxtGameStatus.Text = "JUEGO NO LOCALIZADO";
                PillGameStatus.Background = (Brush)FindResource("Danger");
                TxtPipelineStatusTitle.Text = "Cities.exe no encontrado";
                TxtPipelineStatusSubtitle.Text = "Elige el ejecutable Cities.exe en Sistema para continuar.";
                TxtInstallHint.Text = "Sin la ruta del juego no se puede instalar ni verificar nada.";
                TxtComponentsSummary.Text = "Componentes";
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
                return;
            }

            var guidance = InstallationGuidance.Create(true, _hardwareInfo.CanWriteGameDir, _hardwareInfo.IsGameRunning, _report, _dependencies);
            // Lo mismo, pero suponiendo el juego ya cerrado: es lo que podremos hacer después de cerrarlo.
            var afterClosing = InstallationGuidance.Create(true, _hardwareInfo.CanWriteGameDir, false, _report, _dependencies);
            bool canCloseGame = _hardwareInfo.IsGameRunning;
            bool installable = guidance.CanInstall || (canCloseGame && afterClosing.CanInstall);
            bool hasArtifacts = PipelineFootprint.HasArtifacts(root) || Directory.Exists(Path.Combine(root, ".neuralfx-transaction"));
            bool isVerified = _report is { Managed: true, Valid: true, NeedsRepair: false };
            bool isLegacyOrIncomplete = _report != null && (_report.NeedsRepair || _report.CanMigrate || (hasArtifacts && !isVerified));

            if (isVerified)
            {
                TxtGameStatus.Text = "ARCHIVOS VERIFICADOS";
                PillGameStatus.Background = (Brush)FindResource("Accent");
                TxtPipelineStatusTitle.Text = "Archivos instalados y verificados";
                TxtPipelineStatusSubtitle.Text = "Los 19 archivos coinciden con el registro. Que el portador y NR se ejecuten se comprueba por separado al cargar una ciudad.";
                BtnInstall.Content = guidance.InstallLabel;
                BtnInstall.IsEnabled = !_busy && installable;
                BtnUninstall.IsEnabled = !_busy && !_hardwareInfo.IsGameRunning && _hardwareInfo.CanWriteGameDir;
            }
            else if (isLegacyOrIncomplete)
            {
                TxtGameStatus.Text = "INSTALACIÓN INCOMPLETA";
                PillGameStatus.Background = (Brush)FindResource("Warning");
                TxtPipelineStatusTitle.Text = "Instalación incompleta o antigua";
                TxtPipelineStatusSubtitle.Text = "Hay archivos en el juego que no coinciden con el catálogo actual. Actualizarlos conserva una copia verificada del estado previo.";
                BtnInstall.Content = guidance.InstallLabel;
                BtnInstall.IsEnabled = !_busy && installable;
                BtnUninstall.IsEnabled = !_busy && !_hardwareInfo.IsGameRunning && _hardwareInfo.CanWriteGameDir;
            }
            else
            {
                TxtGameStatus.Text = "SIN INSTALAR";
                PillGameStatus.Background = (Brush)FindResource("TextFaint");
                TxtPipelineStatusTitle.Text = "Sin instalación de NeuralFX";
                TxtPipelineStatusSubtitle.Text = "No hay archivos gestionados en la carpeta del juego. Antes de escribir nada verás la lista completa de cambios.";
                BtnInstall.Content = guidance.InstallLabel;
                BtnInstall.IsEnabled = !_busy && installable;
                BtnUninstall.IsEnabled = false;
            }

            bool connected = _channel != null && _pid != 0;
            BtnGameProcess.Content = _hardwareInfo.IsGameRunning ? "Cerrar el juego" : "Abrir Cities: Skylines";
            BtnGameProcess.IsEnabled = !_busy;
            BtnGameProcess.ToolTip = _hardwareInfo.IsGameRunning
                ? connected
                    ? "El mod cierra el juego por su salida normal. Guarda antes: no se guarda la ciudad por ti."
                    : "Sin telemetría se pide el cierre a la ventana del juego, como al pulsar la X. Guarda antes."
                : "Lanza Cities: Skylines por Steam.";

            if (canCloseGame && afterClosing.CanInstall)
                BtnInstall.Content = "Cerrar el juego y " + guidance.InstallLabel.Substring(0, 1).ToLowerInvariant() + guidance.InstallLabel.Substring(1);

            TxtInstallHint.Text = canCloseGame && afterClosing.CanInstall
                ? "Se cerrará Cities: Skylines, se escribirán los archivos y se volverá a abrir. Guarda la ciudad antes."
                : _hardwareInfo.IsGameRunning
                ? "Cierra Cities: Skylines para poder escribir en su carpeta."
                : !_hardwareInfo.CanWriteGameDir ? "Sin permiso de escritura en la carpeta del juego."
                : !guidance.CanInstall ? guidance.Next
                : "El juego debe seguir cerrado mientras se escriben los archivos.";

            TxtComponentsSummary.Text = guidance.Total == 0
                ? "Componentes"
                : $"Componentes · {guidance.Ready} de {guidance.Total} listos para instalar";

            BtnDownloadAllPublic.IsEnabled = _dependencies.Any(x => x.CanDownload);
            BtnAutoDetectDownloads.IsEnabled = _dependencies.Any(x => x.CanImport && !x.IsReady);

            TxtIntegrity.Text = _report == null ? "Elige la carpeta del juego y vuelve a analizar." :
                (!_report.Managed ? "No hay una instalación registrada activa. El juego se encuentra limpio o con archivos sin registrar." :
                    _report.Details.Length == 0 ? "Todos los archivos registrados coinciden con sus hashes guardados." : string.Join("\n", _report.Details)) +
                "\n\nLos hashes verifican integridad de archivos. La compatibilidad, la carga y NR se comprueban por separado.";
        }

        private static int FindGameProcess(string expectedPath)
        {
            foreach (var process in Process.GetProcessesByName("Cities"))
            {
                using (process)
                {
                    try { if (string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase)) return process.Id; }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
            }
            return 0;
        }

        private void ReadTelemetry()
        {
            if (_channel == null || !_channel.TryRead(out var frame) || frame.ProcessId != _pid || !TelemetryStatus.IsFresh(frame, DateTime.UtcNow))
            {
                TxtTelemetry.Text = _pid == 0 ? "Juego cerrado." : "Sin telemetría reciente. Activa NeuralFX en el Gestor de contenido y abre una ciudad.";
                TxtSessionSummary.Text = _pid == 0 ? "Juego cerrado" : "Esperando al mod en ciudad";
                TxtSessionHint.Text = _pid == 0 ? "Abre Cities: Skylines para recibir métricas." : "El proceso está en marcha; el mod todavía no publica telemetría.";
                PillSession.Background = (Brush)FindResource("TextFaint");
                TxtMetricFps.Text = TxtMetricFrame.Text = TxtMetricWork.Text = TxtMetricReset.Text = "—";
                TelemetryCommands.IsEnabled = false; return;
            }
            TelemetryCommands.IsEnabled = true;
            if (_lastFrame.UtcTicks == frame.UtcTicks) return;
            _lastFrame = frame;
            TxtTelemetry.Text = TelemetryStatus.Describe(frame);
            TxtSessionSummary.Text = SessionViewState.Summary(frame);
            TxtSessionHint.Text = "NGX, NR y salida incorporada son estados distintos. El tiempo mostrado es el intervalo del juego, no el coste GPU de NR.";
            PillSession.Background = (Brush)FindResource(SessionBrushKey(frame));
            TxtMetricFps.Text = frame.Fps.ToString("F1");
            TxtMetricFrame.Text = frame.FrameMs.ToString("F2") + " ms";
            TxtMetricWork.Text = frame.RenderWidth > 0 && frame.WorkWidth > 0
                ? Math.Round(100.0 * frame.WorkWidth / frame.RenderWidth) + "%"
                : "—";
            TxtMetricReset.Text = frame.ResetCount + " / " + frame.CameraCuts;

            bool running = SessionViewState.Has(frame, RuntimeFlags.PipelineRequested);
            BtnPipelineToggle.Content = running ? "Desactivar NeuralFX" : "Activar NeuralFX";
            BtnPipelineToggle.Tag = running ? "DisablePipeline" : "EnablePipeline";
        }

        // El mismo vocabulario de estado que SessionViewState.Summary, en color.
        private static string SessionBrushKey(TelemetryFrame frame)
        {
            if (!SessionViewState.Has(frame, RuntimeFlags.CameraPresent)) return "TextFaint";
            if (!SessionViewState.Has(frame, RuntimeFlags.PipelineRequested)) return "TextFaint";
            if (!SessionViewState.Has(frame, RuntimeFlags.NativeConnected) || frame.BackendError != 0) return "Warning";
            if (SessionViewState.Has(frame, RuntimeFlags.NrConfirmed)) return "Accent";
            return SessionViewState.Has(frame, RuntimeFlags.EvaluationSucceeded) ? "Accent" : "Warning";
        }

        private void BtnTelemetryCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button) SendTelemetryCommand(button.Tag?.ToString());
        }

        // Los segmentados aplican al cambiar la selección: no hacen falta botones "Aplicar".
        private void SessionControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || sender is not ListBox list || !list.IsEnabled) return;
            SendTelemetryCommand(list.Tag?.ToString());
        }

        private void SendTelemetryCommand(string? tag)
        {
            if (_channel == null || !Enum.TryParse<CommandKind>(tag, out var command)) return;
            _revision = Math.Max(_revision, _lastFrame.LastCommand) + 1;
            int work = 0; float sharpness = -1;
            if (command == CommandKind.SetWorkResolution) work = new[] {100,85,66}[Math.Max(0,SessionWorkSelector.SelectedIndex)];
            if (command == CommandKind.SetSharpness) sharpness = new[] {0f,.15f,.3f}[Math.Max(0,SessionSharpSelector.SelectedIndex)];
            _channel.Send(new TelemetryCommand { Revision = _revision, Kind = command, SessionId = _lastFrame.SessionId, IntValue=work, FloatValue=sharpness });
            LogHub("Comando in-game enviado: " + command + " (#" + _revision + ")");
        }

        private async void BtnRefreshDiagnostics_Click(object sender, RoutedEventArgs e) => await RunFullDiagnosticsAsync();

        // Abrir y cerrar CS1 desde el Hub. El cierre viaja por el mismo canal de comandos que el
        // resto: el mod ejecuta la salida normal del juego. Nunca se termina el proceso a la fuerza,
        // porque eso sí perdería la ciudad sin remedio.
        /// <summary>
        /// Pide el cierre y espera. Primero al mod, que ejecuta la salida propia del juego;
        /// si no hay telemetría —el mod solo publica dentro de una ciudad— se le pide a la
        /// ventana, igual que al pulsar la X. Nunca se termina el proceso a la fuerza.
        /// </summary>
        private async Task<bool> CloseGameAsync()
        {
            if (_channel != null && _pid != 0)
            {
                SendTelemetryCommand(CommandKind.QuitGame.ToString());
                if (await WaitForGameExitAsync(20)) return true;
            }
            int pid = await Task.Run(() => FindGameProcess(_hardwareInfo.GameExePath));
            if (pid == 0) return true;
            try
            {
                using var game = Process.GetProcessById(pid);
                if (!game.CloseMainWindow()) LogHub("El juego no aceptó la petición de cierre de su ventana.");
            }
            catch (Exception ex) { LogHub("Cerrar el juego: " + ex.Message); }
            return await WaitForGameExitAsync(60);
        }

        private async Task<bool> WaitForGameExitAsync(int seconds = 60)
        {
            string path = _hardwareInfo.GameExePath;
            for (int attempt = 0; attempt < seconds * 2 && !_closed; attempt++)
            {
                if (await Task.Run(() => FindGameProcess(path)) == 0)
                {
                    _hardwareInfo.IsGameRunning = false; _pid = 0;
                    _channel?.Dispose(); _channel = null; _lastFrame = default;
                    await Task.Delay(1500);   // deja que el proceso suelte los archivos del juego
                    return true;
                }
                await Task.Delay(500);
            }
            return false;
        }

        private bool LaunchGame()
        {
            try
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/255710") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) { LogHub("Lanzar el juego: " + ex.Message); return false; }
        }

        private async void BtnGameProcess_Click(object sender, RoutedEventArgs e)
        {
            if (_hardwareInfo.IsGameRunning)
            {
                if (MessageBox.Show(this,
                        "Se pedirá a Cities: Skylines que se cierre por su salida normal.\n\nGuarda la ciudad antes: NeuralFX no la guarda por ti.",
                        "Cerrar Cities: Skylines", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
                await PerformAsync("Cerrando Cities: Skylines", async () => await CloseGameAsync()
                    ? new(true, "Juego cerrado", "Cities: Skylines ya no está en ejecución.")
                    : new(false, "El juego sigue abierto", "No respondió a la petición de cierre. Ciérralo a mano."));
                return;
            }

            if (LaunchGame()) SetNotice("Abriendo Cities: Skylines", "Lanzado a través de Steam. El Hub seguirá el proceso cuando arranque.");
            else SetNotice("No se pudo lanzar el juego", "Ábrelo desde Steam. El detalle está en el registro del Hub.", false);
        }

        private async void BtnBrowseGame_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Seleccionar Cities.exe", Filter = "Cities.exe|Cities.exe" };
            if (dialog.ShowDialog() != true) return;
            _preferences.GameExecutable = dialog.FileName;
            try { _preferences.Save(); } catch (Exception ex) { LogHub("Preferencias: " + ex.Message); }
            await RunFullDiagnosticsAsync();
        }

        private void SetNotice(string title, string detail, bool? success = null)
        {
            OperationNotice.Visibility = Visibility.Visible;
            TxtOperationTitle.Text = title; TxtOperationDetail.Text = detail;
            OperationNotice.Background = new SolidColorBrush(success == true ? Color.FromRgb(27, 56, 43) : success == false ? Color.FromRgb(65, 39, 32) : Color.FromRgb(28, 52, 69));
            OperationNotice.BorderBrush = new SolidColorBrush(success == true ? Color.FromRgb(78, 150, 113) : success == false ? Color.FromRgb(188, 123, 88) : Color.FromRgb(73, 125, 156));
        }

        private async Task PerformAsync(string title, Func<Task<OperationResult>> action)
        {
            if (_busy) return;
            _busy = true; _operationLastMessage = "";
            PipelineActions.IsEnabled = false; BtnBrowseGame.IsEnabled = false; BtnRefreshDiagnostics.IsEnabled = false;
            SetNotice(title, "Operación en curso..."); OperationProgress.Visibility = Visibility.Visible;
            OperationResult result;
            try
            {
                result = await action();
            }
            catch (Exception ex) { result = new(false, "No se pudo completar la operación", ex.Message + " Consulta el registro para ver el detalle."); }
            finally
            {
                await RunFullDiagnosticsAsync();
                _busy = false; PipelineActions.IsEnabled = true; BtnBrowseGame.IsEnabled = true; BtnRefreshDiagnostics.IsEnabled = true;
                OperationProgress.Visibility = Visibility.Collapsed;
                await PollBackgroundAsync();
            }
            SetNotice(result.Title, result.Detail, result.Success);
            LogHub(result.Title + ": " + result.Detail);
        }

        private async void BtnDownloadSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DependencyItem item }) await PerformAsync("Descargando " + item.DisplayName, async () =>
            {
                bool success = await _dependencyManager.DownloadDependencyAsync(item, LogHub);
                return new(success, success ? "Descarga verificada" : "La descarga no se completó", success ? item.DisplayName + " ya está disponible en el almacén local. Listo para instalar." : item.StatusMessage);
            });
        }

        private async void BtnDownloadAllPublic_Click(object sender, RoutedEventArgs e) => await PerformAsync("Descargando componentes públicos", async () =>
        {
            int needed = _dependencies.Count(x => x.CanDownload);
            int count = await _dependencyManager.DownloadAllPublicMissingAsync(_dependencies, LogHub);
            return new(count == needed, count == needed ? "Descargas completadas" : "Descarga parcial", $"{count} de {needed} componentes descargados y verificados con éxito en el almacén local.");
        });

        private async void BtnAutoDetectDownloads_Click(object sender, RoutedEventArgs e) => await PerformAsync("Buscando archivos en Descargas", async () =>
        {
            int before = _dependencies.Count(x => x.IsReady);
            await Task.Run(() => _dependencyManager.AutoDetectAndImportFromDownloads(_dependencies, LogHub));
            int count = _dependencies.Count(x => x.IsReady) - before;
            return new(count > 0, count > 0 ? "Archivos encontrados e importados" : "No se encontraron nuevos archivos en Descargas", count > 0
                ? $"{count} componentes guardados y verificados en el almacén local. ¡Listos para instalar!"
                : "Usa el botón 'Importar' en los componentes de NVIDIA para seleccionar nvngx_dlss.dll o nvngx_dlssnr.dll.");
        });

        private async void BtnImportDependency_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: DependencyItem item }) return;
            var dialog = new OpenFileDialog { Title = "Importar " + item.DisplayName, Filter = "Binarios y paquetes|*.dll;*.zip;*.exe;*.addon64|Todos los archivos|*.*" };
            if (dialog.ShowDialog() != true) return;
            await PerformAsync("Importando " + item.DisplayName, async () =>
            {
                bool success = await _dependencyManager.ImportFileAsync(item, dialog.FileName);
                return new(success, success ? "Archivo importado y verificado" : "Importación rechazada", success ? item.DisplayName + " está listo en el almacén local. Pulsa 'Instalar' para aplicarlo al juego." : item.StatusMessage);
            });
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (GameDirectory is not string root) return;

            // ReShade no se puede escribir con el juego abierto, así que el Hub lo cierra por la
            // salida normal del juego y lo vuelve a abrir al terminar. Nunca mata el proceso.
            bool restart = false;
            if (_hardwareInfo.IsGameRunning)
            {
                if (MessageBox.Show(this,
                        "Se cerrará Cities: Skylines para escribir los archivos y se volverá a abrir al terminar.\n\nGuarda la ciudad antes: NeuralFX no la guarda por ti.",
                        "Cerrar, instalar y volver a abrir", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
                SetNotice("Cerrando Cities: Skylines", "Esperando a que el juego termine de cerrarse antes de escribir nada.");
                if (!await CloseGameAsync())
                {
                    SetNotice("El juego sigue abierto",
                        "No se escribió nada. Ciérralo a mano y vuelve a intentarlo.", false);
                    return;
                }
                restart = true;
            }

            await PerformAsync("Instalando y verificando archivos del juego", async () =>
            {
                var preset = (PipelinePreset)PresetSelector.SelectedIndex;
                var preview = await Task.Run(() => InstallationPreview.Create(root, _dependencies, _dependencyManager, preset));
                if (!ReviewInstallation(preview.Description)) return new(null, "Instalación cancelada", "No se aplicaron cambios.");
                bool success = await _installEngine.InstallAsync(root, _dependencies, LogHub, preset: preset, preview: preview);
                if (!success) return new(false, "La instalación no se completó", _operationLastMessage);

                var verified = await Task.Run(() => IntegrityMonitor.Scan(root, _dependencies.SelectMany(x => _dependencyManager.GetPackageFiles(x).Values)));
                if (!verified.Valid || verified.NeedsRepair) return new(false,"Archivos aplicados; verificación incompleta",string.Join("\n",verified.Details));
                _preferences.Preset = preset;
                try { _preferences.Save(); } catch (Exception ex) { LogHub("No se pudo recordar el preset: " + ex.Message); }
                _integrity.Invalidate();

                string presetName = (int)preset >= 0 && (int)preset < PresetNames.Length ? PresetNames[(int)preset] : preset.ToString();
                if (restart) LaunchGame();
                return new(true, "Pipeline instalado y verificado", (restart ? "Abriendo Cities: Skylines de nuevo. " : "") + "Componentes aplicados con el preset " + presetName + ".\nEl mod y el Hub se actualizan con el instalador del paquete.");
            });
        }

        private bool ReviewInstallation(string description)
        {
            var text = new TextBox { Text = description, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) };
            var apply = new Button { Content = "Aplicar este plan", IsDefault = true, Margin = new Thickness(12), Padding = new Thickness(16,8,16,8) };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(12) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(apply); actions.Children.Add(cancel);
            var panel = new DockPanel(); DockPanel.SetDock(actions,Dock.Bottom); panel.Children.Add(actions); panel.Children.Add(text);
            var window = new Window { Owner=this,Title="Revisar cambios de archivos",Width=Math.Min(760,SystemParameters.WorkArea.Width),Height=Math.Min(620,SystemParameters.WorkArea.Height),Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner };
            apply.Click += (_,_)=>window.DialogResult=true;
            return window.ShowDialog()==true;
        }
        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (GameDirectory is not string root) return;
            await PerformAsync("Preparando restauración", async () => {
                var report = await Task.Run(() => IntegrityMonitor.Scan(root));
                if (report.Restoration == null) return new(false,"Restauración no disponible",string.Join("\n",report.Details));
                var manifest = ManifestStore.Read(root);
                var snapshot = new InstallationPreview();
                foreach (string relative in manifest.InstalledFiles.Concat(manifest.BackedUpFiles.Values).Concat(new[] {"NeuralFX_Manifest.json"})) {
                    string path=ManagedPaths.Resolve(root,relative);snapshot.ExpectedBefore[relative]=File.Exists(path)?DependencyManagerService.CalculateSha256(path):null;
                }
                if (!ReviewInstallation(report.Restoration.Details)) return new(null,"Restauración cancelada","No se aplicaron cambios.");
                bool restored=await new RollbackService().RollbackAsync(root,LogHub,preview:snapshot);
                _integrity.Invalidate();return new(restored,restored?"Estado anterior restaurado":"Restauración incompleta",_operationLastMessage);
            });
        }
        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (GameDirectory is not string root) return;
            await PerformAsync("Preparando desinstalación", async () => {
                await _uninstaller.RecoverAsync(root);
                var plan = await Task.Run(() => UninstallService.Inspect(root));
                if (!ReviewInstallation(plan.Details)) return new(null,"Desinstalación cancelada","No se aplicaron cambios.");
                var result=await _uninstaller.UninstallAsync(plan,LogHub);
                _integrity.Invalidate();
                return new(result.Success,result.Success?"Componentes gráficos retirados":"Desinstalación incompleta",result.Message);
            });
        }

        private void LogHub(string message)
        {
            if (_busy) _operationLastMessage = message;
            if (_closed) return;
            Dispatcher.InvokeAsync(() =>
            {
                _hubLogs.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " " + message);
                if (_hubLogs.Length > IncrementalLogReader.Limit) _hubLogs.Remove(0, _hubLogs.Length - IncrementalLogReader.Limit);
                if (RadioLogHub != null && RadioLogHub.IsChecked == true && TxtLogConsole != null) ShowLog(_hubLogs.ToString());
            });
        }

        private void ShowLog(string text)
        {
            if (TxtLogConsole == null || TxtLogConsole.Text == text) return;
            TxtLogConsole.Text = text; TxtLogConsole.ScrollToEnd();
        }

        private async void RadioLog_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || RadioLogHub == null || TxtLogConsole == null) return;
            if (RadioLogHub.IsChecked == true) ShowLog(_hubLogs.ToString());
            else await PollBackgroundAsync();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (TxtLogConsole == null) return;
            if (RadioLogHub?.IsChecked == true) _hubLogs.Clear();
            TxtLogConsole.Clear();
        }

        private void OpenLocation(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { LogHub(ex.Message); }
        }

        private void BtnOpenDownloads_Click(object sender, RoutedEventArgs e) => OpenLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        private void BtnOpenCache_Click(object sender, RoutedEventArgs e) => OpenLocation(_dependencyManager.CacheDirectory);
        private void BtnOpenModFolder_Click(object sender, RoutedEventArgs e) => OpenLocation(ModDirectory);
        private void BtnShowLogs_Click(object sender, RoutedEventArgs e) { MainTabs.SelectedIndex = 2; RadioLogHub.IsChecked = true; }
        private void BtnOpenGameFolder_Click(object sender, RoutedEventArgs e) { if (GameDirectory is string root) OpenLocation(root); }

        private void BtnOpenWebOfficial_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: DependencyItem item } && Uri.TryCreate(item.OfficialWebUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https") OpenLocation(uri.AbsoluteUri);
        }

        private async Task CheckUpdatesAsync()
        {
            try { TxtUpdates.Text = await new ReleaseUpdateService().CheckAsync(_dependencies); }
            catch (Exception ex) { TxtUpdates.Text = "No se pudieron consultar novedades: " + ex.Message; }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync();
    }
}

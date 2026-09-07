using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;

namespace NeuralFX.Hub
{
    public partial class MainWindow : Window
    {
        private readonly HardwareDiagnosticsService _diagnosticsService;
        private readonly DependencyManagerService _dependencyManager;
        private readonly InstallationEngineService _installEngine;
        private readonly RollbackService _rollbackService;

        private HardwareInfo _hardwareInfo = new();
        private List<DependencyItem> _dependencies = new();
        private readonly StringBuilder _hubLogs = new();
        private readonly DispatcherTimer _liveLogTimer;

        public MainWindow()
        {
            InitializeComponent();

            _diagnosticsService = new HardwareDiagnosticsService();
            _dependencyManager = new DependencyManagerService();
            _installEngine = new InstallationEngineService(_dependencyManager);
            _rollbackService = new RollbackService();

            _liveLogTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _liveLogTimer.Tick += LiveLogTimer_Tick;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LogHub("NeuralFX Hub v1.0.0 inicializado.");
            LogHub($"Caché local de dependencias: {_dependencyManager.CacheDirectory}");
            RunFullDiagnostics();
            _liveLogTimer.Start();
        }

        private void RunFullDiagnostics()
        {
            LogHub("Ejecutando diagnóstico de hardware y software...");
            _hardwareInfo = _diagnosticsService.RunDiagnostics();

            // Actualizar UI Hardware
            TxtGpuName.Text = _hardwareInfo.GpuName;
            TxtArchitecture.Text = $"Arquitectura: {_hardwareInfo.Architecture} | DLSS 5 Soportado: {(_hardwareInfo.SupportsDLSS5 ? "SÍ (Neural Reconstruction)" : "Solo DLSS 2/3")}";
            
            if (_hardwareInfo.IsNvidia)
            {
                TxtBadgeGpu.Text = "COMPATIBLE";
                BadgeGpu.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x4E, 0x26));
            }
            else
            {
                TxtBadgeGpu.Text = "NO NVIDIA";
                BadgeGpu.Background = new SolidColorBrush(Color.FromRgb(0x8C, 0x24, 0x24));
            }

            TxtVram.Text = _hardwareInfo.VramDisplay;
            if (_hardwareInfo.VramGB >= 8.0)
            {
                TxtBadgeVram.Text = ">= 8 GB (ÓPTIMO)";
                BadgeVram.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x4E, 0x26));
            }
            else if (_hardwareInfo.VramGB >= 4.0)
            {
                TxtBadgeVram.Text = "4-8 GB (ACEPTABLE)";
                BadgeVram.Background = new SolidColorBrush(Color.FromRgb(0x8C, 0x6E, 0x24));
            }
            else
            {
                TxtBadgeVram.Text = "< 4 GB (INSUFICIENTE)";
                BadgeVram.Background = new SolidColorBrush(Color.FromRgb(0x8C, 0x24, 0x24));
            }

            TxtDriverVer.Text = $"Versión NVIDIA: {_hardwareInfo.ParsedDriverVersion}";
            TxtDriverRaw.Text = $"Driver Sistema Windows: {_hardwareInfo.RawDriverVersion}";
            if (_hardwareInfo.DriverMeetsRequirement)
            {
                TxtBadgeDriver.Text = ">= 570.xx (CUMPLE)";
                BadgeDriver.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x4E, 0x26));
            }
            else
            {
                TxtBadgeDriver.Text = "< 570.xx (REQUIERE UPDATE)";
                BadgeDriver.Background = new SolidColorBrush(Color.FromRgb(0x8C, 0x24, 0x24));
            }

            // Actualizar UI Cities Skylines
            if (_hardwareInfo.GameFound)
            {
                TxtGameExe.Text = _hardwareInfo.GameExePath;
                TxtWritePerm.Text = _hardwareInfo.CanWriteGameDir ? "SÍ (Permiso Completo)" : "NO (Requiere permisos de Admin)";
                TxtWritePerm.Foreground = _hardwareInfo.CanWriteGameDir 
                    ? (SolidColorBrush)FindResource("Success") 
                    : (SolidColorBrush)FindResource("Danger");

                if (_hardwareInfo.IsGameRunning)
                {
                    TxtGameProcess.Text = "EN EJECUCIÓN (Cierra el juego antes de inyectar/desinstalar)";
                    TxtGameProcess.Foreground = (SolidColorBrush)FindResource("Danger");
                }
                else
                {
                    TxtGameProcess.Text = "CERRADO (Listo para inyección o rollback)";
                    TxtGameProcess.Foreground = (SolidColorBrush)FindResource("Success");
                }
            }
            else
            {
                TxtGameExe.Text = "No detectado automáticamente en Steam.";
                TxtWritePerm.Text = "N/A";
                TxtGameProcess.Text = "N/A";
            }

            RefreshDependenciesList();
            UpdateInjectionStatusPill();
        }

        private void RefreshDependenciesList()
        {
            string? gameDir = _hardwareInfo.GameFound ? Path.GetDirectoryName(_hardwareInfo.GameExePath) : null;
            _dependencies = _dependencyManager.GetInitialDependencies(gameDir);
            ListDependencies.ItemsSource = null;
            ListDependencies.ItemsSource = _dependencies;
        }

        private void UpdateInjectionStatusPill()
        {
            if (!_hardwareInfo.GameFound)
            {
                TxtGameStatus.Text = "ESTADO: JUEGO NO LOCALIZADO";
                PillGameStatus.Background = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
                return;
            }

            string gameDir = Path.GetDirectoryName(_hardwareInfo.GameExePath)!;
            bool active = _rollbackService.IsInjectionActive(gameDir);

            if (active)
            {
                TxtGameStatus.Text = "ESTADO: INYECCIÓN DLSS 5 / RESHADE ACTIVA";
                PillGameStatus.Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C));
                BtnInstall.Content = "Actualizar / Re-inyectar Pipeline";
            }
            else
            {
                TxtGameStatus.Text = "ESTADO: VANILLA LIMPIO (Zero-Trace)";
                PillGameStatus.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x4E, 0x26));
                BtnInstall.Content = "Instalar / Inyectar Pipeline DLSS 5";
            }
        }

        private void BtnRefreshDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            RunFullDiagnostics();
        }

        private void BtnBrowseGame_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar Cities.exe",
                Filter = "Cities.exe|Cities.exe|Todos los archivos (*.*)|*.*",
                InitialDirectory = @"C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines"
            };

            if (dlg.ShowDialog() == true)
            {
                _hardwareInfo.GameExePath = dlg.FileName;
                _hardwareInfo.GameFound = true;
                _hardwareInfo.CanWriteGameDir = HardwareDiagnosticsService.TestDirectoryWritable(Path.GetDirectoryName(dlg.FileName)!);
                RunFullDiagnostics();
            }
        }

        private async void BtnImportDependency_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DependencyItem item)
            {
                var dlg = new OpenFileDialog
                {
                    Title = $"Importar archivo para {item.DisplayName}",
                    Filter = $"{Path.GetFileName(item.TargetRelativePath)}|{Path.GetFileName(item.TargetRelativePath)}|Archivos de biblioteca (*.dll)|*.dll|Todos los archivos (*.*)|*.*",
                    InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                };

                if (dlg.ShowDialog() == true)
                {
                    LogHub($"Importando {dlg.FileName} para {item.Id}...");
                    bool success = await _dependencyManager.ImportFileAsync(item, dlg.FileName);
                    if (success)
                    {
                        LogHub($"Archivo {Path.GetFileName(dlg.FileName)} importado a la caché con éxito.");
                        RefreshDependenciesList();
                    }
                    else
                    {
                        LogHub($"Error al importar {dlg.FileName}.");
                    }
                }
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!_hardwareInfo.GameFound)
            {
                MessageBox.Show("No se ha localizado Cities.exe. Por favor localiza el juego antes de instalar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (HardwareDiagnosticsService.IsCitiesSkylinesRunning())
            {
                MessageBox.Show("Cities: Skylines se encuentra actualmente en ejecución.\n\nPor favor cierra completamente el juego antes de proceder para que Windows permita escribir y registrar las librerías nativas.", "Cierra el Juego", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "¿Deseas proceder con la inyección del pipeline DLSS 5 y ReShade en Cities: Skylines?\n\nSe creará un manifiesto atómico (NeuralFX_Manifest.json) que permitirá rollback 100% limpio en cualquier momento.",
                "Confirmar Inyección",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            string gameDir = Path.GetDirectoryName(_hardwareInfo.GameExePath)!;
            BtnInstall.IsEnabled = false;
            BtnRollback.IsEnabled = false;

            try
            {
                bool success = await _installEngine.InstallAsync(gameDir, _dependencies, LogHub);
                if (success)
                {
                    MessageBox.Show("Pipeline DLSS 5 instalado con éxito.\n\nPuedes ejecutar Cities: Skylines ahora.\n- Pulsa Home para ReShade.\n- Pulsa Ctrl + Alt + N para el panel de telemetría de NeuralFX Mod.", "Instalación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Ocurrió un error o faltan dependencias obligatorias. Revisa el registro en la pestaña de Logs.", "Error en Instalación", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                BtnRollback.IsEnabled = true;
                RunFullDiagnostics();
            }
        }

        private async void BtnRollback_Click(object sender, RoutedEventArgs e)
        {
            if (!_hardwareInfo.GameFound)
            {
                MessageBox.Show("No se ha localizado Cities.exe.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (HardwareDiagnosticsService.IsCitiesSkylinesRunning())
            {
                MessageBox.Show("Cities: Skylines se encuentra actualmente en ejecución.\n\nPor favor cierra completamente el juego antes de proceder con el rollback para liberar los archivos nativos.", "Cierra el Juego", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "¿Deseas desinstalar completamente el pipeline y restaurar Cities: Skylines a estado vanilla?\n\nSe eliminarán todos los binarios inyectados (dxgi.dll, DLSS5-Feeder, shaders, configs y logs) sin dejar rastro alguno.",
                "Confirmar Rollback Atómico",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            string gameDir = Path.GetDirectoryName(_hardwareInfo.GameExePath)!;
            BtnInstall.IsEnabled = false;
            BtnRollback.IsEnabled = false;

            try
            {
                bool clean = await _rollbackService.RollbackAsync(gameDir, LogHub);
                if (clean)
                {
                    MessageBox.Show("Rollback completado con éxito. El juego ha quedado en estado Vanilla Zero-Trace.", "Rollback Exitoso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Rollback finalizado con advertencias. Revisa los detalles en la pestaña de logs.", "Aviso de Rollback", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                BtnRollback.IsEnabled = true;
                RunFullDiagnostics();
            }
        }

        private void LogHub(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _hubLogs.AppendLine(message);
                if (RadioLogHub.IsChecked == true)
                {
                    TxtLogConsole.Text = _hubLogs.ToString();
                    TxtLogConsole.ScrollToEnd();
                }
            });
        }

        private void LiveLogTimer_Tick(object? sender, EventArgs e)
        {
            if (!_hardwareInfo.GameFound) return;
            string gameDir = Path.GetDirectoryName(_hardwareInfo.GameExePath)!;

            if (RadioLogReShade.IsChecked == true)
            {
                string path = Path.Combine(gameDir, "ReShade.log");
                ReadLogFileSafe(path);
            }
            else if (RadioLogFeeder.IsChecked == true)
            {
                string path = Path.Combine(gameDir, "dlss5-feed.log");
                ReadLogFileSafe(path);
            }
        }

        private void ReadLogFileSafe(string path)
        {
            if (!File.Exists(path))
            {
                TxtLogConsole.Text = $"[Archivo {Path.GetFileName(path)} no encontrado en {Path.GetDirectoryName(path)}]";
                return;
            }

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string content = reader.ReadToEnd();
                TxtLogConsole.Text = content;
                TxtLogConsole.ScrollToEnd();
            }
            catch (Exception ex)
            {
                TxtLogConsole.Text = $"Error leyendo {Path.GetFileName(path)}: {ex.Message}";
            }
        }

        private void RadioLog_Checked(object sender, RoutedEventArgs e)
        {
            if (RadioLogHub.IsChecked == true)
            {
                TxtLogConsole.Text = _hubLogs.ToString();
                TxtLogConsole.ScrollToEnd();
            }
            else
            {
                LiveLogTimer_Tick(null, EventArgs.Empty);
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (RadioLogHub.IsChecked == true)
            {
                _hubLogs.Clear();
                TxtLogConsole.Text = string.Empty;
            }
            else
            {
                TxtLogConsole.Text = string.Empty;
            }
        }

        private void BtnOpenGameFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_hardwareInfo.GameFound)
            {
                string dir = Path.GetDirectoryName(_hardwareInfo.GameExePath)!;
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        }
    }
}

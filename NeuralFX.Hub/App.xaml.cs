using System;
using System.Windows;

namespace NeuralFX.Hub
{
    public partial class App : Application
    {
        private System.Threading.Mutex? _instance;
        private bool _ownsInstance;
        protected override void OnStartup(StartupEventArgs e)
        {
            _instance = new System.Threading.Mutex(false, "Local\\NeuralFX_Hub_Control_v2");
            try { _ownsInstance = _instance.WaitOne(0); }
            catch (System.Threading.AbandonedMutexException) { _ownsInstance = true; }
            if (!_ownsInstance) { MessageBox.Show("NeuralFX Hub ya está abierto."); Shutdown(); return; }
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                MessageBox.Show($"Error no controlado en NeuralFX Hub:\n\n{args.ExceptionObject}", "Error Fatal en NeuralFX Hub", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Error de interfaz en NeuralFX Hub:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "Error en NeuralFX Hub", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsInstance) _instance?.ReleaseMutex();
            _instance?.Dispose();
            base.OnExit(e);
        }
    }
}

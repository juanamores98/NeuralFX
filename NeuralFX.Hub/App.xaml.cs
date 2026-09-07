using System;
using System.Windows;

namespace NeuralFX.Hub
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
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
    }
}

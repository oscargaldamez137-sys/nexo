using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Nexo
{
    public partial class App : Application
    {
        private Mutex _single;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Una sola ventana de Nexo: un segundo doble clic no abre otra copia.
            _single = new Mutex(true, "Nexo.Launcher.SingleInstance", out var created);
            if (!created)
            {
                Shutdown();
                return;
            }
            DispatcherUnhandledException += OnUnhandled;
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _single?.Dispose();
            base.OnExit(e);
        }

        private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Algo salió mal en Nexo:\n\n" + e.Exception.Message, "Nexo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}

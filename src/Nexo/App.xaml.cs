using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Nexo
{
    public partial class App : Application
    {
        private Mutex _single;

        /// <summary>
        /// Solo para pruebas: «--nexo-captura=inicio|opciones|carpetas» abre Nexo en una ventana normal, sin activarla
        /// ni mostrarla en la barra de tareas, con ese panel abierto, sin consultar actualizaciones, y se cierra sola a los 30 s.
        /// Sirve para capturar la ventana con PrintWindow sin enviar teclas ni clics. null en un arranque normal.
        /// </summary>
        internal static string CaptureView { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            foreach (var arg in e.Args)
                if (arg.StartsWith("--nexo-captura=", StringComparison.OrdinalIgnoreCase))
                {
                    string view = arg.Substring("--nexo-captura=".Length).ToLowerInvariant();
                    if (view == "inicio" || view == "opciones" || view == "carpetas") CaptureView = view;
                }
            // Una sola ventana de Nexo: un segundo doble clic no abre otra copia (la captura de prueba no cuenta).
            if (CaptureView == null)
            {
                _single = new Mutex(true, "Nexo.Launcher.SingleInstance", out var created);
                if (!created)
                {
                    Shutdown();
                    return;
                }
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
            if (CaptureView != null) { e.Handled = true; Current?.Shutdown(1); return; } // nunca un diálogo en una captura
            MessageBox.Show("Algo salió mal en Nexo:\n\n" + e.Exception.Message, "Nexo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nexo.Core;

namespace Nexo
{
    // Panel de Opciones y Carpetas: se maneja con ratón, teclado (↑↓ Enter Esc) y mando (▲▼ A B).
    public partial class MainWindow
    {
        private enum OverlayPage { None, Options, Folders }
        private OverlayPage _overlay = OverlayPage.None;
        private bool _foldersFromOptions;     // B en Carpetas vuelve a Opciones si se llegó desde allí
        private int _overlayIndex;
        private readonly List<OverlayRow> _rows = new List<OverlayRow>();
        private bool _changingFolder, _checkingUpdates, _artDirty;

        private sealed class OverlayRow
        {
            public Border Chrome;
            public Action Activate;
            public bool IsToggle;
            public bool Enabled = true;
        }

        private bool OverlayOpen => _overlay != OverlayPage.None;

        private void OpenOverlay(OverlayPage page, bool fromOptions = false)
        {
            if (_service == null) return;
            bool wasOpen = OverlayOpen;
            if (_gamesMode) ExitGames(silent: true);
            _overlay = page;
            _foldersFromOptions = page == OverlayPage.Folders && fromOptions;
            _overlayIndex = 0;
            BuildOverlay();
            if (!wasOpen)
            {
                _sounds.Enter();
                Overlay.Visibility = Visibility.Visible;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                OverlayMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            }
            else _sounds.Move(_overlayIndex);
        }

        private void CloseOverlay()
        {
            if (!OverlayOpen) return;
            if (_overlay == OverlayPage.Folders && _foldersFromOptions) { OpenOverlay(OverlayPage.Options); _overlayIndex = 0; Highlight(); return; }
            _overlay = OverlayPage.None;
            _sounds.Back();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) => { if (!OverlayOpen) Overlay.Visibility = Visibility.Collapsed; };
            Overlay.BeginAnimation(OpacityProperty, fade);
            // Con las portadas en línea recién activadas, la estantería vuelve a pedir el arte que falte.
            if (_artDirty) { _artDirty = false; _artRequested.Clear(); RefreshCurrent(force: true); }
            Keyboard.Focus(this);
        }

        // ---------------------------------------------------------------- Construcción

        private void BuildOverlay()
        {
            OverlayRows.Children.Clear();
            _rows.Clear();
            if (_overlay == OverlayPage.Options) BuildOptionsPage(); else BuildFoldersPage();
            _overlayIndex = Math.Max(0, Math.Min(_overlayIndex, _rows.Count - 1));
            Highlight();
            UpdateOverlayHints();
        }

        private void BuildOptionsPage()
        {
            OverlayKicker.Text = "NEXO " + AppUpdates.Current.ToString(3);
            OverlayTitle.Text = "Opciones";
            OverlaySubtitle.Text = "Se guardan en nexo.json, junto a Nexo. Por defecto todo está activado, como siempre.";

            Section("BIBLIOTECA");
            Row(Icons.Folder, "Carpetas de juegos y emuladores",
                "Juegos: " + Short(_service.GamesRoot) + "\nEmuladores: " + Short(_service.EmulatorsRoot),
                Chevron("Cambiar"), () => OpenOverlay(OverlayPage.Folders, fromOptions: true));
            Toggle(Icons.Image, "Descargar portadas y fondos en línea",
                "Busca lo que falte en GameTDB, libretro-thumbnails y ps2-covers. Desactivado, Nexo no se conecta para esto: usa solo tus imágenes y las ya descargadas.",
                NexoOption.DownloadArtwork, on => { if (on) _artDirty = true; });

            Section("AL ABRIR UN JUEGO O EMULADOR");
            Toggle(Icons.Expand, "Abrir juegos a pantalla completa",
                "Añade la opción de pantalla completa de cada emulador. Desactivado, cada uno usa su propio ajuste de ventana. Los argumentos personalizados de nexo.json mandan siempre.",
                NexoOption.FullscreenGames);
            Toggle(Icons.Chip, "Gráficos automáticos",
                "Ajusta motor, resolución y calidad según tu tarjeta gráfica, sin tocar tus ajustes personales. Desactivado, Nexo no cambia la configuración gráfica.",
                NexoOption.AutoGraphics);
            Toggle(Icons.Pad, "Configurar mandos automáticamente",
                "Prepara el mando XInput y el modo portable del emulador si faltan. Desactivado, Nexo no toca los controles del emulador.",
                NexoOption.AutoControllers);

            Section("INTERFAZ");
            Toggle(Icons.Sound, "Sonidos de la interfaz", "Efectos al moverte y al elegir. También con la tecla M.", NexoOption.Sounds, on =>
            {
                _sounds.Muted = !on;
                UpdateSoundIcon();
            });

            Section("ACTUALIZACIONES");
            Toggle(Icons.Download, "Buscar actualizaciones al abrir",
                "Consulta GitHub unos segundos después de abrir Nexo y, si hay una versión nueva, se instala sola. Desactivado, Nexo no se conecta para esto.",
                NexoOption.CheckUpdates);
            Row(Icons.Refresh, "Buscar actualizaciones ahora",
                _checkingUpdates ? "Consultando GitHub…" : UpdateStatusText(),
                Chevron(_checkingUpdates ? "…" : "Buscar"), () => _ = CheckUpdatesAsync(manual: true));

            Section("ACERCA DE");
            Row(Icons.Info, "Nexo " + AppUpdates.Current.ToString(3),
                "Código, novedades y versiones en " + NexoLinks.Repository.Replace("https://", ""),
                Chevron("GitHub"), () => OpenLink(NexoLinks.Repository, "Abriendo el repositorio de Nexo en tu navegador."));
            Row(Icons.Heart, "Donar", "Si Nexo te resulta útil, puedes apoyarlo en " + NexoLinks.Donate.Replace("https://", "") + ".",
                Chevron("Ko-fi"), OpenDonate);
        }

        private void BuildFoldersPage()
        {
            OverlayKicker.Text = "OPCIONES  ·  BIBLIOTECA";
            OverlayTitle.Text = "Carpetas";
            OverlaySubtitle.Text = "Nexo crea dentro una carpeta por consola (PS2, Switch…) con un LEEME breve. Cambiar de carpeta no mueve ni borra nada: "
                                   + "lo que haya en la anterior se queda donde está y puedes moverlo tú.";
            FolderSection(true);
            FolderSection(false);
        }

        private void FolderSection(bool games)
        {
            string what = games ? "juegos" : "emuladores";
            string current = games ? _service.GamesRoot : _service.EmulatorsRoot;
            string fallback = games ? _service.DefaultGamesRoot : _service.DefaultEmulatorsRoot;
            bool isDefault = games ? _service.GamesRootIsDefault : _service.EmulatorsRootIsDefault;
            Section(games ? "JUEGOS" : "EMULADORES");
            Row(games ? Icons.Folder : Icons.Box, "Carpeta de " + what, current + (isDefault ? "   (predeterminada)" : ""),
                Chevron("Cambiar"), () => _ = ChangeFolderAsync(games), mono: true);
            Row(Icons.Open, "Abrir la carpeta de " + what, "La abre en el Explorador de Windows.", Chevron("Abrir"),
                () => Run(() => _service.OpenFolder(current)));
            if (!isDefault)
                Row(Icons.Reset, "Restablecer la predeterminada", fallback, Chevron("Restablecer"),
                    () => _ = ApplyFolderAsync(games, null), mono: true);
            string previous = games ? _previousGames : _previousEmulators;
            if (previous != null && !string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
                Row(Icons.History, "Abrir la carpeta anterior", previous + "\nNexo no movió nada: si quieres, mueve tú su contenido a la nueva.", Chevron("Abrir"),
                    () => Run(() => _service.OpenFolder(previous)), mono: true);
        }

        private string _previousGames, _previousEmulators;

        private static string Short(string path) => path != null && path.Length > 70 ? "…" + path.Substring(path.Length - 68) : path;

        private string UpdateStatusText()
        {
            switch (_updateState)
            {
                case UpdateState.Downloading: return "Descargando Nexo " + _update?.Version + "…";
                case UpdateState.Waiting: case UpdateState.Countdown: case UpdateState.Postponed: return "Nexo " + _update?.Version + " descargado: se instalará solo.";
                case UpdateState.Notice: return "Nexo " + _update?.Version + " disponible: usa el aviso de la barra superior.";
                default: return "Tienes la versión " + AppUpdates.Current.ToString(3) + ".";
            }
        }

        // ---------------------------------------------------------------- Filas

        private void Section(string text)
        {
            OverlayRows.Children.Add(new TextBlock
            {
                Text = text, FontSize = 11.5, FontWeight = FontWeights.Bold, Margin = new Thickness(6, OverlayRows.Children.Count == 0 ? 0 : 14, 0, 6),
                Foreground = (Brush)FindResource("Brush.InkFaint"),
            });
        }

        private FrameworkElement Chevron(string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.InkSoft"), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new Path
            {
                Data = Geometry.Parse("M9,5 L16,12 L9,19"), Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(8, 0, 0, 0),
                Stroke = (Brush)FindResource("Brush.InkSoft"), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return row;
        }

        private void Toggle(string icon, string title, string detail, NexoOption option, Action<bool> changed = null)
        {
            bool on = _service.GetOption(option);
            var track = new Border { Width = 50, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(3) };
            var knob = new Ellipse { Width = 22, Height = 22, Fill = Brushes.White };
            track.Child = knob;
            void Paint(bool value)
            {
                track.Background = value ? (Brush)_accent : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                knob.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Fill = value ? new SolidColorBrush(Color.FromRgb(0x07, 0x08, 0x0D)) : Brushes.White;
                track.ToolTip = value ? "Activado" : "Desactivado";
            }
            Paint(on);
            var value = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.InkSoft"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Width = 22, TextAlignment = TextAlignment.Right };
            label.Text = on ? "Sí" : "No";
            value.Children.Add(label);
            value.Children.Add(track);
            var row = Row(icon, title, detail, value, null);
            row.IsToggle = true;
            row.Activate = () =>
            {
                bool next = !_service.GetOption(option);
                try { _service.SetOption(option, next); }
                catch (Exception error) { _sounds.Error(); ShowToast(error.Message, error: true); return; }
                Paint(next);
                label.Text = next ? "Sí" : "No";
                changed?.Invoke(next);
                _sounds.Move(next ? 4 : 1);
            };
        }

        private OverlayRow Row(string icon, string title, string detail, FrameworkElement value, Action activate, bool mono = false)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = new Path
            {
                Data = Geometry.Parse(icon), Width = 20, Height = 20, Stretch = Stretch.Uniform, Margin = new Thickness(0, 2, 16, 0), VerticalAlignment = VerticalAlignment.Top,
                Stroke = (Brush)_accent, StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            grid.Children.Add(glyph);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(detail))
                text.Children.Add(new TextBlock
                {
                    Text = detail, FontSize = mono ? 12.5 : 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 19,
                    Foreground = (Brush)FindResource("Brush.InkFaint"), FontFamily = mono ? (FontFamily)FindResource("Font.Mono") : (FontFamily)FindResource("Font.Ui"),
                });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (value != null)
            {
                value.Margin = new Thickness(18, 0, 0, 0);
                Grid.SetColumn(value, 2);
                grid.Children.Add(value);
            }
            var chrome = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 0, 4),
                BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            };
            var row = new OverlayRow { Chrome = chrome, Activate = activate };
            int index = _rows.Count;
            chrome.MouseEnter += (s, e) => { if (_overlayIndex != index) { _overlayIndex = index; Highlight(); } };
            chrome.MouseLeftButtonUp += (s, e) => { _overlayIndex = index; Highlight(); ActivateRow(); e.Handled = true; };
            _rows.Add(row);
            OverlayRows.Children.Add(chrome);
            return row;
        }

        private void Highlight()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool selected = i == _overlayIndex;
                _rows[i].Chrome.Background = selected ? (Brush)FindResource("Brush.GlassStrong") : Brushes.Transparent;
                _rows[i].Chrome.BorderBrush = selected ? (Brush)_accent : Brushes.Transparent;
            }
            if (_overlayIndex >= 0 && _overlayIndex < _rows.Count) _rows[_overlayIndex].Chrome.BringIntoView();
        }

        private void UpdateOverlayHints()
        {
            OverlayHints.Children.Clear();
            void Hint(string text, params string[] keys)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 0) };
                foreach (var key in keys)
                    row.Children.Add(new Border { Style = (Style)FindResource("Kbd"), Child = new TextBlock { Text = key, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.InkSoft") } });
                row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.InkFaint") });
                OverlayHints.Children.Add(row);
            }
            bool back = _overlay == OverlayPage.Folders && _foldersFromOptions;
            if (_padConnected) { Hint("elegir", "▲", "▼"); Hint("cambiar", "A"); Hint(back ? "volver" : "cerrar", "B"); }
            else { Hint("elegir", "↑", "↓"); Hint("cambiar", "Enter"); Hint(back ? "volver" : "cerrar", "Esc"); }
        }

        // ---------------------------------------------------------------- Entrada

        private void MoveOverlay(int direction)
        {
            if (_rows.Count == 0) return;
            int next = Math.Max(0, Math.Min(_rows.Count - 1, _overlayIndex + direction));
            if (next == _overlayIndex) return;
            _overlayIndex = next;
            Highlight();
            _sounds.Move(next);
        }

        private void ActivateRow()
        {
            if (_overlayIndex < 0 || _overlayIndex >= _rows.Count) return;
            var row = _rows[_overlayIndex];
            if (!row.Enabled || row.Activate == null) return;
            row.Activate();
        }

        private void ToggleRow()
        {
            if (_overlayIndex >= 0 && _overlayIndex < _rows.Count && _rows[_overlayIndex].IsToggle) ActivateRow();
        }

        private void OverlayKey(Key key)
        {
            switch (key)
            {
                case Key.Up: case Key.W: MoveOverlay(-1); break;
                case Key.Down: case Key.S: MoveOverlay(1); break;
                case Key.Left: case Key.Right: case Key.A: case Key.D: ToggleRow(); break;
                case Key.Home: _overlayIndex = 0; Highlight(); break;
                case Key.End: _overlayIndex = _rows.Count - 1; Highlight(); break;
                case Key.Enter: case Key.Space: ActivateRow(); break;
                case Key.Escape: case Key.Back: CloseOverlay(); break;
                case Key.O: if (_overlay == OverlayPage.Options) CloseOverlay(); else OpenOverlay(OverlayPage.Options); break;
                case Key.C: if (_overlay == OverlayPage.Folders && !_foldersFromOptions) CloseOverlay(); else OpenOverlay(OverlayPage.Folders, _overlay == OverlayPage.Options); break;
                case Key.M: ToggleSound(); break;
                case Key.F11: ToggleFullscreen(); break;
            }
        }

        private void OverlayPad(PadButton button)
        {
            switch (button)
            {
                case PadButton.Up: MoveOverlay(-1); break;
                case PadButton.Down: MoveOverlay(1); break;
                case PadButton.Left: case PadButton.Right: ToggleRow(); break;
                case PadButton.A: ActivateRow(); break;
                case PadButton.B: CloseOverlay(); break;
                case PadButton.Start: _foldersFromOptions = false; CloseOverlay(); break;
            }
        }

        // ---------------------------------------------------------------- Carpetas

        private string FolderBusyReason()
        {
            if (_extracting.Count > 0) return "Espera a que termine de descomprimir o instalar el juego antes de cambiar de carpeta.";
            if (_installing.Count > 0) return "Espera a que termine la instalación del emulador antes de cambiar de carpeta.";
            if (_importing) return "Espera a que termine la importación antes de cambiar de carpeta.";
            return null;
        }

        private async Task ChangeFolderAsync(bool games)
        {
            if (_changingFolder) return;
            var busy = FolderBusyReason();
            if (busy != null) { _sounds.Error(); ShowToast(busy, error: true); return; }
            string chosen = FolderPicker.Pick(this, games ? "Elige la carpeta de tus juegos (Nexo creará dentro una carpeta por consola)"
                                                          : "Elige la carpeta de tus emuladores (Nexo creará dentro una carpeta por consola)",
                                              games ? _service.GamesRoot : _service.EmulatorsRoot);
            if (chosen == null) return;
            await ApplyFolderAsync(games, chosen);
        }

        // path null: restablecer la predeterminada.
        private async Task ApplyFolderAsync(bool games, string path)
        {
            if (_changingFolder) return;
            var busy = FolderBusyReason();
            if (busy != null) { _sounds.Error(); ShowToast(busy, error: true); return; }
            _changingFolder = true;
            FolderChangeResult result;
            try { result = await Task.Run(() => games ? _service.SetGamesRoot(path) : _service.SetEmulatorsRoot(path)); }
            catch (Exception error)
            {
                _changingFolder = false;
                _sounds.Error();
                ShowToast(error is NexoException ? error.Message : "No se pudo cambiar la carpeta: " + error.Message, error: true, seconds: 8);
                return;
            }
            _changingFolder = false;
            _sounds.Confirm();
            ShowToast(result.Message, seconds: result.PreviousHasContent ? 10 : 6);
            if (result.Changed)
            {
                if (games) _previousGames = result.PreviousFolder; else _previousEmulators = result.PreviousFolder;
                ReloadLibrary();
            }
            if (_overlay == OverlayPage.Folders) BuildOverlay();
        }

        // Tras cambiar de carpeta: estados de todas las consolas y la estantería actual, desde cero.
        private void ReloadLibrary()
        {
            _compatibilityChecked.Clear();
            _covers.Clear();
            _backdrops.Clear();
            ClearShelf();
            _status = null;
            for (int i = 0; i < _cards.Count && i < _systems.Count; i++) _ = InitialBadgeAsync(_cards[i], _systems[i]);
            FillInfo();
            RefreshCurrent(force: true);
        }

        // ---------------------------------------------------------------- Iconos (trazos de 24×24)

        private static class Icons
        {
            public const string Folder = "M3,7 A2,2 0 0 1 5,5 H9 L11,7 H19 A2,2 0 0 1 21,9 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z";
            public const string Box = "M4,6 H20 V18 H4 Z M4,10 H20 M8,14 H12";
            public const string Open = "M14,4 H20 V10 M20,4 L11,13 M18,14 V18 A2,2 0 0 1 16,20 H6 A2,2 0 0 1 4,18 V8 A2,2 0 0 1 6,6 H10";
            public const string Reset = "M4,12 A8,8 0 1 0 6.3,6.3 M4,4 V9 H9";
            public const string History = "M12,7 V12 L15,14 M3.5,12 A8.5,8.5 0 1 0 6,6 M3,3 V7 H7";
            public const string Image = "M4,5 H20 V19 H4 Z M4,15 L9,10 L14,15 M13,13 L16,10 L20,14 M15,8.5 A0.5,0.5 0 1 0 15.01,8.5";
            public const string Expand = "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15";
            public const string Chip = "M3,7 H21 V17 H3 Z M7,17 V20 M11,17 V20 M15,17 V20 M7,10 H11 V14 H7 Z M14,10 H18 M14,13 H18";
            public const string Pad = "M7,8 H17 A5,5 0 0 1 21.8,14.4 L20.8,17.8 A2.4,2.4 0 0 1 16.8,18.8 L14.5,17 H9.5 L7.2,18.8 A2.4,2.4 0 0 1 3.2,17.8 L2.2,14.4 A5,5 0 0 1 7,8 Z M8,11 V14 M6.5,12.5 H9.5";
            public const string Sound = "M4,9 H8 L13,5 V19 L8,15 H4 Z M16,8.5 A5,5 0 0 1 16,15.5 M18.5,6 A8.5,8.5 0 0 1 18.5,18";
            public const string Download = "M12,4 V15 M7,10 L12,15 L17,10 M5,20 H19";
            public const string Refresh = "M20,12 A8,8 0 1 1 17.7,6.3 M20,4 V9 H15";
            public const string Info = "M12,3 A9,9 0 1 1 11.99,3 Z M12,11 V17 M12,7.5 V8";
            public const string Heart = "M12,20 L4.5,12.5 A4.5,4.5 0 0 1 12,6.5 A4.5,4.5 0 0 1 19.5,12.5 Z";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Nexo.Core;

namespace Nexo
{
    public partial class MainWindow : Window
    {
        private NexoService _service;
        private IReadOnlyList<GameSystem> _systems = Array.Empty<GameSystem>();
        private readonly List<ConsoleCard> _cards = new List<ConsoleCard>();
        private readonly Sounds _sounds = new Sounds();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastFrame;

        // Carrusel de consolas: posición continua (circular) y destino entero.
        private double _pos, _target;
        private int _active = -1;
        private SystemStatus _status;

        // Estantería de juegos.
        private bool _gamesMode;
        private double _focus;                       // 0 = consolas, 1 = juegos (suavizado)
        private double _gamePos;
        private int _gameTarget, _gameActive = -1;
        private CancellationTokenSource _scan;
        private DateTime _lastRefresh = DateTime.MinValue;

        // Arrastre con ratón o dedo.
        private bool _pressed, _dragging;
        private Point _pressPoint;
        private double _pressPos;
        private int _pressCard = -1;
        private readonly List<(double X, double T)> _samples = new List<(double, double)>();

        // Mando.
        private XInputGamepad _pad;
        private bool _padConnected;

        // Tema animado.
        private readonly SolidColorBrush _accent;
        private readonly SolidColorBrush _primary;
        private readonly RadialGradientBrush _auroraA = new RadialGradientBrush();
        private readonly RadialGradientBrush _auroraB = new RadialGradientBrush();
        private readonly RadialGradientBrush _floor = new RadialGradientBrush { Center = new Point(0.5, 1.15), GradientOrigin = new Point(0.5, 1.15), RadiusX = 0.75, RadiusY = 0.6 };
        private readonly LinearGradientBrush _primaryButton = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        private readonly LinearGradientBrush _nameFill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Random _random = new Random();
        private DispatcherTimer _toastTimer;

        public MainWindow()
        {
            InitializeComponent();
            // Los pinceles de App.xaml están congelados: la ventana pone encima los suyos, animables.
            _accent = new SolidColorBrush(Color.FromRgb(0x7C, 0xF4, 0xFF));
            _primary = new SolidColorBrush(Color.FromRgb(0x5B, 0x6C, 0xFF));
            Resources["Brush.Accent"] = _accent;
            Resources["Brush.Primary"] = _primary;
            Loaded += OnLoaded;
        }

        // ================================================================ Arranque

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _service = new NexoService(AppDomain.CurrentDomain.BaseDirectory);
                _systems = _service.Systems;
            }
            catch (Exception error)
            {
                MessageBox.Show("Nexo no pudo preparar sus carpetas:\n\n" + error.Message, "Nexo",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            SetupTheme();
            SetupParticles();
            BuildCards();
            BuildDots();
            WireEvents();

            try { _pad = new XInputGamepad(); } catch { _pad = null; }
            var padTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            padTimer.Tick += (s, a) => PollPad();
            padTimer.Start();

            var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            clockTimer.Tick += (s, a) => UpdateClock();
            clockTimer.Start();
            UpdateClock();
            UpdateSoundIcon();
            AdaptToSize();

            _ = ShowGpuAsync();
            _ = CheckUpdatesAsync();
            CompositionTarget.Rendering += OnFrame;
            Layout(); // primera consola visible antes del primer fotograma
            Keyboard.Focus(this);
        }

        private void WireEvents()
        {
            Stage.MouseLeftButtonDown += Stage_Down;
            Stage.MouseMove += Stage_Move;
            Stage.MouseLeftButtonUp += Stage_Up;
            Stage.LostMouseCapture += (s, e) => { if (_pressed) FinishDrag(null); };
            Stage.MouseWheel += (s, e) => { Step(e.Delta > 0 ? -1 : 1); e.Handled = true; };
            Stage.SizeChanged += (s, e) => SizeCards();
            Shelf.MouseWheel += (s, e) => { if (ItemCount > 0) { EnterGames(); StepGame(e.Delta > 0 ? -1 : 1); } e.Handled = true; };
            SizeChanged += (s, e) => AdaptToSize();

            PrevButton.Click += (s, e) => Step(-1);
            NextButton.Click += (s, e) => Step(1);
            PrimaryButton.Click += (s, e) => PrimaryAction();
            GamesFolderButton.Click += (s, e) => OpenGamesFolder();
            EmulatorFolderButton.Click += (s, e) => OpenEmulatorFolder();
            LocateButton.Click += (s, e) => LocateExecutable();
            ImportButton.Click += (s, e) => ImportSystemFiles();
            WebsiteButton.Click += (s, e) => OpenWebsite();
            SoundButton.Click += (s, e) => ToggleSound();
            UpdateChip.Click += (s, e) => UpdateChipAction();
            DonateButton.Click += (s, e) => OpenDonate();
            MinimizeButton.Click += (s, e) => WindowState = WindowState.Minimized;
            CloseButton.Click += (s, e) => Close();
            TopBar.MouseLeftButtonDown += (s, e) => { if (WindowState == WindowState.Normal && e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            PreviewKeyDown += OnKey;
            Activated += (s, e) =>
            {
                // Al volver de un juego, lo que se siga pulsando en el mando no debe actuar sobre Nexo.
                _padQuietUntil = _clock.Elapsed.TotalMilliseconds + 700;
                RefreshCurrent(force: false);
            };
            Closed += (s, e) => { CompositionTarget.Rendering -= OnFrame; _scan?.Cancel(); _art.Cancel(); _closing.Cancel(); InstallUpdateOnClose(); };
        }

        // ================================================================ Tarjetas de consola

        private sealed class ConsoleCard
        {
            public Grid Root;
            public ScaleTransform Scale;
            public SkewTransform Skew;
            public TranslateTransform Move;
            public Rectangle Shade;
            public Border Highlight;
            public Border Badge;
            public TextBlock BadgeText;
            public Image Art;
            public TextBlock NameText;
            public FrameworkElement Shadow;
        }

        private void BuildCards()
        {
            Stage.Children.Clear();
            _cards.Clear();
            for (int i = 0; i < _systems.Count; i++)
            {
                var card = CreateCard(_systems[i], i);
                _cards.Add(card);
                Stage.Children.Add(card.Shadow);
                Stage.Children.Add(card.Root);
            }
            SizeCards();
        }

        private ConsoleCard CreateCard(GameSystem system, int index)
        {
            var primary = Palette.Parse(system.Primary);
            var secondary = Palette.Parse(system.Secondary);
            var accent = Palette.Parse(system.Accent);
            var card = new ConsoleCard();

            var root = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), Tag = index, Cursor = Cursors.Hand };
            card.Scale = new ScaleTransform();
            card.Skew = new SkewTransform();
            card.Move = new TranslateTransform();
            var transform = new TransformGroup();
            transform.Children.Add(card.Scale);
            transform.Children.Add(card.Skew);
            transform.Children.Add(card.Move);
            root.RenderTransform = transform;

            var background = new LinearGradientBrush { StartPoint = new Point(0.15, 0), EndPoint = new Point(0.85, 1) };
            background.GradientStops.Add(new GradientStop(primary, 0));
            background.GradientStops.Add(new GradientStop(secondary, 1));
            var surface = new Border
            {
                CornerRadius = new CornerRadius(28), Background = background,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            };
            var content = new Grid { ClipToBounds = true };
            surface.Child = content;

            // Halo superior con el color de acento, como en la versión web.
            var halo = new RadialGradientBrush { Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.9, RadiusY = 0.65 };
            halo.GradientStops.Add(new GradientStop(Palette.WithAlpha(accent, 0x70), 0));
            halo.GradientStops.Add(new GradientStop(Palette.WithAlpha(accent, 0x00), 1));
            content.Children.Add(new Rectangle { Fill = halo });

            // Trama de puntos que se desvanece hacia abajo.
            var dot = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)), null, new EllipseGeometry(new Point(9, 9), 1.1, 1.1));
            var pattern = new DrawingBrush(dot) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 18, 18), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 18, 18), ViewboxUnits = BrushMappingMode.Absolute };
            var fade = new LinearGradientBrush(Colors.Black, Colors.Transparent, 90);
            content.Children.Add(new Rectangle { Fill = pattern, Opacity = 0.18, OpacityMask = fade });

            card.Art = new Image
            {
                // Foto real de la consola; si faltara, la ilustración dibujada.
                Source = LoadPhoto(system.Id) ?? TryFindResource("Art." + system.Id) as ImageSource,
                Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 46, 10, 0),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 14, Direction = 270, Opacity = 0.45 },
            };
            content.Children.Add(card.Art);

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(22, 0, 22, 22) };
            var labelBack = new Rectangle { VerticalAlignment = VerticalAlignment.Bottom, Height = 120, Fill = new LinearGradientBrush(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0x8C, 0, 0, 0), 90) };
            content.Children.Add(labelBack);
            card.NameText = new TextBlock { Text = system.Name, FontSize = 28, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
            label.Children.Add(card.NameText);
            label.Children.Add(new TextBlock { Text = system.EmulatorName + (system.Experimental ? " · experimental" : ""), FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)) });
            content.Children.Add(label);

            card.BadgeText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
            card.Badge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 16, 0),
                Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1), Child = card.BadgeText,
            };
            content.Children.Add(card.Badge);

            // Sombra de profundidad para las tarjetas laterales.
            card.Shade = new Rectangle { Fill = Brushes.Black, Opacity = 0, RadiusX = 28, RadiusY = 28, IsHitTestVisible = false };
            // Borde luminoso de la tarjeta activa.
            card.Highlight = new Border
            {
                CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Palette.WithAlpha(accent, 0xB3)),
                Opacity = 0, IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = accent, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.8 },
            };

            root.Children.Add(surface);
            root.Children.Add(card.Shade);
            root.Children.Add(card.Highlight);
            card.Root = root;

            // Reflejo/sombra en el suelo: un óvalo difuso con el color de la consola.
            var shadowBrush = new RadialGradientBrush();
            shadowBrush.GradientStops.Add(new GradientStop(Palette.WithAlpha(primary, 0x80), 0));
            shadowBrush.GradientStops.Add(new GradientStop(Palette.WithAlpha(primary, 0x00), 1));
            card.Shadow = new Ellipse { Fill = shadowBrush, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
            card.BadgeText.Text = "…";
            _ = InitialBadgeAsync(card, system);
            return card;
        }

        private static ImageSource LoadPhoto(string systemId)
        {
            var uri = new Uri("pack://application:,,,/Photos/" + systemId + ".png", UriKind.Absolute);
            try
            {
                if (Application.GetResourceStream(uri) == null) return null;
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (System.IO.IOException) { return null; }
        }

        // La detección de cada consola corre en segundo plano para no retrasar la primera imagen.
        private async Task InitialBadgeAsync(ConsoleCard card, GameSystem system)
        {
            try { UpdateBadge(card, await Task.Run(() => _service.GetStatus(system.Id))); }
            catch { card.BadgeText.Text = "?"; }
        }

        private static void UpdateBadge(ConsoleCard card, SystemStatus status)
        {
            card.BadgeText.Text = status.Installed ? "Listo" : "Sin emulador";
            card.BadgeText.Foreground = new SolidColorBrush(status.Installed ? Color.FromRgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        }

        private double _cardWidth = 340, _cardHeight = 400;

        private void SizeCards()
        {
            double h = Math.Max(160, Stage.ActualHeight * 0.9);
            double w = Math.Min(420, Math.Min(h / 1.18, Math.Max(200, Stage.ActualWidth * 0.28)));
            h = w * 1.18;
            _cardWidth = w;
            _cardHeight = h;
            foreach (var card in _cards)
            {
                card.Root.Width = w;
                card.Root.Height = h;
                card.Art.Height = h * 0.52;
                // "Nintendo Switch" y "GameCube / Wii" deben caber enteros en la tarjeta.
                card.NameText.FontSize = Math.Max(15, Math.Min(28, w / (card.NameText.Text.Length * 0.62 + 1)));
                ((Ellipse)card.Shadow).Width = w * 1.1;
                ((Ellipse)card.Shadow).Height = w * 0.22;
            }
        }

        // ================================================================ Bucle de animación

        private void OnFrame(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, now - _lastFrame);
            _lastFrame = now;

            if (!_dragging)
            {
                _pos += (_target - _pos) * (1 - Math.Exp(-dt * 11));
                if (Math.Abs(_target - _pos) < 0.0005) _pos = _target;
            }
            _gamePos += (_gameTarget - _gamePos) * (1 - Math.Exp(-dt * 12));
            if (Math.Abs(_gameTarget - _gamePos) < 0.0005) _gamePos = _gameTarget;
            _focus += ((_gamesMode ? 1 : 0) - _focus) * (1 - Math.Exp(-dt * 9));

            Layout();
            LayoutShelf();
            UpdateParticles(dt);
        }

        private static double Mod(double value, double n) => ((value % n) + n) % n;

        private void Layout()
        {
            int n = _cards.Count;
            if (n == 0) return;
            double cx = Stage.ActualWidth / 2, cy = Stage.ActualHeight / 2;
            double spacing = _cardWidth * 0.74;
            double focusDim = _focus;     // en modo juegos, el carrusel se aparta un poco
            for (int i = 0; i < n; i++)
            {
                var card = _cards[i];
                double offset = Mod(i - _pos + n / 2.0, n) - n / 2.0;
                double distance = Math.Abs(offset);
                double angle = Math.Max(-55, Math.Min(55, -offset * 38)) * Math.PI / 180;
                double scale = (1 - Math.Min(distance, 2) * 0.1) * (1 - focusDim * 0.1);
                // Perspectiva simulada: estrechar y cizallar como si girara hacia el centro.
                card.Scale.ScaleX = scale * Math.Cos(angle);
                card.Scale.ScaleY = scale;
                card.Skew.AngleY = Math.Sin(angle) * 7;
                double x = cx + offset * spacing - _cardWidth / 2;
                double y = cy - _cardHeight / 2 - focusDim * 16;
                card.Move.X = x;
                card.Move.Y = y;
                double fadeOut = distance < 1.6 ? 1 : Math.Max(0, 1 - (distance - 1.6) / 0.7);
                card.Root.Opacity = fadeOut * (1 - focusDim * 0.25);
                card.Root.IsHitTestVisible = fadeOut > 0.2;
                card.Shade.Opacity = Math.Min(distance, 2) * 0.3 + focusDim * 0.15;
                Panel.SetZIndex(card.Root, 100 - (int)Math.Round(distance * 10));
                card.Highlight.Opacity = Math.Max(0, 1 - distance * 2.5) * (1 - focusDim * 0.6);

                var shadow = (TranslateTransform)card.Shadow.RenderTransform;
                shadow.X = cx + offset * spacing - card.Shadow.Width / 2;
                shadow.Y = y + _cardHeight * scale + 6 - card.Shadow.Height / 2 + (1 - scale) * _cardHeight / 2;
                card.Shadow.Opacity = fadeOut * Math.Max(0, 1 - distance * 0.6) * 0.8;
                Panel.SetZIndex(card.Shadow, 0);
            }
            int active = (int)Mod(Math.Round(_pos), n);
            if (active != _active) ShowSystem(active);
        }

        // ================================================================ Consola seleccionada

        private GameSystem Current => _active >= 0 && _active < _systems.Count ? _systems[_active] : null;

        // Nombre del emulador realmente detectado (p. ej. "Eden" en Switch); si no, el del catálogo.
        private string EmulatorLabel
        {
            get
            {
                var system = Current;
                if (system == null) return "";
                var exe = _status?.ExecutablePath;
                if (string.IsNullOrEmpty(exe)) return system.EmulatorName;
                switch (System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant())
                {
                    case "eden": return "Eden";
                    case "ryujinx": return "Ryujinx";
                    case "citron": return "Citron";
                    case "yuzu": return "yuzu";
                    default: return system.EmulatorName;
                }
            }
        }

        private void ShowSystem(int index)
        {
            bool first = _active == -1;
            _active = index;
            var system = _systems[index];
            if (!first) _sounds.Move(index);
            if (_gamesMode) ExitGames(silent: true);

            AnimateTheme(system);
            foreach (var dot in Dots.Children.OfType<Border>()) SetDot(dot, (int)dot.Tag == index);
            Title = "Nexo · " + system.Name;

            // La estantería anterior se vacía ya: sus juegos no deben poder lanzarse con otra consola.
            ClearShelf();
            _status = null;
            FadeInfo();
            _ = RefreshStatusAsync(system, scan: true);
        }

        /// <summary>Detecta el emulador fuera del hilo gráfico y, si la consola sigue siendo la misma, lo aplica.</summary>
        private async Task RefreshStatusAsync(GameSystem system, bool scan)
        {
            SystemStatus status = null;
            string error = null;
            try { status = await Task.Run(() => _service.GetStatus(system.Id)); }
            catch (Exception e) { error = e.Message; }
            if (Current != system) return;
            _status = status;
            if (status != null) UpdateBadge(_cards[_active], status);
            else ShowToast("No pude comprobar el emulador de " + system.Name + ": " + error, error: true);
            FillInfo();
            if (scan) StartScan(system);
            if (string.Equals(system.Id, "PS4", StringComparison.OrdinalIgnoreCase))
                _ = RefreshCompatibilityAsync(system, () => _service.RefreshPs4CompatibilityAsync(_closing.Token));
            else if (string.Equals(system.Id, "Xbox", StringComparison.OrdinalIgnoreCase))
                _ = RefreshCompatibilityAsync(system, () => _service.RefreshXboxCompatibilityAsync(_closing.Token));
        }

        // Las listas de compatibilidad (shadPS4, xemu) se refrescan aparte, una vez por sesión y con caché
        // semanal; si cambiaron, se reescanea para mostrar los datos nuevos.
        private readonly HashSet<string> _compatibilityChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private async Task RefreshCompatibilityAsync(GameSystem system, Func<Task<bool>> refresh)
        {
            if (!_compatibilityChecked.Add(system.Id)) return;
            bool changed;
            try { changed = await refresh(); }
            catch { return; } // sin red: se usa la última copia
            if (changed && Current == system) StartScan(system);
        }

        private void FillInfo()
        {
            if (_gamesMode && (SelectedGame != null || SelectedArchive != null)) FillGameInfo(); else FillSystemInfo();
        }

        private void FillSystemInfo()
        {
            var system = Current;
            if (system == null) return;
            var details = new List<string>();
            details.Add(system.Maker + (system.Year.HasValue ? " " + system.Year.Value : ""));
            details.Add("Emulador: " + EmulatorLabel + (system.Experimental ? " (experimental)" : ""));
            InfoSystem.Text = string.Join("  ·  ", details).ToUpperInvariant();
            InfoName.Text = system.Name;
            InfoDescription.Text = system.Description;
            InfoDescription.Foreground = (Brush)FindResource("Brush.InkSoft");
            InfoDescription.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;

            bool known = _status != null;
            bool ready = known && _status.Installed;
            bool installing = _installing.TryGetValue(system.Id, out var progress);
            var pending = ready && _status.SetupMessages != null ? _status.SetupMessages : (IReadOnlyList<string>)new string[0];
            var color = !known ? Color.FromRgb(0x7C, 0x82, 0x99) : installing ? Color.FromRgb(0x7C, 0xC4, 0xFF)
                : ready && pending.Count == 0 ? Color.FromRgb(0x4A, 0xDE, 0x80) : Color.FromRgb(0xFB, 0xBF, 0x24);
            StatusDot.Fill = new SolidColorBrush(color);
            StatusGlow.Color = color;
            StatusText.Foreground = new SolidColorBrush(color);
            StatusText.Text = !known ? "Comprobando…" : installing ? "Instalando " + InstallLabel(system)
                : ready ? (pending.Count == 0 ? "Emulador listo" : "Falta configurar") : "Falta el emulador";
            // Sin estado no hay acciones que dependan de él.
            PrimaryButton.IsEnabled = GamesFolderButton.IsEnabled = EmulatorFolderButton.IsEnabled = LocateButton.IsEnabled = known;
            if (installing) PrimaryButton.IsEnabled = false;
            StatusPath.Text = !known ? "" : installing ? progress : ready ? (pending.Count == 0 ? _status.ExecutablePath : pending[0])
                : CanInstall(system) ? "Pulsa Instalar: Nexo lo descarga de su web oficial" : "Cópialo en  Emuladores\\" + system.Id;
            StatusPath.ToolTip = ready ? string.Join("\n", new[] { _status.ExecutablePath }.Concat(pending)) : _status?.EmulatorFolder;
            ImportButton.Visibility = ready && ImportLabel(system) != null ? Visibility.Visible : Visibility.Collapsed;
            ImportText.Text = ImportLabel(system) ?? "";

            PrimaryText.Text = ready ? "Abrir " + EmulatorLabel : CanInstall(system) ? "Instalar " + InstallLabel(system) : "Abrir carpeta";
            PrimaryIcon.Data = Geometry.Parse(ready ? "M8,5.5 V18.5 L19,12 Z" : "M12,4 V15 M7,10 L12,15 L17,10 M5,19 H19");
            PrimaryIcon.Fill = ready ? Brushes.White : null;
            PrimaryIcon.Stroke = ready ? null : Brushes.White;
            PrimaryIcon.StrokeThickness = 2.2;
            LocateButton.Visibility = Visibility.Visible;
            WebsiteButton.Visibility = string.IsNullOrEmpty(system.Website) ? Visibility.Collapsed : Visibility.Visible;
            UpdateHints();
        }

        private void FillGameInfo()
        {
            var system = Current;
            if (system == null) return;
            var archive = SelectedArchive;
            if (archive != null)
            {
                // Un comprimido: se ofrece descomprimirlo, o se explica por qué no se puede.
                InfoSystem.Text = (system.Name + (ArchiveView.IsPackage(archive) ? "  ·  paquete para instalar" : "  ·  archivo comprimido")).ToUpperInvariant();
                InfoName.Text = archive.Title;
                bool ready = archive.State == ArchiveState.Ready;
                bool busy = _extracting.Contains(archive.Path);
                bool package = ArchiveView.IsPackage(archive);
                // Antes de instalar un PKG enorme, lo que se sabe de su compatibilidad.
                var archiveCompat = Compatibility.Of(archive.Compatibility, archive.SystemId);
                string archiveNote = package ? Compatibility.Note(archive.Compatibility, archive.SystemId) : "";
                if (package && archiveCompat.Risky) archiveNote += "Puede que no funcione en el emulador actual: piénsalo antes de instalarlo.\n";
                InfoDescription.Text = busy ? (package ? "Instalando el juego, su parche y sus DLC… " : "Descomprimiendo… ") + "cuando termine aparecerá aquí como un juego más."
                    : ready && package ? archiveNote + "Pulsa Enter o A para instalarlo, con el parche y los DLC de esta carpeta. Al terminar, los .pkg van a la Papelera."
                    : ready ? "Pulsa Enter o A para descomprimirlo. Nexo lo deja listo para jugar y manda el comprimido a la Papelera."
                    : (string.IsNullOrEmpty(archive.Message) ? ArchiveView.StateText(archive) : archive.Message);
                InfoDescription.Visibility = Visibility.Visible;
                InfoDescription.Foreground = ready && !(package && archiveCompat.Risky) ? (Brush)FindResource("Brush.InkSoft") : new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
                PrimaryText.Text = ready ? (package ? "Instalar" : "Descomprimir") : "Abrir carpeta";
                PrimaryIcon.Data = Geometry.Parse(ready ? "M12,4 V15 M7,10 L12,15 L17,10 M5,19 H19" : "M3,7 A2,2 0 0 1 5,5 H9 L11,7 H19 A2,2 0 0 1 21,9 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z");
                PrimaryIcon.Fill = null;
                PrimaryIcon.Stroke = Brushes.White;
                PrimaryIcon.StrokeThickness = 2.2;
                PrimaryButton.IsEnabled = !busy;
                UpdateHints();
                return;
            }
            var game = SelectedGame;
            if (game == null) return;
            InfoSystem.Text = (system.Name + "  ·  juego " + (_gameActive - _archives.Count + 1) + " de " + _games.Count).ToUpperInvariant();
            InfoName.Text = game.Title;
            // Compatibilidad conocida del emulador: se avisa antes de jugar, sobre todo si no funciona.
            var compat = Compatibility.Of(game);
            InfoDescription.Text = Compatibility.Note(game.Compatibility, game.SystemId) + "Pulsa Enter o A para jugar  ·  Esc o B para volver a las consolas";
            InfoDescription.Foreground = compat.Risky ? new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)) : (Brush)FindResource("Brush.InkSoft");
            InfoDescription.Visibility = Visibility.Visible;
            PrimaryText.Text = "Jugar";
            PrimaryIcon.Data = Geometry.Parse("M8,5.5 V18.5 L19,12 Z");
            PrimaryIcon.Fill = Brushes.White;
            PrimaryIcon.Stroke = null;
            PrimaryButton.IsEnabled = _status != null;
            UpdateHints();
        }

        // Al terminar el fundido se rellena según el estado de ese momento (consola o juego),
        // no según el que había al empezar.
        private void FadeInfo()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (s, e) =>
            {
                FillInfo();
                InfoPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
                InfoMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            };
            InfoPanel.BeginAnimation(OpacityProperty, fadeOut);
        }

        // ================================================================ Tema animado

        private void SetupTheme()
        {
            _auroraA.GradientStops.Add(new GradientStop(Color.FromRgb(0x5B, 0x6C, 0xFF), 0));
            _auroraA.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x5B, 0x6C, 0xFF), 0.62));
            _auroraB.GradientStops.Add(new GradientStop(Color.FromRgb(0x7C, 0xF4, 0xFF), 0));
            _auroraB.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x7C, 0xF4, 0xFF), 0.6));
            _floor.GradientStops.Add(new GradientStop(Color.FromArgb(0xB3, 0x1B, 0x1F, 0x3A), 0));
            _floor.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x1B, 0x1F, 0x3A), 1));
            _primaryButton.GradientStops.Add(new GradientStop(Color.FromRgb(0x5B, 0x6C, 0xFF), 0));
            _primaryButton.GradientStops.Add(new GradientStop(Color.FromRgb(0x6B, 0xB0, 0xFF), 1));
            _nameFill.GradientStops.Add(new GradientStop(Colors.White, 0.3));
            _nameFill.GradientStops.Add(new GradientStop(Color.FromRgb(0xBD, 0xF9, 0xFF), 1));
            AuroraA.Fill = _auroraA;
            AuroraB.Fill = _auroraB;
            FloorGlow.Fill = _floor;
            PrimaryButton.Background = _primaryButton;
            InfoName.Foreground = _nameFill;

            // Las auroras derivan lentamente, como en la web.
            Drift(AuroraAMove, 260, 140, 22);
            Drift(AuroraBMove, -230, -110, 28);
            Breathe(AuroraAScale, 1.1, 22);
            Breathe(AuroraBScale, 1.15, 28);
        }

        private static void Drift(TranslateTransform t, double x, double y, double seconds)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, x, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, y, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }

        private static void Breathe(ScaleTransform t, double to, double seconds)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var a = new DoubleAnimation(1, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            t.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            t.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private void AnimateTheme(GameSystem system)
        {
            var primary = Palette.Parse(system.Primary);
            var secondary = Palette.Parse(system.Secondary);
            var accent = Palette.Parse(system.Accent);
            var time = TimeSpan.FromMilliseconds(900);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            void To(GradientStop stop, Color color) => stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(color, time) { EasingFunction = ease });

            _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, time) { EasingFunction = ease });
            _primary.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(primary, time) { EasingFunction = ease });
            To(_auroraA.GradientStops[0], primary);
            To(_auroraA.GradientStops[1], Palette.WithAlpha(primary, 0));
            To(_auroraB.GradientStops[0], accent);
            To(_auroraB.GradientStops[1], Palette.WithAlpha(accent, 0));
            To(_floor.GradientStops[0], Palette.WithAlpha(secondary, 0xB3));
            To(_floor.GradientStops[1], Palette.WithAlpha(secondary, 0));
            To(_primaryButton.GradientStops[0], primary);
            To(_primaryButton.GradientStops[1], Palette.Mix(primary, accent, 0.45));
            To(_nameFill.GradientStops[1], Palette.Mix(accent, Colors.White, 0.45));
            BrandGlow.Color = accent;
            // El efecto del estilo está sellado: se sustituye por uno nuevo con el color de la consola.
            PrimaryButton.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.55, Color = primary };
        }

        // ================================================================ Puntos e indicaciones

        private void BuildDots()
        {
            Dots.Children.Clear();
            for (int i = 0; i < _systems.Count; i++)
            {
                int index = i;
                var dot = new Border
                {
                    Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(5, 0, 5, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Cursor = Cursors.Hand, Tag = i,
                    ToolTip = _systems[i].Name,
                };
                dot.MouseLeftButtonUp += (s, e) => GoTo(index);
                Dots.Children.Add(dot);
            }
        }

        private void SetDot(Border dot, bool active)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(active ? 34 : 10, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
            dot.Background = active ? (Brush)_accent : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        }

        private void UpdateHints()
        {
            Hints.Children.Clear();
            if (_compactHints) return;
            void Hint(string text, params string[] keys)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 0, 11, 0) };
                foreach (var key in keys)
                    row.Children.Add(new Border { Style = (Style)FindResource("Kbd"), Child = new TextBlock { Text = key, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.InkSoft") } });
                row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brush.InkFaint") });
                Hints.Children.Add(row);
            }
            if (_padConnected)
            {
                if (_gamesMode) { Hint("elegir juego", "◀", "▶"); Hint("jugar", "A"); Hint("volver", "B"); Hint("cambiar consola", "LB", "RB"); }
                else { Hint("cambiar consola", "◀", "▶"); Hint("ver juegos", "▼"); Hint("abrir", "A"); Hint("carpeta de juegos", "X"); }
            }
            else if (_gamesMode)
            {
                Hint("elegir juego", "←", "→"); Hint("jugar", "Enter"); Hint("volver", "Esc"); Hint("carpeta de juegos", "F");
            }
            else
            {
                Hint("o desliza para cambiar", "←", "→"); Hint("ver juegos", "↓"); Hint("abrir", "Enter"); Hint("carpeta de juegos", "F"); Hint("pantalla completa", "F11");
            }
        }

        // ================================================================ Movimiento

        private void Step(int direction)
        {
            if (_systems.Count == 0) return;
            if (_gamesMode) ExitGames(silent: true);
            _target += direction;
        }

        private void GoTo(int index)
        {
            int n = _systems.Count;
            int from = (int)Mod(Math.Round(_target), n);
            int delta = index - from;
            if (delta > n / 2) delta -= n;
            if (delta < -n / 2) delta += n;
            if (_gamesMode) ExitGames(silent: true);
            _target += delta;
        }

        private void Stage_Down(object sender, MouseButtonEventArgs e)
        {
            _pressed = true;
            _dragging = false;
            _pressPoint = e.GetPosition(Stage);
            _pressPos = _pos;
            _pressCard = CardIndexFrom(e.OriginalSource as DependencyObject);
            _samples.Clear();
            _samples.Add((_pressPoint.X, _clock.Elapsed.TotalSeconds));
            Stage.CaptureMouse();
        }

        private void Stage_Move(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;
            var p = e.GetPosition(Stage);
            double dx = p.X - _pressPoint.X;
            if (!_dragging && Math.Abs(dx) > 6)
            {
                _dragging = true;
                if (_gamesMode) ExitGames(silent: true);
            }
            if (_dragging) _pos = _pressPos - dx / (_cardWidth * 0.74);
            _samples.Add((p.X, _clock.Elapsed.TotalSeconds));
            if (_samples.Count > 6) _samples.RemoveAt(0);
        }

        private void Stage_Up(object sender, MouseButtonEventArgs e) => FinishDrag(e);

        private void FinishDrag(MouseButtonEventArgs e)
        {
            if (!_pressed) return;
            _pressed = false;
            bool wasDragging = _dragging;
            _dragging = false;
            if (Stage.IsMouseCaptured) Stage.ReleaseMouseCapture();
            if (!wasDragging)
            {
                if (e != null && _pressCard >= 0)
                {
                    if (_pressCard == _active && !_gamesMode) PrimaryAction();
                    else GoTo(_pressCard);
                }
                return;
            }
            var first = _samples.First();
            var last = _samples.Last();
            double seconds = Math.Max(0.016, last.T - first.T);
            double velocity = -((last.X - first.X) / seconds) / (_cardWidth * 0.74);
            double momentum = Math.Max(-2, Math.Min(2, velocity * 0.22));
            _target = Math.Round(_pos + momentum);
        }

        private int CardIndexFrom(DependencyObject source)
        {
            while (source != null && source != Stage)
            {
                if (source is Grid g && g.Tag is int index && _cards.Any(c => c.Root == g)) return index;
                source = VisualTreeHelper.GetParent(source);
            }
            return -1;
        }

        // ================================================================ Juegos

        // La lista completa vive en _games; solo existen las piezas visibles (más un margen),
        // así una biblioteca de cientos de juegos no frena la animación.
        // Los comprimidos van primero (para que se vean nada más copiarlos) y luego los juegos.
        private readonly List<ArchiveEntry> _archives = new List<ArchiveEntry>();
        private readonly List<GameEntry> _games = new List<GameEntry>();
        private readonly Dictionary<int, FrameworkElement> _views = new Dictionary<int, FrameworkElement>();
        private readonly HashSet<string> _extracting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private string _selectAfterScan;
        private readonly Dictionary<string, ImageSource> _covers = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _coversLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int CoverCacheLimit = 240;
        private GameSystem _shelfSystem;

        private int ItemCount => _archives.Count + _games.Count;
        private ArchiveEntry ArchiveAt(int i) => i >= 0 && i < _archives.Count ? _archives[i] : null;
        private GameEntry GameAt(int i) => i >= _archives.Count && i < ItemCount ? _games[i - _archives.Count] : null;
        private GameEntry SelectedGame => GameAt(_gameActive);
        private ArchiveEntry SelectedArchive => ArchiveAt(_gameActive);
        private IShelfView ViewAt(int index) => _views.TryGetValue(index, out var view) ? view as IShelfView : null;

        private void ClearShelf()
        {
            _scan?.Cancel();
            _art.Cancel();
            _art = new CancellationTokenSource();
            _artRequested.Clear();
            foreach (var view in _views.Values) Shelf.Children.Remove(view);
            _views.Clear();
            _games.Clear();
            _archives.Clear();
            _shelfSystem = null;
            _gameActive = -1;
            HideGameArt();
            _gameTarget = 0;
            _gamePos = 0;
            ShelfEmpty.Visibility = Visibility.Collapsed;
            ShelfHint.Text = "";
            ShelfCount.Text = "";
        }

        private void StartScan(GameSystem system)
        {
            _scan?.Cancel();
            var cts = new CancellationTokenSource();
            _scan = cts;
            ShelfTitle.Text = "Juegos de " + system.Name;
            ShelfCount.Text = "buscando…";
            _ = ScanAsync(system, cts);
        }

        private async Task ScanAsync(GameSystem system, CancellationTokenSource cts)
        {
            try
            {
                GameScanResult result;
                try
                {
                    result = await _service.ScanGamesDetailedAsync(system.Id, cts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception error)
                {
                    if (Current == system) ShowToast("No pude leer la carpeta de juegos: " + error.Message, error: true);
                    result = null;
                }
                if (cts.IsCancellationRequested || Current != system) return;
                BuildShelf(system, result);
            }
            finally
            {
                // El escaneo vigente conserva su token para cancelarlo después; los reemplazados se liberan.
                if (!ReferenceEquals(_scan, cts)) cts.Dispose();
            }
        }

        private void BuildShelf(GameSystem system, GameScanResult result)
        {
            string keep = _shelfSystem == system ? (SelectedGame?.Path ?? SelectedArchive?.Path) : null;
            foreach (var view in _views.Values) Shelf.Children.Remove(view);
            _views.Clear();
            _games.Clear();
            _archives.Clear();
            if (result != null)
            {
                _archives.AddRange(result.Archives);
                _games.AddRange(result.Games);
            }
            _shelfSystem = system;

            // Tras descomprimir se selecciona el juego que salió del archivo.
            int restored = -1;
            bool fromExtraction = false;
            if (_selectAfterScan != null)
            {
                int found = _games.FindIndex(g => g.Path.StartsWith(_selectAfterScan, StringComparison.OrdinalIgnoreCase));
                if (found >= 0) { restored = _archives.Count + found; fromExtraction = true; }
                _selectAfterScan = null;
            }
            if (restored < 0 && keep != null)
            {
                int a = _archives.FindIndex(x => string.Equals(x.Path, keep, StringComparison.OrdinalIgnoreCase));
                int g = _games.FindIndex(x => string.Equals(x.Path, keep, StringComparison.OrdinalIgnoreCase));
                restored = a >= 0 ? a : g >= 0 ? _archives.Count + g : -1;
            }
            _gameTarget = Math.Min(Math.Max(0, restored), Math.Max(0, ItemCount - 1));
            _gamePos = _gameTarget;
            int wasActive = _gameActive;
            _gameActive = -1;

            int count = ItemCount;
            var parts = new List<string>();
            if (_games.Count > 0) parts.Add(_games.Count == 1 ? "1 juego" : _games.Count + " juegos");
            if (_archives.Count > 0) parts.Add(_archives.Count == 1 ? "1 comprimido" : _archives.Count + " comprimidos");
            ShelfCount.Text = string.Join("  ·  ", parts);
            ShelfEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int ignored = result?.IgnoredCount ?? 0;
            if (count == 0 && ignored > 0)
            {
                ShelfEmptyTitle.Text = ignored == 1 ? "Hay 1 juego que aún no se puede abrir" : "Hay " + ignored + " juegos que aún no se pueden abrir";
                ShelfEmptyText.Text = result.Reason;
            }
            else
            {
                ShelfEmptyTitle.Text = "Aún no hay juegos de " + system.Name;
                ShelfEmptyText.Text = "Copia tus juegos en  Juegos\\" + system.Id + "  dentro de la carpeta de Nexo (botón «Carpeta de juegos» o tecla F). Aparecerán aquí solos."
                                      + (string.IsNullOrEmpty(system.GamesHint) ? "" : "\n" + system.GamesHint);
            }
            ShelfHint.Text = count == 0 || _gamesMode ? "" : ignored > 0
                ? ignored + " sin núcleo compatible  ·  Pulsa ↓ para elegir un juego"
                : "Pulsa ↓ o el ▼ del mando para elegir un juego";
            if (_gamesMode && count == 0) ExitGames(silent: true);
            else if (_gamesMode && wasActive >= 0) SelectGame(_gameTarget);
            else if (fromExtraction) { EnterGames(); SelectGame(_gameTarget); }
            RealizeViews();
        }

        // Crea las piezas que entran en pantalla y retira las que salieron.
        private void RealizeViews()
        {
            var system = _shelfSystem;
            if (system == null || ItemCount == 0) return;
            double spacing = MediaView.SlotWidth + 18;
            int half = (int)Math.Ceiling(Math.Max(1, Shelf.ActualWidth) / 2 / spacing) + 2;
            int lo = Math.Max(0, (int)Math.Floor(_gamePos) - half);
            int hi = Math.Min(ItemCount - 1, (int)Math.Ceiling(_gamePos) + half);

            foreach (var index in _views.Keys.Where(i => i < lo || i > hi).ToList())
            {
                Shelf.Children.Remove(_views[index]);
                _views.Remove(index);
            }
            for (int i = lo; i <= hi; i++)
            {
                if (!_views.ContainsKey(i)) CreateView(system, i);
            }
        }

        private void CreateView(GameSystem system, int index)
        {
            var archive = ArchiveAt(index);
            if (archive != null)
            {
                var box = new ArchiveView(archive);
                box.MouseLeftButtonUp += (s, e) =>
                {
                    if (_gamesMode && _gameActive == index) ExtractSelected();
                    else { EnterGames(); SelectGame(index); }
                    e.Handled = true;
                };
                _views[index] = box;
                Shelf.Children.Add(box);
                if (_gamesMode && index == _gameActive) box.IsSelected = true;
                return;
            }
            var game = GameAt(index);
            ImageSource cover = null;
            if (!string.IsNullOrEmpty(game.CoverPath) && !_covers.TryGetValue(game.CoverPath, out cover))
            {
                _ = LoadCoverAsync(system, game.CoverPath);
            }
            if (_artRequested.Add(game.Path)) _ = EnsureArtAsync(system, game, _art.Token);
            var view = new MediaView(game, index, system, cover);
            view.MouseLeftButtonUp += (s, e) =>
            {
                if (_gamesMode && _gameActive == index) LaunchSelectedGame();
                else { EnterGames(); SelectGame(index); }
                e.Handled = true;
            };
            _views[index] = view;
            Shelf.Children.Add(view);
            if (_gamesMode && index == _gameActive) view.IsSelected = true;
        }

        // Arte que falta (portada, fondo, plataforma): solo para las piezas que se ven.
        private CancellationTokenSource _art = new CancellationTokenSource();
        private readonly HashSet<string> _artRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private async Task EnsureArtAsync(GameSystem system, GameEntry game, CancellationToken ct)
        {
            GameEntry updated;
            try { updated = await _service.EnsureArtAsync(game, ct); }
            catch { return; } // sin red o cancelado: se queda la carátula que hay
            if (ct.IsCancellationRequested || updated == null || _shelfSystem != system) return;
            if (string.Equals(updated.CoverPath, game.CoverPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(updated.BackgroundPath, game.BackgroundPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(updated.Platform, game.Platform, StringComparison.Ordinal)) return;

            int found = _games.IndexOf(game);
            if (found < 0) return;
            _games[found] = updated;
            int index = _archives.Count + found;
            if (_views.TryGetValue(index, out var old))
            {
                Shelf.Children.Remove(old);
                _views.Remove(index);
                CreateView(system, index);
                LayoutShelf();
            }
            if (_gamesMode && index == _gameActive)
            {
                FillGameInfo();
                ShowGameArt(updated);
            }
        }

        // Portadas: se decodifican fuera del hilo gráfico y llegan congeladas.
        private async Task LoadCoverAsync(GameSystem system, string path)
        {
            if (!_coversLoading.Add(path)) return;
            ImageSource image = null;
            try { image = await Task.Run(() => MediaView.LoadCover(path)); }
            catch { image = null; }
            finally { _coversLoading.Remove(path); }

            if (_covers.Count >= CoverCacheLimit) _covers.Clear(); // límite simple de memoria
            _covers[path] = image;
            if (image == null || _shelfSystem != system) return;
            int found = _games.FindIndex(g => string.Equals(g.CoverPath, path, StringComparison.OrdinalIgnoreCase));
            int index = found < 0 ? -1 : _archives.Count + found;
            if (index >= 0 && _views.TryGetValue(index, out var old))
            {
                Shelf.Children.Remove(old);
                _views.Remove(index);
                CreateView(system, index);
            }
        }

        private void LayoutShelf()
        {
            if (ItemCount == 0) return;
            RealizeViews();
            double center = Shelf.ActualWidth / 2;
            double spacing = MediaView.SlotWidth + 18;
            foreach (var pair in _views)
            {
                var view = pair.Value;
                double offset = pair.Key - _gamePos;
                double distance = Math.Abs(offset);
                Canvas.SetLeft(view, center + offset * spacing - MediaView.SlotWidth / 2);
                Canvas.SetTop(view, Math.Max(0, Shelf.ActualHeight - MediaView.SlotHeight));
                double scale = (0.86 + 0.14 * _focus) * (1 - Math.Min(distance, 4) * 0.05 * _focus);
                var st = (ScaleTransform)view.RenderTransform;
                st.ScaleX = st.ScaleY = scale;
                view.Opacity = (0.72 + 0.28 * _focus) * Math.Max(0.25, 1 - Math.Max(0, distance - 3) * 0.25);
                Panel.SetZIndex(view, 100 - (int)Math.Round(distance * 5));
            }
        }

        private void EnterGames()
        {
            if (_gamesMode || ItemCount == 0) return;
            _gamesMode = true;
            _sounds.Enter();
            ShelfHint.Text = "";
            SelectGame(Math.Max(0, Math.Min(_gameTarget, ItemCount - 1)));
        }

        private void ExitGames(bool silent = false)
        {
            if (!_gamesMode) return;
            _gamesMode = false;
            if (!silent) _sounds.Back();
            var view = ViewAt(_gameActive);
            if (view != null) view.IsSelected = false;
            _gameActive = -1;
            HideGameArt();
            ShelfHint.Text = ItemCount == 0 ? "" : "Pulsa ↓ o el ▼ del mando para elegir un juego";
            FadeInfo();
        }

        private void StepGame(int direction)
        {
            if (ItemCount == 0) return;
            int next = Math.Max(0, Math.Min(ItemCount - 1, _gameTarget + direction));
            if (next == _gameTarget && _gameActive == next) { Bump(); return; }
            SelectGame(next);
            _sounds.Move(next);
        }

        private void SelectGame(int index)
        {
            if (index < 0 || index >= ItemCount) return;
            if (_gameActive != index)
            {
                var previous = ViewAt(_gameActive);
                if (previous != null) previous.IsSelected = false;
            }
            _gameTarget = index;
            _gameActive = index;
            var view = ViewAt(index);
            if (view != null) view.IsSelected = true; // si aún no existe, RealizeViews la crea ya seleccionada
            FillGameInfo();
            ShowGameArt(SelectedGame);
        }

        private void Bump()
        {
            // Tope de la estantería: un pequeño rebote en vez de no hacer nada.
            ViewAt(_gameActive)?.Nudge();
        }

        // ================================================================ Arte de fondo del juego

        private sealed class Backdrop
        {
            public ImageSource Image;
            public Color Tint;
            public bool Hero; // portada nítida en composición, cuando no hay fondo HD
        }

        private readonly Dictionary<string, Backdrop> _backdrops = new Dictionary<string, Backdrop>(StringComparer.OrdinalIgnoreCase);
        private string _backdropPath;
        private bool _backdropFront; // false: GameArtA está delante

        // Fondo HD del juego (PIC1, arte descargado o generado) o, si no hay, su portada nítida.
        private static string BackdropOf(GameEntry game, out bool hero)
        {
            if (!string.IsNullOrEmpty(game?.BackgroundPath) && System.IO.File.Exists(game.BackgroundPath))
            {
                hero = false;
                return game.BackgroundPath;
            }
            hero = true;
            return game?.CoverPath;
        }

        private static Backdrop LoadBackdrop(string path, bool hero)
        {
            var image = MediaView.LoadImage(path, hero ? 900 : 1920);
            if (image == null) return null;
            return new Backdrop { Image = image, Tint = AverageColor(MediaView.LoadImage(path, 24) as BitmapSource), Hero = hero };
        }

        // Color medio de la imagen, algo más vivo, para el degradado que acompaña a la portada.
        private static Color AverageColor(BitmapSource small)
        {
            if (small == null) return Color.FromRgb(0x30, 0x34, 0x48);
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < pixels.Length; i += 4) { b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; n++; }
            if (n == 0) return Color.FromRgb(0x30, 0x34, 0x48);
            var c = Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
            double max = Math.Max(c.R, Math.Max(c.G, c.B));
            double boost = max > 0 ? Math.Min(2.2, 200 / max) : 1;
            return Color.FromRgb((byte)Math.Min(255, c.R * boost), (byte)Math.Min(255, c.G * boost), (byte)Math.Min(255, c.B * boost));
        }

        private async void ShowGameArt(GameEntry game)
        {
            var path = BackdropOf(game, out bool hero);
            if (string.IsNullOrEmpty(path)) { HideGameArt(); return; }
            if (string.Equals(path, _backdropPath, StringComparison.OrdinalIgnoreCase)) return;
            _backdropPath = path;

            if (!_backdrops.TryGetValue(path, out var art))
            {
                // Un pequeño margen para no decodificar cada juego al recorrer la estantería deprisa.
                await Task.Delay(140);
                if (!string.Equals(path, _backdropPath, StringComparison.OrdinalIgnoreCase)) return;
                try { art = await Task.Run(() => LoadBackdrop(path, hero)); }
                catch { art = null; }
                if (_backdrops.Count >= 12) _backdrops.Clear();
                _backdrops[path] = art;
                if (!string.Equals(path, _backdropPath, StringComparison.OrdinalIgnoreCase)) return;
            }
            if (art == null || !_gamesMode) { HideGameArt(); return; }

            // Fundido cruzado: la capa de atrás recibe el arte nuevo y pasa delante.
            var incoming = _backdropFront ? GameArtA : GameArtB;
            var outgoing = _backdropFront ? GameArtB : GameArtA;
            _backdropFront = !_backdropFront;
            var wash = (Rectangle)incoming.Children[0];
            var picture = (Image)incoming.Children[1];
            picture.Source = art.Image;
            if (art.Hero)
            {
                // Sin fondo HD: la portada nítida a la derecha, como en la tienda de una consola,
                // sobre un degradado con sus colores. Nunca ampliada ni difuminada.
                var source = (BitmapSource)art.Image;
                double height = Math.Min(Root.ActualHeight * 0.62, source.PixelHeight * 1.25);
                picture.Stretch = Stretch.Uniform;
                picture.Height = height;
                picture.HorizontalAlignment = HorizontalAlignment.Right;
                picture.VerticalAlignment = VerticalAlignment.Center;
                picture.Margin = new Thickness(0, 0, Root.ActualWidth * 0.07, Root.ActualHeight * 0.08);
                picture.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black };
                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x07, 0x08, 0x0D), 0.1));
                gradient.GradientStops.Add(new GradientStop(Palette.Mix(Color.FromRgb(0x07, 0x08, 0x0D), art.Tint, 0.55), 0.75));
                gradient.GradientStops.Add(new GradientStop(Palette.Mix(Color.FromRgb(0x07, 0x08, 0x0D), art.Tint, 0.8), 1));
                wash.Fill = gradient;
            }
            else
            {
                picture.Stretch = Stretch.UniformToFill;
                picture.Height = double.NaN;
                picture.HorizontalAlignment = HorizontalAlignment.Stretch;
                picture.VerticalAlignment = VerticalAlignment.Stretch;
                picture.Margin = new Thickness(0);
                picture.Effect = null;
                wash.Fill = null;
            }
            Panel.SetZIndex(incoming, 1);
            Panel.SetZIndex(outgoing, 0);
            var fade = TimeSpan.FromMilliseconds(650);
            incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade));
            outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fade) { BeginTime = TimeSpan.FromMilliseconds(250) });
            GameArtDim.BeginAnimation(OpacityProperty, new DoubleAnimation(art.Hero ? 0.2 : 0.55, fade));
            // Acercamiento lento, como en el menú de una consola.
            var zoom = (ScaleTransform)incoming.RenderTransform;
            var drift = new DoubleAnimation(1.0, art.Hero ? 1.03 : 1.08, TimeSpan.FromSeconds(24))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, drift);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, drift);
            GameArt.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(500)));
        }

        private void HideGameArt()
        {
            _backdropPath = null;
            GameArt.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(450)));
        }


        // ================================================================ Acciones

        private void PrimaryAction()
        {
            if (_gamesMode && SelectedArchive != null) ExtractSelected();
            else if (_gamesMode) LaunchSelectedGame();
            else if (_status != null && _status.Installed) LaunchEmulator();
            else if (Current != null && CanInstall(Current)) _ = InstallEmulatorAsync(Current);
            else InstallEmulatorHelp();
        }

        private double _padQuietUntil;
        private DateTime _lastLaunch = DateTime.MinValue;

        // Nunca dos lanzamientos seguidos: una ráfaga de pulsaciones no debe abrir varios emuladores.
        private bool LaunchAllowed()
        {
            if ((DateTime.UtcNow - _lastLaunch).TotalSeconds < 2) return false;
            _lastLaunch = DateTime.UtcNow;
            return true;
        }

        private void LaunchEmulator()
        {
            if (!LaunchAllowed()) return;
            var system = Current;
            if (system == null) return;
            Run(() =>
            {
                _launched.Add(_service.LaunchEmulator(system.Id));
                _sounds.Confirm();
                Flash(_cards[_active]);
                ShowToast("Abriendo " + EmulatorLabel + "…" + SetupNotes());
            });
        }

        private void LaunchSelectedGame()
        {
            var game = SelectedGame;
            var system = Current;
            // El juego debe ser de la consola mostrada (la estantería puede estar recargándose).
            if (game == null || system == null || !string.Equals(game.SystemId, system.Id, StringComparison.OrdinalIgnoreCase)) return;
            if (_status == null || !_status.Installed)
            {
                _sounds.Error();
                ShowToast("Para jugar a " + game.Title + " falta el emulador de " + system.Name + "."
                    + (CanInstall(system) ? " Vuelve a la consola y pulsa «Instalar " + InstallLabel(system) + "»." : " Cópialo en Emuladores\\" + system.Id + "."), error: true);
                return;
            }
            // Sin BIOS, firmware o claves el emulador solo mostraría un error confuso: se explica aquí.
            var missing = (_status.SetupMessages ?? new string[0]).FirstOrDefault(m => m.StartsWith("Falta", StringComparison.Ordinal));
            if (missing != null && ImportLabel(system) != null)
            {
                _sounds.Error();
                ShowToast(missing + " Sin eso " + EmulatorLabel + " no puede abrir juegos: vuelve a la consola (Esc o B) y pulsa «" + ImportLabel(system) + "».", error: true);
                return;
            }
            if (!LaunchAllowed()) return;
            Run(() =>
            {
                _launched.Add(_service.LaunchGame(game));
                _sounds.Confirm();
                (ViewAt(_gameActive) as MediaView)?.PlayLaunch();
                ShowToast("¡A jugar! Abriendo " + game.Title + " en " + EmulatorLabel + "…" + SetupNotes());
            });
        }

        // Descomprime el archivo seleccionado; al terminar, el juego aparece en la estantería ya elegido.
        private void ExtractSelected() => _ = ExtractSelectedAsync();

        private async Task ExtractSelectedAsync()
        {
            var archive = SelectedArchive;
            var system = Current;
            if (archive == null || system == null || !string.Equals(archive.SystemId, system.Id, StringComparison.OrdinalIgnoreCase)) return;
            if (archive.State != ArchiveState.Ready)
            {
                _sounds.Error();
                ShowToast(string.IsNullOrEmpty(archive.Message) ? ArchiveView.StateText(archive) : archive.Message, error: true);
                if (_status != null) Run(() => _service.OpenFolder(_status.GamesFolder));
                return;
            }
            if (!_extracting.Add(archive.Path)) return;
            _sounds.Confirm();
            bool package = ArchiveView.IsPackage(archive);
            ShowToast((package ? "Instalando " : "Descomprimiendo ") + archive.Title + "… puedes seguir usando Nexo mientras tanto.");
            FillGameInfo();
            var progress = new Progress<long>(bytes =>
            {
                // La pieza puede haberse reciclado al moverse por la estantería: se busca la actual.
                var current = _views.Values.OfType<ArchiveView>().FirstOrDefault(a => string.Equals(a.Archive.Path, archive.Path, StringComparison.OrdinalIgnoreCase));
                // Un PKG instala un lote (base + parche + DLC) mayor que su propio tamaño: sin porcentaje.
                current?.ShowProgress(bytes, package ? 0 : archive.ExpectedSize ?? 0);
            });
            try
            {
                string folder = await _service.ExtractArchiveAsync(archive, progress, _closing.Token);
                _sounds.Confirm();
                ShowToast("¡Listo! " + archive.Title + " ya está en tu estantería.");
                if (Current == system) _selectAfterScan = folder;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error)
            {
                _sounds.Error();
                ShowToast("No se pudo " + (package ? "instalar " : "descomprimir ") + archive.Title + ": " + error.Message, error: true);
                var current = _views.Values.OfType<ArchiveView>().FirstOrDefault(a => string.Equals(a.Archive.Path, archive.Path, StringComparison.OrdinalIgnoreCase));
                current?.ShowError(package ? "Error al instalar" : "Error al descomprimir");
            }
            finally
            {
                _extracting.Remove(archive.Path);
            }
            if (Current == system) StartScan(system); // el juego nuevo aparece (y el comprimido desaparece)
        }

        // Lo que Nexo ajustó en el emulador antes de abrirlo (mando y gráficos), para informar al usuario.
        private string SetupNotes()
        {
            var notes = "";
            var controllers = _service.LastControllerSetup;
            if (controllers != null && controllers.Changed && !string.IsNullOrEmpty(controllers.Message))
                notes += "\n🎮 " + controllers.Message;
            var graphics = _service.LastGraphicsSetup;
            if (graphics != null && graphics.Changed && !string.IsNullOrEmpty(graphics.Message))
                notes += "\n✨ " + graphics.Message;
            return notes;
        }

        // Chip de la barra superior: la tarjeta gráfica detectada y el nivel de calidad que usa Nexo.
        private async Task ShowGpuAsync()
        {
            try
            {
                var gpu = await Task.Run(() => _service.GpuInfo);
                if (gpu == null || string.IsNullOrEmpty(gpu.Name)) return;
                GpuText.Text = "Gráficos " + gpu.Tier + "  ·  " + gpu.Name.Replace("NVIDIA GeForce ", "").Replace("AMD Radeon ", "Radeon ");
                GpuChip.ToolTip = gpu.Name + " · " + Math.Round(gpu.VramBytes / 1073741824.0, 1) + " GB de VRAM. "
                                  + "Nexo ajusta cada emulador al máximo que esta tarjeta sostiene.";
                GpuChip.Visibility = Visibility.Visible;
            }
            catch { /* sin datos de GPU: el chip no se muestra */ }
        }

        // ================================================================ Instalación y archivos del sistema

        private readonly Dictionary<string, string> _installing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Every system has an official download, PS5 included (Nova5).
        private static bool CanInstall(GameSystem system) => true;

        private static string InstallLabel(GameSystem system) =>
            string.Equals(system.Id, "Switch", StringComparison.OrdinalIgnoreCase) ? "Eden" : system.EmulatorName;

        private static string ImportLabel(GameSystem system)
        {
            switch (system.Id)
            {
                case "PS2": return "Importar BIOS";
                case "PS3": return "Instalar firmware";
                case "Switch": return "Importar claves y firmware";
                case "Xbox": return "Importar BIOS y MCPX";
                default: return null;
            }
        }

        private async Task InstallEmulatorAsync(GameSystem system)
        {
            if (_installing.ContainsKey(system.Id)) return;
            _installing[system.Id] = "Conectando con la web oficial…";
            _sounds.Confirm();
            FillInfo();
            var progress = new Progress<SetupProgress>(p =>
            {
                if (!_installing.ContainsKey(system.Id)) return;
                string amount = p.TotalBytes > 0
                    ? string.Format("{0:0} %  ·  {1:0} de {2:0} MB", 100.0 * p.BytesCompleted / p.TotalBytes.Value, p.BytesCompleted / 1048576.0, p.TotalBytes.Value / 1048576.0)
                    : string.Format("{0:0} MB", p.BytesCompleted / 1048576.0);
                _installing[system.Id] = p.Stage + "…  " + amount;
                if (Current == system && !_gamesMode) StatusPath.Text = _installing[system.Id];
            });
            try
            {
                var result = await _service.InstallEmulatorAsync(system.Id, progress, _closing.Token);
                _installing.Remove(system.Id);
                _sounds.Confirm();
                ShowToast(InstallLabel(system) + ": " + result.Message);
            }
            catch (OperationCanceledException) { _installing.Remove(system.Id); return; }
            catch (Exception error)
            {
                _installing.Remove(system.Id);
                _sounds.Error();
                ShowToast("No se pudo instalar " + InstallLabel(system) + ": " + error.Message, error: true);
            }
            if (Current == system) RefreshCurrent(force: true);
        }

        private async void ImportSystemFiles()
        {
            var system = Current;
            if (system == null || ImportLabel(system) == null) return;
            var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
            switch (system.Id)
            {
                case "PS2":
                    dialog.Title = "Elige la BIOS de tu PlayStation 2";
                    dialog.Filter = "BIOS de PS2 (*.bin)|*.bin|Todos los archivos|*.*";
                    break;
                case "PS3":
                    dialog.Title = "Elige PS3UPDAT.PUP (el firmware oficial de Sony)";
                    dialog.Filter = "Firmware de PS3 (PS3UPDAT.PUP)|*.pup|Todos los archivos|*.*";
                    dialog.Multiselect = false;
                    break;
                case "Xbox":
                    dialog.Title = "Elige la MCPX boot ROM y la BIOS (flash) de tu Xbox";
                    dialog.Filter = "MCPX y BIOS de Xbox (*.bin;*.rom)|*.bin;*.rom|Todos los archivos|*.*";
                    break;
                default:
                    dialog.Title = "Elige prod.keys (y title.keys) y el ZIP del firmware de tu Switch";
                    dialog.Filter = "Claves y firmware (*.keys;*.zip)|*.keys;*.zip|Todos los archivos|*.*";
                    break;
            }
            string downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (System.IO.Directory.Exists(downloads)) dialog.InitialDirectory = downloads;
            if (dialog.ShowDialog(this) != true) return;
            _importing = true;
            try
            {
                ShowToast("Importando…");
                var result = await _service.ImportSystemFilesAsync(system.Id, dialog.FileNames, _closing.Token);
                _sounds.Confirm();
                ShowToast(result.Message);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) { _sounds.Error(); ShowToast(error.Message, error: true); }
            finally { _importing = false; }
            if (Current == system) RefreshCurrent(force: true);
        }

        private void InstallEmulatorHelp()
        {
            var system = Current;
            if (system == null) return;
            Run(() =>
            {
                _service.OpenFolder(_status.EmulatorFolder);
                ShowToast("Copia aquí " + system.EmulatorName + " (la carpeta completa del emulador). Nexo lo detectará solo al volver.");
            });
        }

        private void OpenGamesFolder()
        {
            var system = Current;
            if (system == null || _status == null) return;
            Run(() =>
            {
                _service.OpenFolder(_status.GamesFolder);
                ShowToast("Copia aquí tus juegos de " + system.Name + ". Aparecerán al volver a Nexo.");
            });
        }

        private void OpenEmulatorFolder()
        {
            if (_status == null) return;
            Run(() => _service.OpenFolder(_status.EmulatorFolder));
        }

        private void OpenWebsite()
        {
            var system = Current;
            if (system == null || string.IsNullOrEmpty(system.Website)) return;
            Run(() =>
            {
                _service.OpenWebsite(system.Id);
                ShowToast("Abriendo la web oficial de " + system.EmulatorName + " en tu navegador.");
            });
        }

        private void LocateExecutable()
        {
            var system = Current;
            if (system == null) return;
            var dialog = new OpenFileDialog
            {
                Title = "Elige el ejecutable de " + system.EmulatorName,
                Filter = "Aplicaciones (*.exe)|*.exe",
                InitialDirectory = _status?.EmulatorFolder,
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != true) return;
            Run(() =>
            {
                _service.SetExecutable(system.Id, dialog.FileName);
                RefreshCurrent(force: true);
                _sounds.Confirm();
                ShowToast(EmulatorLabel + " ya está listo para abrir.");
            });
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (NexoException error) { _sounds.Error(); ShowToast(error.Message, error: true); }
            catch (Exception error) { _sounds.Error(); ShowToast("No se pudo completar: " + error.Message, error: true); }
        }

        private void RefreshCurrent(bool force)
        {
            var system = Current;
            if (system == null || _service == null) return;
            if (!force && (DateTime.UtcNow - _lastRefresh).TotalSeconds < 1.5) return;
            _lastRefresh = DateTime.UtcNow;
            // Recoge juegos o emuladores copiados mientras Nexo estaba en segundo plano.
            _ = RefreshStatusAsync(system, scan: true);
        }

        private void Flash(ConsoleCard card)
        {
            var a = new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(200)) { AutoReverse = true, EasingFunction = new CubicEase() };
            // FillBehavior.Stop: al terminar, Layout vuelve a mandar sobre la opacidad del borde.
            card.Highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(600)) { FillBehavior = FillBehavior.Stop });
            var s = new ScaleTransform(1, 1);
            card.Art.RenderTransformOrigin = new Point(0.5, 0.5);
            card.Art.RenderTransform = s;
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ================================================================ Teclado y mando

        private void OnKey(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Durante la cuenta atrás de la actualización, Esc/Retroceso la dejan para cuando se cierre Nexo.
            if ((key == Key.Escape || key == Key.Back) && PostponeUpdate()) { e.Handled = true; return; }
            switch (key)
            {
                case Key.Left: case Key.A: if (_gamesMode) StepGame(-1); else Step(-1); break;
                case Key.Right: case Key.D: if (_gamesMode) StepGame(1); else Step(1); break;
                case Key.Down: case Key.S:
                    if (ItemCount > 0) EnterGames(); else { _sounds.Error(); ShowToast("Todavía no hay juegos de " + Current?.Name + ". Pulsa F para abrir su carpeta."); }
                    break;
                case Key.Up: case Key.W: case Key.Escape: case Key.Back:
                    if (_gamesMode) ExitGames(); else if (key == Key.Escape && WindowState == WindowState.Maximized) { /* nada: Nexo se cierra con el botón o Alt+F4 */ }
                    break;
                case Key.Enter: case Key.Space:
                    PrimaryAction();
                    break;
                case Key.PageUp: GoTo((int)Mod(_active - 1, _systems.Count)); break;
                case Key.PageDown: GoTo((int)Mod(_active + 1, _systems.Count)); break;
                case Key.Home: if (_gamesMode) SelectGame(0); else GoTo(0); break;
                case Key.End: if (_gamesMode) SelectGame(ItemCount - 1); else GoTo(_systems.Count - 1); break;
                case Key.F: OpenGamesFolder(); break;
                case Key.E: LocateExecutable(); break;
                case Key.M: ToggleSound(); break;
                case Key.F11: ToggleFullscreen(); break;
                default: return;
            }
            e.Handled = true;
        }

        private void PollPad()
        {
            if (_pad == null) return;
            IReadOnlyList<PadButton> presses;
            try { presses = _pad.Poll((long)_clock.Elapsed.TotalMilliseconds); }
            catch { return; }
            if (_pad.Connected != _padConnected)
            {
                _padConnected = _pad.Connected;
                PadChip.Visibility = _padConnected ? Visibility.Visible : Visibility.Collapsed;
                DonateKey.Visibility = PadChip.Visibility;
                if (_padConnected) ShowToast("Mando conectado: muévete con la cruceta o el stick y pulsa A.");
                UpdateHints();
            }
            if (presses == null || !IsActive || _clock.Elapsed.TotalMilliseconds < _padQuietUntil) return;
            foreach (var button in presses)
            {
                switch (button)
                {
                    case PadButton.Left: if (_gamesMode) StepGame(-1); else Step(-1); break;
                    case PadButton.Right: if (_gamesMode) StepGame(1); else Step(1); break;
                    case PadButton.Down: if (ItemCount > 0) EnterGames(); break;
                    case PadButton.Up: ExitGames(); break;
                    case PadButton.B: if (!PostponeUpdate()) ExitGames(); break;
                    case PadButton.Back: OpenDonate(); break;
                    case PadButton.LB: Step(-1); break;
                    case PadButton.RB: Step(1); break;
                    case PadButton.A: case PadButton.Start: PrimaryAction(); break;
                    case PadButton.X: OpenGamesFolder(); break;
                    case PadButton.Y: OpenWebsite(); break;
                }
            }
        }

        private void ToggleFullscreen()
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                ResizeMode = ResizeMode.CanResize;
            }
            else
            {
                ResizeMode = ResizeMode.CanMinimize;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
        }

        // ================================================================ Actualizaciones y donación

        // Como Nova5 en modo «automático»: al abrir se consulta la última versión publicada en GitHub;
        // si hay una nueva se descarga en segundo plano, se verifica y se instala sola sobre esta carpeta.
        private enum UpdateState { None, Notice, Downloading, Waiting, Countdown, Postponed, Installing }
        private UpdateState _updateState;
        private AppUpdate _update;
        private string _updatePayload;
        private int _updateCountdown;
        private DispatcherTimer _updateTimer;
        private bool _importing;
        private readonly List<int> _launched = new List<int>();
        private static string InstallDir => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        private async Task CheckUpdatesAsync()
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), _closing.Token); }
            catch (OperationCanceledException) { return; }
            // Sin red, sin publicación o con GitHub caído: null y ningún aviso.
            var update = await AppUpdates.CheckAsync(AppUpdates.Current, _closing.Token);
            if (update == null || _closing.IsCancellationRequested) return;
            _update = update;
            string version = update.Version.ToString();
            if (!AppUpdates.IsInstalled(InstallDir))
            {
                UpdateNotice("Nexo " + version + " disponible", "Hay una versión nueva de Nexo (" + version + "). Esta copia no está instalada, así que no puede actualizarse sola: "
                    + "instala Nexo con Instalar-Nexo.exe. Pulsa «Nexo " + version + " disponible» arriba para descargarlo.");
                return;
            }
            if (!AppUpdates.CanWrite(InstallDir))
            {
                UpdateNotice("Nexo " + version + " disponible", "Hay una versión nueva de Nexo (" + version + "), pero no tengo permiso para escribir en " + InstallDir
                    + ". Descárgala (pulsa el aviso de arriba) y ejecuta Instalar-Nexo.exe sobre esta carpeta.");
                return;
            }
            if (AppUpdates.AttemptFailed(update.Version, AppUpdates.Current))
            {
                string reason = "";
                try { reason = System.IO.File.Exists(AppUpdates.LogPath) ? " (" + System.IO.File.ReadAllText(AppUpdates.LogPath).Trim() + ")" : ""; } catch (System.IO.IOException) { }
                UpdateNotice("Nexo " + version + " disponible", "No se pudo instalar Nexo " + version + " automáticamente" + reason
                    + ". Pulsa el aviso de arriba para descargar el instalador y ejecútalo sobre esta carpeta.", error: true);
                return;
            }
            _updateState = UpdateState.Downloading;
            UpdateText.Text = "Descargando Nexo " + version + "…";
            UpdateChip.ToolTip = "Nexo " + version + " se descarga en segundo plano y se instalará solo.";
            UpdateChip.Visibility = Visibility.Visible;
            ShowToast("Hay una versión nueva de Nexo (" + version + "). Se descarga en segundo plano y se instalará sola; tus juegos, emuladores y ajustes se conservan.", seconds: 7);
            try
            {
                var progress = new Progress<double>(p => { if (_updateState == UpdateState.Downloading) UpdateText.Text = string.Format("Descargando Nexo {0}  ·  {1:0} %", version, p * 100); });
                string setup = await AppUpdates.DownloadAsync(update, AppUpdates.WorkFolder, progress, _closing.Token);
                UpdateText.Text = "Preparando Nexo " + version + "…";
                _updatePayload = await Task.Run(() => AppUpdates.ExtractInstaller(setup, AppUpdates.WorkFolder));
            }
            catch (Exception error)
            {
                if (_closing.IsCancellationRequested) return;
                _updateState = UpdateState.None;
                UpdateChip.Visibility = Visibility.Collapsed;
                ShowToast("No se pudo descargar la actualización de Nexo: " + error.Message + " Se volverá a intentar la próxima vez que abras Nexo.", error: true, seconds: 7);
                return;
            }
            if (_closing.IsCancellationRequested) return;
            _updateState = UpdateState.Waiting;
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _updateTimer.Tick += (s, e) => UpdateTick();
            _updateTimer.Start();
            UpdateTick();
        }

        private void UpdateNotice(string chip, string message, bool error = false)
        {
            _updateState = UpdateState.Notice;
            UpdateText.Text = chip;
            UpdateChip.ToolTip = "Abrir la página de descargas de Nexo";
            UpdateChip.Visibility = Visibility.Visible;
            ShowToast(message, error, seconds: 9);
        }

        // No se cierra Nexo mientras descomprime, instala o importa algo, ni mientras se juega a algo lanzado desde Nexo.
        private bool UpdateBusy()
        {
            _launched.RemoveAll(pid => { try { using (var p = Process.GetProcessById(pid)) return p.HasExited; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } });
            return _extracting.Count > 0 || _installing.Count > 0 || _importing || WindowState == WindowState.Minimized || (_launched.Count > 0 && !IsActive);
        }

        private void UpdateTick()
        {
            string version = _update.Version.ToString();
            if (_updateState == UpdateState.Waiting)
            {
                if (UpdateBusy()) { UpdateText.Text = "Nexo " + version + " listo · se instalará al terminar"; return; }
                _updateState = UpdateState.Countdown;
                _updateCountdown = 8;
                _sounds.Confirm();
                ShowToast("Nexo se actualizará a la versión " + version + " en unos segundos y se abrirá de nuevo. "
                    + (_padConnected ? "Pulsa B" : "Pulsa Esc") + " para dejarlo para cuando cierres Nexo.", seconds: 8);
            }
            if (_updateState != UpdateState.Countdown) return;
            if (UpdateBusy()) { _updateState = UpdateState.Waiting; return; }
            if (_updateCountdown <= 0) { InstallUpdateNow(); return; }
            UpdateText.Text = "Actualizando a " + version + " en " + _updateCountdown + " s  ·  " + (_padConnected ? "B" : "Esc") + ": más tarde";
            _updateCountdown--;
        }

        private bool PostponeUpdate()
        {
            if (_updateState != UpdateState.Countdown) return false;
            _updateState = UpdateState.Postponed;
            UpdateText.Text = "Nexo " + _update.Version + " se instalará al cerrar";
            ShowToast("De acuerdo: Nexo " + _update.Version + " se instalará cuando cierres Nexo. Pulsa el aviso de arriba para instalarlo ya.");
            return true;
        }

        private void UpdateChipAction()
        {
            if (_updateState == UpdateState.Notice) OpenLink(AppUpdates.ReleasesPage, "Abriendo la página de descargas de Nexo en tu navegador.");
            else if (_updateState == UpdateState.Waiting || _updateState == UpdateState.Countdown || _updateState == UpdateState.Postponed) InstallUpdateNow();
        }

        private void InstallUpdateNow()
        {
            _updateTimer?.Stop();
            _updateState = UpdateState.Installing;
            UpdateText.Text = "Instalando Nexo " + _update.Version + "…";
            try
            {
                AppUpdates.RecordAttempt(_update.Version);
                // El instalador espera a que Nexo se cierre, sustituye el programa y lo vuelve a abrir.
                AppUpdates.StartInstaller(_updatePayload, InstallDir, relaunch: true);
                Close();
            }
            catch (Exception error)
            {
                _updateState = UpdateState.None;
                UpdateChip.Visibility = Visibility.Collapsed;
                ShowToast("No se pudo iniciar la actualización de Nexo: " + error.Message, error: true, seconds: 7);
            }
        }

        // Pospuesta, o lista mientras había algo en marcha: se instala al cerrar, sin volver a abrir Nexo.
        private void InstallUpdateOnClose()
        {
            if (_updateState != UpdateState.Postponed && _updateState != UpdateState.Waiting && _updateState != UpdateState.Countdown) return;
            _updateTimer?.Stop();
            _updateState = UpdateState.Installing;
            try
            {
                AppUpdates.RecordAttempt(_update.Version);
                AppUpdates.StartInstaller(_updatePayload, InstallDir, relaunch: false);
            }
            catch (Exception) { /* se reintentará la próxima vez que se abra Nexo */ }
        }

        private void OpenDonate() => OpenLink(NexoLinks.Donate, "Abriendo Ko-fi en tu navegador.");

        private void OpenLink(string url, string message) => Run(() => { NexoLinks.Open(url); ShowToast(message); });

        // ================================================================ Varios

        private bool _compact, _compactHints;

        private void AdaptToSize()
        {
            double h = ActualHeight;
            _compact = h < 900;
            _compactHints = h < 760;
            InfoName.FontSize = h < 760 ? 40 : h < 900 ? 50 : 64;
            ShelfArea.Height = h < 760 ? 214 : 250;
            if (!_gamesMode) InfoDescription.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
            Tagline.Visibility = ActualWidth < 1100 ? Visibility.Collapsed : Visibility.Visible;
            UpdateHints();
        }

        private void ToggleSound()
        {
            _sounds.Muted = !_sounds.Muted;
            UpdateSoundIcon();
            if (!_sounds.Muted) _sounds.Move(2);
        }

        private void UpdateSoundIcon()
        {
            SoundIcon.Data = Geometry.Parse(_sounds.Muted
                ? "M4,9 H8 L13,5 V19 L8,15 H4 Z M17,9 L22,15 M22,9 L17,15"
                : "M4,9 H8 L13,5 V19 L8,15 H4 Z M16,8.5 A5,5 0 0 1 16,15.5 M18.5,6 A8.5,8.5 0 0 1 18.5,18");
        }

        private void UpdateClock() => Clock.Text = DateTime.Now.ToString("HH:mm");

        private void ShowToast(string message, bool error = false, double seconds = 3.6)
        {
            ToastText.Text = message;
            Toast.BorderBrush = new SolidColorBrush(error ? Color.FromArgb(0x80, 0xF8, 0x71, 0x71) : Color.FromArgb(0x60, 0x4A, 0xDE, 0x80));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
            ToastMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)) { EasingFunction = ease });
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
                ToastMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-20, TimeSpan.FromMilliseconds(300)));
            };
            _toastTimer.Start();
        }

        // ---------------------------------------------------------------- Partículas

        private sealed class Particle
        {
            public Ellipse Shape;
            public double X, Y, Vx, Vy, Phase, Alpha;
        }

        private void SetupParticles()
        {
            for (int i = 0; i < 70; i++)
            {
                double r = 0.6 + _random.NextDouble() * 1.8;
                var p = new Particle
                {
                    Shape = new Ellipse { Width = r * 2, Height = r * 2, Fill = _accent },
                    X = _random.NextDouble() * 1920, Y = _random.NextDouble() * 1080,
                    Vy = -(6 + _random.NextDouble() * 18), Vx = (_random.NextDouble() - 0.5) * 6,
                    Phase = _random.NextDouble() * Math.PI * 2, Alpha = 0.15 + _random.NextDouble() * 0.5,
                };
                _particles.Add(p);
                Particles.Children.Add(p.Shape);
            }
        }

        private void UpdateParticles(double dt)
        {
            double w = Math.Max(1, Particles.ActualWidth), h = Math.Max(1, Particles.ActualHeight);
            double drift = (_target - _pos) * 40;
            foreach (var p in _particles)
            {
                p.Phase += dt;
                p.Y += p.Vy * dt;
                p.X += (p.Vx + drift) * dt;
                if (p.Y < -10) { p.Y = h + 10; p.X = _random.NextDouble() * w; }
                if (p.X < -10) p.X = w + 10;
                if (p.X > w + 10) p.X = -10;
                if (p.Y > h + 10) p.Y = h + 10;
                Canvas.SetLeft(p.Shape, p.X);
                Canvas.SetTop(p.Shape, p.Y);
                p.Shape.Opacity = p.Alpha * (0.6 + 0.4 * Math.Sin(p.Phase * 1.5));
            }
        }
    }
}

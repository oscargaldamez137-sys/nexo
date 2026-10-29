using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Nexo.Core;

namespace Nexo
{
    /// <summary>Lo común a las piezas de la estantería: juegos y archivos comprimidos.</summary>
    public interface IShelfView
    {
        bool IsSelected { get; set; }
        void Nudge();
    }

    /// <summary>
    /// Un juego todavía comprimido (.7z/.zip/.rar): una caja con cremallera. Lista para descomprimir
    /// (ámbar), descarga incompleta (roja) o ilegible (gris). Muestra el progreso al descomprimir.
    /// </summary>
    public sealed class ArchiveView : Grid, IShelfView
    {
        public ArchiveEntry Archive { get; }

        private readonly Grid _body = new Grid { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _lift = new TranslateTransform();
        private readonly TranslateTransform _zipperPull = new TranslateTransform();
        private readonly DropShadowEffect _glow;
        private readonly TextBlock _title = new TextBlock();
        private readonly TextBlock _status = new TextBlock();
        private readonly Border _progressTrack;
        private readonly Border _progressFill;
        private bool _selected;
        private bool _busy;

        public ArchiveView(ArchiveEntry archive)
        {
            Archive = archive;
            Width = MediaView.SlotWidth;
            Height = MediaView.SlotHeight;
            Background = Brushes.Transparent;
            Cursor = System.Windows.Input.Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.75);
            RenderTransform = new ScaleTransform(1, 1);
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            Color top, bottom, accent;
            switch (archive.State)
            {
                case ArchiveState.Ready:
                    top = Color.FromRgb(0xF7, 0xB7, 0x4A); bottom = Color.FromRgb(0xB8, 0x6B, 0x1E); accent = Color.FromRgb(0xFF, 0xD2, 0x7A); break;
                case ArchiveState.Incomplete:
                    top = Color.FromRgb(0xE5, 0x4B, 0x4B); bottom = Color.FromRgb(0x8E, 0x1F, 0x2A); accent = Color.FromRgb(0xFF, 0x8A, 0x8A); break;
                default:
                    top = Color.FromRgb(0x7C, 0x82, 0x99); bottom = Color.FromRgb(0x3A, 0x3E, 0x4C); accent = Color.FromRgb(0xC9, 0xCD, 0xD5); break;
            }

            const double w = 124, h = 150;
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_lift);
            _body.RenderTransformOrigin = new Point(0.5, 1);
            _body.RenderTransform = group;
            _glow = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 0, Opacity = 0, Color = accent };

            var box = new Grid { Width = w, Height = h, ClipToBounds = true };
            box.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10);
            box.Children.Add(new Rectangle { Fill = new LinearGradientBrush(top, bottom, 90) });
            // Brillo superior, como el de las cajas.
            box.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90), Height = h * 0.45, VerticalAlignment = VerticalAlignment.Top });

            // Cremallera: una banda central con dientes alternos.
            var zipper = new Canvas { Width = 16, HorizontalAlignment = HorizontalAlignment.Center };
            zipper.Children.Add(new Rectangle { Width = 6, Height = h, Fill = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), RenderTransform = new TranslateTransform(5, 0) });
            for (double y = 4; y < h; y += 8)
            {
                zipper.Children.Add(new Rectangle
                {
                    Width = 6, Height = 4, RadiusX = 1, RadiusY = 1, Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0xF4, 0xF1, 0xEA)),
                    RenderTransform = new TranslateTransform(((int)(y / 8) % 2 == 0) ? 1 : 9, y),
                });
            }
            box.Children.Add(zipper);
            // Tirador: baja un poco al seleccionar, como si se abriera.
            var pull = new Border
            {
                Width = 16, Height = 26, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
                Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF1, 0xEA), Color.FromRgb(0xB9, 0xB4, 0xA8), 90),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), BorderThickness = new Thickness(1),
                RenderTransform = _zipperPull,
            };
            pull.Child = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
            box.Children.Add(pull);

            // Etiqueta con el formato y el estado.
            var label = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 8), Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x0E, 0x10, 0x1A)),
            };
            var labelStack = new StackPanel();
            labelStack.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetExtension(archive.Path).TrimStart('.').ToUpperInvariant(),
                FontSize = 15, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Center,
            });
            _status.FontSize = 10.5;
            _status.FontWeight = FontWeights.SemiBold;
            _status.Foreground = Brushes.White;
            _status.TextAlignment = TextAlignment.Center;
            _status.TextWrapping = TextWrapping.Wrap;
            _status.Text = StateText(archive);
            labelStack.Children.Add(_status);
            _progressTrack = new Border
            {
                Height = 5, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 5, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), Visibility = Visibility.Collapsed,
            };
            _progressFill = new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _progressTrack.Child = _progressFill;
            labelStack.Children.Add(_progressTrack);
            label.Child = labelStack;
            box.Children.Add(label);

            var frame = new Grid { Width = w, Height = h };
            frame.Children.Add(box);
            frame.Children.Add(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.2), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)) });
            frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
            _body.Children.Add(frame);
            _body.Effect = _glow;
            Children.Add(_body);

            _title.Text = archive.Title;
            _title.FontSize = 13;
            _title.FontWeight = FontWeights.SemiBold;
            _title.TextAlignment = TextAlignment.Center;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.TextWrapping = TextWrapping.Wrap;
            _title.MaxHeight = 34;
            _title.Margin = new Thickness(6, 6, 6, 0);
            _title.Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0xBD, 0xD0));
            SetRow(_title, 1);
            Children.Add(_title);
        }

        public static string StateText(ArchiveEntry archive)
        {
            switch (archive.State)
            {
                case ArchiveState.Ready: return IsPackage(archive) ? "Listo para instalar" : "Listo para descomprimir";
                case ArchiveState.Incomplete:
                    return archive.ExpectedSize.HasValue
                        ? "Incompleto: " + Size(archive.Size) + " de " + Size(archive.ExpectedSize.Value)
                        : "Descarga incompleta";
                default: return "No se puede leer";
            }
        }

        public static string Size(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            return Math.Max(1, bytes / (1L << 20)) + " MB";
        }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
                var time = TimeSpan.FromMilliseconds(380);
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value ? 1.1 : 1, time) { EasingFunction = ease });
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value ? 1.1 : 1, time) { EasingFunction = ease });
                _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(value ? -10 : 0, time) { EasingFunction = ease });
                _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(value ? 38 : 0, time));
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(value ? 0.75 : 0, time));
                _zipperPull.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(value && Archive.State == ArchiveState.Ready ? 22 : 0, time) { EasingFunction = ease });
                _title.Foreground = new SolidColorBrush(value ? Colors.White : Color.FromRgb(0xB7, 0xBD, 0xD0));
            }
        }

        public void Nudge()
        {
            _lift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(260),
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(-7, KeyTime.FromPercent(0.25)),
                    new LinearDoubleKeyFrame(7, KeyTime.FromPercent(0.75)),
                    new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)),
                },
            });
        }

        /// <summary>Muestra el avance; total puede ser 0 si no se conoce (barra en movimiento).</summary>
        /// <summary>Un PKG de PS4 no se descomprime: se instala (con su parche y DLC).</summary>
        public static bool IsPackage(ArchiveEntry archive) =>
            archive != null && archive.Path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase);

        public void ShowProgress(long written, long total)
        {
            if (!_busy)
            {
                _busy = true;
                _progressTrack.Visibility = Visibility.Visible;
                _zipperPull.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(4, 110, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            double trackWidth = Math.Max(1, _progressTrack.ActualWidth);
            double fraction = total > 0 ? Math.Min(1, written / (double)total) : (written / (double)(64L << 20)) % 1.0;
            _progressFill.Width = trackWidth * fraction;
            _status.Text = (IsPackage(Archive) ? "Instalando… " : "Descomprimiendo… ") + Size(written);
        }

        public void ShowError(string text)
        {
            _busy = false;
            _progressTrack.Visibility = Visibility.Collapsed;
            _zipperPull.BeginAnimation(TranslateTransform.YProperty, null);
            _status.Text = text;
        }
    }
}

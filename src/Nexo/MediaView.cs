using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Nexo.Core;

namespace Nexo
{
    /// <summary>
    /// Un juego dibujado como el soporte físico de su consola: caja con disco, UMD, tarjeta o cartucho.
    /// Al seleccionarlo cobra vida: el disco sale de la caja y gira, la tarjeta se inclina y destella.
    /// </summary>
    public sealed class MediaView : Grid, IShelfView
    {
        public const double SlotWidth = 176;
        public const double SlotHeight = 214;

        public GameEntry Game { get; }
        public int Index { get; }

        private readonly MediaKind _kind;
        private readonly Grid _body = new Grid();
        private readonly ScaleTransform _bodyScale = new ScaleTransform(1, 1);
        private readonly RotateTransform _bodyTilt = new RotateTransform();
        private readonly TranslateTransform _bodyLift = new TranslateTransform();
        private readonly TextBlock _title = new TextBlock();
        private readonly DropShadowEffect _glow;
        private Grid _disc;
        private RotateTransform _discSpin;
        private TranslateTransform _discSlide;
        private double _discTravel;
        private Rectangle _shine;
        private TranslateTransform _shineMove;
        private bool _selected;

        public MediaView(GameEntry game, int index, GameSystem system, ImageSource cover = null)
        {
            Game = game;
            Index = index;
            _kind = system.Media;
            Width = SlotWidth;
            Height = SlotHeight;
            Background = Brushes.Transparent; // también recibe clics en los huecos
            Cursor = System.Windows.Input.Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.75);
            RenderTransform = new ScaleTransform(1, 1);

            var accent = Palette.Parse(system.Accent);
            var primary = Palette.Parse(system.Primary);
            var secondary = Palette.Parse(system.Secondary);

            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            _body.VerticalAlignment = VerticalAlignment.Bottom;
            _body.HorizontalAlignment = HorizontalAlignment.Center;
            _body.RenderTransformOrigin = new Point(0.5, 1);
            var group = new TransformGroup();
            group.Children.Add(_bodyScale);
            group.Children.Add(_bodyTilt);
            group.Children.Add(_bodyLift);
            _body.RenderTransform = group;
            _glow = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 0, Opacity = 0, Color = accent };
            _body.Effect = _glow;
            Children.Add(_body);

            switch (_kind)
            {
                case MediaKind.Umd:
                    BuildUmd(game, cover, primary, secondary);
                    break;
                case MediaKind.GameCard:
                    BuildGameCard(game, cover, primary, secondary);
                    break;
                case MediaKind.Cartridge:
                    if (string.Equals(system.Id, "GBC", StringComparison.OrdinalIgnoreCase)) BuildGameBoyCartridge(game, cover, primary, secondary);
                    else if (string.Equals(system.Id, "GBA", StringComparison.OrdinalIgnoreCase)) BuildAdvanceCartridge(game, cover, primary, secondary);
                    else BuildCartridge(game, cover, primary, secondary);
                    break;
                default:
                    BuildCase(game, cover, primary, secondary, CaseStyle.For(system, PlatformOf(game), primary));
                    break;
            }

            // Punto de color con la compatibilidad conocida del emulador (verde, ámbar o rojo).
            var compat = Compatibility.Of(game);
            if (compat.Color.HasValue)
                _title.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = new SolidColorBrush(compat.Color.Value) });
            _title.Inlines.Add(new System.Windows.Documents.Run(game.Title));
            _title.ToolTip = compat.Label;
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

        /// <summary>"GameCube" o "Wii" cuando se sabe; decide si la caja es negra o blanca.</summary>
        private static string PlatformOf(GameEntry game) => game.Platform;

        /// <summary>Cómo es la caja real de cada consola: plástico, franja impresa y disco.</summary>
        private sealed class CaseStyle
        {
            public double Width, Height;
            public Color Plastic, Band, Ink;
            public string Brand;
            public double BrandSize = 9;
            public bool BrandItalic;
            public bool BandSheen;
            public bool FullDisc = true;
            public Color DiscRing = Color.FromArgb(0xE6, 0xD8, 0xDD, 0xE6);

            public static CaseStyle For(GameSystem system, string platform, Color primary)
            {
                switch ((system.Id ?? "").ToUpperInvariant())
                {
                    case "PS2":
                        // Caja DVD negra y alta con la franja negra de "PlayStation 2" arriba.
                        return new CaseStyle
                        {
                            Width = 106, Height = 152, Plastic = Rgb(0x16, 0x17, 0x1B), Band = Rgb(0x05, 0x05, 0x07),
                            Ink = Colors.White, Brand = "PlayStation 2", BrandSize = 8.5, BrandItalic = true,
                            DiscRing = Color.FromArgb(0xE6, 0x9E, 0xA8, 0xC8),
                        };
                    case "PS3":
                        // Caja Blu-ray negra; franja negra con brillo plateado.
                        return new CaseStyle
                        {
                            Width = 118, Height = 146, Plastic = Rgb(0x14, 0x15, 0x19), Band = Rgb(0x0A, 0x0B, 0x0E),
                            Ink = Rgb(0xE6, 0xE8, 0xEE), Brand = "PS3", BrandSize = 10, BandSheen = true,
                        };
                    case "PS4":
                        // Caja Blu-ray azul oscuro con la franja azul de PS4.
                        return new CaseStyle
                        {
                            Width = 118, Height = 146, Plastic = Rgb(0x0E, 0x2A, 0x66), Band = Rgb(0x00, 0x43, 0xB0),
                            Ink = Colors.White, Brand = "PS4", BrandSize = 10,
                        };
                    case "PS5":
                        // Caja con la franja blanca y el logo en negro.
                        return new CaseStyle
                        {
                            Width = 118, Height = 146, Plastic = Rgb(0x1A, 0x3E, 0x8C), Band = Rgb(0xFA, 0xFB, 0xFD),
                            Ink = Rgb(0x10, 0x11, 0x14), Brand = "PS5", BrandSize = 10,
                        };
                    case "GAMECUBE-WII":
                        if (string.Equals(platform, "Wii", StringComparison.OrdinalIgnoreCase))
                        {
                            // Caja blanca de Wii con la franja blanca y el logo gris.
                            return new CaseStyle
                            {
                                Width = 108, Height = 150, Plastic = Rgb(0xEE, 0xF0, 0xF4), Band = Rgb(0xFF, 0xFF, 0xFF),
                                Ink = Rgb(0x8C, 0x91, 0x9A), Brand = "Wii", BrandSize = 10, BrandItalic = true,
                            };
                        }
                        // GameCube (o desconocido): caja negra, minidisco.
                        return new CaseStyle
                        {
                            Width = 108, Height = 150, Plastic = Rgb(0x15, 0x15, 0x19), Band = Rgb(0x05, 0x05, 0x07),
                            Ink = Rgb(0xA6, 0x96, 0xFF), Brand = string.Equals(platform, "GameCube", StringComparison.OrdinalIgnoreCase) ? "GAMECUBE" : "GAMECUBE · Wii",
                            BrandSize = 7.5, FullDisc = false,
                        };
                    case "XBOX":
                        // Caja negra de Xbox con la franja verde y el nombre en blanco.
                        return new CaseStyle
                        {
                            Width = 106, Height = 152, Plastic = Rgb(0x12, 0x13, 0x15), Band = Rgb(0x3E, 0x8E, 0x1F),
                            Ink = Colors.White, Brand = "XBOX", BrandSize = 9.5, BandSheen = true,
                        };
                    case "XBOX360":
                        // Caja de plástico verde con la franja blanca de Xbox 360.
                        return new CaseStyle
                        {
                            Width = 106, Height = 152, Plastic = Rgb(0x2F, 0x7D, 0x24), Band = Rgb(0xF7, 0xF8, 0xF9),
                            Ink = Rgb(0x5C, 0x9E, 0x2E), Brand = "XBOX 360", BrandSize = 9,
                        };
                    default:
                        return new CaseStyle
                        {
                            Width = 118, Height = 146, Plastic = Palette.Mix(primary, Colors.Black, 0.7), Band = Palette.Mix(primary, Colors.Black, 0.35),
                            Ink = Colors.White, Brand = system.Name, BrandSize = 8.5,
                        };
                }
            }

            private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
        }

        // ---------------------------------------------------------------- Construcción

        private void BuildCase(GameEntry game, ImageSource cover, Color primary, Color secondary, CaseStyle style)
        {
            double width = style.Width, height = style.Height;
            const double spine = 7, edge = 2;
            // Una portada real (vertical) ya trae impresa la franja de su consola; la dibujamos solo
            // sobre carátulas generadas o iconos apaisados.
            bool boxArt = cover != null && cover.Height > 0 && cover.Width / cover.Height < 0.95;
            double band = boxArt ? 0 : 15;

            // Disco detrás de la caja: asoma y gira al seleccionar.
            double diameter = style.FullDisc ? width * 0.9 : width * 0.66;
            _disc = BuildDisc(diameter, cover, primary, secondary, style.DiscRing);
            _disc.VerticalAlignment = VerticalAlignment.Bottom;
            _disc.HorizontalAlignment = HorizontalAlignment.Center;
            _disc.Margin = new Thickness(0, 0, 0, height - diameter - 6);
            _discTravel = diameter * 0.58;
            _body.Children.Add(_disc);

            // La caja de plástico: un poco más grande que la portada, con el lomo a la izquierda.
            var box = new Grid { Width = width, Height = height };
            box.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 4, 4);
            bool lightPlastic = IsLight(style.Plastic);
            var plastic = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            plastic.GradientStops.Add(new GradientStop(Palette.Mix(style.Plastic, lightPlastic ? Colors.Gray : Colors.White, 0.12), 0));
            plastic.GradientStops.Add(new GradientStop(style.Plastic, 0.1));
            plastic.GradientStops.Add(new GradientStop(Palette.Mix(style.Plastic, Colors.Black, 0.25), 1));
            box.Children.Add(new Rectangle { Fill = plastic });

            // Lomo: bisagra con dos nervios en relieve.
            for (int i = 0; i < 2; i++)
            {
                box.Children.Add(new Rectangle
                {
                    Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2 + i * 2.5, 3, 0, 3),
                    Fill = new SolidColorBrush(lightPlastic ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                });
            }

            // Portada bajo la funda transparente: franja impresa de la consola + arte del juego.
            var sleeve = new Grid { Margin = new Thickness(spine, edge, edge, edge), ClipToBounds = true };
            sleeve.RowDefinitions.Add(new RowDefinition { Height = new GridLength(band) });
            sleeve.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (!boxArt) sleeve.Children.Add(BrandBand(style));
            var art = CoverElement(game, cover, primary, secondary, (width - spine - edge) / (height - 2 * edge - band));
            SetRow(art, 1);
            sleeve.Children.Add(art);
            box.Children.Add(sleeve);

            // Reflejo de la funda de plástico y canto iluminado.
            box.Children.Add(Gloss(height));
            box.Children.Add(new Rectangle
            {
                Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(spine - 1, edge, 0, edge),
                Fill = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00)),
            });
            AddShine(box, width, height);

            var frame = new Border
            {
                Width = width, Height = height, CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(lightPlastic ? Color.FromArgb(0x60, 0x00, 0x00, 0x00) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                Child = box,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.55 },
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _body.Children.Add(frame);
        }

        // Franja superior impresa en la portada con el nombre de la consola.
        private static UIElement BrandBand(CaseStyle style)
        {
            var bandBrush = style.BandSheen
                ? (Brush)new LinearGradientBrush(style.Band, Palette.Mix(style.Band, Color.FromRgb(0x9A, 0xA0, 0xAC), 0.45), 0)
                : new SolidColorBrush(style.Band);
            return new Border
            {
                Background = bandBrush,
                Child = new TextBlock
                {
                    Text = style.Brand, FontSize = style.BrandSize, FontWeight = FontWeights.Black,
                    FontStyle = style.BrandItalic ? FontStyles.Italic : FontStyles.Normal,
                    Foreground = new SolidColorBrush(style.Ink), VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
        }

        // UMD de PSP: carcasa con la parte superior redondeada; la etiqueta con el arte va arriba
        // y por la ventana inferior se ve girar el disco.
        private void BuildUmd(GameEntry game, ImageSource cover, Color primary, Color secondary)
        {
            const double w = 112, h = 128;
            var outline = Geometry.Parse($"M0,44 A56,44 0 0 1 {w},44 V{h - 8} A8,8 0 0 1 {w - 8},{h} H8 A8,8 0 0 1 0,{h - 8} Z");
            var shell = new Grid { Width = w, Height = h, Clip = outline };
            shell.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromRgb(0x3A, 0x3E, 0x47), Color.FromRgb(0x1A, 0x1C, 0x22), 90),
            });

            // Ventana inferior con el disco girando detrás del plástico ahumado.
            _disc = BuildDisc(96, cover, primary, secondary, Color.FromArgb(0xE6, 0xD8, 0xDD, 0xE6));
            _disc.VerticalAlignment = VerticalAlignment.Bottom;
            _disc.Margin = new Thickness(0, 0, 0, -44);
            _discTravel = 0;
            var window = new Grid { Height = 40, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 18, 8), ClipToBounds = true };
            window.Clip = new RectangleGeometry(new Rect(0, 0, w - 36, 40), 6, 6);
            window.Children.Add(_disc);
            window.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x12, 0x18)) });
            shell.Children.Add(window);

            // Etiqueta impresa con la portada, siguiendo la curva superior.
            var labelShape = Geometry.Parse($"M0,38 A48,38 0 0 1 96,38 V72 H0 Z");
            var label = new Grid
            {
                Width = 96, Height = 72, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0),
                Clip = labelShape,
            };
            label.Children.Add(CoverElement(game, cover, primary, secondary, 96.0 / 72));
            shell.Children.Add(label);
            shell.Children.Add(new TextBlock
            {
                Text = "UMD", FontSize = 7, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 82, 10, 0),
            });
            shell.Children.Add(Gloss(h));
            AddShine(shell, w, h);

            var frame = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Bottom };
            frame.Children.Add(shell);
            frame.Children.Add(new Path { Data = outline, Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.2 });
            frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
            _body.Children.Add(frame);
        }

        // Tarjeta de Switch: cuerpo negro pequeño con la esquina superior derecha recortada,
        // pestaña con estrías arriba y la etiqueta con el arte ocupando casi todo el frente.
        private void BuildGameCard(GameEntry game, ImageSource cover, Color primary, Color secondary)
        {
            const double w = 100, h = 146, cut = 14, lip = 16;
            var outline = Geometry.Parse($"M6,0 H{w - cut} L{w},{cut} V{h - 6} A6,6 0 0 1 {w - 6},{h} H6 A6,6 0 0 1 0,{h - 6} V6 A6,6 0 0 1 6,0 Z");
            var card = new Grid { Width = w, Height = h, Clip = outline };
            card.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromRgb(0x33, 0x35, 0x3B), Color.FromRgb(0x17, 0x18, 0x1C), 90),
            });
            // Pestaña superior con estrías de agarre.
            card.Children.Add(new Rectangle
            {
                Height = lip, VerticalAlignment = VerticalAlignment.Top,
                Fill = new LinearGradientBrush(Color.FromRgb(0x42, 0x45, 0x4D), Color.FromRgb(0x26, 0x28, 0x2E), 90),
            });
            for (int i = 0; i < 3; i++)
            {
                card.Children.Add(new Rectangle
                {
                    Height = 1.2, Width = w - cut - 22, HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 4 + i * 3.5, 0, 0),
                    Fill = new SolidColorBrush(Color.FromArgb(0x70, 0x00, 0x00, 0x00)),
                });
            }

            // Etiqueta pegada: el arte del juego con un fino borde de papel.
            var label = new Border
            {
                Margin = new Thickness(6, lip + 3, 6, 7), CornerRadius = new CornerRadius(3), ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)), Padding = new Thickness(1.2),
            };
            var labelArt = new Border { CornerRadius = new CornerRadius(2), ClipToBounds = true };
            labelArt.Child = CoverElement(game, cover, primary, secondary, (w - 14.4) / (h - lip - 12.4));
            label.Child = labelArt;
            label.Clip = new RectangleGeometry(new Rect(0, 0, w - 12, h - lip - 10), 3, 3);
            card.Children.Add(label);
            card.Children.Add(Gloss(h));
            AddShine(card, w, h);

            var frame = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Bottom };
            frame.Children.Add(card);
            frame.Children.Add(new Path { Data = outline, Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
            frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.55 };
            _body.Children.Add(frame);
        }

        // Cartucho retro gris con estrías y etiqueta frontal con la portada.
        private void BuildCartridge(GameEntry game, ImageSource cover, Color primary, Color secondary)
        {
            const double w = 136, h = 124;
            var outline = Geometry.Parse($"M10,0 H{w - 10} A10,10 0 0 1 {w},10 V{h - 18} H{w - 12} V{h} H12 V{h - 18} H0 V10 A10,10 0 0 1 10,0 Z");
            var cart = new Grid { Width = w, Height = h, Clip = outline };
            cart.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromRgb(0x9A, 0x9F, 0xA9), Color.FromRgb(0x6B, 0x70, 0x7A), 90),
            });
            for (int i = 0; i < 5; i++)
            {
                cart.Children.Add(new Rectangle
                {
                    Width = 3, Height = 14, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(w / 2 - 22 + i * 10, 4, 0, 0),
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)),
                });
            }
            var label = new Border
            {
                Margin = new Thickness(11, 22, 11, 22), CornerRadius = new CornerRadius(4), ClipToBounds = true,
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00)),
                Child = CoverElement(game, cover, primary, secondary, (w - 22) / (h - 44)),
            };
            cart.Children.Add(label);
            cart.Children.Add(Gloss(h));
            AddShine(cart, w, h);
            var frame = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Bottom };
            frame.Children.Add(cart);
            frame.Children.Add(new Path { Data = outline, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)), StrokeThickness = 1.5 });
            frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
            _body.Children.Add(frame);
        }

        // Cartucho de Game Boy Color: vertical, oscuro, con la esquina superior derecha recortada,
        // estrías arriba, la etiqueta con el arte y la flecha en relieve abajo.
        private void BuildGameBoyCartridge(GameEntry game, ImageSource cover, Color primary, Color secondary)
        {
            const double w = 104, h = 120, cut = 12;
            var outline = Geometry.Parse($"M5,0 H{w - cut} L{w},{cut} V{h - 6} A6,6 0 0 1 {w - 6},{h} H6 A6,6 0 0 1 0,{h - 6} V5 A5,5 0 0 1 5,0 Z");
            var cart = new Grid { Width = w, Height = h, Clip = outline };
            cart.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromRgb(0x4A, 0x4C, 0x55), Color.FromRgb(0x24, 0x25, 0x2B), 90),
            });
            for (int i = 0; i < 4; i++)
            {
                cart.Children.Add(new Rectangle
                {
                    Height = 1.4, Width = w - cut - 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(10, 5 + i * 3.4, 0, 0), Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
                });
            }
            var label = new Border
            {
                Margin = new Thickness(9, 22, 9, 26), CornerRadius = new CornerRadius(3), ClipToBounds = true,
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x00, 0x00, 0x00)),
                Child = CoverElement(game, cover, primary, secondary, (w - 18) / (h - 48)),
            };
            cart.Children.Add(label);
            // Flecha en relieve que indica el sentido de inserción.
            cart.Children.Add(new Path
            {
                Data = Geometry.Parse("M0,0 L8,0 L4,5 Z"), Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10),
            });
            cart.Children.Add(Gloss(h));
            AddShine(cart, w, h);
            var frame = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Bottom };
            frame.Children.Add(cart);
            frame.Children.Add(new Path { Data = outline, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
            frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.55 };
            _body.Children.Add(frame);
        }

        // Cartucho de Game Boy Advance: pequeño y apaisado, gris, con la pestaña superior curva
        // y la etiqueta con el arte ocupando casi todo el frente.
        private void BuildAdvanceCartridge(GameEntry game, ImageSource cover, Color primary, Color secondary)
        {
            const double w = 128, h = 84, bump = 7;
            var outline = Geometry.Parse($"M6,{bump} Q{w / 2},-{bump - 2} {w - 6},{bump} A6,6 0 0 1 {w},{bump + 6} V{h - 6} A6,6 0 0 1 {w - 6},{h} H6 A6,6 0 0 1 0,{h - 6} V{bump + 6} A6,6 0 0 1 6,{bump} Z");
            var cart = new Grid { Width = w, Height = h, Clip = outline };
            cart.Children.Add(new Rectangle
            {
                Fill = new LinearGradientBrush(Color.FromRgb(0x8E, 0x92, 0x9C), Color.FromRgb(0x5E, 0x62, 0x6B), 90),
            });
            var label = new Border
            {
                Margin = new Thickness(8, bump + 6, 8, 9), CornerRadius = new CornerRadius(3), ClipToBounds = true,
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00)),
                Child = CoverElement(game, cover, primary, secondary, (w - 16) / (h - bump - 15)),
            };
            cart.Children.Add(label);
            cart.Children.Add(Gloss(h));
            AddShine(cart, w, h);
            var frame = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Bottom };
            frame.Children.Add(cart);
            frame.Children.Add(new Path { Data = outline, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00)), StrokeThickness = 1.2 });
            frame.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.5 };
            _body.Children.Add(frame);
        }

        // Disco con la carátula impresa, brillo tornasol y anillo central transparente.
        private Grid BuildDisc(double d, ImageSource cover, Color primary, Color secondary, Color ring)
        {
            var disc = new Grid { Width = d, Height = d, RenderTransformOrigin = new Point(0.5, 0.5) };
            _discSpin = new RotateTransform();
            _discSlide = new TranslateTransform();
            var t = new TransformGroup();
            t.Children.Add(_discSpin);
            t.Children.Add(_discSlide);
            disc.RenderTransform = t;

            Brush face = cover != null
                ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill }
                : (Brush)new LinearGradientBrush(primary, secondary, 45);
            disc.Children.Add(new Ellipse { Fill = face });
            var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0x9C, 0xF4, 0xFF), 0.35));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 0.5));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0xFF, 0xA8, 0xE0), 0.65));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
            disc.Children.Add(new Ellipse { Fill = sheen });
            disc.Children.Add(new Ellipse
            {
                Width = d * 0.36, Height = d * 0.36,
                Fill = new SolidColorBrush(ring),
                Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1,
            });
            disc.Children.Add(new Ellipse { Width = d * 0.13, Height = d * 0.13, Fill = new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x10)) });
            disc.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.2 });
            // Una marca para que se note el giro.
            disc.Children.Add(new Rectangle
            {
                Width = d * 0.08, Height = 3, Fill = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, d * 0.1, 0),
            });
            return disc;
        }

        /// <summary>
        /// La portada ajustada al hueco. Si su forma es muy distinta (p. ej. el icono apaisado de PS3
        /// en una caja vertical), se ve entera sobre una versión difuminada de sí misma, sin recortes feos.
        /// </summary>
        private static UIElement CoverElement(GameEntry game, ImageSource cover, Color primary, Color secondary, double slotAspect)
        {
            if (cover != null)
            {
                double aspect = cover.Height > 0 ? cover.Width / cover.Height : slotAspect;
                if (Math.Abs(Math.Log(aspect / slotAspect)) < 0.28)
                    return new Image { Source = cover, Stretch = Stretch.UniformToFill };
                var fit = new Grid { ClipToBounds = true };
                fit.Children.Add(new Image
                {
                    Source = cover, Stretch = Stretch.UniformToFill, RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1.25, 1.25), Effect = new BlurEffect { Radius = 14 },
                });
                fit.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)) });
                fit.Children.Add(new Image
                {
                    Source = cover, Stretch = Stretch.Uniform,
                    Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.6 },
                });
                return fit;
            }
            // Sin portada: una carátula generada con los colores de la consola y las iniciales.
            // Cada juego con su propio tono (derivado del título) dentro de la paleta de la consola.
            var tint = Palette.FromHash(game.Title);
            var grid = new Grid { Background = new LinearGradientBrush(Palette.Mix(primary, tint, 0.45), Palette.Mix(secondary, tint, 0.25), 60) };
            grid.Children.Add(new TextBlock
            {
                Text = Initials(game.Title), FontSize = 30, FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18),
            });
            grid.Children.Add(new TextBlock
            {
                Text = game.Title, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxHeight = 28,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 6),
            });
            return grid;
        }

        private static string Initials(string title)
        {
            var words = (title ?? "?").Split(new[] { ' ', '-', '_', ':', '.' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Where(w => char.IsLetterOrDigit(w[0])).Take(2).ToArray();
            if (words.Length == 0) return "?";
            return string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
        }

        private static Rectangle Gloss(double height)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.8));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1));
            return new Rectangle { Fill = brush, IsHitTestVisible = false };
        }

        // Destello diagonal que cruza la pieza al seleccionarla.
        private void AddShine(Panel host, double width, double height)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
            _shineMove = new TranslateTransform(-width * 1.5, 0);
            var group = new TransformGroup();
            group.Children.Add(new SkewTransform(-20, 0));
            group.Children.Add(_shineMove);
            _shine = new Rectangle
            {
                Width = width * 0.5, Height = height * 1.4, Fill = brush, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = group, Opacity = 0,
            };
            host.Children.Add(_shine);
        }

        private static bool IsLight(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 160;

        /// <summary>Decodifica y congela una portada (seguro desde cualquier hilo); null si falta o está dañada.</summary>
        public static ImageSource LoadCover(string path) => LoadImage(path, 320);

        /// <summary>Decodifica y congela una imagen al ancho pedido; null si falta o está dañada.</summary>
        public static ImageSource LoadImage(string path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            try
            {
                var image = new BitmapImage();
                using (var stream = System.IO.File.OpenRead(path))
                {
                    // Nunca por encima de su tamaño real: ampliar solo gasta memoria y no gana nitidez.
                    var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    int native = frame.PixelWidth;
                    stream.Position = 0;
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad; // no deja el archivo bloqueado
                    image.DecodePixelWidth = native > 0 ? Math.Min(decodeWidth, native) : decodeWidth;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                return image;
            }
            catch
            {
                return null; // imagen dañada: se usa la carátula generada
            }
        }

        // ---------------------------------------------------------------- Selección

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                Animate(value);
            }
        }

        private void Animate(bool on)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            var fast = TimeSpan.FromMilliseconds(380);

            _bodyScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1.1 : 1, fast) { EasingFunction = back });
            _bodyScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.1 : 1, fast) { EasingFunction = back });
            _bodyLift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? -10 : 0, fast) { EasingFunction = ease });
            _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(on ? 38 : 0, fast));
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(on ? 0.75 : 0, fast));
            _title.Foreground = new SolidColorBrush(on ? Colors.White : Color.FromRgb(0xB7, 0xBD, 0xD0));
            _title.FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold;

            if (_kind == MediaKind.GameCard || _kind == MediaKind.Cartridge || _kind == MediaKind.Umd)
            {
                // Se inclina como al sacarla de la caja.
                _bodyTilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(on ? -6 : 0, fast) { EasingFunction = back });
            }

            if (_disc != null)
            {
                if (_discTravel > 0)
                    _discSlide.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(on ? -_discTravel : 0, TimeSpan.FromMilliseconds(on ? 520 : 300)) { EasingFunction = ease });
                if (on)
                {
                    _discSpin.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.2)) { RepeatBehavior = RepeatBehavior.Forever });
                }
                else
                {
                    var current = _discSpin.Angle % 360;
                    _discSpin.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(current, current + 90, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
                }
            }

            if (_shine != null && on)
            {
                _shine.Opacity = 1;
                var width = ((FrameworkElement)_shine.Parent).Width;
                _shineMove.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-width, width * 1.5, TimeSpan.FromMilliseconds(900))
                    { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = ease });
            }
        }

        /// <summary>Rebote lateral: no hay más juegos en esa dirección.</summary>
        public void Nudge()
        {
            _bodyLift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames
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

        /// <summary>Animación al lanzar: el disco entra en la consola o la tarjeta se inserta.</summary>
        public void PlayLaunch()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            if (_disc != null && _discTravel > 0)
            {
                _discSpin.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.5)) { RepeatBehavior = RepeatBehavior.Forever });
                _discSlide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-_discTravel * 1.9, TimeSpan.FromMilliseconds(450)) { EasingFunction = ease, AutoReverse = true });
            }
            else
            {
                _bodyLift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-10, 18, TimeSpan.FromMilliseconds(220)) { AutoReverse = true, EasingFunction = ease });
            }
            _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                new DoubleAnimation(38, 70, TimeSpan.FromMilliseconds(300)) { AutoReverse = true });
        }
    }

    /// <summary>Compatibilidad de un juego traducida a etiqueta y color para la interfaz.</summary>
    internal sealed class Compatibility
    {
        public string Label { get; private set; }
        public Color? Color { get; private set; }
        public bool Risky { get; private set; }   // probablemente no se pueda jugar

        public static Compatibility Of(GameEntry game) => Of(game?.Compatibility, game?.SystemId);

        public static Compatibility Of(GameCompatibility info, string systemId)
        {
            var status = info?.Status ?? "Desconocido";
            bool ps4 = string.Equals(systemId, "PS4", StringComparison.OrdinalIgnoreCase);
            Color green = System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80);
            Color amber = System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24);
            Color red = System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71);
            switch (status)
            {
                case "Perfect": return new Compatibility { Label = "Compatibilidad: perfecto", Color = green };
                case "Playable": return new Compatibility { Label = "Compatibilidad: jugable", Color = green };
                case "Ingame": return new Compatibility { Label = "Compatibilidad: entra al juego, con fallos", Color = amber };
                case "Menu": return new Compatibility { Label = "Compatibilidad: solo llega a los menús", Color = amber, Risky = true };
                case "Intro": return new Compatibility { Label = "Compatibilidad: no pasa de la intro", Color = red, Risky = true };
                case "Loadable": return new Compatibility { Label = ps4 ? "Compatibilidad: arranca, sin llegar al menú" : "Compatibilidad: solo carga, no se ve", Color = red, Risky = true };
                case "Nothing": return new Compatibility { Label = "Compatibilidad: no arranca", Color = red, Risky = true };
                default: return new Compatibility { Label = "Compatibilidad desconocida", Color = null };
            }
        }

        /// <summary>Texto para la ficha: estado, cuándo y con qué versión se probó, y por qué falla.</summary>
        public static string Note(GameCompatibility info, string systemId)
        {
            var compat = Of(info, systemId);
            if (!compat.Color.HasValue || info == null) return "";
            var when = new System.Collections.Generic.List<string>();
            // La explicación de PS4 ya cuenta fecha, versión y sistema: no se repite entre paréntesis.
            bool explained = !string.IsNullOrEmpty(info.Explanation) && !string.IsNullOrEmpty(info.Date) && info.Explanation.Contains(info.Date);
            if (!explained)
            {
                if (!string.IsNullOrEmpty(info.Date)) when.Add(info.Date);
                if (!string.IsNullOrEmpty(info.EmulatorVersion)) when.Add(info.EmulatorVersion);
                if (!string.IsNullOrEmpty(info.OperatingSystem) && !info.OperatingSystem.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
                    when.Add("informe de " + info.OperatingSystem);
            }
            string note = "● " + compat.Label.Replace("Compatibilidad: ", "") + (when.Count == 0 ? "" : " (" + string.Join(", ", when) + ")");
            if (!string.IsNullOrEmpty(info.Explanation) && (compat.Risky || string.Equals(systemId, "PS4", StringComparison.OrdinalIgnoreCase)))
                note += "\n" + info.Explanation;
            if (!string.IsNullOrEmpty(info.UpdateHint)) note += "\n" + info.UpdateHint;
            return note + "\n";
        }
    }

    internal static class Palette
    {
        public static Color Parse(string hex, string fallback = "#5B6CFF")
        {
            try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrEmpty(hex) ? fallback : hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        /// <summary>Un color vivo y estable para un texto (mismo título, mismo color).</summary>
        public static Color FromHash(string text)
        {
            unchecked
            {
                int hash = 17;
                foreach (var ch in text ?? "") hash = hash * 31 + ch;
                return FromHsv((hash & 0x7fffffff) % 360, 0.65, 0.9);
            }
        }

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}

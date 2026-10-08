using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Themes;
using Resonate.Themes;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// A miniature of a look, drawn in its own colours and shapes: the
/// backdrop, the sidebar, a page and the player bar with its progress bar,
/// play button and cover. Used for the theme cards in Settings.
/// </summary>
internal sealed partial class LookPreview : Grid
{
    public const double PreviewWidth = 196;
    public const double PreviewHeight = 112;

    // The miniature is about a third of the real size.
    private const double MiniatureScale = 0.34;

    private readonly ThemeDefinition _look;
    private readonly ThemePalette _palette;

    public LookPreview(ThemeDefinition look)
    {
        _look = look;
        _palette = ThemePalette.From(look);
        Width = PreviewWidth;
        Height = PreviewHeight;
        CornerRadius = new CornerRadius(6);
        Background = BackdropBrush();
        IsHitTestVisible = false;

        var gap = _palette.PanelGap <= 0 ? 0 : Math.Round(Math.Clamp(2 + (_palette.PanelGap * MiniatureScale), 3, 8));
        var floating = look.PlayerLayout == PlayerLayout.Floating;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(9) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var shell = new Grid
        {
            Margin = new Thickness(gap, 0, gap, gap),
            ColumnSpacing = gap,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(46) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        SetRow(shell, 1);
        shell.Children.Add(Panel(_palette.Sidebar, Sidebar(), PanelCorner));
        var page = Panel(_palette.Surface, Page(), PanelCorner);
        SetColumn(page, 1);
        shell.Children.Add(page);
        Children.Add(shell);

        var playerGap = floating ? Math.Max(gap, 4) : 0;
        var player = Panel(
            _palette.Player,
            Player(),
            floating ? PanelCorner : new CornerRadius(0),
            floating ? null : new Thickness(0, _palette.BorderWidth > 0 ? 1 : 0, 0, 0));
        player.Margin = new Thickness(playerGap, 0, playerGap, playerGap);
        SetRow(player, 2);
        Children.Add(player);
    }

    private CornerRadius PanelCorner => new(Math.Min(_palette.CornerLarge * MiniatureScale * 1.4, 12));

    private Brush BackdropBrush()
    {
        var background = _palette.Background;
        switch (_look.Backdrop)
        {
            case WindowBackdrop.Gradient:
                return Gradient(_look.GradientAngle, background, _look.Background2.Opaque);

            case WindowBackdrop.Artwork:
                // Stands in for a song's cover: blurred colour behind the theme's tint.
                var tint = _look.BackdropTint;
                return Gradient(
                    120,
                    _palette.Accent2.Mix(background, tint),
                    _palette.Accent.Mix(background, tint * 0.8),
                    ThemeColor.FromRgb(0xF2A65A).Mix(background, tint * 1.1));

            case WindowBackdrop.Mica or WindowBackdrop.Acrylic:
                return Gradient(90, background, background.Mix(_palette.TextPrimary, 0.07));

            default:
                return background.ToBrush();
        }
    }

    private static LinearGradientBrush Gradient(double angle, params ThemeColor[] colors)
    {
        var (start, end) = ThemeColorExtensions.GradientPoints(angle);
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        for (var i = 0; i < colors.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop { Color = colors[i].ToColor(), Offset = (double)i / (colors.Length - 1) });
        }

        return brush;
    }

    /// <summary>A panel with the look's fill, outline and shadow.</summary>
    private FrameworkElement Panel(ThemeColor fill, UIElement content, CornerRadius corner, Thickness? border = null)
    {
        var host = new Grid();
        var shadow = _palette.PanelShadow;
        if (shadow.IsVisible)
        {
            // A hint of the shadow's kind: a hard offset copy, a glow, or soft depth.
            var hard = _look.Shadow == ShadowStyle.Hard;
            var glow = _look.Shadow == ShadowStyle.Glow;
            host.Children.Add(new Border
            {
                CornerRadius = corner,
                Margin = glow ? new Thickness(-1.5) : new Thickness(0),
                Background = (hard ? shadow.Color : shadow.Color.WithAlpha(glow ? 0.45 : shadow.Color.Opacity * 0.9)).ToBrush(),
                RenderTransform = new TranslateTransform
                {
                    X = hard ? 2 : 0,
                    Y = hard ? 2 : glow ? 0 : 1.5,
                },
            });
        }

        host.Children.Add(new Border
        {
            CornerRadius = corner,
            Background = fill.ToBrush(),
            BorderBrush = _palette.Border.ToBrush(),
            BorderThickness = border ?? new Thickness(_palette.BorderWidth > 0 ? 1 : 0),
            Child = content,
        });
        return host;
    }

    private StackPanel Sidebar()
    {
        var rows = new StackPanel { Padding = new Thickness(4, 6, 4, 4), Spacing = 3 };
        for (var i = 0; i < 4; i++)
        {
            var row = new Grid
            {
                Height = 9,
                Padding = new Thickness(3, 0, 3, 0),
                ColumnSpacing = 3,
                CornerRadius = new CornerRadius(Math.Min(_palette.CornerSmall * MiniatureScale, 3)),
                Background = (i == 0 ? _palette.Pressed : ThemeColor.Transparent).ToBrush(),
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            };
            row.Children.Add(new Rectangle
            {
                Width = 5,
                Height = 5,
                RadiusX = 1,
                RadiusY = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (i == 0 ? _palette.Accent : _palette.TextTertiary).ToBrush(),
            });
            var line = Bar(i == 0 ? _palette.TextPrimary : _palette.TextTertiary, 3, 30 - (i * 4));
            SetColumn(line, 1);
            row.Children.Add(line);
            rows.Children.Add(row);
        }

        return rows;
    }

    private StackPanel Page()
    {
        var page = new StackPanel { Padding = new Thickness(7, 4, 7, 6), Spacing = 4 };
        page.Children.Add(new TextBlock
        {
            Text = "Aa",
            FontFamily = new FontFamily(BundledFonts.Resolve(_look.DisplayFont)),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _palette.TextPrimary.ToBrush(),
        });
        page.Children.Add(Bar(_palette.TextSecondary, 3, 52));

        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 3, 0, 0) };
        var corner = new CornerRadius(Math.Min(_palette.CornerSmall * MiniatureScale * 1.5, 4));
        foreach (var color in new[] { _palette.Accent, _palette.Accent2, _palette.Accent.Mix(_palette.Accent2, 0.5) })
        {
            tiles.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = corner, Background = color.ToBrush() });
        }

        page.Children.Add(tiles);
        return page;
    }

    private Grid Player()
    {
        var player = new Grid
        {
            Height = 24,
            Padding = new Thickness(6, 0, 8, 0),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        player.Children.Add(Cover());

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        middle.Children.Add(PlayButton());
        middle.Children.Add(Progress(64));
        SetColumn(middle, 1);
        player.Children.Add(middle);

        var volume = Bar(_palette.Track, 2, 18);
        volume.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(volume, 2);
        player.Children.Add(volume);
        return player;
    }

    private Border Cover()
    {
        var size = 14.0;
        return new Border
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(_look.Cover switch
            {
                CoverStyle.Vinyl => size / 2,
                CoverStyle.Square => 0,
                _ => Math.Min(_palette.CornerMedium * MiniatureScale, 4),
            }),
            Background = Gradient(45, _palette.Accent2, _palette.Accent),
            Child = _look.Cover == CoverStyle.Vinyl
                ? new Ellipse { Width = 4, Height = 4, Fill = _palette.Player.Opaque.ToBrush() }
                : null,
        };
    }

    private Border PlayButton()
    {
        const double Size = 9;
        return new Border
        {
            Width = Size,
            Height = Size,
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(Math.Min(_palette.CornerButton * MiniatureScale, Size / 2)),
            Background = _palette.PlayButtonBackground.ToBrush(),
            BorderBrush = _palette.PlayButtonBorder.ToBrush(),
            BorderThickness = new Thickness(_palette.PlayButtonBorderWidth > 0 ? 1 : 0),
            Child = _look.PlayButton == PlayButtonStyle.Plain
                ? new Rectangle { Width = 3, Height = 5, Fill = _palette.TextPrimary.ToBrush() }
                : null,
        };
    }

    /// <summary>The progress bar in the look's style, about 40% played.</summary>
    private FrameworkElement Progress(double width)
    {
        var played = width * 0.4;
        var (height, round) = _look.Progress switch
        {
            ProgressStyle.Bold => (3.0, true),
            ProgressStyle.Gradient => (3.0, true),
            ProgressStyle.Minimal => (1.0, false),
            _ => (2.0, true),
        };

        var bar = new Grid { Width = width, Height = 6, HorizontalAlignment = HorizontalAlignment.Center };
        if (_look.Progress == ProgressStyle.Wave)
        {
            var points = new PointCollection();
            for (double x = 0; x <= played; x += 1)
            {
                points.Add(new Point(x, 3 - (Math.Sin(x / 6 * Math.PI * 2) * 1.6)));
            }

            bar.Children.Add(new Polyline { Points = points, Stroke = _palette.Accent.ToBrush(), StrokeThickness = 1.4 });
            var rest = Bar(_palette.Track, 1.4, width - played - 1);
            rest.HorizontalAlignment = HorizontalAlignment.Right;
            rest.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(rest);
            return bar;
        }

        var track = Bar(_palette.Track, height, width, round);
        track.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(track);

        var fill = Bar(_palette.Accent, height, played, round);
        fill.VerticalAlignment = VerticalAlignment.Center;
        if (_look.Progress == ProgressStyle.Gradient)
        {
            fill.Background = Gradient(0, _palette.Accent, _palette.Accent2);
        }

        bar.Children.Add(fill);
        return bar;
    }

    private static Border Bar(ThemeColor color, double height, double width, bool round = true) => new()
    {
        Width = Math.Max(width, 0),
        Height = height,
        HorizontalAlignment = HorizontalAlignment.Left,
        CornerRadius = new CornerRadius(round ? height / 2 : 0),
        Background = color.ToBrush(),
    };
}

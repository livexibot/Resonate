using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Resonate.App.Controls;

/// <summary>
/// What a <see cref="BarStrip"/> draws: one bar per value, the one at
/// <see cref="Highlight"/> (the hour or day now) in the accent, a label
/// under some bars and a tip for each.
/// </summary>
public sealed partial class BarSeries
{
    public BarSeries(IReadOnlyList<double> values, int highlight, IReadOnlyList<string?> labels, int labelSpan, IReadOnlyList<string> tips)
    {
        Values = values;
        Highlight = highlight;
        Labels = labels;
        LabelSpan = Math.Max(1, labelSpan);
        Tips = tips;
    }

    public IReadOnlyList<double> Values { get; }

    /// <summary>The bar drawn in the accent, or -1 for none.</summary>
    public int Highlight { get; }

    /// <summary>Text under a bar, null for none; one per value.</summary>
    public IReadOnlyList<string?> Labels { get; }

    /// <summary>How many bars a label may run across (a time under every sixth hour needs more room than one bar).</summary>
    public int LabelSpan { get; }

    /// <summary>What the pointer shows over each bar.</summary>
    public IReadOnlyList<string> Tips { get; }

    public bool SameAs(BarSeries? other) =>
        other is not null
        && Highlight == other.Highlight
        && LabelSpan == other.LabelSpan
        && Values.SequenceEqual(other.Values)
        && Labels.SequenceEqual(other.Labels)
        && Tips.SequenceEqual(other.Tips);
}

/// <summary>
/// A small bar chart (listening per hour or per day on Home), built from
/// plain elements in code so it stays sharp at any display scale and costs
/// a few dozen elements. It is rebuilt only when the numbers change. The
/// first time it shows, the bars rise once, one after another; nothing
/// moves after that.
/// </summary>
public sealed partial class BarStrip : Grid
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(object),
        typeof(BarStrip),
        new PropertyMetadata(null, (d, _) => ((BarStrip)d).OnSeriesChanged()));

    private const double BarHeight = 56;
    private const double ShortestBar = 3;

    private static readonly TimeSpan RiseDuration = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan RiseStagger = TimeSpan.FromMilliseconds(18);

    private readonly List<Border> _bars = [];
    private BarSeries? _shown;
    private bool _risen;

    public BarStrip()
    {
        Loaded += (_, _) => Rise();
    }

    public BarSeries? Series
    {
        get => GetValue(SeriesProperty) as BarSeries;
        set => SetValue(SeriesProperty, value);
    }

    private void OnSeriesChanged()
    {
        var series = Series;
        if (series is null)
        {
            _shown = null;
            _bars.Clear();
            Children.Clear();
            return;
        }

        if (series.SameAs(_shown))
        {
            return;
        }

        _shown = series;
        Build(series);
    }

    private void Build(BarSeries series)
    {
        var theme = App.Services.Theme;
        var accent = theme.GetBrush("ResonateAccentBrush");

        // The bars that are not the highlight are a lighter shade of the accent, still 3:1 on the card.
        var quiet = theme.GetBrush("ResonateChartQuietBrush");
        var empty = theme.GetBrush("ResonateTrackBrush");
        var clear = theme.GetBrush("ResonateTransparentBrush");
        var strong = theme.GetBrush("ResonateTextPrimaryBrush");
        var caption = Application.Current.Resources.TryGetValue("ResonateChartLabelTextStyle", out var found) && found is Style style ? style : null;

        _bars.Clear();
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowSpacing = 6;
        ColumnSpacing = series.Values.Count > 12 ? 3 : 6;

        var values = series.Values;
        var most = values.Count > 0 ? values.Max() : 0;
        for (var i = 0; i < values.Count; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            var value = values[i];
            var bar = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = value > 0 && most > 0 ? Math.Max(ShortestBar, Math.Round(value / most * BarHeight)) : ShortestBar,
                CornerRadius = new CornerRadius(3, 3, 1, 1),
                Background = value <= 0 ? empty : i == series.Highlight ? accent : quiet,
            };
            _bars.Add(bar);

            // The whole column shows the tip, so a short bar is easy to point at.
            var column = new Grid { Background = clear };
            column.Children.Add(bar);
            if (i < series.Tips.Count)
            {
                ToolTipService.SetToolTip(column, series.Tips[i]);
            }

            SetColumn(column, i);
            Children.Add(column);

            if (i < series.Labels.Count && series.Labels[i] is { } label)
            {
                // A label with too few bars after it to fit ends at its bar instead of starting there.
                var room = values.Count - i;
                var ending = series.LabelSpan > 1 && room < series.LabelSpan;
                var text = new TextBlock
                {
                    Text = label,
                    HorizontalAlignment = series.LabelSpan == 1 ? HorizontalAlignment.Center : ending ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.None,
                };
                if (caption is not null)
                {
                    text.Style = caption;
                }

                if (i == series.Highlight)
                {
                    text.Foreground = strong;
                    text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }

                SetRow(text, 1);
                SetColumn(text, ending ? Math.Max(0, i + 1 - series.LabelSpan) : i);
                SetColumnSpan(text, ending ? Math.Min(series.LabelSpan, i + 1) : Math.Min(series.LabelSpan, room));
                Children.Add(text);
            }
        }

        if (IsLoaded)
        {
            // New numbers on a chart already shown: no rise.
            _risen = true;
        }
    }

    /// <summary>The bars grow from the bottom, once, easing in and out (on the compositor; nothing per frame here).</summary>
    private void Rise()
    {
        if (_risen || _bars.Count == 0)
        {
            return;
        }

        _risen = true;
        if (!App.Services.Theme.AnimationsEnabled)
        {
            return;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.65f, 0f), new Vector2(0.35f, 1f));
        for (var i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            var visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3(0, (float)bar.Height, 0);
            var rise = compositor.CreateVector3KeyFrameAnimation();
            rise.InsertKeyFrame(0, new Vector3(1, 0, 1));
            rise.InsertKeyFrame(1, Vector3.One, easing);
            rise.Duration = RiseDuration;
            rise.DelayTime = RiseStagger * i;
            rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Scale", rise);
        }
    }
}

using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Resonate.App.Themes;
using Resonate.Themes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Resonate.App.Controls;

/// <summary>
/// The Look section of Settings. Picking a card switches looks with the
/// chosen animation; everything under Customize edits the look in use
/// (a preset is copied first, so presets never change). Sliders and the
/// colour picker apply continuously; choices animate briefly. The cover art
/// switches are the user's own and apply to every look.
/// </summary>
public sealed partial class ThemeStudio : UserControl
{
    /// <summary>Fonts that come with Windows 10 and 11, each shown in itself. Any other installed font can be typed.</summary>
    private static readonly string[] Fonts =
    [
        "Segoe UI Variable Display",
        "Segoe UI Variable Text",
        "Segoe UI",
        "Bahnschrift",
        "Sitka Display, Georgia",
        "Sitka Text, Georgia",
        "Georgia",
        "Cambria",
        "Constantia",
        "Palatino Linotype",
        "Candara",
        "Corbel",
        "Trebuchet MS",
        "Verdana",
        "Franklin Gothic Medium",
        "Cascadia Code",
        "Consolas",
        "Ink Free",
        "Segoe Print",
        "Gabriola",
    ];

    private static readonly Dictionary<string, string> PresetBlurbs = new()
    {
        ["midnight"] = "Calm and dark, soft violet",
        ["daylight"] = "Bright and airy, floating player",
        ["glass"] = "Frosted glass over the song's colours",
        ["pure-black"] = "True black, sharp edges",
        ["synthwave"] = "Neon glow on a dusk gradient",
        ["paper"] = "Ink on paper, hard shadows",
    };

    private readonly ThemeService _theme = App.Services.Theme;
    private readonly Dictionary<ComboBox, Func<ThemeDefinition, string, ThemeDefinition>> _choices;
    private readonly Dictionary<Slider, Func<ThemeDefinition, double, ThemeDefinition>> _sliders;
    private readonly List<Swatch> _swatches = [];
    private readonly ColorPicker _picker;
    private readonly Flyout _pickerFlyout;
    private readonly DispatcherQueueTimer _looksTimer;
    private Swatch? _editing;
    private string? _shownLooks;

    // True while controls are being set from the look (and while the page is built), so that is not taken as an edit.
    private bool _loading = true;

    public ThemeStudio()
    {
        InitializeComponent();

        _choices = new()
        {
            [BackdropChoice] = (look, tag) => look with { Backdrop = Enum.Parse<WindowBackdrop>(tag) },
            [ButtonsChoice] = (look, tag) => look with { Buttons = Enum.Parse<ButtonShape>(tag) },
            [ShadowChoice] = (look, tag) => look with { Shadow = Enum.Parse<ShadowStyle>(tag) },
            [LayoutChoice] = (look, tag) => look with { PlayerLayout = Enum.Parse<PlayerLayout>(tag) },
            [ProgressChoice] = (look, tag) => look with { Progress = Enum.Parse<ProgressStyle>(tag) },
            [PlayButtonChoice] = (look, tag) => look with { PlayButton = Enum.Parse<PlayButtonStyle>(tag) },
            [CoverChoice] = (look, tag) => look with { Cover = Enum.Parse<CoverStyle>(tag) },
        };

        _sliders = new()
        {
            [GradientAngleSlider] = (look, value) => look with { GradientAngle = value },
            [TintSlider] = (look, value) => look with { BackdropTint = value / 100 },
            [PanelOpacitySlider] = (look, value) => look with { PanelOpacity = value / 100 },
            [CornerSlider] = (look, value) => look with { CornerRadius = value },
            [BorderSlider] = (look, value) => look with { BorderWidth = value },
            [GapSlider] = (look, value) => look with { PanelGap = value },
        };

        foreach (var font in Fonts)
        {
            DisplayFontChoice.Items.Add(FontItem(font));
            TextFontChoice.Items.Add(FontItem(font));
        }

        AddSwatch("Background", look => look.Background, (look, color) => look with { Background = color });
        AddSwatch("Gradient end", look => look.Background2, (look, color) => look with { Background2 = color });
        AddSwatch("Sidebar", look => look.Sidebar, (look, color) => look with { Sidebar = color });
        AddSwatch("Page", look => look.Surface, (look, color) => look with { Surface = color });
        AddSwatch("Player", look => look.Player, (look, color) => look with { Player = color });
        AddSwatch("Text", look => look.Text, (look, color) => look with { Text = color });
        AddSwatch("Accent", look => look.Accent, (look, color) => look with { Accent = color });
        AddSwatch("Second accent", look => look.Accent2, (look, color) => look with { Accent2 = color });
        AddSwatch("Outlines", look => look.Border ?? ThemePalette.From(look).Border, (look, color) => look with { Border = color }, allowsAlpha: true);

        _picker = new ColorPicker
        {
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
        };
        _picker.ColorChanged += OnPickerColorChanged;
        _pickerFlyout = new Flyout { Content = _picker, Placement = FlyoutPlacementMode.Bottom };

        foreach (var preset in ThemePresets.All)
        {
            PresetGrid.Items.Add(Card(preset, PresetBlurbs.GetValueOrDefault(preset.Id, string.Empty), menu: null));
        }

        _looksTimer = DispatcherQueue.CreateTimer();
        _looksTimer.Interval = TimeSpan.FromMilliseconds(250);
        _looksTimer.IsRepeating = false;

        CustomizeExpander.IsExpanded = CustomizeOpen;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _loading = false;
    }

    /// <summary>Whether Customize is open; kept while Resonate runs.</summary>
    public static bool CustomizeOpen { get; set; }

    /// <summary>Opens Customize and scrolls it to the top of the page.</summary>
    internal void ShowCustomize()
    {
        CustomizeExpander.IsExpanded = true;
        CustomizeExpander.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Only while shown: a handler left on the timer (which is outside the
        // tree) would keep this panel alive after Settings closes.
        _looksTimer.Tick -= OnLooksTick;
        _looksTimer.Tick += OnLooksTick;
        _theme.Changed += OnThemeChanged;
        ShowYourLooks();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _theme.Changed -= OnThemeChanged;
        _looksTimer.Stop();
        _looksTimer.Tick -= OnLooksTick;
    }

    private void OnLooksTick(DispatcherQueueTimer sender, object args) => ShowYourLooks();

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Shows the look in use in every control (without treating that as an edit).</summary>
    private void Refresh()
    {
        var look = _theme.Current;
        _loading = true;
        try
        {
            Select(TransitionChoice, _theme.Transition.ToString());
            Select(BackdropChoice, look.Backdrop.ToString());
            Select(ButtonsChoice, look.Buttons.ToString());
            Select(ShadowChoice, look.Shadow.ToString());
            Select(LayoutChoice, look.PlayerLayout.ToString());
            Select(ProgressChoice, look.Progress.ToString());
            Select(PlayButtonChoice, look.PlayButton.ToString());
            Select(CoverChoice, look.Cover.ToString());
            ShowFont(DisplayFontChoice, look.DisplayFont);
            ShowFont(TextFontChoice, look.TextFont);

            GradientAngleSlider.Value = look.GradientAngle;
            TintSlider.Value = Math.Round(look.BackdropTint * 100);
            PanelOpacitySlider.Value = Math.Round(look.PanelOpacity * 100);
            CornerSlider.Value = look.CornerRadius;
            BorderSlider.Value = look.BorderWidth;
            GapSlider.Value = look.PanelGap;
            AdaptiveAccentSwitch.IsOn = look.AdaptiveAccent;
            ShowSliderValues();
            ShowCoverArt(look);

            foreach (var swatch in _swatches)
            {
                swatch.Dot.Fill = swatch.Get(look).Opaque.ToBrush();
            }

            GradientAngleRow.Visibility = look.Backdrop == WindowBackdrop.Gradient ? Visibility.Visible : Visibility.Collapsed;
            TintRow.Visibility = look.Backdrop is WindowBackdrop.Artwork or WindowBackdrop.Mica or WindowBackdrop.Acrylic
                ? Visibility.Visible
                : Visibility.Collapsed;
            ProgressPreview.BarStyle = look.Progress;

            CustomizeHint.Text = _theme.Library.ActiveIsPreset
                ? $"Changing {look.Name} makes your own copy; the preset stays as it is."
                : $"Editing {look.Name}. Changes are kept as you go.";

            SelectCard(PresetGrid);
            SelectCard(YourLooksGrid);
        }
        finally
        {
            _loading = false;
        }

        // The cards of the user's own looks follow their edits, a moment after the last change.
        if (LooksKey() != _shownLooks)
        {
            _looksTimer.Stop();
            _looksTimer.Start();
        }
    }

    /// <summary>
    /// The cover art switches, and a word when the blurred cover is on but
    /// the look in use has a background of its own, so nothing seems to happen.
    /// </summary>
    private void ShowCoverArt(ThemeDefinition look)
    {
        SpinningCoverSwitch.IsOn = _theme.SpinningCover;
        BlurredCoverSwitch.IsOn = _theme.BlurredCoverBackground;

        var hint = _theme.BlurredCoverBackground && look.Backdrop != WindowBackdrop.Artwork;
        BlurredCoverHint.Visibility = hint ? Visibility.Visible : Visibility.Collapsed;
        if (hint)
        {
            BlurredCoverHint.Text = $"{look.Name} has its own background, so there is no cover to blur. "
                + $"{ThemePresets.Glass.Name} shows the song cover, or pick Song cover under Customize, Backdrop.";
        }
    }

    private void ShowSliderValues()
    {
        GradientAngleText.Text = $"{GradientAngleSlider.Value:0}°";
        TintText.Text = $"{TintSlider.Value:0}%";
        PanelOpacityText.Text = $"{PanelOpacitySlider.Value:0}%";
        CornerText.Text = $"{CornerSlider.Value:0}";
        BorderText.Text = $"{BorderSlider.Value:0.#}";
        GapText.Text = $"{GapSlider.Value:0}";
    }

    private void SelectCard(GridView grid)
    {
        var active = _theme.Library.ActiveId;
        grid.SelectedItem = grid.Items.OfType<FrameworkElement>().FirstOrDefault(card => card.Tag as string == active);
    }

    /// <summary>The custom look and the saved looks, as cards with their own menus.</summary>
    private void ShowYourLooks()
    {
        _shownLooks = LooksKey();
        var library = _theme.Library;
        YourLooksGrid.Items.Clear();
        if (library.Custom is { } custom)
        {
            var menu = new MenuFlyout();
            menu.Items.Add(MenuItem("Save as…", async () => await SaveAsAsync(custom)));
            menu.Items.Add(MenuItem("Copy as text", () => Copy(custom)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Discard", () => _theme.Delete(custom.Id)));
            YourLooksGrid.Items.Add(Card(custom, "Not saved yet", menu));
        }

        foreach (var look in library.Saved)
        {
            var menu = new MenuFlyout();
            menu.Items.Add(MenuItem("Rename…", async () => await RenameAsync(look)));
            menu.Items.Add(MenuItem("Copy as text", () => Copy(look)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem("Delete", () => _theme.Delete(look.Id)));
            YourLooksGrid.Items.Add(Card(look, Describe(look), menu));
        }

        YourLooksPanel.Visibility = YourLooksGrid.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _loading = true;
        SelectCard(YourLooksGrid);
        _loading = false;
    }

    private string LooksKey()
    {
        var library = _theme.Library;
        var looks = library.Saved.Prepend(library.Custom).OfType<ThemeDefinition>();
        return string.Join('|', looks.Select(look => $"{look.Id}:{look.GetHashCode()}"));
    }

    private static string Describe(ThemeDefinition look)
    {
        var mode = ThemePalette.From(look).IsLight ? "Light" : "Dark";
        var backdrop = look.Backdrop switch
        {
            WindowBackdrop.Gradient => "gradient",
            WindowBackdrop.Artwork => "song cover",
            WindowBackdrop.Mica => "Mica",
            WindowBackdrop.Acrylic => "acrylic",
            _ => "solid",
        };
        return $"{mode}, {backdrop}";
    }

    private static StackPanel Card(ThemeDefinition look, string subtitle, MenuFlyout? menu)
    {
        var resources = Application.Current.Resources;
        var card = new StackPanel { Padding = new Thickness(6), Spacing = 8, Tag = look.Id, ContextFlyout = menu };
        card.Children.Add(new LookPreview(look));

        var text = new StackPanel { Padding = new Thickness(2, 0, 2, 2), Spacing = 1, MaxWidth = LookPreview.PreviewWidth };
        text.Children.Add(new TextBlock
        {
            Text = look.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Style = (Style)resources["ResonateBodyTextStyle"],
        });
        text.Children.Add(new TextBlock { Text = subtitle, Style = (Style)resources["ResonateCaptionTextStyle"] });
        card.Children.Add(text);

        AutomationProperties.SetName(card, $"{look.Name}. {subtitle}");
        return card;
    }

    private static MenuFlyoutItem MenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static ComboBoxItem FontItem(string font) => new()
    {
        Content = font.Split(',')[0],
        Tag = font,
        FontFamily = new FontFamily(font),
    };

    private void AddSwatch(string label, Func<ThemeDefinition, ThemeColor> get, Func<ThemeDefinition, ThemeColor, ThemeDefinition> set, bool allowsAlpha = false)
    {
        var dot = new Ellipse
        {
            Width = 24,
            Height = 24,
            Stroke = _theme.GetBrush("ResonateBorderBrush"),
            StrokeThickness = 1,
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(dot);
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        var button = new Button
        {
            Content = content,
            Width = 156,
            Height = 44,
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["ResonateSubtleButtonStyle"],
        };
        AutomationProperties.SetName(button, label + " colour");

        var swatch = new Swatch(label, get, set, allowsAlpha, dot);
        button.Click += (_, _) => EditColor(swatch, button);
        _swatches.Add(swatch);
        SwatchGrid.Children.Add(button);
    }

    private void EditColor(Swatch swatch, FrameworkElement anchor)
    {
        _editing = null;
        _picker.IsAlphaEnabled = swatch.AllowsAlpha;
        _picker.Color = swatch.Get(_theme.Current).ToColor();
        _editing = swatch;
        _pickerFlyout.ShowAt(anchor);
    }

    private void OnPickerColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_editing is not { } swatch)
        {
            return;
        }

        var color = args.NewColor.ToThemeColor();
        if (!swatch.AllowsAlpha)
        {
            color = color.Opaque;
        }

        swatch.Dot.Fill = color.Opaque.ToBrush();
        _theme.Edit(look => swatch.Set(look, color));
    }

    private void OnTransitionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TransitionChoice.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _theme.Transition = Enum.Parse<ThemeTransitionKind>(tag);
        }
    }

    private void OnLookClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FrameworkElement { Tag: string id } card)
        {
            return;
        }

        // The ripple grows from the card that was clicked.
        var origin = card.TransformToVisual(null).TransformPoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2));
        _theme.Select(id, origin);
    }

    private void OnChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && sender is ComboBox combo && combo.SelectedItem is ComboBoxItem { Tag: string tag } && _choices.TryGetValue(combo, out var change))
        {
            _theme.Edit(look => change(look, tag), smooth: true);
        }
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowSliderValues();
        var value = e.NewValue;
        if (sender is Slider slider && _sliders.TryGetValue(slider, out var change))
        {
            _theme.Edit(look => change(look, value));
        }
    }

    private void OnBaseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            var mode = Enum.Parse<ThemeBase>(tag);
            _theme.Edit(look => look.WithBase(mode), smooth: true);
        }
    }

    private void OnAdaptiveAccentToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            var on = AdaptiveAccentSwitch.IsOn;
            _theme.Edit(look => look with { AdaptiveAccent = on }, smooth: true);
        }
    }

    // The cover art switches are not part of a look, so they skip Edit (which would make a custom copy).
    private void OnSpinningCoverToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _theme.SpinningCover = SpinningCoverSwitch.IsOn;
        }
    }

    private void OnBlurredCoverToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _theme.BlurredCoverBackground = BlurredCoverSwitch.IsOn;
        }
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && sender is ComboBox combo && combo.SelectedItem is ComboBoxItem { Tag: string font })
        {
            SetFont(combo, font);
        }
    }

    private void OnFontTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        // A font typed by name; an unknown name falls back to Windows' default font.
        args.Handled = true;
        var match = sender.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Content as string, args.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        SetFont(sender, match?.Tag as string ?? args.Text);
    }

    private void SetFont(ComboBox combo, string font)
    {
        font = font.Trim();
        if (font.Length == 0)
        {
            return;
        }

        if (combo == DisplayFontChoice)
        {
            _theme.Edit(look => look with { DisplayFont = font }, smooth: true);
        }
        else
        {
            _theme.Edit(look => look with { TextFont = font }, smooth: true);
        }
    }

    private static void ShowFont(ComboBox combo, string font)
    {
        var item = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, font, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            combo.SelectedItem = item;
        }
        else
        {
            combo.SelectedItem = null;
            combo.Text = font;
        }
    }

    private static void Select(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == tag);

    private void OnCustomizeExpanding(Expander sender, ExpanderExpandingEventArgs args) => CustomizeOpen = true;

    private void OnCustomizeCollapsed(Expander sender, ExpanderCollapsedEventArgs args) => CustomizeOpen = false;

    private async void OnSaveAsClick(object sender, RoutedEventArgs e) => await SaveAsAsync(_theme.Current);

    private void OnCopyClick(object sender, RoutedEventArgs e) => Copy(_theme.Current);

    private async void OnPasteClick(object sender, RoutedEventArgs e)
    {
        string? text = null;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                text = await content.GetTextAsync();
            }
        }
        catch (COMException)
        {
            // Another app is holding the clipboard.
        }

        if (text is not null && ThemeJson.Import(text) is { } look)
        {
            _theme.Add(look);
            Tell($"Added {_theme.Current.Name} to your looks.", InfoBarSeverity.Success);
        }
        else
        {
            Tell("The clipboard has no Resonate look in it. Copy one with Copy as text first.", InfoBarSeverity.Warning);
        }
    }

    private void Copy(ThemeDefinition look)
    {
        var package = new DataPackage();
        package.SetText(ThemeJson.Export(look));
        try
        {
            Clipboard.SetContent(package);
            Tell($"Copied {look.Name}. Paste it into Resonate with Paste a look.", InfoBarSeverity.Success);
        }
        catch (COMException)
        {
            Tell("The clipboard is busy. Try again in a moment.", InfoBarSeverity.Warning);
        }
    }

    private async Task SaveAsAsync(ThemeDefinition look)
    {
        var suggestion = look.Id == ThemeLibrary.CustomId && look.Name.EndsWith(" (custom)", StringComparison.Ordinal)
            ? "My " + look.Name[..^" (custom)".Length]
            : look.Name;
        if (await AskNameAsync("Save this look", "Save", suggestion) is { } name)
        {
            if (look.Id != _theme.Library.ActiveId)
            {
                _theme.Select(look.Id, transition: ThemeTransitionKind.None);
            }

            _theme.SaveAs(name);
        }
    }

    private async Task RenameAsync(ThemeDefinition look)
    {
        if (await AskNameAsync("Rename this look", "Rename", look.Name) is { } name)
        {
            _theme.Rename(look.Id, name);
        }
    }

    private async Task<string?> AskNameAsync(string title, string action, string name)
    {
        var box = new TextBox { Text = name, MaxLength = ThemeDefinition.MaxNameLength, PlaceholderText = "Name" };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = (XamlRoot.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };

        var result = await dialog.ShowAsync();
        var chosen = box.Text.Trim();
        return result == ContentDialogResult.Primary && chosen.Length > 0 ? chosen : null;
    }

    private static void Tell(string message, InfoBarSeverity severity) => App.MainWindow?.ShowMessage(message, severity);

    /// <summary>One colour of the look, with its button's colour dot.</summary>
    private sealed record Swatch(
        string Label,
        Func<ThemeDefinition, ThemeColor> Get,
        Func<ThemeDefinition, ThemeColor, ThemeDefinition> Set,
        bool AllowsAlpha,
        Ellipse Dot);
}

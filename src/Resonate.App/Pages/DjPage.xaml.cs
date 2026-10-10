using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Services;

namespace Resonate.App.Pages;

/// <summary>
/// Resonate's own DJ (<see cref="DjService"/>): Start DJ plays a run of
/// themed sets from the user's music; while it plays, the button switches
/// it up, the set playing shows under the title and the next ones below.
/// </summary>
public sealed partial class DjPage : Page
{
    private const int SetsShown = 4;

    private readonly AppServices _services = App.Services;
    private readonly DjService _dj;
    private bool _loading;

    public DjPage()
    {
        InitializeComponent();
        _dj = _services.Dj;

        // The look's accent, like Liked Songs and Local Files (the owner's choice, 10 October 2026).
        CoverFrame.Background = Artwork.AccentCoverBrush;
        CoverGlyph.Foreground = _services.Theme.GetBrush("ResonateOnAccentBrush");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _dj.Changed -= OnDjChanged;
        _dj.Changed += OnDjChanged;
        _loading = true;
        VoiceSwitch.IsOn = _services.Settings.DjVoice;
        _loading = false;
        ShowState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _dj.Changed -= OnDjChanged;

    private void OnDjChanged(object? sender, EventArgs e) => ShowState();

    private void ShowState()
    {
        var on = _dj.IsOn;
        StartText.Text = on ? "Switch it up" : "Start DJ";
        StartGlyph.Glyph = on ? "" : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(StartButton, StartText.Text);
        StartButton.IsEnabled = !_dj.IsStarting;
        WaitingRing.IsActive = _dj.IsStarting;

        var sets = _dj.Sets;
        var current = _dj.CurrentSet;
        NowText.Text = on && current < sets.Count ? sets[current].Title : string.Empty;
        NowText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        SetsList.Children.Clear();
        if (on)
        {
            foreach (var set in sets.Skip(current + 1).Take(SetsShown))
            {
                SetsList.Children.Add(SetRow(set));
            }
        }

        SetsPanel.Visibility = SetsList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A set: its title, and the first artists in it.</summary>
    private FrameworkElement SetRow(Resonate.Spotify.Playback.DjSet set)
    {
        var resources = Application.Current.Resources;
        var artists = string.Join(", ", set.Tracks.Select(t => t.Artists.Split(',')[0].Trim()).Distinct().Take(3));
        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(_services.Theme.Palette.CornerMedium),
            Background = _services.Theme.GetBrush("ResonateSurfaceHoverBrush"),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = set.Title, Style = (Style)resources["ResonateBodyTextStyle"], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = artists, Style = (Style)resources["ResonateSecondaryTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
        };
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => _ = StartAsync();

    private async Task StartAsync()
    {
        StatusText.Visibility = Visibility.Collapsed;
        var started = _dj.IsOn ? await _dj.SwitchUpAsync() : await _dj.StartAsync();
        if (!started)
        {
            // Nothing to make sets from (no Liked Songs yet), or Spotify could not be reached.
            StatusText.Text = "The DJ needs some Liked Songs to play.";
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void OnVoiceToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _services.Settings.DjVoice = VoiceSwitch.IsOn;
        _services.SaveSettings();
        if (!VoiceSwitch.IsOn)
        {
            _dj.StopVoice();
        }
    }
}

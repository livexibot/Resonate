using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Helpers;
using Resonate.App.Services;
using Resonate.Spotify.Playback;
using Launcher = Windows.System.Launcher;

namespace Resonate.App.Pages;

/// <summary>
/// Spotify's DJ. Only the Spotify app can start it, so "Start DJ" opens it
/// there; once it plays, Resonate shows and controls it like any music.
/// </summary>
public sealed partial class DjPage : Page
{
    private const string IdleText =
        "Start DJ opens it in the Spotify app. Press play there once, and Resonate takes over the controls. DJ needs Spotify Premium in a country where Spotify offers it; talking to the DJ is only in Spotify's phone app.";

    private const string WebApiOnlyText =
        "With Spotify Web API only, Resonate closes the Spotify app, and Spotify does not let other apps start DJ. Start DJ in a Spotify app (search for “DJ”); Resonate shows and controls it once it plays. Or switch back to Windows' media controls in Settings.";

    private static readonly TimeSpan WaitForStart = TimeSpan.FromSeconds(20);

    private readonly AppServices _services = App.Services;
    private CancellationTokenSource _leaving = new();
    private int _updateQueued;

    public DjPage()
    {
        InitializeComponent();

        // The look's accent, like Liked Songs and Local Files (the owner's choice, 10 October 2026).
        CoverFrame.Background = Artwork.AccentCoverBrush;
        CoverGlyph.Foreground = _services.Theme.GetBrush("ResonateOnAccentBrush");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _leaving = new CancellationTokenSource();
        _services.Player.StateChanged += OnPlayerStateChanged;
        ShowState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _services.Player.StateChanged -= OnPlayerStateChanged;
        _leaving.Cancel();
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _updateQueued, 0);
                ShowState();
            });
        }
    }

    private void ShowState()
    {
        var state = _services.Player.State;
        if (SpotifyDj.IsPlaying(state))
        {
            StartText.Text = state.IsPlaying ? "Playing" : "Resume DJ";
            StartButton.IsEnabled = !state.IsPlaying;
            StatusText.Text = state.IsPlaying
                ? "DJ is playing. Use the player bar to pause or skip; shuffle and repeat are up to the DJ."
                : "DJ is paused. Resume it here or with the player bar.";
            WaitingRing.IsActive = false;
            return;
        }

        StartText.Text = "Start DJ";
        StartButton.IsEnabled = _services.UsesSpotifyApp;
        if (!WaitingRing.IsActive)
        {
            StatusText.Text = _services.UsesSpotifyApp ? IdleText : WebApiOnlyText;
        }
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => _ = StartAsync();

    private async Task StartAsync()
    {
        var player = _services.Player;
        if (SpotifyDj.IsPlaying(player.State))
        {
            // DJ is already the music on this account: just carry on.
            await player.PlayAsync();
            return;
        }

        if (!_services.UsesSpotifyApp)
        {
            // "Spotify Web API only" leaves the Spotify app alone.
            StatusText.Text = WebApiOnlyText;
            return;
        }

        // The Web API cannot start DJ (Spotify declines and stops the music), so open it in Spotify.
        if (!Uri.TryCreate(SpotifyDj.ContextUri, UriKind.Absolute, out var uri) || !await Launcher.LaunchUriAsync(uri))
        {
            StatusText.Text = "Resonate could not open the Spotify app. In Spotify, search for “DJ” and press play; Resonate shows it once it plays.";
            return;
        }

        StatusText.Text = "Spotify is opening DJ. Press play there; Resonate takes over once it starts.";
        WaitingRing.IsActive = true;
        var token = _leaving.Token;
        try
        {
            // Spotify does not tell Resonate when DJ starts; ask until it shows up.
            var deadline = DateTimeOffset.UtcNow + WaitForStart;
            while (DateTimeOffset.UtcNow < deadline && !SpotifyDj.IsPlaying(player.State))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                await Task.Run(() => player.Spotify.RefreshFromWebApiAsync(token), token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Shown below as "not started yet".
        }

        WaitingRing.IsActive = false;
        if (!SpotifyDj.IsPlaying(player.State))
        {
            StatusText.Text = "DJ has not started yet. Press play on DJ in the Spotify app (search for “DJ” there if it did not open). " + IdleText;
        }

        ShowState();
    }
}

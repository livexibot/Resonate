using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Resonate.Spotify.Lyrics;
using Resonate.Spotify.Playback;
using TextDecorations = Windows.UI.Text.TextDecorations;
using VirtualKey = Windows.System.VirtualKey;

namespace Resonate.App.Controls;

/// <summary>
/// The words of the song that plays, in the pane on the right (the Lyrics
/// plugin). Synced lyrics follow the song like Spotify's: the line being
/// sung lights up and glides to about a quarter of the way down, lines
/// already sung dim, and a click on a line plays from there. Plain lyrics
/// just show. The words are looked up only while the pane is open, when it
/// opens and when the song changes; the clock that follows the song runs
/// only while the pane is open, the song plays and the window shows.
/// The panel's behaviour follows spotifast's (src/ui/lyrics.rs, MIT
/// licence, copyright Carmine Paolino).
/// </summary>
public sealed partial class LyricsPane : UserControl
{
    /// <summary>Where the sung line sits, as a fraction of the pane's height from the top: high, so the lines to come fill the view.</summary>
    private const double SungLineAt = 0.25;

    private const double SungOpacity = 1;
    private const double UpcomingOpacity = 0.6;
    private const double PastOpacity = 0.35;
    private const double LineFontSize = 22;

    // About ten looks a second: a line is never more than a tenth of a second late, and the pane costs next to nothing.
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    // A line lights up a moment early, so it is lit by the time its first word is sung.
    private static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan LightUp = TimeSpan.FromMilliseconds(220);

    // After a scroll by hand, the pane leaves the reader alone this long before following again.
    private static readonly TimeSpan FollowAgainAfter = TimeSpan.FromSeconds(4);

    // Skipping through songs looks up only the one that stays.
    private static readonly TimeSpan SongSettles = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan OwnScroll = TimeSpan.FromMilliseconds(900);
    private static readonly Vector3 ClosedOffset = new(24, 0, 0);

    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _settle;
    private readonly List<TextBlock> _lines = [];
    private readonly Border _endSpace = new();
    private PlayerRouter? _player;
    private LyricsLibrary? _library;
    private string _credit = string.Empty;
    private CancellationTokenSource? _loading;
    private string? _songKey;
    private string? _shownKey;
    private SongLyrics? _lyrics;
    private int _active = -1;
    private int _version;
    private int _updateQueued;
    private bool _windowShown = true;
    private DateTimeOffset? _scrolledByHand;
    private DateTimeOffset _ownScrollUntil;

    public LyricsPane()
    {
        InitializeComponent();

        // Closed, the pane waits faded out and a little to the right, so
        // opening slides it in (on the compositor, never delaying a click).
        Opacity = 0;
        Translation = ClosedOffset;
        OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(180) };
        TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(220) };

        var queue = DispatcherQueue.GetForCurrentThread();
        _clock = queue.CreateTimer();
        _clock.Interval = Tick;
        _clock.Tick += (_, _) => FollowClock();
        _settle = queue.CreateTimer();
        _settle.Interval = SongSettles;
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => _ = LoadAsync();

        // A scroll by hand (wheel, scroll bar, touch, keys) means the reader wants to look elsewhere for a while.
        LinesScroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, _) => ScrolledByHand()), handledEventsToo: true);
        LinesScroller.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => ScrolledByHand()), handledEventsToo: true);
        LinesScroller.AddHandler(KeyDownEvent, new KeyEventHandler(OnScrollerKeyDown), handledEventsToo: true);
        LinesScroller.DirectManipulationStarted += (_, _) =>
        {
            if (DateTimeOffset.UtcNow > _ownScrollUntil)
            {
                ScrolledByHand();
            }
        };

        // Room under the last line, so it too can rise to where the sung line sits.
        LinesScroller.SizeChanged += (_, e) =>
        {
            _endSpace.Height = Math.Max(60, e.NewSize.Height * (1 - SungLineAt));
            if (e.NewSize.Height != e.PreviousSize.Height)
            {
                Follow(animate: false);
            }
        };
    }

    /// <summary>Raised when the close button is clicked.</summary>
    public event EventHandler? CloseRequested;

    public bool IsOpen => _player is not null;

    /// <summary>Shows the words of what <paramref name="player"/> plays, and follows it until <see cref="Close"/>.</summary>
    /// <param name="credit">Where the words come from, under the last line.</param>
    public void Open(PlayerRouter player, LyricsLibrary library, string credit, bool windowShown)
    {
        if (_player is not null)
        {
            return;
        }

        _player = player;
        _library = library;
        _credit = credit;
        _windowShown = windowShown;
        player.StateChanged += OnStateChanged;
        Show(player.State, opening: true);

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_player is not null)
            {
                Opacity = 1;
                Translation = Vector3.Zero;
            }
        });
    }

    /// <summary>Stops following the player: no clock and no lookups while the pane is closed.</summary>
    public void Close()
    {
        if (_player is { } player)
        {
            player.StateChanged -= OnStateChanged;
        }

        _player = null;
        _clock.Stop();
        _settle.Stop();
        CancelLoading();
        LoadingRing.IsActive = false;
        Opacity = 0;
        Translation = ClosedOffset;
    }

    /// <summary>Forgets the words shown (the plugin was turned off).</summary>
    public void Clear()
    {
        Close();
        _songKey = null;
        _shownKey = null;
        ShowLines(null);
        MessagePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>The window says when it is minimised or hidden, so the clock can rest.</summary>
    public void SetWindowShown(bool shown)
    {
        _windowShown = shown;
        if (_player is not null)
        {
            FollowClock();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // State changes arrive on background threads; follow the newest one once.
        if (Interlocked.Exchange(ref _updateQueued, 1) == 1)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _updateQueued, 0);
            if (_player is { } player)
            {
                Show(player.State, opening: false);
            }
        });
    }

    private void Show(PlayerState state, bool opening)
    {
        // The same song keeps its words while Spotify fills in its address, cover or length.
        var key = state.HasTrack ? string.Join('\n', state.Source, state.Title, LyricsText.CleanArtist(state.Artists ?? string.Empty)) : null;
        if (key != _songKey)
        {
            _songKey = key;
            CancelLoading();
            _settle.Stop();
            if (key is null)
            {
                ShowMessage("\uE8D6", "Nothing playing", "Play a song to see its lyrics.");
            }
            else if (opening)
            {
                _ = LoadAsync();
            }
            else
            {
                ShowLoading();
                _settle.Start();
            }
        }
        else if (opening && key is not null && key != _shownKey)
        {
            // Closed while the words were on their way: ask again (the disk cache usually has them).
            _ = LoadAsync();
        }

        FollowClock();
    }

    private async Task LoadAsync()
    {
        CancelLoading();
        var version = ++_version;
        if (_player is not { } player || _library is not { } library)
        {
            return;
        }

        var state = player.State;
        var key = _songKey;
        if (LyricsQuery.For(state.Title, state.Artists, state.Album, state.Duration) is not { } query)
        {
            ShowFound(key, null);
            return;
        }

        ShowLoading();
        var loading = new CancellationTokenSource();
        _loading = loading;
        SongLyrics? lyrics;
        try
        {
            // The cache is on disk and LRCLIB is far away: neither on the interface thread.
            lyrics = await Task.Run(() => library.GetAsync(query, loading.Token), loading.Token);
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            if (version == _version)
            {
                LoadingRing.IsActive = false;
                ShowMessage("\uE783", "Couldn't load the lyrics", "Check the connection and try again.", retry: true);
            }

            return;
        }
        finally
        {
            if (_loading == loading)
            {
                _loading = null;
            }

            loading.Dispose();
        }

        if (version == _version)
        {
            ShowFound(key, lyrics);
        }
    }

    private void CancelLoading()
    {
        _version++;
        _loading?.Cancel();
        _loading = null;
    }

    private void ShowLoading()
    {
        ShowLines(null);
        MessagePanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = true;
    }

    private void ShowFound(string? key, SongLyrics? lyrics)
    {
        _shownKey = key;
        LoadingRing.IsActive = false;
        if (lyrics is null)
        {
            ShowMessage("\uE8D6", "No lyrics", "No lyrics found for this song.");
        }
        else if (lyrics.IsInstrumental)
        {
            ShowMessage("\uE8D6", "Instrumental", "This song has no words. Enjoy the music.");
        }
        else
        {
            MessagePanel.Visibility = Visibility.Collapsed;
            ShowLines(lyrics);
        }
    }

    private void ShowMessage(string glyph, string title, string text, bool retry = false)
    {
        ShowLines(null);
        LoadingRing.IsActive = false;
        MessageIcon.Glyph = glyph;
        MessageTitle.Text = title;
        MessageText.Text = text;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
    }

    /// <summary>Builds a line for each line of words (a few dozen; no list needed), or empties the pane.</summary>
    private void ShowLines(SongLyrics? lyrics)
    {
        _lyrics = lyrics;
        _active = -1;
        _scrolledByHand = null;
        _lines.Clear();
        LinesPanel.Children.Clear();
        NoteText.Visibility = lyrics is { IsSynced: false } ? Visibility.Visible : Visibility.Collapsed;
        if (lyrics is null)
        {
            return;
        }

        var style = (Style)Application.Current.Resources["ResonateSectionTextStyle"];
        var animate = App.Services.Theme.AnimationsEnabled;
        foreach (var line in lyrics.Lines)
        {
            var block = new TextBlock
            {
                Style = style,
                FontSize = LineFontSize,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None,

                // A timed line with no words is the band playing on.
                Text = lyrics.IsSynced && string.IsNullOrWhiteSpace(line.Text) ? "\u266A" : line.Text,
                Opacity = lyrics.IsSynced ? UpcomingOpacity : SungOpacity,
                OpacityTransition = animate ? new ScalarTransition { Duration = LightUp } : null,
            };

            if (lyrics.IsSynced && line.At is { } at)
            {
                block.Tapped += (_, _) => PlayFrom(at);
                block.PointerEntered += (_, _) => block.TextDecorations = TextDecorations.Underline;
                block.PointerExited += (_, _) => block.TextDecorations = TextDecorations.None;
                AutomationPropertiesHelper.SetName(block, $"{block.Text}, play from here");
            }

            _lines.Add(block);
            LinesPanel.Children.Add(block);
        }

        LinesPanel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 0),
            Style = (Style)Application.Current.Resources["ResonateCaptionTextStyle"],
            Text = _credit,
        });
        LinesPanel.Children.Add(_endSpace);

        LinesScroller.ChangeView(null, 0, null, disableAnimation: true);
        if (lyrics.IsSynced)
        {
            // Straight to the line being sung, laid out now so its place is known.
            LinesPanel.UpdateLayout();
            FollowClock(jump: true);
        }
    }

    /// <summary>Lights the line being sung and keeps the clock running while there is something to follow.</summary>
    private void FollowClock(bool jump = false)
    {
        if (_player is not { } player || _lyrics is not { IsSynced: true } lyrics || _lines.Count == 0)
        {
            _clock.Stop();
            return;
        }

        var state = player.State;
        var now = DateTimeOffset.UtcNow;
        var active = lyrics.ActiveLine(state.PositionAt(now) + (state.IsPlaying ? Lead : TimeSpan.Zero));
        if (active != _active || jump)
        {
            Light(active);
            if (_scrolledByHand is null)
            {
                Follow(animate: !jump);
            }
        }
        else if (_scrolledByHand is { } since && now - since >= FollowAgainAfter)
        {
            _scrolledByHand = null;
            Follow(animate: true);
        }

        var run = state.IsPlaying && _windowShown;
        if (!run)
        {
            _clock.Stop();
        }
        else if (!_clock.IsRunning)
        {
            _clock.Start();
        }
    }

    private void Light(int active)
    {
        _active = active;
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].Opacity = i == active ? SungOpacity : i < active ? PastOpacity : UpcomingOpacity;
        }
    }

    /// <summary>Glides so the sung line sits <see cref="SungLineAt"/> of the way down; before the first line, to the top.</summary>
    private void Follow(bool animate)
    {
        if (_lyrics is not { IsSynced: true } || _lines.Count == 0 || _scrolledByHand is not null)
        {
            return;
        }

        double target = 0;
        if (_active >= 0 && _active < _lines.Count)
        {
            var line = _lines[_active];
            target = Math.Max(0, line.ActualOffset.Y + (line.ActualHeight / 2) - (LinesScroller.ViewportHeight * SungLineAt));
        }

        _ownScrollUntil = DateTimeOffset.UtcNow + OwnScroll;
        LinesScroller.ChangeView(null, target, null, disableAnimation: !animate || !App.Services.Theme.AnimationsEnabled);
    }

    private void PlayFrom(TimeSpan at)
    {
        if (_player is not { } player)
        {
            return;
        }

        // The reader chose a line: follow from there at once.
        _scrolledByHand = null;
        _ = player.SeekAsync(at);
    }

    private void ScrolledByHand()
    {
        if (_lyrics is { IsSynced: true })
        {
            _scrolledByHand = DateTimeOffset.UtcNow;
        }
    }

    private void OnScrollerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home or VirtualKey.End or VirtualKey.Space)
        {
            ScrolledByHand();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_player is not null)
        {
            _ = LoadAsync();
        }
    }
}

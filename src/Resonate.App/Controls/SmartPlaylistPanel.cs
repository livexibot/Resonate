using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.App.Pages.Lists;
using Resonate.App.Services;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;
using Rect = Windows.Foundation.Rect;
using Size = Windows.Foundation.Size;

namespace Resonate.App.Controls;

/// <summary>
/// A smart playlist's rules as a sentence of chips ("Songs from [Liked
/// Songs] saved [in 2023] released [before 2000] longer than [5 min]"),
/// each opening a small editor, with "+" for another rule; then the count
/// and length, which roll to their new values, "Keep on Spotify", Rename
/// and Delete. Built in code; shown on the smart playlist's page above
/// its songs (<see cref="SmartPlaylistSource"/>).
/// </summary>
public sealed class SmartPlaylistPanel
{
    private static readonly TimeSpan RollTime = TimeSpan.FromMilliseconds(450);
    private static readonly int[] Limits = [25, 50, 100, 250, 500];

    private readonly SmartPlaylistService _smart;
    private readonly string _id;
    private readonly SmartPlaylistSource _source;
    private readonly AppServices _services = App.Services;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly SmartChipFlow _sentence = new() { Spacing = 8 };
    private readonly StackPanel _starters;
    private readonly TextBlock _totals;
    private readonly ToggleSwitch _keep;
    private readonly TextBlock _keepState;
    private readonly List<(Button Chip, Func<SmartPlaylist, string> Text)> _chips = [];
    private bool _showingState;
    private bool _detached;

    // The count and length roll from what is shown to the new values.
    private bool _totalsShown;
    private (double Count, double Seconds) _shown;
    private (double Count, double Seconds) _from;
    private (double Count, double Seconds) _to;
    private long _rollStart;
    private bool _rolling;

    public SmartPlaylistPanel(SmartPlaylistService smart, string id, SmartPlaylistSource source)
    {
        _smart = smart;
        _id = id;
        _source = source;

        _starters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _starters.Children.Add(Text("Start from", "ResonateSecondaryTextStyle"));
        foreach (var name in SmartStarters.Names)
        {
            var starter = new HyperlinkButton { Content = name, Padding = new Thickness(4, 2, 4, 2) };
            starter.Click += (_, _) =>
            {
                _smart.ApplyStarter(_id, name);
                Rebuild();
            };
            _starters.Children.Add(starter);
        }

        _totals = Text(string.Empty, "ResonateSecondaryTextStyle");
        _totals.VerticalAlignment = VerticalAlignment.Center;

        _keep = new ToggleSwitch { OnContent = "Keep on Spotify", OffContent = "Keep on Spotify", MinWidth = 0 };
        ToolTipService.SetToolTip(_keep, "Make a real playlist on Spotify, so it plays on your phone too. Resonate refreshes it once a day; edits made to it in Spotify are replaced.");
        _keep.Toggled += OnKeepToggled;
        _keepState = Text(string.Empty, "ResonateCaptionTextStyle");
        _keepState.VerticalAlignment = VerticalAlignment.Center;

        var rename = IconButton("", "Rename");
        rename.Click += (_, _) => _ = RenameAsync();
        var delete = IconButton("", "Delete smart playlist");
        delete.Click += (_, _) => _ = DeleteAsync();

        var bottom = new Grid { ColumnSpacing = 12 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
        {
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        Place(bottom, _totals, 0);
        Place(bottom, _keepState, 1);
        Place(bottom, _keep, 2);
        Place(bottom, rename, 3);
        Place(bottom, delete, 4);

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(_sentence);
        root.Children.Add(_starters);
        root.Children.Add(bottom);
        Root = root;

        Rebuild();
        ShowState();
    }

    public FrameworkElement Root { get; }

    private static bool Animate => App.Services.Theme.AnimationsEnabled;

    /// <summary>The page was left: nothing more is shown, and the totals stop rolling.</summary>
    public void Detach()
    {
        _detached = true;
        StopRolling();
    }

    /// <summary>On the interface thread: the switch, its note and every chip's words follow the definition.</summary>
    public void ShowState()
    {
        if (_detached || Definition() is not { } playlist)
        {
            return;
        }

        _showingState = true;
        _keep.IsOn = playlist.KeepOnSpotify;
        _showingState = false;
        _keepState.Text = KeepState(playlist);
        foreach (var (chip, text) in _chips)
        {
            chip.Content = text(playlist);
        }
    }

    /// <summary>On any thread, with the songs just worked out: the count and length roll to theirs.</summary>
    public void ShowTotals(IReadOnlyList<TrackInfo> tracks)
    {
        var count = tracks.Count;
        var seconds = tracks.Sum(t => t.Duration.TotalSeconds);
        _dispatcher.TryEnqueue(() => RollTo(count, seconds));
    }

    private SmartPlaylist? Definition() => _smart.Find(_id);

    // ---- The sentence ----

    /// <summary>Builds the chips again (after a rule was added or removed); <paramref name="opened"/>'s editor opens.</summary>
    private void Rebuild(SmartRule? opened = null)
    {
        if (Definition() is not { } playlist)
        {
            return;
        }

        _sentence.Children.Clear();
        _chips.Clear();
        AddGroup("Songs from", Chip(SmartPlaylistText.Source, SourceMenu()));
        foreach (var rule in playlist.Rules)
        {
            var chip = Chip(_ => SmartPlaylistText.Describe(rule).Value, Editor(rule));
            var group = AddGroup(SmartPlaylistText.Describe(rule).Lead, chip);
            if (rule == opened)
            {
                // A rule chosen whole (explicit, a playlist) needs no editor; the others open theirs.
                Reveal(group, chip, openEditor: rule.Kind is not (SmartRuleKind.Explicit or SmartRuleKind.NotExplicit or SmartRuleKind.NotInPlaylist));
            }
        }

        AddGroup(null, Chip(p => SmartPlaylistText.Describe(p.Order, p.FromLikedSongs), OrderMenu()));
        AddGroup(null, Chip(p => SmartPlaylistText.DescribeLimit(p.Limit), LimitMenu()));

        var add = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Flyout = AddMenu(),
            MinHeight = 32,
            Padding = new Thickness(10, 4, 10, 4),
            Style = Resource<Style>("ResonateSubtleButtonStyle"),
        };
        AutomationProperties.SetName(add, "Add a rule");
        ToolTipService.SetToolTip(add, "Add a rule");
        _sentence.Children.Add(add);

        _starters.Visibility = playlist.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private StackPanel AddGroup(string? lead, Button chip)
    {
        // A word and its chip wrap to the next line together.
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrEmpty(lead))
        {
            var words = Text(lead, "ResonateBodyTextStyle");
            words.VerticalAlignment = VerticalAlignment.Center;
            group.Children.Add(words);
        }

        group.Children.Add(chip);
        _sentence.Children.Add(group);
        return group;
    }

    private Button Chip(Func<SmartPlaylist, string> text, FlyoutBase editor)
    {
        var chip = new Button
        {
            Content = Definition() is { } playlist ? text(playlist) : string.Empty,
            Flyout = editor,
            MinHeight = 32,
            Padding = new Thickness(12, 4, 12, 4),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Style = Resource<Style>("ResonateSubtleButtonStyle"),
        };
        if (Resource<SolidColorBrush>("ResonateAccentSoftBrush") is { } fill)
        {
            chip.Background = fill;
        }

        _chips.Add((chip, text));
        return chip;
    }

    /// <summary>A new rule slides in on a spring, and its editor opens.</summary>
    private static void Reveal(UIElement group, Button chip, bool openEditor)
    {
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            chip.Loaded -= OnLoaded;
            if (openEditor)
            {
                chip.Flyout?.ShowAt(chip);
            }

            if (!Animate)
            {
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(group, true);
            var visual = ElementCompositionPreview.GetElementVisual(group);
            var spring = visual.Compositor.CreateSpringVector3Animation();
            spring.InitialValue = new Vector3(-28, 0, 0);
            spring.FinalValue = Vector3.Zero;
            spring.DampingRatio = 0.55f;
            spring.Period = TimeSpan.FromMilliseconds(45);
            visual.StartAnimation("Translation", spring);
        }

        chip.Loaded += OnLoaded;
    }

    /// <summary>The rules changed; <paramref name="rebuild"/> when one was added or removed.</summary>
    private void Edited(SmartPlaylist playlist, bool rebuild = false, SmartRule? opened = null)
    {
        _smart.Edited(playlist);
        if (rebuild)
        {
            Rebuild(opened);
        }
    }

    private MenuFlyout SourceMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (Definition() is not { } playlist)
            {
                return;
            }

            menu.Items.Add(Radio("Liked Songs", "source", playlist.FromLikedSongs, () =>
            {
                playlist.SourcePlaylistId = null;
                playlist.SourceName = null;
                Edited(playlist);
            }));
            foreach (var other in OwnPlaylists(playlist))
            {
                menu.Items.Add(Radio(other.Name, "source", playlist.SourcePlaylistId == other.Id, () =>
                {
                    playlist.SourcePlaylistId = other.Id;
                    playlist.SourceName = other.Name;
                    Edited(playlist);
                }));
            }
        };
        return menu;
    }

    private MenuFlyout OrderMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (Definition() is not { } playlist)
            {
                return;
            }

            foreach (var order in Enum.GetValues<SmartOrder>())
            {
                menu.Items.Add(Radio(SmartPlaylistText.Describe(order, playlist.FromLikedSongs), "order", playlist.Order == order, () =>
                {
                    playlist.Order = order;
                    Edited(playlist);
                }));
            }
        };
        return menu;
    }

    private MenuFlyout LimitMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (Definition() is not { } playlist)
            {
                return;
            }

            foreach (var limit in Limits.Cast<int?>().Prepend(null))
            {
                menu.Items.Add(Radio(SmartPlaylistText.DescribeLimit(limit), "limit", playlist.Limit == limit, () =>
                {
                    playlist.Limit = limit;
                    Edited(playlist);
                }));
            }
        };
        return menu;
    }

    private MenuFlyout AddMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var (kind, name) in new[]
            {
                (SmartRuleKind.SavedInLastDays, "Saved in the last days"),
                (SmartRuleKind.SavedInYears, "Saved in a year"),
                (SmartRuleKind.ReleasedBefore, "Released before"),
                (SmartRuleKind.ReleasedAfter, "Released after"),
                (SmartRuleKind.ReleasedBetween, "Released between"),
                (SmartRuleKind.LongerThan, "Longer than"),
                (SmartRuleKind.ShorterThan, "Shorter than"),
                (SmartRuleKind.Explicit, "Explicit"),
                (SmartRuleKind.NotExplicit, "Not explicit"),
                (SmartRuleKind.ArtistIs, "By an artist"),
                (SmartRuleKind.ArtistIsNot, "Not by an artist"),
            })
            {
                menu.Items.Add(TrackActions.Item(name, null, () => AddRule(SmartStarters.NewRule(kind, DateTimeOffset.Now))));
            }

            if (Definition() is { } playlist && OwnPlaylists(playlist).ToList() is { Count: > 0 } others)
            {
                var notIn = new MenuFlyoutSubItem { Text = "Not in a playlist" };
                foreach (var other in others)
                {
                    notIn.Items.Add(TrackActions.Item(other.Name, null, () =>
                        AddRule(new SmartRule { Kind = SmartRuleKind.NotInPlaylist, Text = other.Id, Label = other.Name })));
                }

                menu.Items.Add(notIn);
            }
        };
        return menu;
    }

    private void AddRule(SmartRule rule)
    {
        if (Definition() is not { } playlist)
        {
            return;
        }

        playlist.Rules.Add(rule);
        Edited(playlist, rebuild: true, opened: rule);
    }

    private void RemoveRule(SmartRule rule, FlyoutBase editor)
    {
        editor.Hide();

        // After the editor closes: its chip leaves with it.
        _dispatcher.TryEnqueue(() =>
        {
            if (Definition() is { } playlist && playlist.Rules.Remove(rule))
            {
                Edited(playlist, rebuild: true);
            }
        });
    }

    // ---- Each rule's editor ----

    private FlyoutBase Editor(SmartRule rule) => rule.Kind switch
    {
        SmartRuleKind.SavedInLastDays => NumberEditor(rule, "Saved in the last", "days", 1, 3650, 1, () => rule.Value, v => rule.Value = (int)Math.Round(v)),
        SmartRuleKind.ReleasedBefore => NumberEditor(rule, "Released before", null, 1900, 2100, 1, () => rule.Value, v => rule.Value = (int)Math.Round(v)),
        SmartRuleKind.ReleasedAfter => NumberEditor(rule, "Released after", null, 1900, 2100, 1, () => rule.Value, v => rule.Value = (int)Math.Round(v)),
        SmartRuleKind.LongerThan => NumberEditor(rule, "Longer than", "minutes", 0.5, 120, 0.5, () => rule.Value / 60.0, v => rule.Value = (int)Math.Round(v * 60)),
        SmartRuleKind.ShorterThan => NumberEditor(rule, "Shorter than", "minutes", 0.5, 120, 0.5, () => rule.Value / 60.0, v => rule.Value = (int)Math.Round(v * 60)),
        SmartRuleKind.SavedInYears => YearsEditor(rule, "Saved in", 2008),
        SmartRuleKind.ReleasedBetween => YearsEditor(rule, "Released in", 1900),
        SmartRuleKind.Explicit or SmartRuleKind.NotExplicit => ExplicitEditor(rule),
        SmartRuleKind.ArtistIs or SmartRuleKind.ArtistIsNot => ArtistEditor(rule),
        _ => PlaylistEditor(rule),
    };

    private Flyout NumberEditor(SmartRule rule, string title, string? unit, double min, double max, double step, Func<double> read, Action<double> write)
    {
        var flyout = new Flyout();
        var box = NumberBox(min, max, step);
        var filling = false;
        box.ValueChanged += (_, e) =>
        {
            if (!filling && !double.IsNaN(e.NewValue) && Definition() is { } playlist)
            {
                write(Math.Clamp(e.NewValue, min, max));
                Edited(playlist);
            }
        };
        flyout.Opening += (_, _) =>
        {
            filling = true;
            box.Value = read();
            filling = false;
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(box);
        if (unit is not null)
        {
            var words = Text(unit, "ResonateBodyTextStyle");
            words.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(words);
        }

        flyout.Content = EditorBody(title, row, rule, flyout);
        return flyout;
    }

    private Flyout YearsEditor(SmartRule rule, string title, int first)
    {
        var flyout = new Flyout();
        var last = DateTime.Now.Year;
        var from = NumberBox(first, last, 1);
        var to = NumberBox(first, last, 1);
        var filling = false;
        void Write()
        {
            if (!filling && !double.IsNaN(from.Value) && !double.IsNaN(to.Value) && Definition() is { } playlist)
            {
                rule.Value = (int)Math.Round(from.Value);
                rule.Value2 = (int)Math.Round(to.Value);
                Edited(playlist);
            }
        }

        from.ValueChanged += (_, _) => Write();
        to.ValueChanged += (_, _) => Write();
        flyout.Opening += (_, _) =>
        {
            filling = true;
            from.Value = Math.Min(rule.Value, rule.Value2);
            to.Value = Math.Max(rule.Value, rule.Value2);
            filling = false;
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(from);
        var words = Text("to", "ResonateBodyTextStyle");
        words.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(words);
        row.Children.Add(to);
        flyout.Content = EditorBody(title, row, rule, flyout);
        return flyout;
    }

    private Flyout ArtistEditor(SmartRule rule)
    {
        var flyout = new Flyout();
        var box = new AutoSuggestBox { PlaceholderText = "Artist", MinWidth = 260 };
        void Commit(string? text)
        {
            text = text?.Trim();
            if (text != (rule.Text ?? string.Empty) && Definition() is { } playlist)
            {
                rule.Text = string.IsNullOrEmpty(text) ? null : text;
                Edited(playlist);
            }
        }

        box.TextChanged += (sender, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var typed = sender.Text.Trim();
                sender.ItemsSource = typed.Length == 0
                    ? null
                    : _source.KnownArtists().Where(a => a.Contains(typed, StringComparison.CurrentCultureIgnoreCase)).Order(StringComparer.CurrentCultureIgnoreCase).Take(8).ToList();
            }
        };
        box.QuerySubmitted += (_, e) => Commit(e.ChosenSuggestion as string ?? e.QueryText);
        flyout.Opening += (_, _) => box.Text = rule.Text ?? string.Empty;
        flyout.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        flyout.Closed += (_, _) => Commit(box.Text);
        flyout.Content = EditorBody(rule.Kind == SmartRuleKind.ArtistIs ? "By" : "Not by", box, rule, flyout);
        return flyout;
    }

    private MenuFlyout ExplicitEditor(SmartRule rule)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var (kind, name) in new[] { (SmartRuleKind.Explicit, "Explicit"), (SmartRuleKind.NotExplicit, "Not explicit") })
            {
                menu.Items.Add(Radio(name, "explicit", rule.Kind == kind, () =>
                {
                    if (Definition() is { } playlist)
                    {
                        rule.Kind = kind;
                        Edited(playlist);
                    }
                }));
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(TrackActions.Item("Remove rule", "", () => RemoveRule(rule, menu)));
        };
        return menu;
    }

    private MenuFlyout PlaylistEditor(SmartRule rule)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (Definition() is not { } playlist)
            {
                return;
            }

            foreach (var other in OwnPlaylists(playlist))
            {
                menu.Items.Add(Radio(other.Name, "playlist", rule.Text == other.Id, () =>
                {
                    rule.Text = other.Id;
                    rule.Label = other.Name;
                    Edited(playlist);
                }));
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(TrackActions.Item("Remove rule", "", () => RemoveRule(rule, menu)));
        };
        return menu;
    }

    private StackPanel EditorBody(string title, UIElement control, SmartRule rule, FlyoutBase editor)
    {
        var body = new StackPanel { Spacing = 12, MinWidth = 220 };
        body.Children.Add(Text(title, "ResonateEyebrowTextStyle"));
        body.Children.Add(control);
        var remove = new Button { Content = "Remove rule", Style = Resource<Style>("ResonateSubtleButtonStyle") };
        remove.Click += (_, _) => RemoveRule(rule, editor);
        body.Children.Add(remove);
        return body;
    }

    /// <summary>The user's own playlists (Spotify lists only their songs), without the copies smart playlists keep on Spotify.</summary>
    private IEnumerable<SimplifiedPlaylist> OwnPlaylists(SmartPlaylist playlist)
    {
        var library = _services.Library;
        var kept = _smart.All.Select(p => p.SpotifyId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return library.Snapshot?.Playlists.Where(p => library.CanListSongs(p) && !kept.Contains(p.Id) && p.Id != playlist.SourcePlaylistId) is { } own
            ? own.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : [];
    }

    // ---- Keep on Spotify, Rename, Delete ----

    private void OnKeepToggled(object sender, RoutedEventArgs e)
    {
        if (!_showingState && Definition() is { } playlist)
        {
            _smart.SetKeepOnSpotify(playlist, _keep.IsOn);
        }
    }

    private string KeepState(SmartPlaylist playlist)
    {
        if (!playlist.KeepOnSpotify)
        {
            return string.Empty;
        }

        if (_smart.IsSyncing(playlist.Id))
        {
            return "Updating…";
        }

        if (playlist.SyncedAt is not { } synced)
        {
            return "Not updated yet";
        }

        var local = synced.ToLocalTime();
        return "Updated " + (local.Date == DateTime.Today
            ? local.ToString("t", CultureInfo.CurrentCulture)
            : local.ToString("d MMM", CultureInfo.CurrentCulture));
    }

    private async Task RenameAsync()
    {
        if (Definition() is not { } playlist || Root.XamlRoot is not { } root)
        {
            return;
        }

        var name = new TextBox { Text = playlist.Name, MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Rename smart playlist",
            Content = name,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };
        name.Loaded += (_, _) =>
        {
            name.Focus(FocusState.Programmatic);
            name.SelectAll();
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _smart.Rename(_id, name.Text);
        }
    }

    private async Task DeleteAsync()
    {
        if (Definition() is not { } playlist || Root.XamlRoot is not { } root)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"Delete “{playlist.Name}”?",
            Content = playlist.SpotifyId is null ? null : "Its playlist on Spotify stays, but Resonate stops updating it.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _smart.Delete(_id);
        }
    }

    // ---- The rolling count and length ----

    private void RollTo(int count, double seconds)
    {
        if (_detached)
        {
            return;
        }

        if (!_totalsShown || !Animate)
        {
            _totalsShown = true;
            _shown = (count, seconds);
            StopRolling();
            _totals.Text = Totals(count, seconds);
            return;
        }

        _from = _shown;
        _to = (count, seconds);
        _rollStart = Stopwatch.GetTimestamp();
        if (!_rolling)
        {
            _rolling = true;
            CompositionTarget.Rendering += OnRollFrame;
        }
    }

    private void OnRollFrame(object? sender, object e)
    {
        var t = Math.Min(1, Stopwatch.GetElapsedTime(_rollStart) / RollTime);
        var eased = 1 - Math.Pow(1 - t, 3);
        _shown = (_from.Count + ((_to.Count - _from.Count) * eased), _from.Seconds + ((_to.Seconds - _from.Seconds) * eased));
        _totals.Text = Totals((int)Math.Round(_shown.Count), _shown.Seconds);
        if (t >= 1)
        {
            StopRolling();
        }
    }

    private void StopRolling()
    {
        if (_rolling)
        {
            _rolling = false;
            CompositionTarget.Rendering -= OnRollFrame;
        }
    }

    /// <summary>"63 songs, 4 hr 12 min", like every list's header.</summary>
    private static string Totals(int count, double seconds)
    {
        if (count == 0)
        {
            return Format.SongCount(0);
        }

        var total = TimeSpan.FromSeconds(Math.Round(seconds));
        var length = total.TotalHours >= 1
            ? string.Format(CultureInfo.CurrentCulture, "{0} hr {1} min", (int)total.TotalHours, total.Minutes)
            : string.Format(CultureInfo.CurrentCulture, "{0} min {1} sec", total.Minutes, total.Seconds);
        return $"{Format.SongCount(count)}, {length}";
    }

    // ---- Small pieces ----

    private static RadioMenuFlyoutItem Radio(string text, string group, bool chosen, Action choose)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = chosen };
        item.Click += (_, _) => choose();
        return item;
    }

    private static NumberBox NumberBox(double min, double max, double step) => new()
    {
        Minimum = min,
        Maximum = max,
        SmallChange = step,
        LargeChange = step * 10,
        MinWidth = 120,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
    };

    private static Button IconButton(string glyph, string label)
    {
        var button = new Button { Content = glyph, Width = 36, Height = 36, Style = Resource<Style>("ResonateIconButtonStyle") };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    private static TextBlock Text(string text, string style) => new()
    {
        Text = text,
        Style = Resource<Style>(style),
        TextWrapping = TextWrapping.NoWrap,
    };

    private static void Place(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        element.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(element);
    }

    private static T? Resource<T>(string key)
        where T : class =>
        Application.Current.Resources.TryGetValue(key, out var found) ? found as T : null;
}

/// <summary>Lays its children out left to right, starting a new line when one does not fit (the sentence of chips).</summary>
public sealed partial class SmartChipFlow : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0, lineWidth = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + Spacing + size.Width > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + Spacing;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += (lineWidth > 0 ? Spacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        width = Math.Max(width, lineWidth);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var line = new List<UIElement>();
        double top = 0, lineWidth = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + Spacing + size.Width > finalSize.Width)
            {
                ArrangeLine(line, top, lineHeight);
                top += lineHeight + Spacing;
                line.Clear();
                lineWidth = 0;
                lineHeight = 0;
            }

            line.Add(child);
            lineWidth += (lineWidth > 0 ? Spacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        ArrangeLine(line, top, lineHeight);
        return finalSize;
    }

    /// <summary>Each child centred in its line's height.</summary>
    private void ArrangeLine(List<UIElement> line, double top, double height)
    {
        double left = 0;
        foreach (var child in line)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(left, top + ((height - size.Height) / 2), size.Width, size.Height));
            left += size.Width + Spacing;
        }
    }
}

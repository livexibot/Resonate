using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Controls;
using Resonate.App.Helpers;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.WebApi;

namespace Resonate.App.ViewModels;

public abstract partial class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A cover and the colour tile behind it. While a cover loads nothing is
/// drawn there, so no colour flashes before the picture appears; the tile
/// shows only for something without a cover, or once its cover turns out
/// to be missing (offline, or a picture Windows can not read).
/// </summary>
public sealed partial class CoverTile : ObservableObject
{
    private readonly string? _url;
    private readonly TrackInfo? _localTrack;
    private readonly int _width;
    private readonly Brush _placeholder;
    private ImageSource? _image;
    private bool _requested;
    private bool _missing;

    /// <param name="url">The cover's address, or null when there is none.</param>
    /// <param name="displayWidth">The width it is shown at (it is decoded at that size).</param>
    /// <param name="placeholder">The colour tile for when there is no cover.</param>
    public CoverTile(string? url, int displayWidth, Brush placeholder)
    {
        _url = url;
        _width = displayWidth;
        _placeholder = placeholder;
        _missing = string.IsNullOrEmpty(url);
    }

    /// <summary>A song in Local Files: its cover is read from the file itself.</summary>
    private CoverTile(TrackInfo track, int displayWidth, Brush placeholder)
    {
        _localTrack = track;
        _width = displayWidth;
        _placeholder = placeholder;
        _missing = App.Services.LocalFiles.Covers is null;
    }

    public static CoverTile ForLocalFile(TrackInfo track, int displayWidth, Brush placeholder) => new(track, displayWidth, placeholder);

    /// <summary>The colour tile while there is no cover to show; nothing while one loads or shows.</summary>
    public Brush? Background => _missing ? _placeholder : null;

    /// <summary>Created on first use, on the interface thread, at the size it is shown.</summary>
    public ImageSource? Image
    {
        get
        {
            if (!_requested && !_missing)
            {
                _requested = true;
                Task<bool> missing;
                _image = _localTrack is { } track
                    ? LocalArtwork.For(track, _width, out missing)
                    : App.Services.Covers.Get(_url, _width, out missing);
                _ = WatchAsync(new WeakReference<CoverTile>(this), missing);
            }

            return _image;
        }
    }

    // Holds the tile only weakly: a cover that never answers (one Windows
    // loads itself but never shows) must not keep a closed page's rows alive
    // through their bindings.
    private static async Task WatchAsync(WeakReference<CoverTile> tile, Task<bool> missing)
    {
        if (await missing && tile.TryGetTarget(out var target) && !target._missing)
        {
            target._missing = true;
            target.OnPropertyChanged(nameof(Background));
        }
    }
}

/// <summary>
/// Which optional columns a song list shows. One instance is shared by all
/// of a list's rows, so when the list gets narrow (a small window, or the
/// queue open beside it) every row drops the same columns at once: the date
/// added first, then the album, as Spotify does.
/// </summary>
public sealed partial class TrackColumns : ObservableObject
{
    /// <summary>Below this list width the album column goes, so titles keep their room.</summary>
    public const double AlbumMinWidth = 560;

    /// <summary>Below this list width the date added column goes.</summary>
    public const double AddedMinWidth = 760;

    /// <summary>Below this list width the year column goes.</summary>
    public const double YearMinWidth = 680;

    private bool _album;
    private bool _dateAdded;
    private bool _year;
    private bool _bpm;
    private bool _key;
    private bool _loudness;
    private bool _energy;
    private GridLength _bpmWidth;
    private GridLength _keyWidth;
    private GridLength _loudnessWidth;
    private GridLength _energyWidth;
    private GridLength _albumWidth;
    private GridLength _addedWidth;
    private GridLength _yearWidth;

    private readonly bool _listHasAlbum;
    private readonly bool _listHasDateAdded;
    private double _width = double.PositiveInfinity;
    private double _textScale = 1;

    /// <param name="album">The list has an album column (not on an album's own page).</param>
    /// <param name="dateAdded">The list knows when songs were added.</param>
    public TrackColumns(bool album, bool dateAdded)
    {
        _listHasAlbum = album;
        _listHasDateAdded = dateAdded;
        ReadOptions();
        Fit(double.PositiveInfinity);
    }

    /// <summary>Raised when the user changes Settings, Layout, Song lists, so open lists follow at once.</summary>
    public static event EventHandler? OptionsChanged;

    public static void NotifyOptionsChanged() => OptionsChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>Each row shows its song's cover.</summary>
    public bool ShowsCovers { get; private set; }

    /// <summary>Takes the user's newest choices: the covers at once, the columns at the width last fitted.</summary>
    public void Reload()
    {
        ReadOptions();
        OnPropertyChanged(nameof(CoverWidth));
        OnPropertyChanged(nameof(CoverSpacing));
        OnPropertyChanged(nameof(CoverVisibility));
        Fit(_width, _textScale);
    }

    private void ReadOptions()
    {
        var settings = App.Services.Settings;
        _album = _listHasAlbum && settings.ShowAlbumColumn;
        _dateAdded = _listHasDateAdded && settings.ShowAddedColumn;
        _year = settings.ShowYearColumn;
        _bpm = settings.SongStats && settings.ShowBpmColumn;
        _key = settings.SongStats && settings.ShowKeyColumn;
        _loudness = settings.SongStats && settings.ShowLoudnessColumn;
        _energy = settings.SongStats && settings.ShowEnergyColumn;
        ShowsCovers = settings.ShowSongCovers;
    }

    public GridLength CoverWidth => ShowsCovers ? new GridLength(40) : new GridLength(0);

    public double CoverSpacing => ShowsCovers ? 12 : 0;

    public Visibility CoverVisibility => ShowsCovers ? Visibility.Visible : Visibility.Collapsed;

    public GridLength YearWidth
    {
        get => _yearWidth;
        private set => Set(ref _yearWidth, value);
    }

    public GridLength BpmWidth
    {
        get => _bpmWidth;
        private set => Set(ref _bpmWidth, value);
    }

    public GridLength KeyWidth
    {
        get => _keyWidth;
        private set => Set(ref _keyWidth, value);
    }

    public GridLength LoudnessWidth
    {
        get => _loudnessWidth;
        private set => Set(ref _loudnessWidth, value);
    }

    public GridLength EnergyWidth
    {
        get => _energyWidth;
        private set => Set(ref _energyWidth, value);
    }

    public GridLength AlbumWidth
    {
        get => _albumWidth;
        private set => Set(ref _albumWidth, value);
    }

    public GridLength AddedWidth
    {
        get => _addedWidth;
        private set => Set(ref _addedWidth, value);
    }

    /// <summary>
    /// Shows the columns the list has that fit in <paramref name="width"/>,
    /// with text <paramref name="textScale"/> times its usual size (Text size).
    /// </summary>
    public void Fit(double width, double textScale = 1)
    {
        _width = width;
        _textScale = textScale;
        AlbumWidth = _album && width >= AlbumMinWidth * textScale ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        AddedWidth = _dateAdded && width >= AddedMinWidth * textScale ? new GridLength(132 * textScale) : new GridLength(0);
        YearWidth = _year && width >= YearMinWidth * textScale ? new GridLength(72 * textScale) : new GridLength(0);
        BpmWidth = _bpm && width >= 640 * textScale ? new GridLength(56 * textScale) : new GridLength(0);
        KeyWidth = _key && width >= 700 * textScale ? new GridLength(56 * textScale) : new GridLength(0);
        LoudnessWidth = _loudness && width >= 820 * textScale ? new GridLength(84 * textScale) : new GridLength(0);
        EnergyWidth = _energy && width >= 900 * textScale ? new GridLength(64 * textScale) : new GridLength(0);
    }
}

/// <summary>One song in a list.</summary>
public sealed partial class TrackRow : ObservableObject
{
    private const string HeartGlyphOutline = "\uEB51";
    private const string HeartGlyphFilled = "\uEB52";

    private string _number;
    private bool _isCurrent;
    private bool _isLiked;
    private CoverTile? _cover;

    public TrackRow(TrackInfo track, int number, TrackColumns? columns = null, bool isLiked = false)
    {
        Track = track;
        _number = number.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _isLiked = isLiked;
        Columns = columns ?? Default;
        DateAdded = Format.DateAdded(track.AddedAt, DateTimeOffset.UtcNow);
    }

    /// <summary>The width of a row's cover.</summary>
    public const int CoverWidth = 40;

    private static TrackColumns Default { get; } = new(album: true, dateAdded: false);

    public TrackInfo Track { get; private set; }

    public TrackColumns Columns { get; }

    /// <summary>The row's place in the list as shown (or the track number on an album).</summary>
    public string Number
    {
        get => _number;
        set => Set(ref _number, value);
    }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    public string Album => Track.Album;

    public string DateAdded { get; }

    public string Duration => Format.Duration(Track.Duration);

    private SongStats? _stats;
    private bool _statsAsked;

    /// <summary>BPM, key, loudness and energy, asked for the first time one of them is drawn.</summary>
    private SongStats? Stats
    {
        get
        {
            // Asked once song stats are on; a row drawn while they were off asks when they come on.
            if (!_statsAsked && App.Services.SongStats.IsOn)
            {
                _statsAsked = true;
                _stats = App.Services.SongStats.Get(Track.Id, this);
            }

            return _stats;
        }
    }

    /// <summary>Song stats were switched on or off: the columns read again.</summary>
    public void RefreshStats()
    {
        if (!App.Services.SongStats.IsOn)
        {
            _statsAsked = false;
            _stats = null;
        }

        ShowStats(_stats);
    }

    public string Bpm => Stats?.TempoText ?? string.Empty;

    public string Key => Stats?.KeyText ?? string.Empty;

    public string Loudness => Stats?.LoudnessText ?? string.Empty;

    public string Energy => Stats?.EnergyText ?? string.Empty;

    /// <summary>The stats came (null: the service does not know the song).</summary>
    public void ShowStats(SongStats? stats)
    {
        _stats = stats;
        OnPropertyChanged(nameof(Bpm));
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Loudness));
        OnPropertyChanged(nameof(Energy));
    }

    /// <summary>The year its album came out, for the Year column.</summary>
    public string Year => Track.ReleaseYear?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    public double RowOpacity => Track.IsPlayable || Track.IsLocal ? 1 : 0.45;

    /// <summary>Created on first use, on the interface thread (local files read their own cover).</summary>
    public CoverTile Cover => _cover ??= CoverFor(Track, CoverWidth);

    /// <summary>Only Spotify songs can be liked (not local files or podcast episodes).</summary>
    public Visibility HeartVisibility => CanLike(Track) ? Visibility.Visible : Visibility.Collapsed;

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (Set(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(TitleBrush));
            }
        }
    }

    public bool IsLiked
    {
        get => _isLiked;
        set
        {
            if (Set(ref _isLiked, value))
            {
                OnPropertyChanged(nameof(HeartGlyph));
                OnPropertyChanged(nameof(HeartBrush));
                OnPropertyChanged(nameof(HeartLabel));
            }
        }
    }

    public string HeartGlyph => IsLiked ? HeartGlyphFilled : HeartGlyphOutline;

    public Brush HeartBrush => App.Services.Theme.GetBrush(IsLiked ? "ResonateAccentBrush" : "ResonateTextTertiaryBrush");

    public string HeartLabel => IsLiked ? "Remove from Liked Songs" : "Save to Liked Songs";

    /// <summary>The playing song's title is drawn in the accent colour.</summary>
    public Brush TitleBrush => App.Services.Theme.GetBrush(IsCurrent ? "ResonateAccentBrush" : "ResonateTextPrimaryBrush");

    /// <summary>The same song with new details that are not shown (its position after a move).</summary>
    public void Replace(TrackInfo track) => Track = track;

    /// <summary>The cover of <paramref name="track"/>, with its album's colour tile for when it has none.</summary>
    public static CoverTile CoverFor(TrackInfo track, int displayWidth)
    {
        var placeholder = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
        return track.FilePath is not null
            ? CoverTile.ForLocalFile(track, displayWidth, placeholder)
            : new CoverTile(track.SmallImageUrl, displayWidth, placeholder);
    }

    public static bool CanLike(TrackInfo track) =>
        track.FilePath is null && !track.IsLocal && track.Uri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true;
}

/// <summary>A playlist in the sidebar.</summary>
public sealed partial class PlaylistNavItem : ObservableObject
{
    private CoverTile? _cover;
    private bool _isCurrent;
    private bool _isPlaying;

    public PlaylistNavItem(SimplifiedPlaylist playlist)
    {
        Playlist = playlist;
        PlaceholderBrush = Artwork.PlaceholderBrush(playlist.Name);
    }

    public SimplifiedPlaylist Playlist { get; }

    public string Id => Playlist.Id;

    public string Name => Playlist.Name;

    public string Details
    {
        get
        {
            // Like Spotify: what it is and whose, never how many songs.
            var owner = Playlist.Owner?.DisplayName ?? Playlist.Owner?.Id;
            return string.IsNullOrEmpty(owner) ? "Playlist" : $"Playlist · {owner}";
        }
    }

    /// <summary>While the sidebar is too narrow for names, only the covers show (see MainWindow.ApplySidebarCompact).</summary>
    public static bool Compact { get; set; }

    public Visibility TextVisibility => Compact ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The user's choice to show playlist covers (Settings, Layout); covers always show while <see cref="Compact"/>.</summary>
    public static bool ShowCovers { get; set; } = true;

    public Visibility CoverVisibility => ShowCovers || Compact ? Visibility.Visible : Visibility.Collapsed;

    public GridLength CoverColumnWidth => ShowCovers || Compact ? new GridLength(48) : new GridLength(0);

    /// <summary>Picks up a change of <see cref="Compact"/> or <see cref="ShowCovers"/>.</summary>
    public void RefreshCompact()
    {
        OnPropertyChanged(nameof(TextVisibility));
        OnPropertyChanged(nameof(CoverVisibility));
        OnPropertyChanged(nameof(CoverColumnWidth));
    }

    public Brush PlaceholderBrush { get; }

    public CoverTile Cover => _cover ??= new CoverTile(ImagePicker.Pick(Playlist.Images, 96), 48, PlaceholderBrush);

    public ImageSource? Image => Cover.Image;

    /// <summary>The music plays from this playlist (or did, if it is paused): its name is drawn in the accent colour.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (Set(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(NameBrush));
                OnPropertyChanged(nameof(SpeakerVisibility));
            }
        }
    }

    /// <summary>The music is playing (not paused): the current playlist shows a speaker.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (Set(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(SpeakerVisibility));
            }
        }
    }

    public Brush NameBrush => App.Services.Theme.GetBrush(IsCurrent ? "ResonateAccentBrush" : "ResonateTextPrimaryBrush");

    public Visibility SpeakerVisibility => IsCurrent && IsPlaying ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>A square card for an album or playlist (search results).</summary>
public sealed partial class CardItem
{
    private CoverTile? _cover;

    public CardItem(string title, string subtitle, string uri, string? id, string? imageUrl, bool isPlaylist)
    {
        Title = title;
        Subtitle = subtitle;
        Uri = uri;
        Id = id;
        ImageUrl = imageUrl;
        IsPlaylist = isPlaylist;
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Uri { get; }

    public string? Id { get; }

    public string? ImageUrl { get; }

    public bool IsPlaylist { get; }

    public CoverTile Cover => _cover ??= new CoverTile(ImageUrl, 160, Artwork.PlaceholderBrush(Title));
}

/// <summary>A navigation entry at the top of the sidebar.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string key, string glyph, string label)
    {
        Key = key;
        Glyph = glyph;
        Label = label;
    }

    /// <summary>Hidden while the sidebar shows only icons and covers (see <see cref="PlaylistNavItem.Compact"/>).</summary>
    public Visibility LabelVisibility => PlaylistNavItem.Compact ? Visibility.Collapsed : Visibility.Visible;

    public void RefreshCompact() => OnPropertyChanged(nameof(LabelVisibility));

    public string Key { get; }

    public string Glyph { get; }

    public string Label { get; }
}

/// <summary>
/// Listening over one stretch of time (the past day or week), a card at the
/// top of Home: the minutes, a bar chart of when, and the top artist and song.
/// </summary>
public sealed partial class StatCard : ObservableObject
{
    private ImageSource? _artistImage;
    private ImageSource? _songImage;

    /// <param name="trend">A line comparing with the time before ("▲ 23% on the week before"), or null.</param>
    public StatCard(string heading, ListeningSummary summary, BarSeries bars, string emptyText, string? trend = null)
    {
        Summary = summary;
        Bars = bars;
        Heading = heading;
        var minutes = (int)Math.Round(summary.Listened.TotalMinutes);
        Minutes = minutes.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        MinutesDetail = (minutes == 1 ? "minute" : "minutes") + " · " + Format.SongCount(summary.Songs);
        ArtistName = summary.TopArtist?.Name ?? string.Empty;
        ArtistInitials = HomeText.Initials(ArtistName);
        ArtistDetail = "Top artist · " + Plays(summary.TopArtistPlays);
        SongTitle = summary.TopSong?.Title ?? string.Empty;
        SongDetail = "Top song · " + Plays(summary.TopSongPlays);
        ArtistPlaceholder = Artwork.PlaceholderBrush(ArtistName);
        ArtistInitialsBrush = Artwork.InitialsBrush(ArtistName);
        SongPlaceholder = Artwork.PlaceholderBrush(summary.TopSong?.Album is { Length: > 0 } album ? album : SongTitle);
        EmptyText = emptyText;
        Trend = trend ?? string.Empty;
        TrendVisibility = trend is null ? Visibility.Collapsed : Visibility.Visible;
        TopVisibility = summary.Songs > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyVisibility = summary.Songs > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public ListeningSummary Summary { get; }

    /// <summary>The chart: listening per hour or per day.</summary>
    public BarSeries Bars { get; }

    public string Heading { get; }

    /// <summary>The big number.</summary>
    public string Minutes { get; }

    /// <summary>"minutes · 26 songs", beside the big number.</summary>
    public string MinutesDetail { get; }

    /// <summary>Spotify reports which songs played, not for how long.</summary>
    public string MinutesNote => "About: Spotify counts a song once it has played for 30 seconds, and Resonate adds up the songs' full lengths.";

    public string Trend { get; }

    public Visibility TrendVisibility { get; }

    public string ArtistName { get; }

    /// <summary>Shown on the artist's colours until the picture arrives (or when there is none).</summary>
    public string ArtistInitials { get; }

    public Brush ArtistInitialsBrush { get; }

    public string ArtistDetail { get; }

    public string SongTitle { get; }

    public string SongDetail { get; }

    public Brush ArtistPlaceholder { get; }

    public Brush SongPlaceholder { get; }

    public string EmptyText { get; }

    public Visibility TopVisibility { get; }

    public Visibility EmptyVisibility { get; }

    /// <summary>Arrives later than the card: the picture may need asking Spotify.</summary>
    public ImageSource? ArtistImage
    {
        get => _artistImage;
        private set => Set(ref _artistImage, value);
    }

    public ImageSource? SongImage => _songImage ??= Artwork.FromUrl(Summary.TopSong?.ImageUrl, 40);

    public void ShowArtistImage(string? url) => ArtistImage = Artwork.FromUrl(url, 40);

    private static string Plays(int count) => count == 1 ? "1 play" : $"{count:N0} plays";
}

/// <summary>One of the four album covers on a mix card, on its album's colours until it loads.</summary>
public sealed partial class MosaicTile
{
    private readonly string? _url;
    private ImageSource? _image;

    public MosaicTile(string? url, Brush placeholder)
    {
        _url = url;
        Placeholder = placeholder;
    }

    public Brush Placeholder { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(_url, 88);
}

/// <summary>
/// A daily mix or "On repeat" on Home: the covers of the four albums it
/// draws on most (or the artist's picture, when it has fewer), fading into
/// the mix's own colour under its number. A click opens it; the play button
/// that shows under the pointer plays it straight away.
/// </summary>
public sealed partial class MixCard
{
    private const int TileCount = 4;

    private readonly string? _portraitUrl;
    private ImageSource? _portrait;

    /// <param name="number">The mix's number, drawn large; null for a card with a symbol instead ("On repeat").</param>
    /// <param name="portraitUrl">The artist the mix is built around, shown when it has too few albums for four covers.</param>
    public MixCard(string key, string title, string subtitle, string eyebrow, string? number, string glyph, string? portraitUrl, IReadOnlyList<TrackInfo> tracks)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Eyebrow = eyebrow;
        Numeral = number ?? string.Empty;
        Glyph = glyph;
        Tracks = tracks;
        PlaceholderBrush = Artwork.PlaceholderBrush(title);
        ScrimBrush = Artwork.ScrimBrush(title);

        var albums = ListeningStats.MostFrequentAlbums(tracks, TileCount);
        var mosaic = albums.Count >= TileCount || portraitUrl is null;
        _portraitUrl = mosaic ? null : portraitUrl;

        // A missing cover gets a colour of its own, so the square still reads as four.
        var names = Enumerable.Range(0, TileCount).Select(i => i < albums.Count ? albums[i].Album : $"{title} {i}").ToList();
        var colours = Artwork.DistinctPlaceholders(names);
        var tiles = new MosaicTile[TileCount];
        for (var i = 0; i < TileCount; i++)
        {
            tiles[i] = new MosaicTile(i < albums.Count ? albums[i].LargeImageUrl ?? albums[i].SmallImageUrl : null, colours[i]);
        }

        TopLeft = tiles[0];
        TopRight = tiles[1];
        BottomLeft = tiles[2];
        BottomRight = tiles[3];
        MosaicVisibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        PortraitVisibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
        NumeralVisibility = number is null ? Visibility.Collapsed : Visibility.Visible;
        GlyphVisibility = number is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The song list it opens ("mix:1", "onrepeat").</summary>
    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>"DAILY MIX", small above the number.</summary>
    public string Eyebrow { get; }

    public string Numeral { get; }

    public string Glyph { get; }

    public IReadOnlyList<TrackInfo> Tracks { get; }

    public Brush PlaceholderBrush { get; }

    public Brush ScrimBrush { get; }

    public MosaicTile TopLeft { get; }

    public MosaicTile TopRight { get; }

    public MosaicTile BottomLeft { get; }

    public MosaicTile BottomRight { get; }

    public ImageSource? Portrait => _portrait ??= Artwork.FromUrl(_portraitUrl, 176);

    public Visibility MosaicVisibility { get; }

    public Visibility PortraitVisibility { get; }

    public Visibility NumeralVisibility { get; }

    public Visibility GlyphVisibility { get; }

    /// <summary>What screen readers say for the play button.</summary>
    public string PlayName => "Play " + Title;

    /// <summary>The play button: the mix from the top (shuffled when shuffle is on), without opening it.</summary>
    public void Play()
    {
        if (Tracks.Count > 0)
        {
            _ = App.Services.Player.PlayAsync(new PlayRequest(Tracks, -1, null, Title));
        }
    }

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Title}, {Subtitle}";
}

/// <summary>A song in Home's "Recently played" row, with when it played.</summary>
public sealed partial class RecentCard
{
    private CoverTile? _cover;

    public RecentCard(TrackInfo track, DateTimeOffset playedAt, DateTimeOffset now)
    {
        Track = track;
        Ago = Format.DateAdded(playedAt, now);
        Tooltip = $"{track.Title} · {track.Artists}\nPlayed {Ago}";
    }

    public TrackInfo Track { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    /// <summary>"2 hours ago".</summary>
    public string Ago { get; }

    public string Tooltip { get; }

    public CoverTile Cover => _cover ??= new CoverTile(Track.LargeImageUrl, 140, Artwork.PlaceholderBrush(Track.Album.Length > 0 ? Track.Album : Track.Title));

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Title}, {Artists}, {Ago}";
}

/// <summary>One of the user's top artists on Spotify, on Home: a round portrait (initials until it loads) with its rank.</summary>
public sealed partial class TopArtistRow
{
    private ImageSource? _image;

    public TopArtistRow(TopArtist artist, int rank)
    {
        Artist = artist;
        Rank = rank.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Initials = HomeText.Initials(artist.Name);
        PlaceholderBrush = Artwork.PlaceholderBrush(artist.Name);
        InitialsBrush = Artwork.InitialsBrush(artist.Name);
        FirstVisibility = rank == 1 ? Visibility.Visible : Visibility.Collapsed;
        OtherVisibility = rank == 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    public TopArtist Artist { get; }

    public string Rank { get; }

    public string Name => Artist.Name;

    public string Initials { get; }

    public Brush InitialsBrush { get; }

    public Brush PlaceholderBrush { get; }

    /// <summary>Number one's rank is in the accent colour.</summary>
    public Visibility FirstVisibility { get; }

    public Visibility OtherVisibility { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(Artist.ImageUrl, 104);

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Rank}. {Name}";
}

/// <summary>One of the user's top songs on Spotify, on Home: its rank, cover, title and artists.</summary>
public sealed partial class TopSongRow
{
    private CoverTile? _cover;

    public TopSongRow(TrackInfo track, int rank)
    {
        Track = track;
        Rank = rank.ToString(System.Globalization.CultureInfo.CurrentCulture);
        FirstVisibility = rank == 1 ? Visibility.Visible : Visibility.Collapsed;
        OtherVisibility = rank == 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    public TrackInfo Track { get; }

    public string Rank { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    /// <summary>Number one's rank is in the accent colour.</summary>
    public Visibility FirstVisibility { get; }

    public Visibility OtherVisibility { get; }

    public CoverTile Cover => _cover ??= new CoverTile(Track.SmallImageUrl ?? Track.LargeImageUrl, 48, Artwork.PlaceholderBrush(Track.Album.Length > 0 ? Track.Album : Track.Title));

    /// <summary>What screen readers say for the row.</summary>
    public override string ToString() => $"{Rank}. {Title}, {Artists}";
}

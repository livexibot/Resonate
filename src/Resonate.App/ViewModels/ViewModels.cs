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

    private readonly bool _album;
    private readonly bool _dateAdded;
    private GridLength _albumWidth;
    private GridLength _addedWidth;

    public TrackColumns(bool album, bool dateAdded)
    {
        _album = album;
        _dateAdded = dateAdded;
        Fit(double.PositiveInfinity);
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

    /// <summary>Shows the columns the list has that fit in <paramref name="width"/>.</summary>
    public void Fit(double width)
    {
        AlbumWidth = _album && width >= AlbumMinWidth ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        AddedWidth = _dateAdded && width >= AddedMinWidth ? new GridLength(132) : new GridLength(0);
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
    private ImageSource? _image;

    public TrackRow(TrackInfo track, int number, TrackColumns? columns = null, bool isLiked = false)
    {
        Track = track;
        _number = number.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _isLiked = isLiked;
        Columns = columns ?? Default;
        DateAdded = Format.DateAdded(track.AddedAt, DateTimeOffset.UtcNow);
        PlaceholderBrush = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
    }

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

    public double RowOpacity => Track.IsPlayable || Track.IsLocal ? 1 : 0.45;

    public Brush PlaceholderBrush { get; }

    /// <summary>Created on first use, on the interface thread, at the size it is shown (local files read their own cover).</summary>
    public ImageSource? Image => _image ??= Track.FilePath is not null ? LocalArtwork.For(Track, 40) : Artwork.FromUrl(Track.SmallImageUrl, 40);

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

    public static bool CanLike(TrackInfo track) =>
        track.FilePath is null && !track.IsLocal && track.Uri?.StartsWith("spotify:track:", StringComparison.Ordinal) == true;
}

/// <summary>A playlist in the sidebar.</summary>
public sealed partial class PlaylistNavItem : ObservableObject
{
    private ImageSource? _image;
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
            var owner = Playlist.Owner?.DisplayName ?? Playlist.Owner?.Id;
            var count = Format.SongCount(Playlist.ItemCount);
            return string.IsNullOrEmpty(owner) ? count : $"{owner} · {count}";
        }
    }

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(ImagePicker.Pick(Playlist.Images, 64), 40);

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
    private ImageSource? _image;

    public CardItem(string title, string subtitle, string uri, string? id, string? imageUrl, bool isPlaylist)
    {
        Title = title;
        Subtitle = subtitle;
        Uri = uri;
        Id = id;
        ImageUrl = imageUrl;
        IsPlaylist = isPlaylist;
        PlaceholderBrush = Artwork.PlaceholderBrush(title);
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Uri { get; }

    public string? Id { get; }

    public string? ImageUrl { get; }

    public bool IsPlaylist { get; }

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(ImageUrl, 160);
}

/// <summary>A navigation entry at the top of the sidebar.</summary>
public sealed partial class NavItem
{
    public NavItem(string key, string glyph, string label)
    {
        Key = key;
        Glyph = glyph;
        Label = label;
    }

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
    private ImageSource? _image;

    public RecentCard(TrackInfo track, DateTimeOffset playedAt, DateTimeOffset now)
    {
        Track = track;
        Ago = Format.DateAdded(playedAt, now);
        Tooltip = $"{track.Title} · {track.Artists}\nPlayed {Ago}";
        PlaceholderBrush = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
    }

    public TrackInfo Track { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    /// <summary>"2 hours ago".</summary>
    public string Ago { get; }

    public string Tooltip { get; }

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(Track.LargeImageUrl, 140);

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
    private ImageSource? _image;

    public TopSongRow(TrackInfo track, int rank)
    {
        Track = track;
        Rank = rank.ToString(System.Globalization.CultureInfo.CurrentCulture);
        PlaceholderBrush = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
        FirstVisibility = rank == 1 ? Visibility.Visible : Visibility.Collapsed;
        OtherVisibility = rank == 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    public TrackInfo Track { get; }

    public string Rank { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    public Brush PlaceholderBrush { get; }

    /// <summary>Number one's rank is in the accent colour.</summary>
    public Visibility FirstVisibility { get; }

    public Visibility OtherVisibility { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(Track.SmallImageUrl ?? Track.LargeImageUrl, 48);

    /// <summary>What screen readers say for the row.</summary>
    public override string ToString() => $"{Rank}. {Title}, {Artists}";
}

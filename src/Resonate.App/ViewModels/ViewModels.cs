using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
using Resonate.Spotify.History;
using Resonate.Spotify.Library;
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
public sealed partial class PlaylistNavItem
{
    private ImageSource? _image;

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

/// <summary>Listening over one stretch of time (the past day or week), a card at the top of Home.</summary>
public sealed partial class StatCard : ObservableObject
{
    private ImageSource? _artistImage;
    private ImageSource? _songImage;

    public StatCard(string heading, ListeningSummary summary, string emptyText)
    {
        Summary = summary;
        Heading = heading;
        var minutes = (int)Math.Round(summary.Listened.TotalMinutes);
        Minutes = minutes.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        MinutesLabel = minutes == 1 ? "minute" : "minutes";
        Songs = summary.Songs.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        SongsLabel = summary.Songs == 1 ? "song" : "songs";
        ArtistName = summary.TopArtist?.Name ?? string.Empty;
        ArtistDetail = "Top artist · " + Plays(summary.TopArtistPlays);
        SongTitle = summary.TopSong?.Title ?? string.Empty;
        SongDetail = "Top song · " + Plays(summary.TopSongPlays);
        ArtistPlaceholder = Artwork.PlaceholderBrush(ArtistName);
        SongPlaceholder = Artwork.PlaceholderBrush(summary.TopSong?.Album is { Length: > 0 } album ? album : SongTitle);
        EmptyText = emptyText;
        TopVisibility = summary.Songs > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyVisibility = summary.Songs > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public ListeningSummary Summary { get; }

    public string Heading { get; }

    public string Minutes { get; }

    public string MinutesLabel { get; }

    /// <summary>Spotify reports which songs played, not for how long.</summary>
    public string MinutesNote => "About: Spotify counts a song once it has played for 30 seconds, and Resonate adds up the songs' full lengths.";

    public string Songs { get; }

    public string SongsLabel { get; }

    public string ArtistName { get; }

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

/// <summary>A daily mix or "On repeat" on Home: a square of colour with the artist's picture.</summary>
public sealed partial class MixCard
{
    private readonly string? _imageUrl;
    private ImageSource? _image;

    public MixCard(string key, string title, string subtitle, string? imageUrl, string glyph)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Glyph = glyph;
        _imageUrl = imageUrl;
        PlaceholderBrush = Artwork.PlaceholderBrush(title);
    }

    /// <summary>The song list it opens ("mix:1", "onrepeat").</summary>
    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Glyph { get; }

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(_imageUrl, 112);

    public Visibility PortraitVisibility => _imageUrl is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GlyphVisibility => _imageUrl is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Title}, {Subtitle}";
}

/// <summary>A song in Home's "Recently played" row.</summary>
public sealed partial class RecentCard
{
    private ImageSource? _image;

    public RecentCard(TrackInfo track, DateTimeOffset playedAt, DateTimeOffset now)
    {
        Track = track;
        Tooltip = $"{track.Title} · {track.Artists}\nPlayed {Format.DateAdded(playedAt, now)}";
        PlaceholderBrush = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
    }

    public TrackInfo Track { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    public string Tooltip { get; }

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(Track.LargeImageUrl, 128);

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Title}, {Artists}";
}

/// <summary>One of the user's top artists on Spotify, on Home: its rank, portrait and name.</summary>
public sealed partial class TopArtistRow
{
    private ImageSource? _image;

    public TopArtistRow(TopArtist artist, int rank)
    {
        Artist = artist;
        Rank = rank.ToString(System.Globalization.CultureInfo.CurrentCulture);
        PlaceholderBrush = Artwork.PlaceholderBrush(artist.Name);
    }

    public TopArtist Artist { get; }

    public string Rank { get; }

    public string Name => Artist.Name;

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(Artist.ImageUrl, 40);

    /// <summary>What screen readers say for the row.</summary>
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
    }

    public TrackInfo Track { get; }

    public string Rank { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    public Brush PlaceholderBrush { get; }

    public ImageSource? Image => _image ??= Artwork.FromUrl(Track.SmallImageUrl ?? Track.LargeImageUrl, 40);

    /// <summary>What screen readers say for the row.</summary>
    public override string ToString() => $"{Rank}. {Title}, {Artists}";
}

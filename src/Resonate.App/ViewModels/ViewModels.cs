using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Resonate.App.Helpers;
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

/// <summary>Which optional columns a song list shows; shared by all its rows.</summary>
public sealed class TrackColumns
{
    public TrackColumns(bool album, bool dateAdded)
    {
        AlbumWidth = album ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        AddedWidth = dateAdded ? new GridLength(132) : new GridLength(0);
    }

    public GridLength AlbumWidth { get; }

    public GridLength AddedWidth { get; }
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
    public ImageSource? Image => _image ??= Track.FilePath is not null ? LocalArtwork.For(Track, 80) : Artwork.FromUrl(Track.SmallImageUrl, 80);

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

    public ImageSource? Image => _image ??= Artwork.FromUrl(ImagePicker.Pick(Playlist.Images, 64), 80);
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

    public ImageSource? Image => _image ??= Artwork.FromUrl(ImageUrl, 300);
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

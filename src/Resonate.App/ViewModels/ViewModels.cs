using System.ComponentModel;
using System.Runtime.CompilerServices;
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

/// <summary>One song in a list.</summary>
public sealed partial class TrackRow : ObservableObject
{
    private bool _isCurrent;
    private ImageSource? _image;

    public TrackRow(TrackInfo track, int number)
    {
        Track = track;
        Number = number.ToString(System.Globalization.CultureInfo.CurrentCulture);
        PlaceholderBrush = Artwork.PlaceholderBrush(track.Album.Length > 0 ? track.Album : track.Title);
    }

    public TrackInfo Track { get; }

    public string Number { get; }

    public string Title => Track.Title;

    public string Artists => Track.Artists;

    public string Album => Track.Album;

    public string Duration => Format.Duration(Track.Duration);

    public double RowOpacity => Track.IsPlayable ? 1 : 0.45;

    public Brush PlaceholderBrush { get; }

    /// <summary>Created on first use, on the interface thread, at the size it is shown.</summary>
    public ImageSource? Image => _image ??= Artwork.FromUrl(Track.SmallImageUrl, 80);

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

    /// <summary>The playing song's title is drawn in the accent colour.</summary>
    public Brush TitleBrush => App.Services.Theme.GetBrush(IsCurrent ? "ResonateAccentBrush" : "ResonateTextPrimaryBrush");
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

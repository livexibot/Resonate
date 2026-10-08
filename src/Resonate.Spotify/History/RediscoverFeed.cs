using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonate.Spotify.Library;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.History;

/// <summary>What Rediscover keeps on disk: the favourite albums' songs, and how many were asked for today.</summary>
public sealed class RediscoverFile
{
    /// <summary>The account the albums were picked for; another account starts afresh.</summary>
    public string? UserId { get; set; }

    public DateOnly FetchDay { get; set; }

    public int FetchesThatDay { get; set; }

    public List<RediscoverAlbum> Albums { get; set; } = [];
}

/// <summary>
/// Rediscover's row on Home (a built-in plugin): works out the day's cards
/// from what is already stored (Liked Songs, the listening history and
/// Spotify's top songs from Home), and asks Spotify for the songs of a few
/// favourite albums a day for "Deep cuts" (single GET /albums/{id}
/// requests, kept for weeks in its own file). Runs only when Home asks.
/// </summary>
public sealed class RediscoverFeed
{
    private readonly LibraryService _library;
    private readonly HomeFeed _home;
    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly Lock _gate = new();
    private RediscoverFile? _file;
    private RediscoverPicks? _picks;
    private int _refreshing;
    private int _generation;

    /// <param name="path">The file to keep albums in; null keeps them in memory only (demo mode, tests).</param>
    public RediscoverFeed(LibraryService library, HomeFeed home, string? path, TimeProvider? time = null, TimeZoneInfo? zone = null)
    {
        _library = library;
        _home = home;
        _path = path;
        _time = time ?? TimeProvider.System;
        _zone = zone ?? TimeZoneInfo.Local;

        // Signing out forgets Home's history and mixes; these go with them.
        home.Changed += (_, _) =>
        {
            if (home.IsLoaded && home.Content is null && home.History.Plays.Count == 0)
            {
                Forget();
            }
        };
    }

    /// <summary>Raised on any thread when new cards were picked.</summary>
    public event EventHandler? Changed;

    /// <summary>The latest cards; null until <see cref="RefreshAsync"/> made some.</summary>
    public RediscoverPicks? Picks => Volatile.Read(ref _picks);

    /// <summary>How many albums' songs are stored (for tests).</summary>
    internal int StoredAlbums => _file?.Albums.Count ?? 0;

    /// <summary>
    /// Picks the day's cards from what is stored (shown at once), then asks
    /// for the songs of a few more favourite albums and picks again. A call
    /// while another runs does nothing. Call it off the interface thread.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            int generation;
            lock (_gate)
            {
                generation = _generation;
            }

            _home.LoadStored();
            var liked = _library.GetStoredLikedSongs() ?? await _library.GetAllLikedSongsAsync(cancellationToken).ConfigureAwait(false);
            var file = LoadFile(_library.Snapshot?.User?.Id);
            Publish(Pick(liked, file), generation);

            var now = _time.GetUtcNow();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);
            var used = file.FetchDay == today ? file.FetchesThatDay : 0;
            var albums = file.Albums.ToDictionary(a => a.Id, StringComparer.Ordinal);
            var wanted = RediscoverBuilder.AlbumsToFetch(liked, albums, now, RediscoverBuilder.AlbumFetchesPerDay - used);
            if (wanted.Count == 0)
            {
                return;
            }

            foreach (var id in wanted)
            {
                // Counted before asking, so a failing album is not asked for again today.
                used++;
                try
                {
                    albums[id] = await FetchAsync(id, now, cancellationToken).ConfigureAwait(false);
                }
                catch (SpotifyApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Gone from Spotify: remembered as empty, so it is not asked for again for weeks.
                    albums[id] = new RediscoverAlbum { Id = id, FetchedAt = now };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Not now (too many requests, offline or signed out): the rest waits for another day.
                    break;
                }
            }

            var updated = new RediscoverFile
            {
                UserId = file.UserId,
                FetchDay = today,
                FetchesThatDay = used,
                Albums = RediscoverBuilder.AlbumsToKeep(liked, albums.Values),
            };
            lock (_gate)
            {
                if (generation != _generation)
                {
                    // Signed out meanwhile.
                    return;
                }

                _file = updated;
                Save(updated);
            }

            Publish(Pick(liked, updated), generation);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <summary>Forgets the cards and the stored albums, in memory and on disk (signing out).</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _generation++;
            _file = null;
            Volatile.Write(ref _picks, null);
            Delete();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private RediscoverPicks Pick(IReadOnlyList<TrackInfo> liked, RediscoverFile file)
    {
        var content = _home.Content;
        var top = (content?.OnRepeat ?? [])
            .Concat(content?.Top.SelectMany(t => t.Songs) ?? [])
            .Select(t => t.Uri)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return RediscoverBuilder.Build(new RediscoverInput(
            liked,
            _home.History.Plays,
            top,
            file.Albums.ToDictionary(a => a.Id, StringComparer.Ordinal),
            _time.GetUtcNow(),
            _zone,
            CultureInfo.CurrentCulture));
    }

    private void Publish(RediscoverPicks picks, int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            Volatile.Write(ref _picks, picks);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<RediscoverAlbum> FetchAsync(string albumId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (album, tracks) = await _library.GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
        return new RediscoverAlbum
        {
            Id = albumId,
            Name = album.Name,
            Uri = album.Uri,
            Artists = string.Join(", ", album.Artists?.Select(a => a.Name) ?? []),
            ImageUrl = ImagePicker.Pick(album.Images, 300),
            ReleaseDate = album.ReleaseDate,
            FetchedAt = now,
            Tracks = tracks.ToList(),
        };
    }

    /// <summary>The stored albums, read once; another account's are dropped.</summary>
    private RediscoverFile LoadFile(string? userId)
    {
        lock (_gate)
        {
            _file ??= Read() ?? new RediscoverFile();
            if (userId is not null && _file.UserId != userId)
            {
                _file = new RediscoverFile { UserId = userId };
            }

            return _file;
        }
    }

    private RediscoverFile? Read()
    {
        if (_path is null)
        {
            return null;
        }

        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, RediscoverJsonContext.Default.RediscoverFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Save(RediscoverFile file)
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, file, RediscoverJsonContext.Default.RediscoverFile);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Asked for again another day.
        }
    }

    private void Delete()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

/// <summary>Source-generated JSON for Rediscover's file (no reflection, for Native AOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(RediscoverFile))]
internal sealed partial class RediscoverJsonContext : JsonSerializerContext;

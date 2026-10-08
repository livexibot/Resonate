using System.Net;
using System.Security.Cryptography;
using System.Text;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

public enum SmartSyncOutcome
{
    /// <summary>The playlist on Spotify now holds the smart playlist's songs.</summary>
    Updated,

    /// <summary>Nothing to send: the songs and the name are as sent last time.</summary>
    Unchanged,

    /// <summary>The playlist on Spotify was deleted; the link should be dropped.</summary>
    Gone,

    /// <summary>Spotify refused or could not be reached (<see cref="SmartSyncResult.Error"/>); try again later.</summary>
    Failed,
}

/// <summary>
/// What a sync did, for the caller to note on the smart playlist (the
/// playlist it was given is a copy, so a sync never races the interface).
/// </summary>
/// <param name="SpotifyId">The playlist on Spotify, also when it was just made and filling it failed.</param>
public sealed record SmartSyncResult(
    SmartSyncOutcome Outcome,
    string? SpotifyId = null,
    DateTimeOffset? LinkedAt = null,
    DateTimeOffset? SyncedAt = null,
    string? Signature = null,
    string? Name = null,
    Exception? Error = null);

/// <summary>
/// Loads what a smart playlist's rules look at, and keeps a real playlist on
/// Spotify in step with it ("Keep on Spotify"): made once, private, with a
/// description saying Resonate owns it, then its songs replaced whenever
/// they change and once a day (which also undoes edits made in Spotify's
/// apps). A playlist deleted on Spotify is noticed, not made again.
/// </summary>
public sealed class SmartPlaylistSync
{
    public const string Description = "Kept up to date by Resonate. Edits made in Spotify are replaced.";

    /// <summary>The playlist on Spotify is refreshed at most this often, besides changes to the rules.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly LibraryService _library;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public SmartPlaylistSync(LibraryService library, TimeProvider? time = null, TimeZoneInfo? zone = null)
    {
        _library = library;
        _time = time ?? TimeProvider.System;
        _zone = zone ?? TimeZoneInfo.Local;
    }

    /// <summary>The source's songs and those of each playlist a "not in" rule names, from the stored copies when current.</summary>
    public async Task<SmartInputs> LoadInputsAsync(SmartPlaylist playlist, CancellationToken cancellationToken)
    {
        var years = SmartPlaylistEvaluator.NeedsReleaseYears(playlist);
        var source = playlist.SourcePlaylistId is { } sourceId
            ? await _library.GetAllPlaylistTracksAsync(sourceId, null, years, cancellationToken).ConfigureAwait(false)
            : new FullTrackList(await _library.GetAllLikedSongsAsync(years, cancellationToken).ConfigureAwait(false), ItemsHidden: false);

        var others = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var id in playlist.Rules.Where(r => r.Kind == SmartRuleKind.NotInPlaylist).Select(r => r.Text).OfType<string>().Distinct())
        {
            try
            {
                var list = await _library.GetAllPlaylistTracksAsync(id, null, cancellationToken).ConfigureAwait(false);
                others[id] = list.Tracks.Select(t => t.Uri).OfType<string>().ToHashSet(StringComparer.Ordinal);
            }
            catch (SpotifyApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // A playlist deleted since excludes nothing.
            }
        }

        return new SmartInputs(source.Tracks, others, source.ItemsHidden);
    }

    public List<TrackInfo> Evaluate(SmartPlaylist playlist, SmartInputs inputs) =>
        SmartPlaylistEvaluator.Evaluate(playlist, inputs, _time.GetUtcNow(), _zone);

    /// <summary>The daily refresh is due (or the playlist on Spotify is still to be made).</summary>
    public bool IsDue(SmartPlaylist playlist) =>
        playlist.KeepOnSpotify && (playlist.SpotifyId is null || playlist.SyncedAt is not { } synced || _time.GetUtcNow() - synced >= Interval);

    /// <summary>
    /// The playlist on Spotify is missing from the user's playlists as read
    /// after it was made: the user deleted it (Spotify only unfollows it).
    /// </summary>
    public bool IsGone(SmartPlaylist playlist) =>
        playlist.SpotifyId is { } id
        && _library.Snapshot is { } snapshot
        && playlist.LinkedAt is { } linked
        && snapshot.SavedAt > linked
        && !snapshot.Playlists.Any(p => p.Id == id);

    /// <summary>
    /// Makes the playlist on Spotify if needed and gives it the smart
    /// playlist's songs. <paramref name="full"/> replaces them even when
    /// they look unchanged (the daily refresh, which undoes edits in Spotify).
    /// </summary>
    /// <param name="playlist">A copy, read only.</param>
    public async Task<SmartSyncResult> SyncAsync(SmartPlaylist playlist, bool full, CancellationToken cancellationToken)
    {
        if (IsGone(playlist))
        {
            return new SmartSyncResult(SmartSyncOutcome.Gone);
        }

        string? id = playlist.SpotifyId;
        var linkedAt = playlist.LinkedAt;
        var name = playlist.SyncedName;
        try
        {
            SmartInputs inputs;
            try
            {
                inputs = await LoadInputsAsync(playlist, cancellationToken).ConfigureAwait(false);
            }
            catch (SpotifyApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The source playlist is gone, not this one: the link stays and its songs are left alone.
                return new SmartSyncResult(SmartSyncOutcome.Failed, id, linkedAt, Name: name, Error: ex);
            }

            if (inputs.SourceHidden)
            {
                // Spotify no longer lists the source's songs; replacing would empty the playlist.
                return new SmartSyncResult(SmartSyncOutcome.Failed, id, linkedAt, Name: name, Error: new InvalidOperationException("The source playlist's songs can not be read."));
            }

            var uris = Evaluate(playlist, inputs).Select(t => t.Uri!).ToList();
            var signature = Signature(uris);
            if (id is null)
            {
                var created = await _library.CreatePlaylistAsync(playlist.Name, Description, cancellationToken).ConfigureAwait(false);
                id = created.Id;
                linkedAt = _time.GetUtcNow();
                name = playlist.Name;
            }
            else if (!full && signature == playlist.SyncedSignature && playlist.Name == playlist.SyncedName)
            {
                return new SmartSyncResult(SmartSyncOutcome.Unchanged, id, linkedAt);
            }

            if (playlist.Name != name)
            {
                await _library.RenamePlaylistAsync(id, playlist.Name, cancellationToken).ConfigureAwait(false);
                name = playlist.Name;
            }

            await _library.ReplacePlaylistItemsAsync(id, uris, cancellationToken).ConfigureAwait(false);
            return new SmartSyncResult(SmartSyncOutcome.Updated, id, linkedAt, _time.GetUtcNow(), signature, name);
        }
        catch (SpotifyApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && playlist.SpotifyId is not null)
        {
            return new SmartSyncResult(SmartSyncOutcome.Gone);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A playlist made before the failure stays linked, so the next try fills it instead of making another.
            return new SmartSyncResult(SmartSyncOutcome.Failed, id, linkedAt, Name: name, Error: ex);
        }
    }

    /// <summary>Notes a sync's result on the smart playlist itself. Returns false when its playlist on Spotify is gone (the link is dropped).</summary>
    public static bool Apply(SmartPlaylist playlist, SmartSyncResult result)
    {
        if (result.Outcome == SmartSyncOutcome.Gone)
        {
            playlist.KeepOnSpotify = false;
            playlist.SpotifyId = null;
            playlist.LinkedAt = null;
            playlist.SyncedAt = null;
            playlist.SyncedSignature = null;
            playlist.SyncedName = null;
            return false;
        }

        if (result.SpotifyId is { } id && playlist.SpotifyId is null)
        {
            playlist.SpotifyId = id;
            playlist.LinkedAt = result.LinkedAt;
            playlist.SyncedName = result.Name;
        }

        if (result.Outcome == SmartSyncOutcome.Updated)
        {
            playlist.SyncedAt = result.SyncedAt;
            playlist.SyncedSignature = result.Signature;
            playlist.SyncedName = result.Name;
        }

        return true;
    }

    /// <summary>Tells one list of songs from another, without keeping the list.</summary>
    public static string Signature(IReadOnlyList<string> uris) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', uris))), 0, 16);
}

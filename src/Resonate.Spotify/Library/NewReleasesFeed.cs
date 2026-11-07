using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonate.Spotify.Auth;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Library;

/// <summary>
/// New releases on Home (the owner's request, 7 November 2026): the newest
/// albums, EPs and singles of the artists the user follows, kept in a small
/// file so Home shows them at once and Spotify is asked as little as can be.
/// Spotify has no list of new releases for the followed artists (and gives
/// development apps no "new releases" at all since February 2026), so each
/// artist is asked about on their own:
/// <list type="bullet">
/// <item>the followed artists, 50 a request, about once a day;</item>
/// <item>each artist's newest releases, one request (and a second for singles
/// when a page of albums hides them), about once a day for artists with a
/// release in the past year and every four days for the rest;</item>
/// <item>at most <see cref="NewReleasesBuilder.MaxRequestsPerDay"/> requests a
/// day in all, one at a time with a pause between, counted as "New releases"
/// in Settings; an artist not reached today is asked about another day.</item>
/// </list>
/// A refusal or no connection ends the round and what was found is kept;
/// the round is tried again half an hour later at the soonest. Runs only
/// when Home asks. Thread-safe.
/// </summary>
public sealed class NewReleasesFeed
{
    private const int SaveEvery = 20;

    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);

    /// <summary>A sign-in Spotify refused for want of the permission shows "Sign in again" this long before it is tried again.</summary>
    private static readonly TimeSpan RefusedFor = TimeSpan.FromHours(6);

    private readonly ISpotifyWebApi _api;
    private readonly Func<string?> _userId;
    private readonly Func<bool> _mayReadFollows;
    private readonly string? _path;
    private readonly TimeSpan _pace;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly Lock _gate = new();
    private NewReleasesFile? _file;
    private NewReleasesView _view = NewReleasesView.Empty;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private DateTimeOffset _refusedUntil = DateTimeOffset.MinValue;
    private int _refreshing;
    private int _generation;

    /// <param name="userId">The signed-in account's ID, once known; another account's file is set aside.</param>
    /// <param name="mayReadFollows">False while the sign-in lacks the permission to read the followed artists (it predates it).</param>
    /// <param name="path">The file to keep the artists and releases in; null keeps them in memory only (demo mode, tests).</param>
    /// <param name="pace">The pause between two requests, so a round never comes as a burst.</param>
    public NewReleasesFeed(
        ISpotifyWebApi api,
        Func<string?> userId,
        Func<bool> mayReadFollows,
        string? path,
        TimeSpan pace,
        TimeProvider? time = null,
        TimeZoneInfo? zone = null)
    {
        _api = api;
        _userId = userId;
        _mayReadFollows = mayReadFollows;
        _path = path;
        _pace = pace;
        _time = time ?? TimeProvider.System;
        _zone = zone ?? TimeZoneInfo.Local;
    }

    /// <summary>Raised on any thread when what Home shows changed.</summary>
    public event EventHandler? Changed;

    /// <summary>What Home shows; empty until <see cref="RefreshAsync"/> read the stored file.</summary>
    public NewReleasesView View => Volatile.Read(ref _view);

    /// <summary>
    /// Shows what is stored at once, then asks Spotify for what is due: the
    /// followed artists, then the artists not asked about lately. A call
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
            NewReleasesFile file;
            lock (_gate)
            {
                generation = _generation;
                file = LoadFile(_userId());
            }

            var now = _time.GetUtcNow();
            var today = Today(now);
            Publish(file, today, generation);
            if (NeedsSignIn || Waiting(now))
            {
                return;
            }

            using (RequestCounter.CountAs(RequestKind.NewReleases))
            {
                await AskAsync(file, today, generation, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <summary>
    /// The user signed in again (granting the permission to read the
    /// followed artists): the next <see cref="RefreshAsync"/> asks at once.
    /// </summary>
    public void SignedInAgain()
    {
        lock (_gate)
        {
            _refusedUntil = DateTimeOffset.MinValue;
            _retryAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Forgets the artists and releases, in memory and on disk (signing out).
    /// Today's count of requests stays: the allowance belongs to the developer
    /// app, whoever signs in next.
    /// </summary>
    public void Forget()
    {
        lock (_gate)
        {
            _generation++;
            var old = _file ?? Read();
            _file = new NewReleasesFile { RequestDay = old?.RequestDay ?? default, RequestsThatDay = old?.RequestsThatDay ?? 0 };
            _refusedUntil = DateTimeOffset.MinValue;
            _retryAt = DateTimeOffset.MinValue;
            Volatile.Write(ref _view, NewReleasesView.Empty);
            Delete();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool NeedsSignIn
    {
        get
        {
            lock (_gate)
            {
                return _time.GetUtcNow() < _refusedUntil || !_mayReadFollows();
            }
        }
    }

    private async Task AskAsync(NewReleasesFile file, DateOnly today, int generation, CancellationToken cancellationToken)
    {
        var round = new Round(this, file, today, generation);
        try
        {
            if (file.FollowedReadAt is not { } readAt || _time.GetUtcNow() - readAt >= NewReleasesBuilder.FollowedEvery)
            {
                if (!await ReadFollowedAsync(round, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                Save(file, generation);
                Publish(file, today, generation);
            }

            var checks = 0;
            foreach (var artist in NewReleasesBuilder.Due(file.Artists, _time.GetUtcNow(), today))
            {
                // Two requests at most per artist: an artist is never left half asked about.
                if (generation != Volatile.Read(ref _generation) || !round.CanAsk(2))
                {
                    break;
                }

                if (!await CheckAsync(round, artist, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                if (++checks % SaveEvery == 0)
                {
                    Save(file, generation);
                }

                Publish(file, today, generation);
            }
        }
        finally
        {
            // The requests sent count against today's allowance, whatever came of them.
            Save(file, generation);
            Publish(file, today, generation);
        }
    }

    /// <summary>Reads the followed artists to the end; false when Spotify could not be asked (the round ends).</summary>
    private async Task<bool> ReadFollowedAsync(Round round, CancellationToken cancellationToken)
    {
        var followed = new List<Artist>();
        string? after = null;
        var complete = false;
        var page = 0;
        for (; page < NewReleasesBuilder.MaxFollowedPages && round.CanAsk(1); page++)
        {
            if (round.Generation != Volatile.Read(ref _generation))
            {
                // Signed out meanwhile.
                return false;
            }

            CursorPage<Artist> answer;
            try
            {
                var from = after;
                answer = await round.AskAsync(c => _api.GetFollowedArtistsAsync(from, NewReleasesBuilder.FollowedPageSize, c), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Failed(ex, round.Generation);
                return false;
            }

            followed.AddRange(answer.Items.OfType<Artist>());
            after = answer.Cursors?.After;
            if (answer.Next is null || string.IsNullOrEmpty(after) || answer.Items.Count == 0)
            {
                complete = true;
                break;
            }
        }

        var file = round.File;
        file.Artists = NewReleasesBuilder.Merge(file.Artists, followed, complete);

        // Read to its end, or as far as it is ever read (a thousand artists): not again until tomorrow.
        if (complete || page == NewReleasesBuilder.MaxFollowedPages)
        {
            file.FollowedReadAt = _time.GetUtcNow();
        }

        return true;
    }

    /// <summary>Asks for one artist's newest releases; false when Spotify could not be asked (the round ends).</summary>
    private async Task<bool> CheckAsync(Round round, FollowedArtist artist, CancellationToken cancellationToken)
    {
        try
        {
            var id = artist.Id;
            var page = await round.AskAsync(
                c => _api.GetArtistReleasesAsync(id, ReleaseGroups.Albums | ReleaseGroups.Singles, 0, NewReleasesBuilder.ReleasesPageSize, c),
                cancellationToken).ConfigureAwait(false);
            IEnumerable<SimplifiedAlbum?> albums = page.Items;
            if (NewReleasesBuilder.NeedsSingles(page, round.Today))
            {
                var singles = await round.AskAsync(
                    c => _api.GetArtistReleasesAsync(id, ReleaseGroups.Singles, 0, NewReleasesBuilder.ReleasesPageSize, c),
                    cancellationToken).ConfigureAwait(false);
                albums = albums.Concat(singles.Items);
            }

            var (recent, latest) = NewReleasesBuilder.Read(albums, round.Today);
            artist.Releases = recent;
            artist.Latest = latest ?? artist.Latest;
            artist.CheckedAt = _time.GetUtcNow();
            return true;
        }
        catch (SpotifyApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            // Gone from Spotify (or an ID it no longer takes): nothing new, and not asked about again for days.
            artist.Releases = [];
            artist.CheckedAt = _time.GetUtcNow();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (ex is JsonException or SpotifyApiException { StatusCode: >= HttpStatusCode.InternalServerError })
            {
                // Spotify stumbled on this artist: the next round starts with the others, so one artist never holds them all back.
                artist.CheckedAt = _time.GetUtcNow();
            }

            Failed(ex, round.Generation);
            return false;
        }
    }

    /// <summary>
    /// Spotify could not be asked: a sign-in without the permission to read
    /// the followed artists shows "Sign in again"; anything else (no
    /// connection, too many requests, signed out) waits a while. A round
    /// from before a sign-out holds nothing back for the next account.
    /// </summary>
    private void Failed(Exception ex, int generation)
    {
        var refused = ex is SpotifyApiException { StatusCode: HttpStatusCode.Forbidden } forbidden
            && forbidden.Message.Contains("scope", StringComparison.OrdinalIgnoreCase);
        var wait = ex is SpotifyApiException { RetryAfter: { } asked } && asked > RetryAfterFailure ? asked : RetryAfterFailure;
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            var now = _time.GetUtcNow();
            if (refused)
            {
                _refusedUntil = now + RefusedFor;
            }

            _retryAt = now + wait;
        }
    }

    /// <summary>Whether a failure a short while ago still holds the next round back.</summary>
    private bool Waiting(DateTimeOffset now)
    {
        lock (_gate)
        {
            return now < _retryAt;
        }
    }

    private void Publish(NewReleasesFile file, DateOnly today, int generation)
    {
        var view = new NewReleasesView
        {
            Releases = NewReleasesBuilder.Pick(file.Artists, today),
            NeedsSignIn = NeedsSignIn,
        };
        lock (_gate)
        {
            if (generation != _generation || view.SameAs(_view))
            {
                return;
            }

            Volatile.Write(ref _view, view);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);

    /// <summary>The stored artists, read once; another account's are set aside.</summary>
    private NewReleasesFile LoadFile(string? userId)
    {
        _file ??= Read() ?? new NewReleasesFile();
        if (userId is not null)
        {
            if (_file.UserId is null)
            {
                _file.UserId = userId;
            }
            else if (_file.UserId != userId)
            {
                // Today's count of requests stays: the allowance belongs to the developer app.
                _file = new NewReleasesFile { UserId = userId, RequestDay = _file.RequestDay, RequestsThatDay = _file.RequestsThatDay };
            }
        }

        return _file;
    }

    private NewReleasesFile? Read()
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
            return JsonSerializer.Deserialize(stream, NewReleasesJsonContext.Default.NewReleasesFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Save(NewReleasesFile file, int generation)
    {
        lock (_gate)
        {
            if (_path is null || generation != _generation)
            {
                // Signed out meanwhile: nothing of the old account is written back.
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                using (var stream = File.Create(temporary))
                {
                    JsonSerializer.Serialize(stream, file, NewReleasesJsonContext.Default.NewReleasesFile);
                }

                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Asked for again another time.
            }
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

    /// <summary>One round of requests: keeps today's count in the file and the pause between requests.</summary>
    private sealed class Round(NewReleasesFeed feed, NewReleasesFile file, DateOnly today, int generation)
    {
        private bool _asked;

        public NewReleasesFile File => file;

        public DateOnly Today => today;

        /// <summary>The sign-in the round belongs to (see <see cref="Forget"/>).</summary>
        public int Generation => generation;

        /// <summary>Whether <paramref name="requests"/> more fit in today's allowance.</summary>
        public bool CanAsk(int requests)
        {
            if (file.RequestDay != today)
            {
                file.RequestDay = today;
                file.RequestsThatDay = 0;
            }

            return file.RequestsThatDay + requests <= NewReleasesBuilder.MaxRequestsPerDay;
        }

        /// <summary>Sends one request after the pause, counted before it goes (a failing one counts too, as Spotify counts it).</summary>
        public async Task<T> AskAsync<T>(Func<CancellationToken, Task<T>> ask, CancellationToken cancellationToken)
        {
            if (_asked && feed._pace > TimeSpan.Zero)
            {
                await Task.Delay(feed._pace, feed._time, cancellationToken).ConfigureAwait(false);
            }

            _asked = true;
            CanAsk(0);
            file.RequestsThatDay++;
            return await ask(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Source-generated JSON for New releases' file (no reflection, for Native AOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(NewReleasesFile))]
internal sealed partial class NewReleasesJsonContext : JsonSerializerContext;

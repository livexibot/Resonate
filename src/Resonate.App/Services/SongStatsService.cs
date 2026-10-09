using Microsoft.UI.Dispatching;
using Resonate.App.ViewModels;
using Resonate.Spotify.Library;

namespace Resonate.App.Services;

/// <summary>
/// Song stats (BPM, key, loudness, energy) for song lists, only while the
/// user has them switched on (Settings, Layout, Song lists). A row asks when
/// it is first drawn; asks are gathered for a moment and sent to ReccoBeats
/// <see cref="ReccoBeatsClient.MaxBatch"/> at a time, the newest first (the
/// songs on screen), and every answer is kept on this PC
/// (<see cref="SongStatsCache"/>), so scrolling back costs nothing. In demo
/// mode the stats are made up and nothing is sent. Call on the interface thread.
/// </summary>
public sealed class SongStatsService
{
    private static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(250);

    private readonly ISongStatsSource? _source;
    private readonly SongStatsCache _cache;
    private readonly AppSettings _settings;
    private readonly DispatcherQueueTimer _timer;
    private readonly List<string> _pending = [];
    private readonly Dictionary<string, List<WeakReference<TrackRow>>> _waiting = new(StringComparer.Ordinal);
    private bool _asking;
    private bool _dirty;

    /// <param name="source">ReccoBeats; null in demo mode, where stats are made up.</param>
    public SongStatsService(ISongStatsSource? source, SongStatsCache cache, AppSettings settings)
    {
        _source = source;
        _cache = cache;
        _settings = settings;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = Gather;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => _ = AskAsync();
    }

    /// <summary>The user has song stats switched on.</summary>
    public bool IsOn => _settings.SongStats;

    /// <summary>
    /// The song's stats when known now; otherwise null, and
    /// <paramref name="row"/> hears <see cref="TrackRow.ShowStats"/> once they come.
    /// </summary>
    public SongStats? Get(string? trackId, TrackRow row)
    {
        if (!IsOn || trackId is null)
        {
            return null;
        }

        if (_source is null)
        {
            return MadeUp(trackId);
        }

        if (!ReccoBeatsClient.IsSpotifyId(trackId))
        {
            return null;
        }

        if (_cache.TryGet(trackId, DateTimeOffset.UtcNow, out var known))
        {
            return known;
        }

        if (!_waiting.TryGetValue(trackId, out var rows))
        {
            _waiting[trackId] = rows = [];
            _pending.Add(trackId);
        }

        rows.Add(new WeakReference<TrackRow>(row));
        if (!_asking)
        {
            _timer.Stop();
            _timer.Start();
        }

        return null;
    }

    private async Task AskAsync()
    {
        if (_source is null || _pending.Count == 0)
        {
            return;
        }

        _asking = true;
        try
        {
            while (_pending.Count > 0 && IsOn)
            {
                // The newest asks are the songs on screen now.
                var count = Math.Min(ReccoBeatsClient.MaxBatch, _pending.Count);
                var batch = _pending.GetRange(_pending.Count - count, count);
                _pending.RemoveRange(_pending.Count - count, count);

                IReadOnlyDictionary<string, SongStats> found;
                try
                {
                    found = await Task.Run(() => _source.GetAsync(batch, CancellationToken.None));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    // Offline or refused: these songs are asked about again when next shown.
                    foreach (var id in batch)
                    {
                        _waiting.Remove(id);
                    }

                    break;
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var id in batch)
                {
                    var stats = found.GetValueOrDefault(id);
                    _cache.Store(id, stats, now);
                    _dirty = true;
                    if (_waiting.Remove(id, out var rows))
                    {
                        foreach (var reference in rows)
                        {
                            if (reference.TryGetTarget(out var row))
                            {
                                row.ShowStats(stats);
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            _asking = false;
        }

        if (_dirty)
        {
            // On this thread, like every change to the cache: a small file, written once per round.
            _dirty = false;
            _cache.Save();
        }
    }

    /// <summary>Steady made-up stats for demo songs, from their IDs.</summary>
    private static SongStats MadeUp(string id)
    {
        var hash = (uint)id.Aggregate(17, (h, c) => (h * 31) + c);
        return new SongStats(80 + (hash % 90), (int)(hash % 12), (int)((hash >> 4) % 2), -3 - ((hash >> 8) % 90 / 10.0), (40 + ((hash >> 12) % 60)) / 100.0);
    }
}

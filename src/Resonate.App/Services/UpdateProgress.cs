using System.Diagnostics;
using System.Globalization;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Resonate.App.Services;

/// <summary>A new version on its way, as the progress bars show it.</summary>
/// <param name="Version">The version being downloaded.</param>
/// <param name="Received">Bytes downloaded so far.</param>
/// <param name="Total">Bytes to download in all.</param>
/// <param name="BytesPerSecond">The recent download speed; 0 until it is known.</param>
/// <param name="Preparing">Everything is downloaded and Velopack is checking and assembling it.</param>
public sealed record UpdateProgress(string Version, long Received, long Total, double BytesPerSecond, bool Preparing)
{
    private const double Megabyte = 1024 * 1024;

    /// <summary>From 0 to 1.</summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Received / Total, 0, 1) : 0;

    /// <summary>The time left at the recent speed, or null while the speed is not known.</summary>
    public TimeSpan? Remaining => !Preparing && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Max(0, Total - Received) / BytesPerSecond)
        : null;

    /// <summary>"12.4 of 38.0 MB · 3.2 MB/s · 8 s left", or what happens after the download.</summary>
    public string Describe()
    {
        if (Preparing)
        {
            return "Downloaded. Getting it ready…";
        }

        var megabytes = Total >= Megabyte;
        var text = $"{Amount(Received, megabytes)} of {Amount(Total, megabytes)} {Unit(megabytes)}";
        if (BytesPerSecond > 0)
        {
            var fast = BytesPerSecond >= Megabyte;
            text += $" · {Amount((long)BytesPerSecond, fast)} {Unit(fast)}/s";
        }

        if (Remaining is { } left)
        {
            text += $" · {TimeLeft(left)}";
        }

        return text;
    }

    /// <summary>In MB with one decimal, or in whole KB.</summary>
    private static string Amount(long bytes, bool megabytes) => megabytes
        ? (bytes / Megabyte).ToString("0.0", CultureInfo.CurrentCulture)
        : Math.Round(bytes / 1024.0).ToString("N0", CultureInfo.CurrentCulture);

    private static string Unit(bool megabytes) => megabytes ? "MB" : "KB";

    private static string TimeLeft(TimeSpan left) => left.TotalSeconds switch
    {
        < 60 => $"{Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} s left",
        < 3600 => $"{(int)Math.Ceiling(left.TotalMinutes)} min left",
        _ => $"{(int)left.TotalHours} h {left.Minutes} min left",
    };
}

/// <summary>
/// Turns Velopack's per-file percentages into bytes, speed and time left.
/// Velopack downloads either the small "delta" files from the installed
/// version (then patches them together) or, when there are none, too many,
/// or patching fails, the whole new version. It reports each file from 0
/// to 100, so the meter follows which files arrive.
/// </summary>
internal sealed class UpdateDownloadMeter
{
    // Velopack's default: more deltas than this, and it downloads the whole version.
    private const int MaxDeltas = 10;

    private static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SpeedSettle = TimeSpan.FromSeconds(0.5);

    private readonly object _gate = new();
    private readonly UpdateInfo _update;
    private readonly string _version;
    private readonly Dictionary<string, long> _received = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(long Time, long Bytes)> _samples = new();
    private Dictionary<string, long> _expected;

    public UpdateDownloadMeter(UpdateInfo update)
    {
        _update = update;
        _version = update.TargetFullRelease.Version.ToString();
        var deltas = update.DeltasToTarget;
        var deltaBytes = deltas.Sum(d => d.Size);
        _expected = update.BaseRelease?.FileName is not null && deltas.Length is > 0 and <= MaxDeltas && deltaBytes <= update.TargetFullRelease.Size
            ? deltas.ToDictionary(d => d.FileName, d => d.Size, StringComparer.OrdinalIgnoreCase)
            : Whole(update);
    }

    /// <summary>Nothing downloaded yet.</summary>
    public UpdateProgress Start() => new(_version, 0, _expected.Values.Sum(), 0, false);

    /// <summary>One file at <paramref name="percent"/>; returns the download as a whole.</summary>
    public UpdateProgress Report(VelopackAsset file, int percent, long timestamp)
    {
        lock (_gate)
        {
            if (!_expected.ContainsKey(file.FileName))
            {
                // Velopack gave up on the deltas and downloads the whole version instead.
                _expected = Whole(_update);
                _received.Clear();
                _samples.Clear();
            }

            var size = _expected.GetValueOrDefault(file.FileName, file.Size);
            _received[file.FileName] = size * Math.Clamp(percent, 0, 100) / 100;
            var received = _received.Values.Sum();
            var total = _expected.Values.Sum();

            _samples.Enqueue((timestamp, received));
            while (_samples.Count > 2 && Stopwatch.GetElapsedTime(_samples.Peek().Time, timestamp) > SpeedWindow)
            {
                _samples.Dequeue();
            }

            var (firstTime, firstBytes) = _samples.Peek();
            var span = Stopwatch.GetElapsedTime(firstTime, timestamp);
            var speed = span >= SpeedSettle ? Math.Max(0, received - firstBytes) / span.TotalSeconds : 0;
            var done = _expected.Keys.All(name => _received.TryGetValue(name, out var bytes) && bytes >= _expected[name]);
            return new UpdateProgress(_version, received, total, speed, done);
        }
    }

    private static Dictionary<string, long> Whole(UpdateInfo update) =>
        new(StringComparer.OrdinalIgnoreCase) { [update.TargetFullRelease.FileName] = update.TargetFullRelease.Size };
}

/// <summary>Passes everything through to the real source, telling the update service how each file's download goes.</summary>
internal sealed class ProgressReportingSource(IUpdateSource inner, Action<VelopackAsset, int> report) : IUpdateSource
{
    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) =>
        inner.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);

    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default) =>
        inner.DownloadReleaseEntry(
            logger,
            releaseEntry,
            localFile,
            percent =>
            {
                progress(percent);
                report(releaseEntry, percent);
            },
            cancelToken);
}

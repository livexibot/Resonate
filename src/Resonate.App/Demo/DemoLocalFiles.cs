using System.Diagnostics;
using Resonate.Spotify.Audio;
using Resonate.Spotify.Library;
using Resonate.Spotify.LocalFiles;

namespace Resonate.App.Demo;

/// <summary>Made-up music files for "--demo" (nothing is read from the disk), and an engine that only pretends to play them.</summary>
public static class DemoLocalFiles
{
    private static readonly (string Title, string Artist, string Album, int Seconds, int DaysAgo)[] Songs =
    [
        ("Kitchen Radio (demo)", "Juniper Lane", "Home Recordings", 184, 0),
        ("Rain on the Skylight", "Juniper Lane", "Home Recordings", 211, 0),
        ("Bus Stop Melody", "Mira Sol", "Voice Memos", 97, 2),
        ("Garage Session 3", "Velvet Static", "Live in the Garage", 342, 5),
        ("Garage Session 4", "Velvet Static", "Live in the Garage", 298, 5),
        ("Lullaby for Ada", "The Quiet Hours", "Lullabies", 165, 12),
        ("Wedding March (piano)", "Echo Harbor", "Family Videos", 223, 40),
        ("Old Cassette, Side A", "Saltwater Radio", "Cassette Transfers", 1265, 90),
    ];

    public static IReadOnlyList<TrackInfo> Tracks(DateTimeOffset now) =>
        Songs.Select((song, i) => new LocalFile
        {
            Path = $@"C:\Users\Demo\Music\{song.Album}\{i + 1:00} {song.Title}.flac",
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            TrackNumber = i + 1,
            DurationMs = song.Seconds * 1000L,
            AddedAt = now.AddDays(-song.DaysAgo).AddMinutes(-i * 7),
        }.ToTrackInfo()).ToList();
}

/// <summary>Plays nothing: keeps time like a player would, so the demo's player bar moves.</summary>
public sealed class DemoLocalAudio : ILocalAudioEngine
{
    private readonly Stopwatch _clock = new();
    private TimeSpan _start;

    public event EventHandler<LocalTrackEnded>? TrackEnded
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? Failed
    {
        add { }
        remove { }
    }

    public TimeSpan Position => _start + _clock.Elapsed;

    public TimeSpan Duration => TimeSpan.Zero;

    public Task<TimeSpan> OpenAsync(string path, TimeSpan position, bool play, CancellationToken cancellationToken)
    {
        _start = position;
        _clock.Reset();
        if (play)
        {
            _clock.Start();
        }

        return Task.FromResult(TimeSpan.Zero);
    }

    public Task PlayAsync()
    {
        _clock.Start();
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        _clock.Stop();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        _start = position;
        if (_clock.IsRunning)
        {
            _clock.Restart();
        }
        else
        {
            _clock.Reset();
        }

        return Task.CompletedTask;
    }

    public void SetNext(string? path)
    {
    }

    public void SetLooping(bool looping)
    {
    }

    public void SetVolume(double volume)
    {
    }

    public void SetEqualizer(EqualizerSettings? settings)
    {
    }

    public void Dispose() => _clock.Stop();
}

using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Library;
using Resonate.Spotify.Playback;
using Resonate.Spotify.Tests.Fakes;
using Resonate.Spotify.WebApi;

namespace Resonate.Spotify.Tests;

/// <summary>Where you left off: the list around the song that played last goes on after a restart.</summary>
public sealed class KeptListTests : IDisposable
{
    private const string Playlist = "spotify:playlist:p";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-18T20:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly Random _random = new(7);
    private readonly List<PlayerController> _players = [];

    public void Dispose()
    {
        foreach (var player in _players)
        {
            player.Dispose();
        }
    }

    [Fact]
    public async Task After_a_restart_play_sends_the_kept_list_from_the_song_at_its_place()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var (player, web) = await RestartAsync(last.At(65_000), kept);

        Assert.Equal("Song 7", player.State.Title);
        Assert.False(player.State.IsPlaying);
        Assert.Equal("Mix", player.State.SourceName);
        Assert.Equal(22, player.UpNext!.Upcoming.Count);
        Assert.Equal("Song 8", player.UpNext.Upcoming[0].Title);

        await player.PlayAsync();

        // Spotify's shuffle was switched off last time, so nothing asks again.
        Assert.Equal(["play@here"], web.Commands);
        var body = Assert.Single(web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Equal(Uris(Songs(30)), body.Uris);
        Assert.Equal(7, body.Offset!.Position);
        Assert.Equal(65_000, body.PositionMs);
        Assert.True(player.State.IsPlaying);
    }

    [Fact]
    public async Task Next_before_play_starts_the_next_song_of_the_kept_list()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var (player, web) = await RestartAsync(last.At(65_000), kept);

        await player.NextAsync();

        Assert.Equal("Song 8", player.State.Title);
        Assert.True(player.State.IsPlaying);
        var body = Assert.Single(web.PlayBodies)!;
        Assert.Equal(Uris(Songs(30)), body.Uris);
        Assert.Equal(8, body.Offset!.Position);
        Assert.Null(body.PositionMs);
        Assert.Equal("Song 9", player.UpNext!.Upcoming[0].Title);
    }

    [Fact]
    public async Task Previous_before_play_goes_back_a_song_at_its_start_and_starts_the_song_again_later_in()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var (early, earlyWeb) = await RestartAsync(last.At(1_000), kept);
        var (later, laterWeb) = await RestartAsync(last.At(65_000), kept);

        await early.PreviousAsync();
        await later.PreviousAsync();

        Assert.Equal("Song 6", early.State.Title);
        Assert.Equal(6, Assert.Single(earlyWeb.PlayBodies)!.Offset!.Position);
        Assert.Equal("Song 7", later.State.Title);
        Assert.Equal(TimeSpan.Zero, later.State.Position);
        var again = Assert.Single(laterWeb.PlayBodies)!;
        Assert.Equal(7, again.Offset!.Position);
        Assert.Null(again.PositionMs);
    }

    [Fact]
    public async Task A_long_list_keeps_a_part_around_the_song_and_Spotify_gets_a_window_with_songs_before_it()
    {
        var songs = Songs(1000);
        var (kept, last) = await KeepAsync(new PlayRequest(songs, 500, null, "Big") { Shuffle = false });

        Assert.False(kept.Complete);
        Assert.Equal("Song 490", kept.Songs[0].Title);
        Assert.Equal(10, kept.Index);
        Assert.Equal("Song 800", kept.Songs[^1].Title);

        var (player, web) = await RestartAsync(last.At(30_000), kept);
        await player.PlayAsync();

        var body = Assert.Single(web.PlayBodies)!;
        Assert.Equal(Uris(songs.Skip(490).Take(100)), body.Uris);
        Assert.Equal(10, body.Offset!.Position);
        Assert.Equal(30_000, body.PositionMs);
    }

    [Fact]
    public async Task A_shuffled_list_keeps_its_order_after_a_restart()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 4, Playlist, "Mix") { Shuffle = true });
        var order = Uris(kept.Songs);
        var (player, web) = await RestartAsync(last.At(5_000), kept);

        Assert.True(player.State.Shuffle);
        Assert.Equal(order.Skip(1), player.UpNext!.Upcoming.Select(s => s.Uri));

        await player.PlayAsync();

        var body = Assert.Single(web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Equal(order, body.Uris);
        Assert.Equal(5_000, body.PositionMs);
    }

    [Fact]
    public async Task Shuffle_switched_before_play_plans_the_kept_songs_again_and_asks_Spotify_nothing_until_play()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var (player, web) = await RestartAsync(last.At(65_000), kept);

        await player.SetShuffleAsync(true);

        Assert.True(player.State.Shuffle);
        Assert.Empty(web.Commands);
        Assert.Equal(Uris(Songs(30)).Order(), Uris([player.UpNext!.Current!, .. player.UpNext.Upcoming]).Order());

        await player.PlayAsync();

        var body = Assert.Single(web.PlayBodies)!;
        Assert.Equal("spotify:track:7", body.Uris![0]);
        Assert.NotEqual(Uris(Songs(30)), body.Uris);
        Assert.Equal(65_000, body.PositionMs);
    }

    [Fact]
    public async Task A_list_played_in_its_playlist_plays_on_in_it_after_a_restart()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, Playlist, "My list") { Shuffle = false });
        var (player, web) = await RestartAsync(last.At(65_000), kept);

        Assert.Equal("Song 8", player.UpNext!.Upcoming[0].Title);
        await player.PlayAsync();

        var body = Assert.Single(web.PlayBodies)!;
        Assert.Equal(Playlist, body.ContextUri);
        Assert.Equal("spotify:track:7", body.Offset!.Uri);
        Assert.Equal(65_000, body.PositionMs);
    }

    [Fact]
    public async Task A_playlist_Spotify_will_not_start_plays_as_the_kept_songs_from_the_same_place()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, Playlist, "My list") { Shuffle = false });
        var (player, web) = await RestartAsync(last.At(65_000), kept);
        web.FailContextPlayback = new SpotifyApiException(HttpStatusCode.Forbidden, null, "no");

        await player.PlayAsync();

        var body = Assert.Single(web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Equal("spotify:track:7", body.Uris![body.Offset?.Position ?? 0]);
        Assert.Equal(65_000, body.PositionMs);
    }

    [Fact]
    public async Task When_Spotify_does_not_take_it_the_next_play_sends_the_kept_list_again()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var (player, web) = await RestartAsync(last.At(65_000), kept);
        web.FailNextCommand = new HttpRequestException("offline");

        await player.PlayAsync();
        Assert.False(player.State.IsPlaying);
        await player.PlayAsync();

        Assert.Equal(2, web.PlayBodies.Count);
        var body = web.PlayBodies[1]!;
        Assert.Equal(Uris(Songs(30)), body.Uris);
        Assert.Equal(7, body.Offset!.Position);
        Assert.Equal(65_000, body.PositionMs);
    }

    [Fact]
    public async Task The_kept_list_goes_when_Spotify_says_another_song_plays()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var local = new FakeLocalChannel();
        var (player, _) = await RestartAsync(last.At(65_000), kept, local);

        local.Report(new LocalMediaSnapshot
        {
            HasSession = true,
            Title = "Something else",
            Artist = "Someone",
            IsPlaying = true,
            Duration = TimeSpan.FromSeconds(180),
            PositionUpdatedAt = _time.GetUtcNow(),
        });

        Assert.Equal("Something else", player.State.Title);
        Assert.Null(player.UpNext);
        Assert.Null(player.State.SourceName);
    }

    [Fact]
    public async Task A_kept_list_of_another_song_is_not_picked_up()
    {
        var (kept, last) = await KeepAsync(new PlayRequest(Songs(30), 7, null, "Mix") { Shuffle = false });
        var other = last.At(0);
        other.TrackUri = "spotify:track:12";
        var (player, web) = await RestartAsync(other, kept);

        Assert.Null(player.UpNext);
        await player.PlayAsync();
        Assert.Equal(["spotify:track:12"], Assert.Single(web.PlayBodies)!.Uris);
    }

    [Fact]
    public async Task Spotifys_DJ_is_never_asked_for_again()
    {
        var (player, web) = await RestartAsync(
            new LastPlayed { TrackUri = "spotify:track:kept", ContextUri = SpotifyDj.ContextUri, Title = "Kept", DurationMs = 200_000, PositionMs = 9_000 },
            kept: null);

        await player.PlayAsync();

        var body = Assert.Single(web.PlayBodies)!;
        Assert.Null(body.ContextUri);
        Assert.Equal(["spotify:track:kept"], body.Uris);
    }

    [Fact]
    public async Task The_kept_list_is_written_and_read_back()
    {
        var (kept, _) = await KeepAsync(new PlayRequest(Songs(30), 7, Playlist, "My list") { Shuffle = false });
        var folder = Path.Combine(Path.GetTempPath(), "resonate-kept-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = new KeptListFile(Path.Combine(folder, "last-list.json"));
            file.SaveNow(kept);
            var read = file.Load();

            Assert.NotNull(read);
            Assert.True(KeptList.Same(kept, read));
            Assert.Equal(kept.Songs[7] with { ArtistRefs = [] }, read.Songs[7] with { ArtistRefs = [] });

            file.SaveNow(null);
            Assert.Null(file.Load());
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>Plays <paramref name="request"/> and keeps what Resonate would keep as it closes.</summary>
    private async Task<(KeptList Kept, LastPlayed Last)> KeepAsync(PlayRequest request)
    {
        var (player, _, _) = Make();
        await player.StartAsync(TestContext.Current.CancellationToken);
        await player.PlayAsync(request);
        var kept = player.KeepList();
        Assert.NotNull(kept);
        return (kept, LastPlayed.From(player.State, _time.GetUtcNow())!);
    }

    /// <summary>A new run of Resonate, nothing playing anywhere, showing <paramref name="last"/> with <paramref name="kept"/> picked up.</summary>
    private async Task<(PlayerController Player, FakeWebApi Web)> RestartAsync(LastPlayed last, KeptList? kept, FakeLocalChannel? local = null)
    {
        var (player, web, _) = Make(local);
        await player.StartAsync(TestContext.Current.CancellationToken);
        player.ShowLastPlayed(last);
        if (kept is not null)
        {
            player.RestoreList(kept);
        }

        return (player, web);
    }

    private (PlayerController Player, FakeWebApi Web, FakeLocalChannel Local) Make(FakeLocalChannel? local = null)
    {
        local ??= new FakeLocalChannel();
        var web = new FakeWebApi();
        var player = new PlayerController(local, local, web, new LocalDeviceResolver(web, "MY-PC", _time), launcher: null, _time, max => _random.Next(max));
        _players.Add(player);
        return (player, web, local);
    }

    private static TrackInfo Song(int i) =>
        new($"spotify:track:{i}", $"Song {i}", "Band", "Record", null, TimeSpan.FromSeconds(200), null, null, IsExplicit: false, IsPlayable: true)
        {
            Position = i,
        };

    private static List<TrackInfo> Songs(int count) => Enumerable.Range(0, count).Select(Song).ToList();

    private static List<string> Uris(IEnumerable<TrackInfo> tracks) => tracks.Select(t => t.Uri!).ToList();
}

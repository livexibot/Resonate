using Resonate.App.Services;
using Resonate.Spotify.Library;

namespace Resonate.App.Pages.Lists;

/// <summary>Music files on this computer, from the folders chosen in Settings. Resonate plays them itself.</summary>
public sealed class LocalFilesSource : TrackListSource
{
    public const string ListKey = "local";

    public LocalFilesSource(AppServices services) => _ = services;

    public override string Key => ListKey;

    public override string EmptyText => "Music files from your folders appear here. Choose the folders in Settings, under Local Files.";

    public override string OwnOrderName => "Recently added";

    public override ListHeader CachedHeader => new("COLLECTION", "Local Files", null, "On this computer", null, "Local Files", Glyph: "");

    public override Task<FullTrackList> LoadAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new FullTrackList([], ItemsHidden: false));
}

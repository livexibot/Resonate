using Resonate.Spotify.History;

namespace Resonate.App.ViewModels;

/// <summary>One card in Rediscover's row on Home (a built-in plugin; the card itself is built in HomePage.Rediscover.cs).</summary>
public sealed partial class RediscoverTile(RediscoverCard card)
{
    public RediscoverCard Card { get; } = card;

    /// <summary>What screen readers say for the card.</summary>
    public override string ToString() => $"{Card.Label}: {Card.Title}, {Card.Subtitle}, {Card.Note}";
}

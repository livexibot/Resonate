using System.Text;

namespace Resonate.Spotify.Auth;

/// <summary>
/// An access token and the refresh token that renews it. <see cref="ToString"/>
/// never includes either token, so a token can not end up in a log by accident.
/// </summary>
public sealed record SpotifyToken(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string Scope)
{
    /// <summary>Refresh this long before the token actually expires.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    public bool NeedsRefresh(DateTimeOffset now) => now >= ExpiresAt - RefreshMargin;

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresAt = ").Append(ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(", Scope = ").Append(Scope);
        return true;
    }
}

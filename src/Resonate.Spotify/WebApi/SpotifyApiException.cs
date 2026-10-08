using System.Net;

namespace Resonate.Spotify.WebApi;

/// <summary>The Web API refused a request.</summary>
public sealed class SpotifyApiException : Exception
{
    public SpotifyApiException()
    {
    }

    public SpotifyApiException(string message)
        : base(message)
    {
    }

    public SpotifyApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SpotifyApiException(HttpStatusCode statusCode, string? reason, string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = statusCode;
        Reason = reason;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Spotify's reason code, such as <c>PREMIUM_REQUIRED</c> or <c>NO_ACTIVE_DEVICE</c>.</summary>
    public string? Reason { get; }

    /// <summary>How long Spotify asked us to wait (rate limit or quota).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Resonate's own reason, with "Spotify Web API only": no Spotify device
    /// is online, and Resonate closes the Spotify app on this computer in that mode.
    /// </summary>
    public const string NoDeviceOnlineReason = "NO_DEVICE_ONLINE";

    public bool IsPremiumRequired => Reason == "PREMIUM_REQUIRED";

    public bool IsNoActiveDevice => Reason == "NO_ACTIVE_DEVICE" || StatusCode == HttpStatusCode.NotFound;

    public bool IsQuotaExceeded => Reason == "QUOTA_EXCEEDED";

    /// <summary>A sentence the interface can show as it is.</summary>
    public string UserMessage => this switch
    {
        { IsPremiumRequired: true } => "Spotify only allows playback control with a Premium account.",
        { IsQuotaExceeded: true } => "Your Spotify developer app has used up its request allowance for now. Try again later.",
        { StatusCode: HttpStatusCode.TooManyRequests } => "Spotify asked Resonate to slow down. Try again in a moment.",
        { StatusCode: HttpStatusCode.Forbidden } => "Spotify does not allow this for your account or this app.",
        { Reason: NoDeviceOnlineReason } => "No Spotify device is online. Open Spotify on your phone or a speaker and pick it with the devices button, or switch to Windows media controls in Settings for sound on this PC.",
        { IsNoActiveDevice: true } => "The Spotify app on this computer is not reachable. Make sure it is installed and signed in (Settings, Show the Spotify app).",
        _ => "Spotify could not do that right now.",
    };
}

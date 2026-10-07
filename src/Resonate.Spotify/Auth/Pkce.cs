using System.Security.Cryptography;
using System.Text;

namespace Resonate.Spotify.Auth;

/// <summary>
/// Proof Key for Code Exchange (RFC 7636). Lets a desktop app sign in without
/// a client secret: the app proves it started the sign-in by revealing the
/// verifier whose hash it sent first.
/// </summary>
public static class Pkce
{
    /// <summary>A random verifier, 86 characters of base64url (the RFC allows 43 to 128).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));

    /// <summary>The S256 challenge for <paramref name="verifier"/>.</summary>
    public static string CreateChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>A random value that ties the browser's answer to this sign-in attempt.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    internal static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

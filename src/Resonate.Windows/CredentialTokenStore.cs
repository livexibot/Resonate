using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Resonate.Spotify.Auth;
using Resonate.Windows.Interop;

namespace Resonate.Windows;

/// <summary>
/// Keeps the Spotify sign-in in the Windows Credential Manager, protected by
/// the user's Windows account, never in a plain file.
/// </summary>
public sealed class CredentialTokenStore : ITokenStore
{
    /// <summary>The Credential Manager refuses secrets larger than this.</summary>
    private const int MaxBlobBytes = 5 * 512;

    private const string FormatVersion = "resonate-token-1";

    private readonly string _target;

    public CredentialTokenStore(string target = "Resonate/Spotify") => _target = target;

    public unsafe SpotifyToken? Load()
    {
        if (!Advapi32.CredRead(_target, Advapi32.CredTypeGeneric, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = *(Advapi32.Credential*)pointer;
            if (credential.CredentialBlob == 0 || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Parse(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            Advapi32.CredFree(pointer);
        }
    }

    public void Save(SpotifyToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(Format(token, includeAccessToken: true));
        if (bytes.Length > MaxBlobBytes)
        {
            // The access token is renewed on the next start anyway.
            bytes = Encoding.UTF8.GetBytes(Format(token, includeAccessToken: false));
        }

        var blob = Marshal.AllocHGlobal(bytes.Length);
        var target = Marshal.StringToHGlobalUni(_target);
        var userName = Marshal.StringToHGlobalUni("Spotify");
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Advapi32.Credential
            {
                Type = Advapi32.CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = Advapi32.CredPersistLocalMachine,
                UserName = userName,
            };
            if (!Advapi32.CredWrite(credential, 0))
            {
                throw new InvalidOperationException(
                    "Windows did not save the Spotify sign-in (error " + Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture) + ").");
            }
        }
        finally
        {
            // Do not leave the token lying in freed memory.
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(userName);
        }
    }

    public void Clear() => Advapi32.CredDelete(_target, Advapi32.CredTypeGeneric, 0);

    internal static string Format(SpotifyToken token, bool includeAccessToken) =>
        string.Join(
            '\n',
            FormatVersion,
            token.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            token.Scope,
            token.RefreshToken,
            includeAccessToken ? token.AccessToken : string.Empty);

    internal static SpotifyToken? Parse(string text)
    {
        var parts = text.Split('\n');
        if (parts is not [FormatVersion, var expires, var scope, var refresh, var access] || refresh.Length == 0)
        {
            return null;
        }

        var expiresAt = long.TryParse(expires, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.MinValue;

        // Without an access token, make the first call renew it.
        return new SpotifyToken(access, refresh, access.Length == 0 ? DateTimeOffset.MinValue : expiresAt, scope);
    }
}

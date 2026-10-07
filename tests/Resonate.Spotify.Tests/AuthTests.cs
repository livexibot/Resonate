using System.Net;
using Microsoft.Extensions.Time.Testing;
using Resonate.Spotify.Auth;
using Resonate.Spotify.Tests.Fakes;

namespace Resonate.Spotify.Tests;

public class PkceTests
{
    [Fact]
    public void Challenge_matches_the_RFC_7636_example()
    {
        // Appendix B of RFC 7636.
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verifier_is_long_enough_and_url_safe()
    {
        var verifier = Pkce.CreateVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.NotEqual(verifier, Pkce.CreateVerifier());
    }
}

public class SpotifyAuthClientTests
{
    private const string ClientId = "0123456789abcdef0123456789abcdef";

    private static readonly SpotifyAuthOptions Options = new() { ClientId = ClientId };

    [Fact]
    public void Authorize_uri_uses_PKCE_and_the_loopback_redirect()
    {
        var client = new SpotifyAuthClient(new HttpClient(new FakeHttpHandler()), Options);

        var uri = client.BuildAuthorizeUri("the-challenge", "the-state");
        var query = Resonate.Spotify.Auth.LoopbackCallbackListener.ParseQuery(uri.PathAndQuery);

        Assert.Equal("accounts.spotify.com", uri.Host);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("the-challenge", query["code_challenge"]);
        Assert.Equal("the-state", query["state"]);
        Assert.Equal("http://127.0.0.1:43821/callback", query["redirect_uri"]);
        Assert.Contains("user-modify-playback-state", query["scope"].Split(' '));
    }

    [Fact]
    public async Task Exchanging_a_code_sends_the_verifier_and_no_secret()
    {
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.OK,
            """{"access_token":"AT","token_type":"Bearer","scope":"a b","expires_in":3600,"refresh_token":"RT"}""");
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var client = new SpotifyAuthClient(new HttpClient(handler), Options, time);

        var token = await client.ExchangeCodeAsync("the-code", "the-verifier", TestContext.Current.CancellationToken);

        Assert.Equal("AT", token.AccessToken);
        Assert.Equal("RT", token.RefreshToken);
        Assert.Equal(time.GetUtcNow().AddHours(1), token.ExpiresAt);

        var form = Resonate.Spotify.Auth.LoopbackCallbackListener.ParseQuery("?" + handler.Requests[0].Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("the-verifier", form["code_verifier"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.False(form.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task Refresh_keeps_the_old_refresh_token_when_Spotify_sends_none()
    {
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.OK,
            """{"access_token":"AT2","token_type":"Bearer","scope":"a","expires_in":3600}""");
        var client = new SpotifyAuthClient(new HttpClient(handler), Options);

        var token = await client.RefreshAsync("RT1", TestContext.Current.CancellationToken);

        Assert.Equal("AT2", token.AccessToken);
        Assert.Equal("RT1", token.RefreshToken);
    }

    [Fact]
    public async Task Revoked_refresh_token_means_signing_in_again()
    {
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Refresh token revoked"}""");
        var client = new SpotifyAuthClient(new HttpClient(handler), Options);

        var ex = await Assert.ThrowsAsync<SpotifyAuthException>(
            () => client.RefreshAsync("RT1", TestContext.Current.CancellationToken));

        Assert.True(ex.RequiresSignIn);
    }

    [Fact]
    public void Token_text_never_contains_the_tokens()
    {
        var token = new SpotifyToken("secret-access", "secret-refresh", DateTimeOffset.UnixEpoch, "scope");

        var text = token.ToString();

        Assert.DoesNotContain("secret-access", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-refresh", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", true)]
    [InlineData("0123456789abcdef", false)]
    [InlineData("not a client id at all, not one!", false)]
    [InlineData(null, false)]
    public void Client_id_shape_is_checked(string? clientId, bool valid) =>
        Assert.Equal(valid, SpotifyAuthOptions.IsValidClientId(clientId));
}

public class LoopbackCallbackListenerTests
{
    [Fact]
    public async Task Returns_the_code_for_the_matching_state()
    {
        using var listener = new LoopbackCallbackListener(new Uri("http://127.0.0.1:0/callback"));
        listener.Start();
        var waiting = listener.WaitForCodeAsync("s1", TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        var response = await http.GetAsync(
            new Uri($"http://127.0.0.1:{listener.Port}/callback?code=abc%2Bdef&state=s1"),
            TestContext.Current.CancellationToken);

        Assert.Equal("abc+def", await waiting);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ignores_stale_tabs_and_waits_for_the_right_state()
    {
        using var listener = new LoopbackCallbackListener(new Uri("http://127.0.0.1:0/callback"));
        listener.Start();
        var waiting = listener.WaitForCodeAsync("fresh", TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        var stale = await http.GetAsync(
            new Uri($"http://127.0.0.1:{listener.Port}/callback?code=old&state=stale"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.False(waiting.IsCompleted);

        await http.GetAsync(
            new Uri($"http://127.0.0.1:{listener.Port}/callback?code=new&state=fresh"),
            TestContext.Current.CancellationToken);
        Assert.Equal("new", await waiting);
    }

    [Fact]
    public async Task Reports_when_the_user_declines()
    {
        using var listener = new LoopbackCallbackListener(new Uri("http://127.0.0.1:0/callback"));
        listener.Start();
        var waiting = listener.WaitForCodeAsync("s1", TestContext.Current.CancellationToken);

        using var http = new HttpClient();
        await http.GetAsync(
            new Uri($"http://127.0.0.1:{listener.Port}/callback?error=access_denied&state=s1"),
            TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<SpotifyAuthException>(() => waiting);
        Assert.Equal("access_denied", ex.Error);
    }

    [Fact]
    public async Task A_spare_connection_the_browser_leaves_idle_does_not_hold_up_the_redirect()
    {
        using var listener = new LoopbackCallbackListener(new Uri("http://127.0.0.1:0/callback"));
        listener.Start();
        var waiting = listener.WaitForCodeAsync("s1", TestContext.Current.CancellationToken);

        // Browsers open connections ahead of time and may never send anything on them.
        using var idle = new System.Net.Sockets.TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, listener.Port, TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var http = new HttpClient();
        var response = await http.GetAsync(
            new Uri($"http://127.0.0.1:{listener.Port}/callback?code=c&state=s1"),
            timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("c", await waiting.WaitAsync(timeout.Token));
    }

    [Fact]
    public void Refuses_a_non_loopback_redirect() =>
        Assert.Throws<ArgumentException>(() => new LoopbackCallbackListener(new Uri("http://localhost:43821/callback")));
}

public class SpotifySessionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_fresh_token_is_used_as_it_is()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("AT", "RT", Start.AddHours(1), "s"));
        var handler = new FakeHttpHandler();
        using var session = NewSession(handler, store, new FakeTimeProvider(Start));

        Assert.Equal("AT", await session.GetAccessTokenAsync(null, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_expiring_token_is_renewed_once_and_saved()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("AT", "RT", Start.AddMinutes(1), "s"));
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.OK,
            """{"access_token":"AT2","expires_in":3600,"refresh_token":"RT2","scope":"s"}""");
        using var session = NewSession(handler, store, new FakeTimeProvider(Start));

        var results = await Task.WhenAll(
            session.GetAccessTokenAsync(null, TestContext.Current.CancellationToken),
            session.GetAccessTokenAsync(null, TestContext.Current.CancellationToken));

        Assert.All(results, t => Assert.Equal("AT2", t));
        Assert.Single(handler.Requests);
        Assert.Equal("RT2", store.Load()!.RefreshToken);
    }

    [Fact]
    public async Task A_rejected_token_forces_a_renewal()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("AT", "RT", Start.AddHours(1), "s"));
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.OK,
            """{"access_token":"AT2","expires_in":3600,"scope":"s"}""");
        using var session = NewSession(handler, store, new FakeTimeProvider(Start));

        Assert.Equal("AT2", await session.GetAccessTokenAsync("AT", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_revoked_sign_in_signs_out_and_forgets_the_token()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("AT", "RT", Start, "s"));
        var handler = new FakeHttpHandler().Respond(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        using var session = NewSession(handler, store, new FakeTimeProvider(Start));
        var signedOut = false;
        session.SignedOut += (_, _) => signedOut = true;

        await Assert.ThrowsAsync<SpotifyAuthException>(
            () => session.GetAccessTokenAsync(null, TestContext.Current.CancellationToken));

        Assert.True(signedOut);
        Assert.False(session.IsSignedIn);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Signing_out_while_a_token_is_being_renewed_stays_signed_out()
    {
        var store = new InMemoryTokenStore();
        store.Save(new SpotifyToken("AT", "RT", Start, "s"));
        var renewal = new TaskCompletionSource();
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.OK,
            """{"access_token":"AT2","expires_in":3600,"refresh_token":"RT2","scope":"s"}""",
            after: renewal.Task);
        using var session = NewSession(handler, store, new FakeTimeProvider(Start));

        var renewing = session.GetAccessTokenAsync(null, TestContext.Current.CancellationToken);
        session.SignOut();
        renewal.SetResult();

        await Assert.ThrowsAsync<SpotifyAuthException>(() => renewing);
        Assert.False(session.IsSignedIn);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task Sign_in_completes_through_the_browser_redirect()
    {
        var store = new InMemoryTokenStore();
        var handler = new FakeHttpHandler().Respond(
            HttpStatusCode.OK,
            """{"access_token":"AT","expires_in":3600,"refresh_token":"RT","scope":"s"}""");
        var options = new SpotifyAuthOptions
        {
            ClientId = "0123456789abcdef0123456789abcdef",
            RedirectUri = new Uri($"http://127.0.0.1:{FreePort()}/callback"),
        };
        using var session = new SpotifySession(new SpotifyAuthClient(new HttpClient(handler), options), store);

        using var browser = new HttpClient();
        await session.SignInAsync(
            authorizeUri =>
            {
                // Play the browser: approve and follow the redirect.
                var state = LoopbackCallbackListener.ParseQuery(authorizeUri.PathAndQuery)["state"];
                _ = browser.GetAsync(new Uri(options.RedirectUri, $"?code=c&state={state}"));
            },
            TestContext.Current.CancellationToken);

        Assert.True(session.IsSignedIn);
        Assert.Equal("RT", store.Load()!.RefreshToken);
    }

    private static SpotifySession NewSession(FakeHttpHandler handler, ITokenStore store, TimeProvider time) =>
        new(new SpotifyAuthClient(new HttpClient(handler), new SpotifyAuthOptions { ClientId = "0123456789abcdef0123456789abcdef" }, time), store, time);

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}

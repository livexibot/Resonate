using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Resonate.App.Services;
using Resonate.Spotify.Auth;
using Windows.ApplicationModel.DataTransfer;

namespace Resonate.App.Pages;

/// <summary>First run: the user's own Spotify developer app, then sign-in through the browser.</summary>
public sealed partial class SignInPage : Page
{
    private readonly AppServices _services = App.Services;
    private CancellationTokenSource? _signIn;

    public SignInPage()
    {
        InitializeComponent();
        RedirectUriBox.Text = SpotifyAuthOptions.DefaultRedirectUri;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ClientIdBox.Text = _services.Settings.ClientId ?? string.Empty;
        UpdateSignInButton();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _signIn?.Cancel();

    private void OnClientIdChanged(object sender, TextChangedEventArgs e) => UpdateSignInButton();

    private void UpdateSignInButton() =>
        SignInButton.IsEnabled = SpotifyAuthOptions.IsValidClientId(ClientIdBox.Text.Trim()) && _signIn is null;

    private void OnCopyRedirectClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(SpotifyAuthOptions.DefaultRedirectUri);
        try
        {
            Clipboard.SetContent(package);
        }
        catch (COMException)
        {
            // Another app is holding the clipboard. A Click handler must not throw.
            ShowError("The clipboard is busy. Try again in a moment, or select the address and copy it yourself.");
        }
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        var clientId = ClientIdBox.Text.Trim();
        if (!SpotifyAuthOptions.IsValidClientId(clientId))
        {
            return;
        }

        if (_services.Settings.ClientId != clientId)
        {
            // A sign-in belongs to one developer app; start fresh for a new one.
            _services.Account.SignOut();
            _services.Settings.ClientId = clientId;
            _services.SaveSettings();
        }

        _services.Account.Configure(clientId);

        _signIn = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = _signIn.Token;
        ErrorText.Visibility = Visibility.Collapsed;
        WaitingPanel.Visibility = Visibility.Visible;
        UpdateSignInButton();
        try
        {
            await Task.Run(() => _services.Account.SignInAsync(token), token);
            App.MainWindow?.ShowShell();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled, or the browser was left alone too long.
        }
        catch (SpotifyAuthException ex)
        {
            ShowError(ex.Error switch
            {
                "invalid_client" => "Spotify does not recognise that client ID. Copy it again from the app's Settings page.",
                "invalid_grant" => "Spotify refused the sign-in. Check that the redirect URI in your app is exactly " + SpotifyAuthOptions.DefaultRedirectUri,
                _ => ex.Message,
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Offline, or Spotify took too long to answer.
            ShowError("Spotify could not be reached. Check the internet connection and try again.");
        }
        catch (Exception ex)
        {
            // Anything else must not close Resonate (this handler is async void).
            ShowError(ex switch
            {
                System.ComponentModel.Win32Exception => "Resonate could not open your web browser. Check that Windows has a default browser, then try again.",
                InvalidOperationException => ex.Message,
                _ => "Signing in did not work. Try again.",
            });
        }
        finally
        {
            _signIn?.Dispose();
            _signIn = null;
            WaitingPanel.Visibility = Visibility.Collapsed;
            UpdateSignInButton();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _signIn?.Cancel();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}

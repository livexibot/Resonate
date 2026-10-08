using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Opens probe.html in WebView2 and prints what it reports: which DRM key systems
// play audio, and what Spotify's Web Playback SDK says with a made-up token.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var minutes = args.Length > 0 ? double.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 5;
        ApplicationConfiguration.Initialize();

        var form = new Form { Width = 900, Height = 600, Text = "Web player probe" };
        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        var userData = Path.Combine(Path.GetTempPath(), "webplayer-probe-udf");
        var exitCode = 0;

        var deadline = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromMinutes(minutes + 2).TotalMilliseconds };
        deadline.Tick += (_, _) =>
        {
            Console.WriteLine("probe: timed out waiting for the page");
            exitCode = 2;
            form.Close();
        };

        form.Load += async (_, _) =>
        {
            try
            {
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
                Console.WriteLine($"probe: WebView2 runtime {env.BrowserVersionString}");
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.PermissionRequested += (_, e) =>
                {
                    Console.WriteLine($"probe: permission asked: {e.PermissionKind} for {e.Uri}");
                    e.State = CoreWebView2PermissionState.Allow;
                };
                core.ProcessFailed += (_, e) => Console.WriteLine($"probe: process failed: {e.ProcessFailedKind} {e.Reason} {e.ProcessDescription}");
                core.WebMessageReceived += (_, e) =>
                {
                    var text = e.TryGetWebMessageAsString();
                    Console.WriteLine($"page: {text}");
                    if (text == "DONE")
                    {
                        form.Close();
                    }
                };
                core.SetVirtualHostNameToFolderMapping("resonate.example", AppContext.BaseDirectory, CoreWebView2HostResourceAccessKind.Allow);
                deadline.Start();
                core.Navigate($"https://resonate.example/probe.html?minutes={minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"probe: could not start WebView2: {ex}");
                exitCode = 1;
                form.Close();
            }
        };

        Application.Run(form);
        foreach (var dir in Directory.Exists(userData) ? Directory.GetDirectories(userData, "*Widevine*", SearchOption.AllDirectories) : [])
        {
            Console.WriteLine($"probe: user data has {dir}");
        }

        return exitCode;
    }
}

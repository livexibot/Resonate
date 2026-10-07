using System.Diagnostics;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

/// <summary>The helper as its own process, as Resonate runs it, through the same self-test CI runs on Windows.</summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly Packages _packages = new();

    public void Dispose() => _packages.Dispose();

    [Fact]
    public async Task The_self_test_passes_with_the_helper_as_a_separate_process()
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "Resonate.PluginHost.dll");
        Assert.True(File.Exists(helper), "The helper was not built next to the tests.");
        var launcher = new ProcessPluginHostLauncher(_ => new ProcessStartInfo("dotnet") { ArgumentList = { helper } });

        var result = await PluginSelfTest.RunAsync(_packages.Catalog, _packages.Feed, _packages.Installed, launcher, TimeSpan.FromSeconds(60));

        Assert.Matches(@"^OK 2 plugins started; skip-rules skipped, sleep-timer ready; helper memory \d+ MB; first plugin running after \d+ ms$", result);
    }
}

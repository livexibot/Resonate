using System.Text.Json.Nodes;
using Resonate.Plugins.Packaging;
using Resonate.Plugins.Tests.Fakes;

namespace Resonate.Plugins.Tests;

public sealed class ManifestTests
{
    public static TheoryData<string> RepositoryPlugins()
    {
        var data = new TheoryData<string>();
        foreach (var folder in Directory.EnumerateDirectories(Path.Combine(HostHarness.RepositoryRoot(), "plugins")))
        {
            data.Add(Path.GetFileName(folder));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RepositoryPlugins))]
    public void Every_plugin_in_the_repository_is_valid(string id)
    {
        var folder = HostHarness.RepositoryPlugin(id);
        var manifest = PluginPackager.ReadManifest(folder);
        Assert.Equal(id, manifest.Id);
        Assert.Empty(manifest.Validate(file => File.Exists(Path.Combine(folder, file))));
    }

    [Fact]
    public void Validation_names_each_problem()
    {
        var manifest = new PluginManifest
        {
            Id = "Bad Id",
            Name = "",
            Version = "one",
            Description = "Something",
            Main = "../escape.js",
            Permissions = ["player.read", "files.everything"],
            Settings =
            [
                new PluginSetting { Key = "a", Type = "toggle", Title = "A" },
                new PluginSetting { Key = "a", Type = "toggle", Title = "Again" },
                new PluginSetting { Key = "c", Type = "choice", Title = "C" },
                new PluginSetting { Key = "d", Type = "colour", Title = "D" },
            ],
        };

        var errors = manifest.Validate();
        Assert.Contains(errors, e => e.Contains("\"Bad Id\"", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("name", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("\"one\"", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("escape.js", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("files.everything", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("appears twice", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("without options", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("\"colour\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Values_are_kept_to_their_settings_type_and_limits()
    {
        var number = new PluginSetting { Type = PluginSettingTypes.Number, Min = 5, Max = 240, Default = 90 };
        Assert.Equal(240, number.Normalize(1000).GetValue<double>());
        Assert.Equal(5, number.Normalize(-3).GetValue<double>());
        Assert.Equal(42, number.Normalize("42").GetValue<double>());
        Assert.Equal(90, number.Normalize("lots").GetValue<double>());
        Assert.Equal(90, number.Normalize(null).GetValue<double>());

        var toggle = new PluginSetting { Type = PluginSettingTypes.Toggle, Default = true };
        Assert.True(toggle.Normalize("yes").GetValue<bool>());
        Assert.False(toggle.Normalize(false).GetValue<bool>());

        var list = new PluginSetting { Type = PluginSettingTypes.List };
        Assert.Equal("[\"Drake\",\"Future\"]", list.Normalize(" Drake \n\nFuture\ndrake").ToJsonString());
        Assert.Equal("[\"a\"]", list.Normalize(new JsonArray("a", 3, "")).ToJsonString());

        var choice = new PluginSetting
        {
            Type = PluginSettingTypes.Choice,
            Options = [new PluginChoice { Value = "a", Title = "A" }, new PluginChoice { Value = "b", Title = "B" }],
        };
        Assert.Equal("b", choice.Normalize("b").GetValue<string>());
        Assert.Equal("a", choice.Normalize("z").GetValue<string>());

        var text = new PluginSetting { Type = PluginSettingTypes.Text };
        Assert.Equal(PluginSetting.MaxTextLength, text.Normalize(new string('x', 900)).GetValue<string>().Length);
    }

    [Fact]
    public void Effective_settings_fill_in_defaults_and_drop_unknown_keys()
    {
        var manifest = PluginPackager.ReadManifest(HostHarness.RepositoryPlugin("sleep-timer"));
        var settings = manifest.EffectiveSettings(new JsonObject { ["customMinutes"] = 45, ["stray"] = 1 });
        Assert.Equal("{\"fade\":true,\"customMinutes\":45}", settings.ToJsonString());
    }

    [Fact]
    public void Permissions_read_as_a_sentence()
    {
        Assert.Equal("see what is playing", PluginPermissions.Describe(["player.read"]));
        Assert.Equal(
            "see what is playing, control playback and change the volume",
            PluginPermissions.Describe(["player.read", "player.control", "player.volume"]));
        Assert.Equal("nothing beyond its own settings", PluginPermissions.Describe([]));
    }
}

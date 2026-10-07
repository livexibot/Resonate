using Resonate.Spotify.Audio;

namespace Resonate.Spotify.Tests;

public sealed class EqualizerTests
{
    [Fact]
    public void Bands_match_the_Spotify_apps_own()
    {
        Assert.Equal([60, 150, 400, 1000, 2400, 15000], EqualizerSettings.Bands.Select(b => b.FrequencyHz));
        Assert.Equal(EqualizerFilter.LowShelf, EqualizerSettings.Bands[0].Filter);
        Assert.Equal(EqualizerFilter.HighShelf, EqualizerSettings.Bands[^1].Filter);
        Assert.Equal(["60 Hz", "150 Hz", "400 Hz", "1 kHz", "2.4 kHz", "15 kHz"], EqualizerSettings.Bands.Select(b => b.Label));
    }

    [Fact]
    public void Every_preset_has_one_gain_per_band_within_range()
    {
        Assert.Equal(21, EqualizerPresets.All.Count);
        Assert.Equal(EqualizerPresets.All.Count, EqualizerPresets.All.Select(p => p.Name).Distinct().Count());
        Assert.All(EqualizerPresets.All, p =>
        {
            Assert.Equal(EqualizerSettings.Bands.Count, p.GainsDb.Count);
            Assert.All(p.GainsDb, g => Assert.InRange(g, -EqualizerSettings.MaxGainDb, EqualizerSettings.MaxGainDb));
        });
    }

    [Fact]
    public void Gains_are_clamped_and_presets_are_recognised()
    {
        var rock = EqualizerPresets.All.Single(p => p.Name == "Rock");
        var settings = EqualizerSettings.Flat.WithPreset(rock);

        Assert.Same(rock, settings.Preset);
        Assert.Equal(-4.5, settings.PreampDb);

        var changed = settings.WithGain(0, 30);
        Assert.Equal(12, changed.GainsDb[0]);
        Assert.Null(changed.Preset);
        Assert.Equal(-12, changed.PreampDb);

        // The original is left alone, and an unknown band changes nothing.
        Assert.Equal(4, settings.GainsDb[0]);
        Assert.Same(settings, settings.WithGain(6, 3));
    }

    [Fact]
    public void Cuts_need_no_preamp_and_flat_or_off_is_neutral()
    {
        var reducer = EqualizerSettings.Flat.WithPreset(EqualizerPresets.All.Single(p => p.Name == "Bass reducer"));

        Assert.Equal(0, reducer.PreampDb);
        Assert.True(reducer.IsNeutral);
        Assert.False((reducer with { Enabled = true }).IsNeutral);
        Assert.True((EqualizerSettings.Flat with { Enabled = true }).IsNeutral);
        Assert.Equal("Flat", EqualizerSettings.Flat.Preset?.Name);
    }

    [Fact]
    public void Settings_compare_by_their_values()
    {
        var a = EqualizerSettings.Flat.WithGain(2, 3) with { Enabled = true };
        var b = (EqualizerSettings.Flat with { Enabled = true }).WithGain(2, 3);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.WithGain(2, 2));
        Assert.NotEqual(a, a with { Enabled = false });
    }
}

public sealed class SpotifyPrefsTests
{
    private const string Prefs =
        "language=\"en\"\n" +
        "audio.play_bitrate_enumeration=5\n" +
        "audio.equalizer_v2=true\n" +
        "audio.equalizer.low_shelf_gain_v2=760567125\n" +
        "audio.equalizer.high_shelf_gain_v2=-1073741824\n" +
        "ui.track_notifications_enabled=false\n";

    [Fact]
    public void Gains_use_Spotifys_scale_where_the_largest_number_is_twelve_decibels()
    {
        Assert.Equal(760567125, SpotifyPrefs.ToStored(4.25));
        Assert.Equal(int.MaxValue, SpotifyPrefs.ToStored(12));
        Assert.Equal(-int.MaxValue, SpotifyPrefs.ToStored(-40));
        Assert.Equal(0, SpotifyPrefs.ToStored(0));
    }

    [Fact]
    public void Reads_the_equalizer_and_the_streaming_quality()
    {
        var prefs = SpotifyPrefs.Parse(Prefs);
        var eq = SpotifyPrefs.ReadEqualizer(prefs);

        Assert.True(eq.Enabled);
        Assert.Equal([4.25, 0, 0, 0, 0, -6], eq.GainsDb);
        Assert.True(SpotifyPrefs.IsLossless(prefs));
        Assert.Equal("\"en\"", prefs["language"]);
    }

    [Fact]
    public void Missing_settings_mean_off_and_unknown_quality()
    {
        var prefs = SpotifyPrefs.Parse("audio.play_bitrate_enumeration=4\r\n");

        Assert.Equal(EqualizerSettings.Flat, SpotifyPrefs.ReadEqualizer(prefs));
        Assert.False(SpotifyPrefs.IsLossless(prefs));
        Assert.Null(SpotifyPrefs.IsLossless(SpotifyPrefs.Parse(string.Empty)));
    }

    [Fact]
    public void Writing_changes_only_the_equalizer_keys()
    {
        var settings = EqualizerSettings.Flat.WithPreset(EqualizerPresets.All.Single(p => p.Name == "Bass booster")) with { Enabled = false };

        var written = SpotifyPrefs.WriteEqualizer(Prefs, settings);
        var lines = written.Split('\n');

        // Lines it does not own stay where they were, untouched.
        Assert.Equal("language=\"en\"", lines[0]);
        Assert.Equal("audio.play_bitrate_enumeration=5", lines[1]);
        Assert.Equal("audio.equalizer_v2=false", lines[2]);
        Assert.Equal("audio.equalizer.low_shelf_gain_v2=760567125", lines[3]);
        Assert.Equal("audio.equalizer.high_shelf_gain_v2=0", lines[4]);
        Assert.Equal("ui.track_notifications_enabled=false", lines[5]);
        Assert.EndsWith("\n", written, StringComparison.Ordinal);

        // Missing keys are added once, and reading back gives the same settings.
        Assert.All(
            SpotifyPrefs.EqualizerGainKeys.Append(SpotifyPrefs.EqualizerEnabledKey),
            key => Assert.Single(lines, l => l.StartsWith(key + "=", StringComparison.Ordinal)));
        Assert.Equal(settings, SpotifyPrefs.ReadEqualizer(SpotifyPrefs.Parse(written)));
        Assert.Equal(written, SpotifyPrefs.WriteEqualizer(written, settings));
    }

    [Fact]
    public void Writing_keeps_windows_line_endings()
    {
        var written = SpotifyPrefs.WriteEqualizer("a=1\r\nb=2\r\n", EqualizerSettings.Flat with { Enabled = true });

        Assert.StartsWith("a=1\r\nb=2\r\naudio.equalizer_v2=true\r\n", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\r", written, StringComparison.Ordinal);
        Assert.Equal(9, written.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }
}

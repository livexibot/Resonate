using Resonate.Spotify.Audio;
using Resonate.Spotify.LocalFiles;

namespace Resonate.Spotify.Tests;

public class LocalEqualizerTests
{
    [Fact]
    public void Off_or_flat_changes_nothing()
    {
        foreach (var settings in new[] { null, EqualizerSettings.Flat, new EqualizerSettings { Enabled = false, GainsDb = [6, 6, 6, 6, 6, 6] } })
        {
            var mapped = LocalEqualizer.From(settings);

            Assert.False(mapped.IsActive);
            Assert.False(mapped.NeedsLimiter);
            Assert.Equal(1.0, mapped.PreampGain);
            Assert.All(mapped.Bands, b => Assert.Equal(1.0, b.Gain, 3));
        }
    }

    [Fact]
    public void Six_bands_become_eight_in_rising_order_with_two_spare_ones_at_no_gain()
    {
        var mapped = LocalEqualizer.From(new EqualizerSettings { Enabled = true, GainsDb = [6, 0, -6, 0, 0, 3] });

        Assert.Equal(8, mapped.Bands.Count);
        Assert.Equal(mapped.Bands.Select(b => b.FrequencyHz).Order(), mapped.Bands.Select(b => b.FrequencyHz));
        Assert.Equal([60, 150, 400, 1000, 2400, 15_000, 17_000, 19_000], mapped.Bands.Select(b => b.FrequencyHz));
        Assert.Equal(1.995, mapped.Bands[0].Gain, 3);
        Assert.Equal(0.501, mapped.Bands[2].Gain, 3);
        Assert.Equal(1.413, mapped.Bands[5].Gain, 3);
        Assert.Equal(1.0, mapped.Bands[6].Gain);
        Assert.Equal(1.0, mapped.Bands[7].Gain);
    }

    [Fact]
    public void Shelves_are_wide_and_peaks_have_spotifys_width()
    {
        var mapped = LocalEqualizer.From(new EqualizerSettings { Enabled = true, GainsDb = [1, 1, 1, 1, 1, 1] });

        Assert.Equal(LocalEqualizer.ShelfBandwidth, mapped.Bands[0].Bandwidth);
        Assert.Equal(1.388, mapped.Bands[1].Bandwidth, 3);
        Assert.Equal(LocalEqualizer.ShelfBandwidth, mapped.Bands[5].Bandwidth);
    }

    [Fact]
    public void Boosts_turn_the_preamp_down_and_the_limiter_on()
    {
        var boosted = LocalEqualizer.From(new EqualizerSettings { Enabled = true, GainsDb = [0, 0, 0, 12, 0, 0] });
        var cut = LocalEqualizer.From(new EqualizerSettings { Enabled = true, GainsDb = [0, -6, 0, 0, 0, 0] });

        Assert.Equal(0.251, boosted.PreampGain, 3);
        Assert.True(boosted.NeedsLimiter);
        Assert.True(cut.IsActive);
        Assert.Equal(1.0, cut.PreampGain);
        Assert.False(cut.NeedsLimiter);
    }

    [Fact]
    public void Gains_stay_within_what_windows_accepts()
    {
        Assert.Equal(LocalEqualizer.MaxGain, LocalEqualizer.ToGain(40));
        Assert.Equal(LocalEqualizer.MinGain, LocalEqualizer.ToGain(-40));
    }

    [Fact]
    public void The_volume_slider_is_squared()
    {
        Assert.Equal(0.25, LocalVolume.ToGain(0.5));
        Assert.Equal(1, LocalVolume.ToGain(2));
        Assert.Equal(0, LocalVolume.ToGain(-1));
    }
}

using Resonate.Spotify.Audio;
using Resonate.Spotify.LocalFiles;
using static Resonate.Spotify.Tests.Fakes.AudioFiles;

namespace Resonate.Spotify.Tests;

public sealed class SignalPathTests
{
    private static readonly AudioOutputInfo Dac = new("Speakers (USB DAC)", 44_100, 24, IsBluetooth: false);

    private static readonly SpotifyAudioPrefs LosslessPrefs =
        new("prefs", EqualizerSettings.Flat, IsLossless: true) { Quality = 5, Normalize = false };

    private static SpotifySignal Spotify(SpotifyAudioPrefs? prefs = null, double volume = 1, AudioOutputInfo? output = null) =>
        new() { Prefs = prefs ?? LosslessPrefs, Volume = volume, Output = output ?? Dac };

    [Fact]
    public void Lossless_setting_full_volume_and_a_44_1_khz_output_is_lossless()
    {
        var report = SignalPath.ForSpotify(Spotify());

        Assert.Equal(SignalVerdict.Lossless, report.Verdict);
        Assert.Equal("LOSSLESS", report.Badge);
        Assert.All(report.Steps, s => Assert.Equal(SignalStepState.Good, s.State));
        Assert.Equal("Speakers (USB DAC), 24-bit 44.1 kHz", report.Steps[^1].Value);
    }

    [Fact]
    public void Very_high_is_not_lossless_and_points_at_Spotifys_settings()
    {
        var report = SignalPath.ForSpotify(Spotify(LosslessPrefs with { Quality = 4, IsLossless = false }));

        Assert.Equal(SignalVerdict.NotLossless, report.Verdict);
        var quality = report.Steps[0];
        Assert.Equal("Very high", quality.Value);
        Assert.Equal(SignalStepState.Problem, quality.State);
        Assert.Equal(SignalFix.SpotifySettings, quality.Fix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(6)]
    public void A_missing_or_unknown_quality_can_not_be_told(int? quality)
    {
        var report = SignalPath.ForSpotify(Spotify(LosslessPrefs with { Quality = quality, IsLossless = null }));

        Assert.Equal(SignalVerdict.CantTell, report.Verdict);
        Assert.Equal(SignalStepState.Unknown, report.Steps[0].State);
    }

    [Fact]
    public void Without_Spotifys_settings_it_can_not_tell()
    {
        var report = SignalPath.ForSpotify(new SpotifySignal { Prefs = null, Output = Dac });

        Assert.Equal(SignalVerdict.CantTell, report.Verdict);
        Assert.Equal("Settings not found", report.Steps[0].Value);
    }

    [Fact]
    public void Web_api_only_and_other_devices_are_not_checked()
    {
        var webApi = SignalPath.ForSpotify(Spotify() with { UsesSpotifyApp = false });
        var phone = SignalPath.ForSpotify(Spotify() with { OtherDevice = "Pixel" });

        Assert.Equal(SignalVerdict.CantTell, webApi.Verdict);
        Assert.Empty(webApi.Steps);
        Assert.Equal(SignalVerdict.CantTell, phone.Verdict);
        Assert.Contains("Pixel", phone.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_another_computer_or_device_counts_as_another_device()
    {
        Assert.Null(SignalPath.OtherDevice(null, "DESKTOP-1"));
        Assert.Null(SignalPath.OtherDevice("desktop-1", "DESKTOP-1"));
        Assert.Equal("Kitchen", SignalPath.OtherDevice("Kitchen", "DESKTOP-1"));
    }

    [Fact]
    public void Volume_equalizer_and_resampling_adjust_it_and_say_why()
    {
        var report = SignalPath.ForSpotify(Spotify(
            LosslessPrefs with { Equalizer = EqualizerSettings.Flat with { Enabled = true } },
            volume: 0.8,
            Dac with { SampleRate = 48_000 }));

        Assert.Equal(SignalVerdict.Adjusted, report.Verdict);
        Assert.Equal("Lossless, adjusted", report.Title);
        Assert.Equal(
            "Lossless, but the equalizer is on, the volume is below 100 % and Windows converts 44.1 to 48 kHz.",
            report.Summary);
        Assert.Equal(SignalFix.Equalizer, report.Steps.Single(s => s.Name == "Spotify's equalizer").Fix);
        Assert.Equal("80 %", report.Steps.Single(s => s.Name == "Spotify's volume").Value);
        Assert.Equal(SignalFix.SoundSettings, report.Steps.Single(s => s.Name == "Windows mixer").Fix);
    }

    [Fact]
    public void Normalisation_adjusts_it_and_an_unknown_one_does_not_count()
    {
        Assert.Equal(SignalVerdict.Adjusted, SignalPath.ForSpotify(Spotify(LosslessPrefs with { Normalize = true })).Verdict);

        var unknown = SignalPath.ForSpotify(Spotify(LosslessPrefs with { Normalize = null }));
        Assert.Equal(SignalVerdict.Lossless, unknown.Verdict);
        Assert.Equal(SignalStepState.Unknown, unknown.Steps.Single(s => s.Name == "Normalize volume").State);
    }

    [Fact]
    public void Bluetooth_is_not_lossless_even_when_the_setting_is_unknown()
    {
        var report = SignalPath.ForSpotify(Spotify(LosslessPrefs with { Quality = null }, output: Dac with { IsBluetooth = true }));

        Assert.Equal(SignalVerdict.NotLossless, report.Verdict);
        Assert.Equal(SignalStepState.Problem, report.Steps[^1].State);
    }

    [Fact]
    public void Without_an_output_it_can_not_tell()
    {
        var report = SignalPath.ForSpotify(new SpotifySignal { Prefs = LosslessPrefs, Output = null });

        Assert.Equal(SignalVerdict.CantTell, report.Verdict);
    }

    [Fact]
    public void A_lossless_file_at_the_outputs_rate_is_lossless()
    {
        var report = SignalPath.ForLocalFile(new LocalSignal { Format = new("FLAC", true, 44_100, 16), Output = Dac });

        Assert.Equal(SignalVerdict.Lossless, report.Verdict);
        Assert.Equal("FLAC, 16-bit 44.1 kHz", report.Steps[0].Value);
    }

    [Fact]
    public void A_high_resolution_file_is_converted_by_Resonate_and_then_by_Windows()
    {
        var report = SignalPath.ForLocalFile(new LocalSignal
        {
            Format = new("FLAC", true, 96_000, 24),
            Output = Dac with { SampleRate = 96_000 },
        });

        Assert.Equal(SignalVerdict.Adjusted, report.Verdict);
        Assert.Equal("Converts 96 to 48 kHz", report.Steps.Single(s => s.Name == "Resonate's player").Value);
        Assert.Equal("Converts 48 to 96 kHz", report.Steps.Single(s => s.Name == "Windows mixer").Value);
        Assert.Equal("Lossless, but Resonate converts 96 to 48 kHz and Windows converts 48 to 96 kHz.", report.Summary);
    }

    [Fact]
    public void Lossy_and_unknown_files()
    {
        var mp3 = SignalPath.ForLocalFile(new LocalSignal { Format = new("MP3", false, 44_100), Output = Dac });
        var m4a = SignalPath.ForLocalFile(new LocalSignal { Format = new("M4A", null), Output = Dac });

        Assert.Equal(SignalVerdict.NotLossless, mp3.Verdict);
        Assert.Equal("MP3, 44.1 kHz", mp3.Steps[0].Value);
        Assert.Equal(SignalVerdict.CantTell, m4a.Verdict);
    }

    [Fact]
    public void Reports_compare_by_what_they_say()
    {
        Assert.True(SignalPath.ForSpotify(Spotify()).SameAs(SignalPath.ForSpotify(Spotify())));
        Assert.False(SignalPath.ForSpotify(Spotify()).SameAs(SignalPath.ForSpotify(Spotify(volume: 0.5))));
    }

    [Fact]
    public void Prefs_give_the_quality_and_normalisation()
    {
        var prefs = SpotifyPrefs.Parse("audio.play_bitrate_enumeration=4\naudio.normalize_v2=false\n");

        Assert.Equal(4, SpotifyPrefs.ReadQuality(prefs));
        Assert.False(SpotifyPrefs.ReadNormalize(prefs));
        Assert.Null(SpotifyPrefs.ReadQuality(SpotifyPrefs.Parse(string.Empty)));
        Assert.Null(SpotifyPrefs.ReadNormalize(SpotifyPrefs.Parse("audio.normalize_v2=maybe")));
    }
}

public sealed class LocalAudioFormatTests
{
    private static LocalAudioFormat? Read(byte[] file, string name)
    {
        using var stream = new MemoryStream(file);
        return LocalAudioFormat.Read(stream, name);
    }

    [Fact]
    public void Flac_gives_its_rate_and_depth()
    {
        Assert.Equal(new LocalAudioFormat("FLAC", true, 96_000, 16), Read(Flac(96_000, 1000), "a.flac"));
    }

    [Fact]
    public void Wav_gives_pcm_rate_and_depth_and_compressed_wav_is_lossy()
    {
        static byte[] Wave(byte[] format)
        {
            var body = Concat(Ascii("WAVE"), Chunk("fmt ", format), Chunk("data", new byte[100]));
            return Concat(Ascii("RIFF"), LE32((uint)body.Length), body);
        }

        var pcm = Concat(LE16(1), LE16(2), LE32(48_000), LE32(288_000), LE16(6), LE16(24));
        var extensible = Concat(LE16(0xFFFE), LE16(2), LE32(96_000), LE32(768_000), LE16(8), LE16(32), LE16(22), LE16(24), LE32(3), LE16(1), new byte[14]);
        var adpcm = Concat(LE16(2), LE16(2), LE32(44_100), LE32(44_100), LE16(1024), LE16(4));

        Assert.Equal(new LocalAudioFormat("WAV", true, 48_000, 24), Read(Wave(pcm), "a.wav"));
        Assert.Equal(new LocalAudioFormat("WAV", true, 96_000, 24), Read(Wave(extensible), "a.wav"));
        Assert.Equal(false, Read(Wave(adpcm), "a.wav")?.IsLossless);
    }

    [Fact]
    public void Mp3_behind_an_id3_tag_and_adts_aac_are_lossy()
    {
        var mp3 = Read(Concat(Id3Tag(3, 0, Frame23("TIT2", Latin1Text("x"))), MpegFrames(2)), "a.bin");
        var aac = Read([0xFF, 0xF1, 0x50, 0x80, 0, 0x1F, 0xFC], "a.aac");

        Assert.Equal(new LocalAudioFormat("MP3", false, 44_100), mp3);
        Assert.Equal(new LocalAudioFormat("AAC", false, 44_100), aac);
    }

    [Fact]
    public void Mp4_tells_apple_lossless_from_aac()
    {
        static byte[] Mp4(byte[] entry) => Concat(
            Atom("ftyp", Ascii("M4A "), new byte[4]),
            Atom("moov", Atom("trak", Atom("mdia", Atom("minf", Atom("stbl", Atom("stsd", new byte[4], BE32(1), entry)))))));

        static byte[] Fields(ushort bits, uint rate) => Concat(new byte[6], [0, 1], new byte[8], [0, 2], [(byte)(bits >> 8), (byte)bits], new byte[4], BE32(rate << 16));

        var alacConfig = Concat(new byte[4], BE32(4096), [0, 24, 40, 10, 14, 2], [0, 255], BE32(0), BE32(0), BE32(96_000));
        var alac = Mp4(Atom("alac", Fields(16, 0), Atom("alac", alacConfig)));
        var aac = Mp4(Atom("mp4a", Fields(16, 44_100)));

        Assert.Equal(new LocalAudioFormat("ALAC", true, 96_000, 24), Read(alac, "a.m4a"));
        Assert.Equal(new LocalAudioFormat("AAC", false, 44_100), Read(aac, "a.m4a"));
    }

    [Fact]
    public void Ogg_names_its_codec()
    {
        var vorbis = Read(OggPage(1, 0, 0, [30], Concat([1], Ascii("vorbis"), new byte[23])), "a.ogg");
        var opus = Read(OggPage(1, 0, 0, [19], Concat(Ascii("OpusHead"), new byte[11])), "a.ogg");

        Assert.Equal(new LocalAudioFormat("Vorbis", false), vorbis);
        Assert.Equal(new LocalAudioFormat("Opus", false), opus);
    }

    [Fact]
    public void Unreadable_files_fall_back_to_their_name()
    {
        Assert.Equal(new LocalAudioFormat("FLAC", true), LocalAudioFormat.Read(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".flac")));
        Assert.Equal(new LocalAudioFormat("M4A", null), Read([1, 2, 3], "a.m4a"));
        Assert.Null(Read([1, 2, 3], "a.txt"));
    }
}

using Resonate.Themes.Skins;

namespace Resonate.Themes.Tests;

public sealed class SpectrumAnalyserTests
{
    private const int Rate = 48_000;
    private const int Quantum = 480;
    private const int Channels = 2;

    [Fact]
    public void Silence_from_the_start_shows_every_bar_at_rest_and_then_nothing_new()
    {
        var analyser = new SpectrumAnalyser();
        var silence = new float[Quantum * Channels];

        for (var q = 0; q < 10; q++)
        {
            analyser.Process(silence, Channels, Rate);
        }

        var frame = analyser.Read();
        Assert.True(frame.IsAtRest);
        Assert.Equal(1, frame.Sequence);
        Assert.All(frame.Bars, bar => Assert.Equal(0, bar));
        Assert.All(frame.Peaks, peak => Assert.Equal(0, peak));
        Assert.All(frame.Scope, point => Assert.Equal(0, point));
    }

    [Fact]
    public void A_tone_lights_its_own_band_and_leaves_the_ends_low()
    {
        var analyser = new SpectrumAnalyser();
        for (var q = 0; q < 3; q++)
        {
            analyser.Process(Tone(1000, 0.8f, Quantum, q * Quantum), Channels, Rate);
        }

        var frame = analyser.Read();

        // Band 22 is about 1 kHz at 48 kHz (Winamp's bands are counted in FFT bins).
        Assert.Equal(75, frame.BarCount);
        Assert.Equal(22, Loudest(frame));
        Assert.True(frame.Bars[22] >= 14.9f, $"band 22 at {frame.Bars[22]}");
        Assert.True(frame.Bars[0] < 4.5f && frame.Bars[74] < 4.5f, $"ends at {frame.Bars[0]} and {frame.Bars[74]}");
        Assert.False(frame.IsAtRest);
    }

    [Fact]
    public void In_the_classic_bars_a_tone_lights_the_bar_of_its_four_bands()
    {
        var analyser = new SpectrumAnalyser { Bars = VisualiserBars.Classic, Motion = VisualiserMotion.Stepped };
        for (var q = 0; q < 6; q++)
        {
            analyser.Process(Tone(1000, 0.8f, Quantum, q * Quantum), Channels, Rate);
        }

        var frame = analyser.Read();

        Assert.Equal(19, frame.BarCount);
        Assert.Equal(22 / 4, Loudest(frame));
        Assert.True(frame.Bars[22 / 4] >= 1, $"bar 5 at {frame.Bars[22 / 4]}");
    }

    [Fact]
    public void Mono_sound_works_like_stereo()
    {
        var analyser = new SpectrumAnalyser();
        var mono = new float[Quantum];
        for (var q = 0; q < 3; q++)
        {
            for (var i = 0; i < Quantum; i++)
            {
                mono[i] = 0.8f * MathF.Sin(MathF.Tau * 1000 * ((q * Quantum) + i) / Rate);
            }

            analyser.Process(mono, 1, Rate);
        }

        Assert.Equal(22, Loudest(analyser.Read()));
    }

    [Fact]
    public void After_the_music_stops_the_cap_hangs_then_everything_comes_to_rest()
    {
        var analyser = new SpectrumAnalyser();
        for (var q = 0; q < 3; q++)
        {
            analyser.Process(Tone(1000, 0.8f, Quantum, q * Quantum), Channels, Rate);
        }

        var top = analyser.Read().Peaks[22];
        var silence = new float[Quantum * Channels];
        var restAfter = -1;
        var restSequence = 0L;
        for (var q = 0; q < 200; q++)
        {
            analyser.Process(silence, Channels, Rate);
            var frame = analyser.Read();

            // 200 ms in, the cap still hangs where the tone left it (it hangs 0.35 s).
            if (q == 20)
            {
                Assert.True(frame.Peaks[22] >= top - 0.01f, $"cap at {frame.Peaks[22]}, was {top}");
                Assert.True(frame.Bars[22] < top, "the bar falls while the cap hangs");
            }

            if (frame.IsAtRest && restAfter < 0)
            {
                restAfter = q;
                restSequence = frame.Sequence;
            }
        }

        Assert.InRange(restAfter, 31, 199);
        Assert.Equal(restSequence, analyser.Read().Sequence);
        Assert.All(analyser.Read().Scope, point => Assert.Equal(0, point));
    }

    [Fact]
    public void Stepped_bars_move_in_whole_rows_within_the_display()
    {
        var analyser = new SpectrumAnalyser { Bars = VisualiserBars.Classic, Motion = VisualiserMotion.Stepped };
        var random = new Random(1);
        var noise = new float[Quantum * Channels];
        var highest = 0f;
        var highestCap = 0f;
        for (var q = 0; q < 300; q++)
        {
            for (var i = 0; i < noise.Length; i++)
            {
                noise[i] = (float)((random.NextDouble() * 2) - 1) * 0.5f;
            }

            analyser.Process(noise, Channels, Rate);
            var frame = analyser.Read();
            for (var b = 0; b < frame.BarCount; b++)
            {
                Assert.Equal(MathF.Round(frame.Bars[b]), frame.Bars[b]);
                Assert.Equal(MathF.Round(frame.Peaks[b]), frame.Peaks[b]);
                highest = Math.Max(highest, frame.Bars[b]);
                highestCap = Math.Max(highestCap, frame.Peaks[b]);
            }
        }

        Assert.Equal(19, analyser.Read().BarCount);
        Assert.InRange(highest, 1, 15);
        Assert.InRange(highestCap, 1, 15);
    }

    [Theory]
    [InlineData(441)]
    [InlineData(480)]
    [InlineData(960)]
    public void Stepped_bars_fall_at_Winamps_rate_whatever_the_quantum(int quantum)
    {
        var analyser = new SpectrumAnalyser { Bars = VisualiserBars.Classic, Motion = VisualiserMotion.Stepped };
        analyser.Process(Tone(1000, 0.8f, quantum * 4), Channels, Rate);
        analyser.Process(Tone(1000, 0.8f, quantum), Channels, Rate);
        var start = analyser.Read();
        var bar = Loudest(start);
        var height = start.Bars[bar];

        var quiet = new float[quantum * Channels];
        var seconds = 0.0;
        while (seconds < 0.2 - 1e-9)
        {
            analyser.Process(quiet, Channels, Rate);
            seconds += quantum / (double)Rate;
        }

        // Winamp's "moderate" falloff: 12/16 of a row every 1/60 s of music.
        var fell = height - analyser.Read().Bars[bar];
        Assert.InRange(fell, (0.75 * seconds * 60) - 1.5, (0.75 * seconds * 60) + 1.5);
    }

    [Fact]
    public void The_scope_has_76_points_within_range()
    {
        var analyser = new SpectrumAnalyser();

        analyser.Process(Tone(440, 1f, Quantum), Channels, Rate);

        var scope = analyser.Read().Scope;
        Assert.Equal(76, scope.Length);
        Assert.All(scope, point => Assert.InRange(point, -1f, 1f));
        Assert.Contains(scope, point => point != 0);
    }

    [Fact]
    public void Read_gives_the_same_picture_until_a_new_one_is_made()
    {
        var analyser = new SpectrumAnalyser();
        analyser.Process(Tone(1000, 0.5f, Quantum), Channels, Rate);

        var first = analyser.Read();
        var sequence = first.Sequence;
        Assert.Same(first, analyser.Read());
        Assert.Equal(sequence, analyser.Read().Sequence);

        analyser.Process(Tone(1000, 0.5f, Quantum, Quantum), Channels, Rate);

        Assert.Equal(sequence + 1, analyser.Read().Sequence);
    }

    [Fact]
    public void Reset_or_another_look_drops_the_caps_at_once()
    {
        // Without a reset the cap hangs after the tone; with one it is gone at the next quantum.
        Assert.True(CapAfterSilence(_ => { }) > 14);
        Assert.True(CapAfterSilence(a => a.Reset()) < 5);
        Assert.True(CapAfterSilence(a => a.Motion = VisualiserMotion.Stepped) < 5);
        Assert.True(CapAfterSilence(a => a.Bars = VisualiserBars.Classic) < 5);
    }

    [Fact]
    public void Empty_or_shapeless_quanta_are_ignored()
    {
        var analyser = new SpectrumAnalyser();
        var tone = Tone(1000, 0.5f, Quantum);

        analyser.Process([], Channels, Rate);
        analyser.Process(tone, 0, Rate);
        analyser.Process(tone, -2, Rate);
        analyser.Process(tone, Channels, 0);

        Assert.Equal(0, analyser.Read().Sequence);
    }

    [Fact]
    public void The_look_back_stays_within_the_ring()
    {
        var analyser = new SpectrumAnalyser { LagSamples = -5 };
        Assert.Equal(0, analyser.LagSamples);

        analyser.LagSamples = 1_000_000;
        Assert.Equal(SpectrumAnalyser.MaxLag, analyser.LagSamples);

        // The deepest look-back still finds the music.
        for (var q = 0; q < 8; q++)
        {
            analyser.Process(Tone(1000, 0.8f, Quantum, q * Quantum), Channels, Rate);
        }

        Assert.Equal(22, Loudest(analyser.Read()));
    }

    [Theory]
    [InlineData(VisualiserBars.Wide, VisualiserMotion.Smooth)]
    [InlineData(VisualiserBars.Classic, VisualiserMotion.Stepped)]
    public void The_audio_thread_allocates_nothing(VisualiserBars bars, VisualiserMotion motion)
    {
        var analyser = new SpectrumAnalyser { Bars = bars, Motion = motion };
        var random = new Random(2);
        var quanta = new float[8][];
        for (var k = 0; k < quanta.Length; k++)
        {
            quanta[k] = new float[Quantum * Channels];
            for (var i = 0; i < quanta[k].Length; i++)
            {
                quanta[k][i] = (float)((random.NextDouble() * 2) - 1) * 0.4f;
            }
        }

        var silence = new float[Quantum * Channels];
        for (var q = 0; q < 500; q++)
        {
            analyser.Process(q % 50 < 40 ? quanta[q % 8] : silence, Channels, Rate);
            _ = analyser.Read();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var q = 0; q < 2000; q++)
        {
            analyser.Process(q % 50 < 40 ? quanta[q % 8] : silence, Channels, Rate);
            if ((q & 1) == 0)
            {
                _ = analyser.Read();
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void A_reader_never_sees_the_pictures_go_backwards()
    {
        var analyser = new SpectrumAnalyser();
        var stop = false;
        var backwards = 0;
        var reads = 0;
        var reader = new Thread(() =>
        {
            var last = 0L;
            while (!Volatile.Read(ref stop))
            {
                var frame = analyser.Read();
                reads++;
                if (frame.Sequence < last)
                {
                    backwards++;
                }

                last = frame.Sequence;
            }
        });
        reader.Start();

        var tones = Enumerable.Range(0, 10).Select(k => Tone(200 + (k * 300), 0.5f, Quantum)).ToArray();
        for (var q = 0; q < 20_000; q++)
        {
            analyser.Process(tones[q % tones.Length], Channels, Rate);
        }

        Volatile.Write(ref stop, true);
        reader.Join();

        Assert.Equal(0, backwards);
        Assert.True(reads > 0);
    }

    private static float CapAfterSilence(Action<SpectrumAnalyser> change)
    {
        var analyser = new SpectrumAnalyser();
        for (var q = 0; q < 3; q++)
        {
            analyser.Process(Tone(1000, 0.8f, Quantum, q * Quantum), Channels, Rate);
        }

        change(analyser);
        analyser.Process(new float[Quantum * Channels], Channels, Rate);
        return analyser.Read().Peaks[22 / (analyser.Bars == VisualiserBars.Classic ? 4 : 1)];
    }

    private static int Loudest(VisualiserFrame frame)
    {
        var loudest = 0;
        for (var b = 1; b < frame.BarCount; b++)
        {
            if (frame.Bars[b] > frame.Bars[loudest])
            {
                loudest = b;
            }
        }

        return loudest;
    }

    private static float[] Tone(double hz, float amplitude, int frames, int offset = 0)
    {
        var data = new float[frames * Channels];
        for (var i = 0; i < frames; i++)
        {
            var value = amplitude * (float)Math.Sin(2 * Math.PI * hz * (i + offset) / Rate);
            data[i * Channels] = value;
            data[(i * Channels) + 1] = value;
        }

        return data;
    }
}

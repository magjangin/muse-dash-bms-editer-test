using System;
using System.IO;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

public sealed class AudioOffsetTests
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private static OggAudioData BuildShiftedClickTrack(double bpm, int beats, double shiftSeconds)
    {
        var secondsPerBeat = 60.0 / bpm;
        var totalFrames = (int)(secondsPerBeat * (beats + 2) * SampleRate);
        var pcm = new byte[totalFrames * Channels * sizeof(short)];

        for (var frame = 0; frame < totalFrames; frame++)
        {
            var t = (double)frame / SampleRate;
            var value = 0.0;

            var shifted = t - shiftSeconds;
            var beatIndex = (int)Math.Floor(shifted / secondsPerBeat);
            var sinceBeat = shifted - (beatIndex * secondsPerBeat);

            if (beatIndex >= 1 && beatIndex <= beats && sinceBeat >= 0 && sinceBeat < 0.05)
                value = Math.Sin(2 * Math.PI * 180.0 * sinceBeat) * Math.Exp(-sinceBeat * 80.0);

            var sample = (short)(Math.Clamp(value, -1.0, 1.0) * short.MaxValue);
            for (var c = 0; c < Channels; c++)
            {
                var offset = ((frame * Channels) + c) * sizeof(short);
                pcm[offset] = (byte)(sample & 0xff);
                pcm[offset + 1] = (byte)((sample >> 8) & 0xff);
            }
        }

        return new OggAudioData(pcm, SampleRate, Channels, (double)totalFrames / SampleRate);
    }

    [Theory]
    [InlineData(-0.0229)]
    [InlineData(0.0180)]
    [InlineData(-0.0400)]
    public void 자동_검출이_어긋난_만큼을_찾아낸다(double shiftSeconds)
    {
        var waveform = OggPeakLoader.Load(BuildShiftedClickTrack(141.0, 24, shiftSeconds));

        var owner = new MainWindowViewModel { Bpm = 141.0 };
        owner.OggOnsets = waveform.Onsets;
        owner.OggDurationSeconds = waveform.DurationSeconds;

        var detected = owner.TryDetectAudioOffsetMs();

        Assert.NotNull(detected);

        var expectedMs = -shiftSeconds * 1000;
        Assert.True(
            Math.Abs(detected!.Value - expectedMs) < 6.0,
            $"{expectedMs:F1}ms 를 찾아야 하는데 {detected.Value:F1}ms 가 나왔다");
    }

    [Fact]
    public void 자동_검출은_16분음표_반칸_안에서만_고른다()
    {
        var waveform = OggPeakLoader.Load(BuildShiftedClickTrack(141.0, 24, -0.0229));

        var owner = new MainWindowViewModel { Bpm = 141.0 };
        owner.OggOnsets = waveform.Onsets;
        owner.OggDurationSeconds = waveform.DurationSeconds;

        var halfStepMs = 240.0 / 141.0 / 16.0 / 2.0 * 1000;
        var detected = owner.TryDetectAudioOffsetMs();

        Assert.NotNull(detected);
        Assert.InRange(detected!.Value, -halfStepMs, halfStepMs);
    }

    [Fact]
    public void 온셋이_없으면_검출하지_않는다()
    {
        var owner = new MainWindowViewModel { Bpm = 141.0 };
        Assert.Null(owner.TryDetectAudioOffsetMs());

        owner.OggOnsets = Array.Empty<float>();
        Assert.Null(owner.TryDetectAudioOffsetMs());
    }

    [Fact]
    public void 오프셋이_ms_와_초로_같은_값을_가리킨다()
    {
        var owner = new MainWindowViewModel { AudioOffsetMs = 22.9 };
        Assert.Equal(0.0229, owner.AudioOffsetSeconds, 9);
    }

    private static string WriteTempChart(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "chart.bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void 오프셋이_저장되고_다시_읽힌다()
    {
        var path = WriteTempChart("#TITLE t`r`n#BPM 141`r`n#WAV01 a.wav`r`n#00111:01`r`n");

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);

        owner.AudioOffsetMs = 22.9;
        var text = BmsWriter.Write(owner.Chart, owner.Title, owner.Artist, owner.Genre, owner.Bpm,
            owner.Player, owner.Rank, owner.Level, owner.WavList, path);

        Assert.Contains("#BMSEDITER_OFFSET 22.9", text);

        File.WriteAllText(path, text, new UTF8Encoding(false));

        var reloaded = new MainWindowViewModel();
        Assert.True(reloaded.LoadBms(path), reloaded.LastErrorMessage);
        Assert.Equal(22.9, reloaded.AudioOffsetMs, 3);
    }

    [Fact]
    public void 오프셋이_0이면_헤더를_쓰지_않는다()
    {
        var path = WriteTempChart("#TITLE t\r\n#BPM 141\r\n#WAV01 a.wav\r\n#00111:01\r\n");

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);

        var text = BmsWriter.Write(owner.Chart, owner.Title, owner.Artist, owner.Genre, owner.Bpm,
            owner.Player, owner.Rank, owner.Level, owner.WavList, path);

        Assert.DoesNotContain("BMSEDITER_OFFSET", text);
    }

    [Fact]
    public void 오프셋_헤더가_보존줄로_중복되지_않는다()
    {
        var path = WriteTempChart("#TITLE t\r\n#BPM 141\r\n#BMSEDITER_OFFSET 22.9\r\n#WAV01 a.wav\r\n#00111:01\r\n");

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);
        Assert.Equal(22.9, owner.AudioOffsetMs, 3);

        var text = BmsWriter.Write(owner.Chart, owner.Title, owner.Artist, owner.Genre, owner.Bpm,
            owner.Player, owner.Rank, owner.Level, owner.WavList, path);

        var occurrences = text.Split("BMSEDITER_OFFSET").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void 음수_오프셋도_왕복한다()
    {
        var path = WriteTempChart("#TITLE t\r\n#BPM 141\r\n#BMSEDITER_OFFSET -15.5\r\n#WAV01 a.wav\r\n#00111:01\r\n");

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);

        Assert.Equal(-15.5, owner.AudioOffsetMs, 3);
    }
}

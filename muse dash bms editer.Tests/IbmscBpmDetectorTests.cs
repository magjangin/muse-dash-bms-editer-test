using System;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

public sealed class IbmscBpmDetectorTests
{
    private static OggAudioData BuildClickTrack(double bpm, double seconds, int sampleRate = 44100, int channels = 2, double startSeconds = 0.0)
    {
        var totalFrames = (int)(seconds * sampleRate);
        var pcm = new byte[totalFrames * channels * sizeof(short)];
        var period = 60.0 / bpm;

        for (var frame = 0; frame < totalFrames; frame++)
        {
            var t = ((double)frame / sampleRate) - startSeconds;
            if (t < 0)
                continue;
            var since = t - (Math.Floor(t / period) * period);

            // 100Hz 감쇠 사인: 전대역·저음(50~250Hz) 모드 모두에 잡힌다.
            var value = since < 0.08 ? Math.Sin(2 * Math.PI * 100 * since) * Math.Exp(-since * 40) * 0.8 : 0.0;
            var sample = (short)(value * short.MaxValue);
            for (var c = 0; c < channels; c++)
            {
                var offset = ((frame * channels) + c) * sizeof(short);
                pcm[offset] = (byte)(sample & 0xff);
                pcm[offset + 1] = (byte)((sample >> 8) & 0xff);
            }
        }

        return new OggAudioData(pcm, sampleRate, channels, (double)totalFrames / sampleRate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 박이_0초에서_시작하면_BPM을_맞힌다(bool fullBand)
    {
        // 소수점은 정확하지 않다. cos 만 쓰는 식이라 박의 시작 위상이 템포 쪽으로 새어 나와,
        // 완벽한 120 클릭도 120.30 이 나온다(어택 곡선의 첫 스파이크가 0 번이 아니라 -1 번 칸이라
        // 위상 0.63rad 가 남고, 그걸 상쇄하려 템포가 살짝 올라간다). 정수 원본은 반올림에 가려 120 이었다.
        Assert.InRange(IbmscBpmDetector.Detect(BuildClickTrack(120.0, 30.0), fullBand), 119.5, 120.5);
    }

    [Fact]
    public void iBMSC_값은_소수점_둘째_자리_단위다()
    {
        var bpm = IbmscBpmDetector.Detect(BuildClickTrack(120.0, 30.0), fullBand: true);
        Assert.Equal(Math.Round(bpm, 2), bpm);
    }

    [Fact]
    public void 무음이면_0()
    {
        var silent = new OggAudioData(new byte[44100 * 4 * 5], 44100, 2, 5.0);
        Assert.Equal(0.0, IbmscBpmDetector.Detect(silent, fullBand: true));
        Assert.Equal(0.0, IbmscBpmDetector.Detect(silent, fullBand: false));
    }

    [Fact]
    public void 빈_음원이면_0()
    {
        var empty = new OggAudioData(Array.Empty<byte>(), 44100, 2, 0.0);
        Assert.Equal(0.0, IbmscBpmDetector.Detect(empty, fullBand: true));
    }

    [Fact]
    public void iBMSC도_200_이상은_고르지_않는다()
    {
        // 230 BPM 클릭. 원본(60~400)이면 230 이 나올 자리다.
        var bpm = IbmscBpmDetector.Detect(BuildClickTrack(230.0, 30.0), fullBand: true);
        Assert.InRange(bpm, 0.0, 199.99);
    }

    [Theory]
    [InlineData(128.37, 0.0)]
    [InlineData(164.0, 0.137)]   // 박이 0초에서 시작하지 않아도(iBMSC 가 약한 경우) 맞혀야 한다
    [InlineData(102.5, 0.31)]
    [InlineData(175.0, 0.05)]
    public void 정밀_측정은_소수점까지_맞힌다(double bpm, double start)
    {
        var detected = PreciseBpmDetector.Detect(BuildClickTrack(bpm, 60.0, sampleRate: 48000, startSeconds: start));
        Assert.InRange(detected, bpm - 0.02, bpm + 0.02);
    }

    [Fact]
    public void 정밀_측정도_200_미만으로_묶는다()
    {
        var detected = PreciseBpmDetector.Detect(BuildClickTrack(230.0, 40.0));
        Assert.InRange(detected, PreciseBpmDetector.MinBpm, PreciseBpmDetector.MaxBpm);
    }

    [Fact]
    public void 정밀_측정은_무음이면_0()
    {
        Assert.Equal(0.0, PreciseBpmDetector.Detect(new OggAudioData(new byte[44100 * 4 * 5], 44100, 2, 5.0)));
        Assert.Equal(0.0, PreciseBpmDetector.Detect(new OggAudioData(Array.Empty<byte>(), 44100, 2, 0.0)));
    }

    [Fact]
    public void 표시_문자열은_iBMSC와_정밀을_나란히_보인다()
    {
        Assert.Equal("iBMSC 123.00 · 정밀 164.06", MainWindowViewModel.FormatAutoBpm(123, 164.056));
        Assert.Equal("iBMSC 122.97 · 정밀 164.06", MainWindowViewModel.FormatAutoBpm(122.97, 164.056));
        Assert.Equal("iBMSC - · 정밀 -", MainWindowViewModel.FormatAutoBpm(0, 0.0));
    }
}

using System;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

public sealed class GridFitAdvisorTests
{
    private const int SampleRate = 48000;

    // 8분마다 치는 클릭(킥 같은 100Hz 감쇠음 + 짧은 잡음). startSeconds 만큼 늦게 시작한다.
    // sectionShift 가 있으면 8마디마다 그만큼 늦게/일찍 친다(구간 번호를 받아 초를 돌려준다).
    private static OggAudioData BuildTrack(double bpm, double seconds, double startSeconds = 0.0, Func<int, double>? sectionShift = null)
    {
        var totalFrames = (int)(seconds * SampleRate);
        var pcm = new byte[totalFrames * sizeof(short)];
        var period = 60.0 / bpm / 2.0;
        var sectionSeconds = 240.0 / bpm * 8;
        var rng = new Random(5);

        for (var frame = 0; frame < totalFrames; frame++)
        {
            var t = ((double)frame / SampleRate) - startSeconds;
            if (sectionShift is not null)
                t -= sectionShift((int)(t / sectionSeconds));
            if (t < 0)
                continue;

            var since = t - (Math.Floor(t / period) * period);
            var value = since < 0.08
                ? ((Math.Sin(2 * Math.PI * 100 * since) * 0.6) + ((rng.NextDouble() * 2 - 1) * 0.2)) * Math.Exp(-since * 40)
                : 0.0;
            var sample = (short)(Math.Clamp(value, -1, 1) * short.MaxValue);
            pcm[frame * 2] = (byte)(sample & 0xff);
            pcm[(frame * 2) + 1] = (byte)((sample >> 8) & 0xff);
        }

        return new OggAudioData(pcm, SampleRate, 1, (double)totalFrames / SampleRate);
    }

    [Fact]
    public void 격자에_맞는_곡은_파형으로_맞추라고_한다()
    {
        var attacks = GridFitAdvisor.FindAttacks(BuildTrack(150.0, 40.0));
        Assert.True(attacks.Length > 50, $"어택 {attacks.Length}개");
        Assert.Equal(GridFitAdvice.Waveform, GridFitAdvisor.Advise(attacks, ChartTimeline.Uniform(150.0), 0.0));
    }

    [Fact]
    public void 음원이_늦게_시작해도_오프셋을_맞추면_파형이다()
    {
        var attacks = GridFitAdvisor.FindAttacks(BuildTrack(150.0, 40.0, startSeconds: 0.031));
        Assert.Equal(GridFitAdvice.Listen, GridFitAdvisor.Advise(attacks, ChartTimeline.Uniform(150.0), 0.0));
        Assert.Equal(GridFitAdvice.Waveform, GridFitAdvisor.Advise(attacks, ChartTimeline.Uniform(150.0), -0.031));
    }

    [Fact]
    public void 중간부터_한쪽으로_오래_밀리면_소리로_들으라고_한다()
    {
        // With 처럼: 앞 두 구간은 맞고, 그 뒤 네 구간이 40ms 늦다.
        var attacks = GridFitAdvisor.FindAttacks(BuildTrack(150.0, 6 * 8 * 1.6, sectionShift: s => s >= 2 ? 0.040 : 0.0));
        var report = GridFitAdvisor.Measure(attacks, ChartTimeline.Uniform(150.0), 0.0);
        Assert.True(report.LongestShiftedRun >= 3, $"연속 {report.LongestShiftedRun}구간");
        Assert.Equal(GridFitAdvice.Listen, GridFitAdvisor.Advise(report));
    }

    [Fact]
    public void 구간마다_방향이_오락가락하면_파형으로_맞춰도_된다()
    {
        // 루바토·스윙 곡처럼 구간마다 ±35ms 로 번갈아 밀린다. 한쪽으로 오래 밀린 게 아니다.
        var attacks = GridFitAdvisor.FindAttacks(BuildTrack(150.0, 6 * 8 * 1.6, sectionShift: s => s % 2 == 0 ? 0.035 : -0.035));
        Assert.Equal(GridFitAdvice.Waveform, GridFitAdvisor.Advise(attacks, ChartTimeline.Uniform(150.0), 0.0));
    }

    [Fact]
    public void 두_구간만_밀리면_아직_파형이다()
    {
        var attacks = GridFitAdvisor.FindAttacks(BuildTrack(150.0, 6 * 8 * 1.6, sectionShift: s => s is 2 or 3 ? 0.040 : 0.0));
        Assert.Equal(GridFitAdvice.Waveform, GridFitAdvisor.Advise(attacks, ChartTimeline.Uniform(150.0), 0.0));
    }

    [Fact]
    public void 무음이면_판단하지_않는다()
    {
        var attacks = GridFitAdvisor.FindAttacks(new OggAudioData(new byte[SampleRate * 2 * 5], SampleRate, 1, 5.0));
        Assert.Equal(GridFitAdvice.None, GridFitAdvisor.Advise(attacks, ChartTimeline.Uniform(150.0), 0.0));
    }

    [Theory]
    [InlineData(GridFitAdvice.Waveform)]
    [InlineData(GridFitAdvice.Listen)]
    public void 안내_문구에는_숫자를_넣지_않고_둘_중_하나만_말한다(GridFitAdvice advice)
    {
        using var owner = new MainWindowViewModel { GridFitAdvice = advice };
        Assert.True(owner.IsGridFitAdviceVisible);
        Assert.False(string.IsNullOrWhiteSpace(owner.GridFitAdviceText));

        // "0.3x" 배속 권장만 예외로 둔다.
        var text = owner.GridFitAdviceText.Replace("0.3x", string.Empty);
        Assert.DoesNotContain(text, char.IsDigit);
        Assert.Equal(advice == GridFitAdvice.Listen, owner.IsListenAdvised);
    }

    [Fact]
    public void 음원이_없으면_안내를_숨긴다()
    {
        using var owner = new MainWindowViewModel();
        Assert.Equal(GridFitAdvice.None, owner.GridFitAdvice);
        Assert.False(owner.IsGridFitAdviceVisible);
    }
}

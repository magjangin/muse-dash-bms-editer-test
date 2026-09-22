using System.Collections.Generic;
using System.Linq;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 격자선 계산은 네 벌로 흩어져 있던 걸 한 곳으로 모은 자리라, 어긋나면
// 화면 전체가 조용히 틀어진다. 눈으로는 "좀 이상한데" 정도로만 보여서
// 좌표를 직접 확인해 둔다.
//
// EnumerateGridLines 는 protected 라 시험용 창구를 뚫어주는 껍데기를 쓴다.
public sealed class GridLineTests
{
    private sealed class Probe : TimelineControlBase
    {
        public IReadOnlyList<(double Position, string Kind, int Measure, double Seconds)> Lines(double length) =>
            EnumerateGridLines(length)
                .Select(line => (line.Position, line.Kind.ToString(), line.Measure, line.Seconds))
                .ToList();
    }

    // 음원 없이 마디 기준으로 그릴 때. 마디 높이 × 마디 수 = 타임라인 길이.
    private static Probe MeasureModeProbe(int measureCount = 2, int beatSplit = 4)
    {
        return new Probe
        {
            DurationSeconds = 0,
            Bpm = 120,
            MeasureCount = measureCount,
            BeatSplit = beatSplit,
            GridMeasure = 4,
            RowHeight = 10,
            VerticalZoom = 1,
            IsHorizontalView = true,
        };
    }

    [Fact]
    public void 마디_기준_가로뷰는_0에서_끝까지_고르게_긋는다()
    {
        var probe = MeasureModeProbe();
        // BeatSplit 4 / GridMeasure 4 이면 간격 배율은 1이라 마디 높이 = RowHeight = 10
        var lines = probe.Lines(20.0);

        Assert.Equal(
            new[] { 0.0, 2.5, 5.0, 7.5, 10.0, 12.5, 15.0, 17.5, 20.0 },
            lines.Select(l => l.Position));
    }

    [Fact]
    public void 마지막_마디의_닫는_선까지_그린다()
    {
        var lines = MeasureModeProbe().Lines(20.0);

        // 예전 NoteGridControl 은 마지막 닫는 선을 빠뜨려서 파형 칸과 어긋났다.
        Assert.Equal(20.0, lines[^1].Position);
        Assert.Equal("Measure", lines[^1].Kind);
        Assert.Equal(2, lines[^1].Measure);
    }

    [Fact]
    public void 마디_시작과_박자와_보조선이_구분된다()
    {
        var probe = MeasureModeProbe(measureCount: 1, beatSplit: 8);
        probe.GridMeasure = 4;

        var kinds = probe.Lines(20.0).Select(l => l.Kind).ToArray();

        // 8분할 / 4박 = 두 칸마다 박자선
        Assert.Equal(
            new[] { "Measure", "SubBeat", "Beat", "SubBeat", "Beat", "SubBeat", "Beat", "SubBeat", "Measure" },
            kinds);
    }

    [Fact]
    public void 세로뷰는_아래가_0초라_위치가_뒤집힌다()
    {
        var probe = MeasureModeProbe();
        probe.IsHorizontalView = false;

        var lines = probe.Lines(20.0);

        Assert.Equal(20.0, lines[0].Position);
        Assert.Equal(0.0, lines[^1].Position);
    }

    [Fact]
    public void 음원이_있으면_초_단위로_긋고_길이를_넘지_않는다()
    {
        var probe = new Probe
        {
            DurationSeconds = 4.0,
            Bpm = 120,          // 마디 하나 = 240/120 = 2초
            BeatSplit = 4,      // 한 칸 = 0.5초
            GridMeasure = 4,
            IsHorizontalView = true,
        };

        var lines = probe.Lines(100.0);

        Assert.Equal(
            new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 },
            lines.Select(l => l.Seconds));

        // 마지막 선이 딱 곡 끝에 놓인다
        Assert.Equal(100.0, lines[^1].Position);
        Assert.All(lines, l => Assert.InRange(l.Position, 0.0, 100.0));
    }

    [Fact]
    public void 음원_모드에서_마디_번호가_올바르게_붙는다()
    {
        var probe = new Probe
        {
            DurationSeconds = 4.0,
            Bpm = 120,
            BeatSplit = 4,
            GridMeasure = 4,
            IsHorizontalView = true,
        };

        var measureLines = probe.Lines(100.0).Where(l => l.Kind == "Measure").ToArray();

        Assert.Equal(new[] { 0, 1, 2 }, measureLines.Select(l => l.Measure));
        Assert.Equal(new[] { 0.0, 2.0, 4.0 }, measureLines.Select(l => l.Seconds));
    }

    [Fact]
    public void BeatSplit이_0이어도_터지지_않는다()
    {
        var probe = MeasureModeProbe(measureCount: 1);
        probe.BeatSplit = 0;

        var lines = probe.Lines(20.0);

        Assert.All(lines, l => Assert.Equal("Measure", l.Kind));
    }
}

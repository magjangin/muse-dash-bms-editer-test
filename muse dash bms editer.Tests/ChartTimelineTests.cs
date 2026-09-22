using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 마디 위치 <-> 시각 변환을 못 박아 두는 테스트. (알려진 문제 14·35번)
//
// 여기가 틀리면 격자도 노트도 키음 타이밍도 다 같이 틀린다. 그런데 파일은 멀쩡해서
// "열어보면 틀리게 보이는" 상태가 되고, 그걸 믿고 씽크를 맞추는 순간 진짜로 상한다.
public sealed class ChartTimelineTests : IDisposable
{
    private readonly string _directory;

    public ChartTimelineTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private string WriteChart(string content)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // ── 예전 계산과 같은 결과가 나와야 하는 경우 ──────────────────────────────

    [Theory]
    [InlineData(120.0, 0.0, 0.0)]
    [InlineData(120.0, 1.0, 2.0)]     // 240/120 = 2초/마디
    [InlineData(120.0, 2.5, 5.0)]
    [InlineData(150.0, 4.0, 6.4)]     // 240/150 = 1.6초/마디
    public void BPM이_일정하면_예전_계산과_같다(double bpm, double measurePosition, double expectedSeconds)
    {
        var timeline = ChartTimeline.Uniform(bpm);

        Assert.Equal(expectedSeconds, timeline.SecondsAt(measurePosition), 9);

        // 예전 공식과 직접 대조한다.
        Assert.Equal(measurePosition * (240.0 / bpm), timeline.SecondsAt(measurePosition), 9);
    }

    [Fact]
    public void 균일한_시간축은_변화가_없다고_알려준다()
    {
        var timeline = ChartTimeline.Uniform(120);
        Assert.False(timeline.HasBpmChanges);
        Assert.False(timeline.HasMeasureLengthChanges);
    }

    // ── 14. #xxx02 박자 변경 ─────────────────────────────────────────────────

    [Fact]
    public void 마디_길이가_짧으면_그만큼_빨리_끝난다()
    {
        // 1마디가 0.75배(3/4박자). BPM 120 이면 2초가 아니라 1.5초다.
        var lengths = new Dictionary<int, double> { [1] = 0.75 };
        var timeline = new ChartTimeline(120, lengths, Array.Empty<BpmChange>());

        Assert.Equal(2.0, timeline.SecondsAt(1.0), 9);   // 0마디는 그대로 4/4
        Assert.Equal(3.5, timeline.SecondsAt(2.0), 9);   // 2.0 + 1.5
        Assert.Equal(5.5, timeline.SecondsAt(3.0), 9);   // 그 뒤는 다시 4/4
    }

    [Fact]
    public void 마디_길이는_마디_안_비율에도_적용된다()
    {
        var lengths = new Dictionary<int, double> { [0] = 0.5 };
        var timeline = new ChartTimeline(120, lengths, Array.Empty<BpmChange>());

        Assert.Equal(0.5, timeline.SecondsAt(0.5), 9);   // 1초짜리 마디의 절반
        Assert.Equal(1.0, timeline.SecondsAt(1.0), 9);
    }

    // ── 35. #xxx03 / #xxx08 BPM 변화 ─────────────────────────────────────────

    [Fact]
    public void 마디_처음에_BPM이_바뀌면_그_마디부터_적용된다()
    {
        // 2마디 시작부터 BPM 240 (마디당 1초).
        var changes = new[] { new BpmChange(2, 0.0, 240) };
        var timeline = new ChartTimeline(120, new Dictionary<int, double>(), changes);

        Assert.Equal(4.0, timeline.SecondsAt(2.0), 9);   // 0~1마디는 2초씩
        Assert.Equal(5.0, timeline.SecondsAt(3.0), 9);   // 2마디는 1초
        Assert.Equal(6.0, timeline.SecondsAt(4.0), 9);
    }

    [Fact]
    public void 마디_한가운데에서_BPM이_바뀌어도_맞는다()
    {
        // 0마디 절반 지점에서 BPM 120 -> 240.
        var changes = new[] { new BpmChange(0, 0.5, 240) };
        var timeline = new ChartTimeline(120, new Dictionary<int, double>(), changes);

        Assert.Equal(1.0, timeline.SecondsAt(0.5), 9);   // 앞 절반: 2초의 절반
        Assert.Equal(1.5, timeline.SecondsAt(1.0), 9);   // 뒤 절반: 1초의 절반
    }

    [Fact]
    public void BPM_변화가_여러_번_있어도_누적된다()
    {
        var changes = new[]
        {
            new BpmChange(1, 0.0, 240),
            new BpmChange(2, 0.0, 60),
        };
        var timeline = new ChartTimeline(120, new Dictionary<int, double>(), changes);

        Assert.Equal(2.0, timeline.SecondsAt(1.0), 9);   // 0마디 @120 = 2초
        Assert.Equal(3.0, timeline.SecondsAt(2.0), 9);   // 1마디 @240 = 1초
        Assert.Equal(7.0, timeline.SecondsAt(3.0), 9);   // 2마디 @60  = 4초
    }

    // ── 되돌리기(초 -> 마디) ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(7.125)]
    public void 초로_바꿨다_되돌리면_제자리로_온다(double measurePosition)
    {
        var timeline = new ChartTimeline(
            140,
            new Dictionary<int, double> { [1] = 0.75, [3] = 1.5 },
            new[] { new BpmChange(2, 0.25, 200), new BpmChange(5, 0.0, 90) });

        var seconds = timeline.SecondsAt(measurePosition);
        Assert.Equal(measurePosition, timeline.MeasurePositionAt(seconds), 6);
    }

    [Fact]
    public void 음수_시각은_0으로_본다()
    {
        var timeline = ChartTimeline.Uniform(120);
        Assert.Equal(0.0, timeline.SecondsAt(-1));
        Assert.Equal(0.0, timeline.MeasurePositionAt(-1));
    }

    [Fact]
    public void BPM이_0이면_기본값으로_돌아간다()
    {
        // 이상한 값이 들어와도 0으로 나누지 않는다.
        var timeline = ChartTimeline.Uniform(0);
        Assert.Equal(120.0, timeline.BaseBpm);
        Assert.Equal(2.0, timeline.SecondsAt(1.0), 9);
    }

    // ── 파서가 채널을 실제로 읽는지 ──────────────────────────────────────────

    [Fact]
    public void 파서가_마디_길이를_읽는다()
    {
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#00102:0.75\r\n#00111:01\r\n");

        var chart = BmsParser.Parse(path).Chart;

        Assert.Equal(0.75, chart.GetMeasureLength(1));
        Assert.Equal(1.0, chart.GetMeasureLength(2));

        // 원문 보존도 그대로여야 한다. 읽는 것과 저장하는 것은 별개다.
        Assert.Contains(chart.PreservedLines, l => l.Text == "#00102:0.75");
    }

    [Fact]
    public void 파서가_채널_03의_16진수_BPM을_읽는다()
    {
        // "F0" = 240. 마디를 둘로 나눈 앞칸이므로 위치 0.0.
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#00203:F000\r\n");

        var chart = BmsParser.Parse(path).Chart;

        var change = Assert.Single(chart.BpmChanges);
        Assert.Equal(2, change.Measure);
        Assert.Equal(0.0, change.Position, 9);
        Assert.Equal(240, change.Bpm);
    }

    [Fact]
    public void 파서가_채널_08과_BPM표를_함께_읽는다()
    {
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#BPM01 187.5\r\n#00308:0001\r\n");

        var chart = BmsParser.Parse(path).Chart;

        Assert.Equal(187.5, chart.BpmTable["01"]);

        var change = Assert.Single(chart.BpmChanges);
        Assert.Equal(3, change.Measure);
        Assert.Equal(0.5, change.Position, 9);
        Assert.Equal(187.5, change.Bpm);

        // #BPMxx 는 저장할 때 되돌려야 하므로 보존줄에도 남아 있어야 한다.
        Assert.Contains(chart.PreservedLines, l => l.Text == "#BPM01 187.5");
    }

    [Fact]
    public void 확장_BPM_헤더가_기본_BPM으로_잘못_읽히지_않는다()
    {
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#BPM01 250\r\n");

        var parsed = BmsParser.Parse(path);

        Assert.Equal(120.0, parsed.Bpm);
        Assert.Equal(250.0, parsed.Chart.BpmTable["01"]);
    }

    [Fact]
    public void 차트로_만든_시간축이_파일_내용을_반영한다()
    {
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#00102:0.5\r\n#00203:F0\r\n");

        var chart = BmsParser.Parse(path).Chart;
        var timeline = ChartTimeline.FromChart(chart, 120);

        Assert.True(timeline.HasMeasureLengthChanges);
        Assert.True(timeline.HasBpmChanges);

        Assert.Equal(2.0, timeline.SecondsAt(1.0), 9);   // 0마디 4/4 @120
        Assert.Equal(3.0, timeline.SecondsAt(2.0), 9);   // 1마디 0.5배 = 1초
        Assert.Equal(4.0, timeline.SecondsAt(3.0), 9);   // 2마디 @240 = 1초
    }
}

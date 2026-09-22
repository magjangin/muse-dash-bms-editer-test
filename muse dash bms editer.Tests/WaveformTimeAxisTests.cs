using System;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 파형 버킷과 시각의 대응을 못 박아 두는 테스트. (알려진 문제 27번)
//
// 이 계열은 세 번 어긋났다. f86a58c(53ms), e19c25b(12ms), 그리고 27번(12~15ms).
// 전부 "같은 규칙을 다른 데서 다시 쓰다가" 생겼고, 눈으로는 거의 안 보이는데
// 씽크를 맞추는 기준선이라 결과는 크다. 그래서 규칙 자체에 테스트를 건다.
public sealed class WaveformTimeAxisTests
{
    [Theory]
    [InlineData(0, 10, 0.0)]
    [InlineData(1, 10, 0.1)]
    [InlineData(5, 10, 0.5)]
    [InlineData(9, 10, 0.9)]
    public void 버킷_i_는_i_나누기_count_시점에_대응한다(int index, int count, double expected)
    {
        Assert.Equal(expected, OggPeakLoader.GetBucketRatio(index, count), 12);
    }

    [Fact]
    public void 마지막_버킷은_끝이_아니라_한_칸_앞이다()
    {
        // i/(count-1) 로 잘못 쓰면 마지막 버킷이 1.0 이 되어 곡 전체가 한 칸씩 당겨진다.
        Assert.Equal(0.9999, OggPeakLoader.GetBucketRatio(9999, 10000), 12);
        Assert.NotEqual(1.0, OggPeakLoader.GetBucketRatio(9999, 10000));
    }

    [Theory]
    [InlineData(60.0, 4800)]
    [InlineData(180.0, 14400)]
    [InlineData(300.0, 20000)]
    public void 잘못된_공식과의_차이가_곡_끝에서_10ms를_넘는다(double durationSeconds, int count)
    {
        // 이 테스트는 "왜 이게 중요한가"를 숫자로 남겨둔다.
        // 옛 공식 i/(count-1) 과 옳은 공식 i/count 의 차이는 곡 끝에서 최대 duration/count 다.
        var last = count - 1;
        var correct = OggPeakLoader.GetBucketRatio(last, count) * durationSeconds;
        var buggy = (double)last / (count - 1) * durationSeconds;

        var driftMs = (buggy - correct) * 1000;

        Assert.True(driftMs > 10.0, $"곡 끝 어긋남이 {driftMs:F1}ms 로 예상보다 작다");
        Assert.True(driftMs < 20.0, $"곡 끝 어긋남이 {driftMs:F1}ms 로 예상보다 크다");
    }

    [Fact]
    public void 버킷_수가_0이면_0을_돌려준다()
    {
        Assert.Equal(0.0, OggPeakLoader.GetBucketRatio(0, 0));
        Assert.Equal(0.0, OggPeakLoader.GetBucketRatio(5, -1));
    }

    // ---------------------------------------------------------------------
    // 여기까지는 "규칙이 무엇인가"를 못 박는 테스트였다. 그런데 규칙만 있고
    // **그 규칙을 쓰는지**를 아무도 검사하지 않아서 네 번째 재발이 났다.
    // OggWaveformControl 이 파형 막대를 그릴 때만 i/(Count-1) 로 되짚었고,
    // 같은 배열을 쓰는 온셋 마커와 최대 17ms 벌어졌다.
    //
    // 아래는 그리는 쪽이 물어보는 역규칙(GetBucketRange)을 잠근다.
    // ---------------------------------------------------------------------

    // 화면 칸 크기. OggWaveformControl.DrawBlockWaveform 의 blockLength 와 같다.
    private const double BlockLength = 2.0;

    // 타임라인 길이. TimelineControlBase.GetTimelineHeight 와 같은 식이다.
    // (RowHeight 16 · BeatSplit 16 / GridMeasure 4 = 기본값)
    private static double TimelineLength(double durationSeconds, double verticalZoom) =>
        durationSeconds * 16 * verticalZoom * 4 / 2.0;

    private static int DisplayPointCount(double timelineLength) =>
        Math.Max(2, (int)Math.Ceiling(timelineLength / BlockLength));

    private static int PeakCount(double durationSeconds) =>
        Math.Clamp((int)(durationSeconds * 80.0), 32, 20000);

    [Fact]
    public void 버킷의_시간_구간을_되물으면_그_버킷이_돌아온다()
    {
        foreach (var count in new[] { 32, 100, 4800, 14400, 20000 })
        {
            for (var i = 0; i < count; i++)
            {
                var (from, to) = OggPeakLoader.GetBucketRange(
                    OggPeakLoader.GetBucketRatio(i, count),
                    OggPeakLoader.GetBucketRatio(i + 1, count),
                    count);

                Assert.True(from <= i && i < to,
                    $"count={count}: 버킷 {i} 가 자기 시간 구간에서 [{from},{to}) 로 나왔다");
            }
        }
    }

    [Theory]
    [InlineData(60.0, 4.0)]
    [InlineData(183.7, 8.0)]   // 타임라인 길이가 화면 칸으로 딱 나누어떨어지지 않는 곡
    [InlineData(300.0, 8.0)]
    [InlineData(420.0, 4.0)]
    public void 돌려준_버킷_범위가_요청한_시간_구간을_빠짐없이_덮는다(double durationSeconds, double verticalZoom)
    {
        var count = PeakCount(durationSeconds);
        var length = TimelineLength(durationSeconds, verticalZoom);

        for (var i = 0; i < DisplayPointCount(length); i++)
        {
            var start = i * BlockLength / length;
            var end = (i + 1) * BlockLength / length;

            var (from, to) = OggPeakLoader.GetBucketRange(start, end, count);

            // 앞쪽 경계: 버킷 from 이 start 를 품어야 한다.
            Assert.True(OggPeakLoader.GetBucketRatio(from, count) <= start + 1e-12,
                $"칸 {i}: 버킷 {from} 이 {start} 보다 뒤에서 시작한다");
            Assert.True(start < OggPeakLoader.GetBucketRatio(from + 1, count) + 1e-12,
                $"칸 {i}: 버킷 {from} 이 {start} 앞에서 끝난다");

            // 뒤쪽 경계: 범위가 end 까지 닿아야 한다. (마지막 칸은 1.0 을 넘어갈 수 있다)
            Assert.True(Math.Min(end, 1.0) <= OggPeakLoader.GetBucketRatio(to, count) + 1e-12,
                $"칸 {i}: 범위 [{from},{to}) 가 {end} 에 못 미친다");
        }
    }

    [Theory]
    [InlineData(300.0, 4.0)]   // 화면 칸이 버킷보다 성긴 구성
    [InlineData(420.0, 4.0)]
    public void 화면_칸이_버킷보다_성겨도_빠지는_버킷이_없다(double durationSeconds, double verticalZoom)
    {
        // 최근접 버킷 하나만 집어 오던 예전 방식에서는 킥 같은 순간 피크가 통째로 빠졌다.
        var count = PeakCount(durationSeconds);
        var length = TimelineLength(durationSeconds, verticalZoom);
        var covered = new bool[count];

        for (var i = 0; i < DisplayPointCount(length); i++)
        {
            var (from, to) = OggPeakLoader.GetBucketRange(
                i * BlockLength / length, (i + 1) * BlockLength / length, count);

            for (var b = from; b < to; b++)
                covered[b] = true;
        }

        Assert.DoesNotContain(false, covered);
    }

    [Theory]
    [InlineData(300.0, 8.0)]
    [InlineData(420.0, 8.0)]
    public void 옛_공식은_온셋_마커와_10ms_넘게_벌어졌다(double durationSeconds, double verticalZoom)
    {
        // 이 테스트는 "왜 이게 중요한가"를 숫자로 남겨둔다.
        // 옛 공식은 화면 칸을 i/(M-1) 로 되짚어 최근접 버킷 하나를 집어 왔다.
        // 같은 배열을 GetBucketRatio 로 배치하는 온셋 마커와 어긋난 양을 잰다.
        var count = PeakCount(durationSeconds);
        var length = TimelineLength(durationSeconds, verticalZoom);
        var displayPointCount = DisplayPointCount(length);

        var worstOld = 0.0;
        var worstNew = 0.0;

        for (var i = 0; i < displayPointCount; i++)
        {
            var drawnSeconds = i * BlockLength / length * durationSeconds;

            var oldRatio = (double)i / (displayPointCount - 1);
            var oldIndex = Math.Clamp((int)Math.Round(oldRatio * (count - 1)), 0, count - 1);
            worstOld = Math.Max(worstOld,
                Math.Abs((OggPeakLoader.GetBucketRatio(oldIndex, count) * durationSeconds) - drawnSeconds));

            var (from, _) = OggPeakLoader.GetBucketRange(
                i * BlockLength / length, (i + 1) * BlockLength / length, count);
            worstNew = Math.Max(worstNew,
                Math.Abs((OggPeakLoader.GetBucketRatio(from, count) * durationSeconds) - drawnSeconds));
        }

        Assert.True(worstOld * 1000 > 10.0,
            $"옛 공식 어긋남이 {worstOld * 1000:F1}ms 로 예상보다 작다");

        // 새 규칙은 "그 칸을 품은 버킷"을 돌려주므로 버킷 하나 폭을 넘지 않는다.
        var bucketMs = durationSeconds / count * 1000;
        Assert.True(worstNew * 1000 <= bucketMs + 1e-6,
            $"새 규칙 어긋남이 {worstNew * 1000:F2}ms 로 버킷 폭({bucketMs:F2}ms)을 넘는다");
    }

    [Fact]
    public void 버킷_수가_0이면_빈_범위를_돌려준다()
    {
        Assert.Equal((0, 0), OggPeakLoader.GetBucketRange(0.0, 1.0, 0));
        Assert.Equal((0, 0), OggPeakLoader.GetBucketRange(0.5, 0.6, -1));
    }

    [Fact]
    public void 범위를_벗어난_비율은_잘라서_최소_한_칸을_돌려준다()
    {
        // 마지막 화면 칸은 타임라인 끝을 조금 넘어간다. 세로 뷰에서는 그 칸이
        // 0초 아래로 내려가 startRatio 가 음수가 된다. 둘 다 잘라서 받아야 한다.
        Assert.Equal((99, 100), OggPeakLoader.GetBucketRange(0.999, 1.004, 100));
        Assert.Equal((0, 1), OggPeakLoader.GetBucketRange(-0.004, 0.001, 100));
    }
}

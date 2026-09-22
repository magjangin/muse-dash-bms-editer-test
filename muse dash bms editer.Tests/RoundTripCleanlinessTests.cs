using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 열었다 저장하기만 했을 때 파일이 "그대로"인지 보는 테스트.
// (알려진 문제 7-6·26-1·26-2·26-3번)
//
// 값이 상하는 것만 문제가 아니다. 줄이 없어지거나 없던 줄이 생기면
// 원본과 비교할 수 없게 되고, 버전 관리에 넣어둔 차트는 매 저장마다 diff 가 튄다.
public sealed class RoundTripCleanlinessTests : IDisposable
{
    private readonly string _directory;

    public RoundTripCleanlinessTests()
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

    private static string WriteBack(BmsParseResult parsed, string path) =>
        BmsWriter.Write(
            parsed.Chart,
            parsed.Chart.Header.Title,
            parsed.Chart.Header.Artist,
            parsed.Chart.Header.Genre,
            parsed.Bpm,
            Math.Clamp(parsed.Chart.Header.Player - 1, 0, 2),
            parsed.Chart.Header.Rank,
            parsed.Chart.Header.Level,
            parsed.WavItems,
            path);

    // ── 26-2. 원본에 없던 빈 헤더가 생긴다 ───────────────────────────────────

    [Fact]
    public void 값이_빈_헤더는_만들어내지_않는다()
    {
        var path = WriteChart("#BPM 120\r\n#00111:01\r\n");

        var text = WriteBack(BmsParser.Parse(path), path);

        Assert.DoesNotContain("#TITLE", text);
        Assert.DoesNotContain("#GENRE", text);
        Assert.DoesNotContain("#PLAYLEVEL", text);
        Assert.DoesNotContain("#ARTIST", text);

        // 재생에 필요한 값은 빠지면 안 된다.
        Assert.Contains("#BPM 120", text);
    }

    [Fact]
    public void 값이_있는_헤더는_그대로_쓴다()
    {
        var path = WriteChart("#TITLE 곡\r\n#ARTIST 사람\r\n#GENRE 장르\r\n#PLAYLEVEL 5\r\n#BPM 120\r\n");

        var text = WriteBack(BmsParser.Parse(path), path);

        Assert.Contains("#TITLE 곡", text);
        Assert.Contains("#ARTIST 사람", text);
        Assert.Contains("#GENRE 장르", text);
        Assert.Contains("#PLAYLEVEL 5", text);
    }

    // ── 26-1. 중복 #WAV 가 저장하면 두 줄로 늘어난다 ─────────────────────────

    [Fact]
    public void 중복_정의된_키음은_한_줄로만_저장된다()
    {
        var path = WriteChart("#BPM 120\r\n#WAV01 first.wav\r\n#WAV01 second.wav\r\n");

        var parsed = BmsParser.Parse(path);
        Assert.Equal(2, parsed.WavItems.Count);   // 파일에는 두 줄이 있었다

        var text = WriteBack(parsed, path);

        // 저장은 한 줄로. 재생에 쓰는 WavTable 과 같은 "마지막 것이 이긴다" 규칙을 따른다.
        Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("#WAV01")));
        Assert.Contains("#WAV01 second.wav", text);
        Assert.DoesNotContain("first.wav", text);
    }

    // ── 26-3. 마디 1000 이상 데이터 줄이 헤더 블록으로 올라간다 ──────────────

    [Fact]
    public void 마디_1000_이상_줄도_데이터_줄로_읽는다()
    {
        var path = WriteChart("#BPM 120\r\n#100101:0102\r\n");

        var chart = BmsParser.Parse(path).Chart;

        var preserved = Assert.Single(chart.PreservedLines);
        Assert.True(preserved.IsData, "데이터 줄로 인식하지 못했다");
        Assert.Equal(1001, preserved.Measure);
    }

    [Fact]
    public void 마디_1000_이상_줄이_헤더_블록으로_올라가지_않는다()
    {
        var path = WriteChart("#BPM 120\r\n#100101:0102\r\n#00111:01\r\n");

        var text = WriteBack(BmsParser.Parse(path), path);

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var bpmIndex = Array.FindIndex(lines, l => l.StartsWith("#BPM "));
        var bigMeasureIndex = Array.FindIndex(lines, l => l.StartsWith("#100101:"));
        var normalIndex = Array.FindIndex(lines, l => l.StartsWith("#00111:"));

        Assert.True(bigMeasureIndex > bpmIndex, "마디 1001 줄이 헤더 블록에 섞였다");
        Assert.True(bigMeasureIndex > normalIndex, "마디 1001 줄이 마디 1보다 앞에 왔다");
    }

    // ── 7-6. 파일 안의 주석이 저장할 때 사라진다 ─────────────────────────────

    [Fact]
    public void 주석_줄이_사라지지_않는다()
    {
        var path = WriteChart("*---------------------- HEADER FIELD\r\n#BPM 120\r\n#00111:01\r\n");

        var parsed = BmsParser.Parse(path);
        var text = WriteBack(parsed, path);

        Assert.Contains("*---------------------- HEADER FIELD", text);
    }

    [Fact]
    public void 주석이_데이터_줄로_잘못_읽히지_않는다()
    {
        var path = WriteChart("*주석\r\n#BPM 120\r\n#00111:01\r\n");

        var chart = BmsParser.Parse(path).Chart;

        var comment = Assert.Single(chart.PreservedLines, l => l.Text.StartsWith("*"));
        Assert.False(comment.IsData);
        Assert.Single(chart.Notes);
    }
}

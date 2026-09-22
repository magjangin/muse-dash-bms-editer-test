using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// 파서와 라이터의 계약을 못 박아 두는 테스트.
//
// 리팩토링 중에 조용히 깨지기 쉬운 것들만 골랐다. 특히
// "읽어서 다시 쓰면 원문이 살아 있는가"는 눈으로 확인하기 어렵고
// 깨져도 저장한 뒤에야 드러나서, 사람이 알아채기 전에 차트가 상한다.
public sealed class BmsRoundTripTests : IDisposable
{
    private readonly string _directory;

    public BmsRoundTripTests()
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

    private string WriteChart(string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private string TouchFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not really audio");
        return path;
    }

    [Fact]
    public void 헤더를_읽고_다시_쓰면_그대로_돌아온다()
    {
        var path = WriteChart(string.Join("\r\n",
            "#PLAYER 1",
            "#TITLE 테스트 곡",
            "#ARTIST 작곡가",
            "#GENRE 장르",
            "#BPM 157.5",
            "#RANK 3",
            "#PLAYLEVEL 12"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);

        Assert.Equal("테스트 곡", result.Header.Title);
        Assert.Equal("작곡가", result.Header.Artist);
        Assert.Equal("장르", result.Header.Genre);
        Assert.Equal(157.5, bpm);
        Assert.Equal(1, result.Header.Player);
        Assert.Equal(3, result.Header.Rank);
        Assert.Equal("12", result.Header.Level);

        // 뷰모델이 #PLAYER 를 0부터 시작하는 콤보박스 인덱스로 바꿔 들고 있다가
        // 라이터에서 다시 +1 한다. 그 왕복이 어긋나면 플레이 모드가 바뀐다.
        var text = BmsWriter.Write(result, result.Header.Title, result.Header.Artist, result.Header.Genre,
            bpm, result.Header.Player - 1, result.Header.Rank, result.Header.Level, wavItems, path);

        Assert.Contains("#PLAYER 1", text);
        Assert.Contains("#TITLE 테스트 곡", text);
        Assert.Contains("#BPM 157.5", text);
        Assert.Contains("#RANK 3", text);
        Assert.Contains("#PLAYLEVEL 12", text);
    }

    [Fact]
    public void 노트_위치는_최소공배수_분할로_정확히_복원된다()
    {
        TouchFile("kick.wav");
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#WAV01 kick.wav",
            // 3분할: 0, 1/3, 2/3
            "#00111:010101"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);

        Assert.Equal(3, result.Notes.Count);
        Assert.Collection(result.Notes.OrderBy(n => n.Position),
            n => Assert.Equal(0.0, n.Position, 9),
            n => Assert.Equal(1.0 / 3.0, n.Position, 9),
            n => Assert.Equal(2.0 / 3.0, n.Position, 9));

        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);

        // 3분할은 3칸으로 되돌아와야 한다. 2의 거듭제곱으로 반올림하면 잇단음이 깨진다.
        Assert.Contains("#00111:010101", text);
    }

    [Fact]
    public void 편집하지_않는_줄은_원문_그대로_보존된다()
    {
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#TOTAL 300",
            "#STAGEFILE title.png",
            "#BPM01 180",
            "#00002:0.75",
            "#00101:01020304",
            "#00108:0001",
            "#00151:0101",
            "#00111:01"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);
        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);

        // 확장 헤더
        Assert.Contains("#TOTAL 300", text);
        Assert.Contains("#STAGEFILE title.png", text);
        Assert.Contains("#BPM01 180", text);
        // 마디 길이 · BGM · BPM 변화 · 롱노트 채널
        Assert.Contains("#00002:0.75", text);
        Assert.Contains("#00101:01020304", text);
        Assert.Contains("#00108:0001", text);
        Assert.Contains("#00151:0101", text);
        // 편집 대상인 11번 채널만 에디터가 새로 쓴다
        Assert.Contains("#00111:01", text);
    }

    [Fact]
    public void 같은_마디에서_보존줄이_건반줄보다_앞에_온다()
    {
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#00111:01",
            "#00101:01"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);
        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);

        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        var bgmIndex = lines.FindIndex(l => l.StartsWith("#00101:"));
        var keyIndex = lines.FindIndex(l => l.StartsWith("#00111:"));

        Assert.True(bgmIndex >= 0 && keyIndex >= 0);
        Assert.True(bgmIndex < keyIndex, "보통의 BMS 파일처럼 BGM 줄이 건반 줄보다 앞에 와야 한다");
    }

    [Fact]
    public void 레인은_정의된_순서대로_출력된다()
    {
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#00115:01",
            "#00116:01",
            "#00111:01"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);
        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);

        var order = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("#001"))
            .Select(l => l.Substring(4, 2))
            .ToList();

        // LaneDefinition.CreateDefault 의 순서: 16, 11, 12, 13, 14, 15, 18
        Assert.Equal(new[] { "16", "11", "15" }, order);
    }

    [Fact]
    public void 세자리_키음_차트는_세자리로_왕복한다()
    {
        TouchFile("a.wav");
        TouchFile("b.wav");
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#WAV001 a.wav",
            "#WAV0ZZ b.wav",
            "#00111:0010ZZ"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);

        Assert.Equal(2, result.Notes.Count);
        Assert.Equal(new[] { "001", "0ZZ" }, result.Notes.Select(n => n.WavKey).ToArray());

        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);

        Assert.Contains("#WAV001 a.wav", text);
        Assert.Contains("#WAV0ZZ b.wav", text);
        Assert.Contains("#00111:0010ZZ", text);
    }

    [Fact]
    public void 두자리_키음_차트는_두자리로_왕복한다()
    {
        TouchFile("a.wav");
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#WAV01 a.wav",
            "#WAVZZ a.wav",
            "#00111:01ZZ"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);

        Assert.Equal(new[] { "01", "ZZ" }, result.Notes.Select(n => n.WavKey).ToArray());

        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);
        Assert.Contains("#00111:01ZZ", text);
    }

    [Fact]
    public void 마디_수는_건반이_없는_뒷마디까지_센다()
    {
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#00111:01",
            // 건반이 없는 BGM 전용 마디가 뒤에 더 있다
            "#04001:01"));

        var measureCount = BmsParser.Parse(path).MeasureCount;

        Assert.Equal(41, measureCount);
    }

    [Fact]
    public void 마디_수는_최소_32다()
    {
        var path = WriteChart("#BPM 120\r\n#00111:01");

        var measureCount = BmsParser.Parse(path).MeasureCount;

        Assert.Equal(32, measureCount);
    }

    [Fact]
    public void 상대경로_키음은_절대경로로_풀린다()
    {
        var expected = TouchFile(Path.Combine("sounds", "kick.wav"));
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#WAV01 sounds/kick.wav"));

        var wavItems = BmsParser.Parse(path).WavItems;

        Assert.Equal(expected, Assert.Single(wavItems).FilePath, ignoreCase: true);
    }

    [Fact]
    public void 절대경로로_풀린_키음은_저장할_때_다시_상대경로가_된다()
    {
        TouchFile(Path.Combine("sounds", "kick.wav"));
        var path = WriteChart(string.Join("\r\n",
            "#BPM 120",
            "#WAV01 sounds/kick.wav"));

        var parsed = BmsParser.Parse(path);
        var (result, bpm, wavItems) = (parsed.Chart, parsed.Bpm, parsed.WavItems);
        var text = BmsWriter.Write(result, "", "", "", bpm, 0, 2, "", wavItems, path);

        Assert.Contains(@"#WAV01 sounds\kick.wav", text);
    }

    [Fact]
    public void CP949_한글_차트를_읽는다()
    {
        var path = WriteChart("#TITLE 한글 제목\r\n#ARTIST 작곡가\r\n#BPM 120", Encoding.GetEncoding(949));

        var result = BmsParser.Parse(path).Chart;

        Assert.Equal("한글 제목", result.Header.Title);
        Assert.Equal("작곡가", result.Header.Artist);
    }

    [Fact]
    public void 없는_파일을_열면_빈_차트가_나온다()
    {
        var parsed = BmsParser.Parse(Path.Combine(_directory, "does-not-exist.bms"));
        var (result, bpm, measureCount, wavItems) = (parsed.Chart, parsed.Bpm, parsed.MeasureCount, parsed.WavItems);

        Assert.Empty(result.Notes);
        Assert.Empty(wavItems);
        Assert.Equal(120.0, bpm);
        Assert.Equal(32, measureCount);
    }

    [Fact]
    public void PLAYLEVEL은_PLAYER로_잘못_읽히지_않는다()
    {
        var path = WriteChart("#PLAYER 3\r\n#PLAYLEVEL 8\r\n#BPM 120");

        var result = BmsParser.Parse(path).Chart;

        Assert.Equal(3, result.Header.Player);
        Assert.Equal("8", result.Header.Level);
    }
}

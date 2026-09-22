using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 인코딩 감지와 저장 인코딩 유지를 못 박아 두는 테스트. (알려진 문제 8·9·19번)
//
// 이 셋은 눈으로 확인하기 가장 어려운 부류다. 제목이 깨진 건 바로 보이지만,
// 키음 파일명이 깨져서 소리가 안 나는 것은 "원래 키음이 없는 차트인가" 싶어 넘어가기 쉽고,
// 저장 인코딩이 갈아치워진 것은 다른 플레이어에서 열어보기 전에는 알 수가 없다.
public sealed class EncodingDetectionTests : IDisposable
{
    private readonly string _directory;

    public EncodingDetectionTests()
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

    private string WriteChart(string content, int codePage)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllBytes(path, Encoding.GetEncoding(codePage).GetBytes(content));
        return path;
    }

    private void TouchFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not really audio");
    }

    [Fact]
    public void ShiftJIS_차트는_제목도_키음도_깨지지_않는다()
    {
        // 파일명 대조가 인코딩 판별의 1순위 증거라, 실제 파일이 있어야 한다.
        TouchFile("キック.wav");
        TouchFile("スネア.wav");

        var path = WriteChart(
            "#TITLE 東方紅魔郷\r\n#ARTIST ZUN\r\n#BPM 150\r\n#WAV01 キック.wav\r\n#WAV02 スネア.wav\r\n", 932);

        var parsed = BmsParser.Parse(path);

        Assert.Equal(932, parsed.Encoding.CodePage);
        Assert.Equal("東方紅魔郷", parsed.Chart.Header.Title);

        // 예전에는 CP949 로 읽혀 '긌긞긏.wav' 같은 이름이 되어 키음이 하나도 안 붙었다.
        Assert.Equal(new[] { "キック.wav", "スネア.wav" }, parsed.WavItems.Select(w => Path.GetFileName(w.FilePath)));
        Assert.All(parsed.WavItems, w => Assert.True(File.Exists(w.FilePath), $"키음을 못 찾음: {w.FilePath}"));
    }

    [Fact]
    public void CP949_차트는_ShiftJIS로_오인되지_않는다()
    {
        TouchFile("킥.wav");

        var path = WriteChart("#TITLE 한글 제목\r\n#ARTIST 작곡가\r\n#BPM 120\r\n#WAV01 킥.wav\r\n", 949);

        var parsed = BmsParser.Parse(path);

        Assert.Equal(949, parsed.Encoding.CodePage);
        Assert.Equal("한글 제목", parsed.Chart.Header.Title);
        Assert.Equal("킥.wav", Path.GetFileName(Assert.Single(parsed.WavItems).FilePath));
    }

    [Fact]
    public void 비ASCII가_앞_100줄_밖에_처음_나와도_찾아낸다()
    {
        // 헤더는 전부 영문이고 #WAV 가 수백 줄인 차트가 실제로 이렇게 생겼다.
        // 예전에는 앞 100줄만 검사해서, 그 뒤의 한글이 복구 불가능한 U+FFFD 로 바뀌었다.
        var sb = new StringBuilder();
        sb.Append("#BPM 120\r\n");
        for (var i = 1; i <= 120; i++)
            sb.Append($"#WAV{i / 36:X1}{"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[i % 36]} snd{i}.wav\r\n");
        sb.Append("#TITLE 한글제목입니다\r\n");

        var path = WriteChart(sb.ToString(), 949);

        var parsed = BmsParser.Parse(path);

        Assert.Equal("한글제목입니다", parsed.Chart.Header.Title);
        Assert.DoesNotContain('�', parsed.Chart.Header.Title);
    }

    [Fact]
    public void UTF8_차트는_UTF8로_읽는다()
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, "#TITLE 한글 제목\r\n#BPM 120\r\n", new UTF8Encoding(false));

        var parsed = BmsParser.Parse(path);

        Assert.Equal(65001, parsed.Encoding.CodePage);
        Assert.Equal("한글 제목", parsed.Chart.Header.Title);
    }

    [Fact]
    public void BOM이_붙은_UTF8_차트도_읽는다()
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, "#TITLE 한글 제목\r\n#BPM 120\r\n", new UTF8Encoding(true));

        var parsed = BmsParser.Parse(path);

        Assert.Equal(65001, parsed.Encoding.CodePage);
        Assert.Equal("한글 제목", parsed.Chart.Header.Title);
    }

    [Theory]
    [InlineData(949, "한글 제목", "작곡가")]
    [InlineData(932, "東方紅魔郷", "ZUN")]
    public void 읽어낸_인코딩_그대로_저장한다(int codePage, string title, string artist)
    {
        // 예전에는 무조건 UTF-8 로 써서, 열었다 저장만 해도 원본 인코딩이 갈아치워졌다.
        var path = WriteChart($"#TITLE {title}\r\n#ARTIST {artist}\r\n#BPM 120\r\n", codePage);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.Equal(codePage, vm.DocumentEncoding.CodePage);

        var savedPath = Path.Combine(_directory, "saved.bms");
        Assert.True(vm.SaveBms(savedPath), vm.LastErrorMessage);

        var reparsed = BmsParser.Parse(savedPath);
        Assert.Equal(codePage, reparsed.Encoding.CodePage);
        Assert.Equal(title, reparsed.Chart.Header.Title);
        Assert.Equal(artist, reparsed.Chart.Header.Artist);
    }

    [Fact]
    public void 원본_인코딩이_못_담는_글자가_생기면_UTF8로_물러나고_알린다()
    {
        // CP932(일본어) 차트에 한글 제목을 넣은 경우. 그대로 쓰면 '?' 로 뭉개져 사라진다.
        var path = WriteChart("#TITLE 東方\r\n#BPM 120\r\n", 932);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.Equal(932, vm.DocumentEncoding.CodePage);

        vm.Title = "한글로 바꾼 제목";

        var savedPath = Path.Combine(_directory, "saved.bms");
        Assert.True(vm.SaveBms(savedPath), vm.LastErrorMessage);

        // 글자를 잃지 않는 쪽을 택했는지.
        var reparsed = BmsParser.Parse(savedPath);
        Assert.Equal("한글로 바꾼 제목", reparsed.Chart.Header.Title);
        Assert.Equal(65001, reparsed.Encoding.CodePage);

        // 그리고 그 사실을 조용히 넘기지 않았는지.
        Assert.False(string.IsNullOrEmpty(vm.LastWarningMessage));
        Assert.Contains("UTF-8", vm.LastWarningMessage);
    }

    [Fact]
    public void 원본_인코딩으로_담을_수_있으면_경고하지_않는다()
    {
        var path = WriteChart("#TITLE 한글 제목\r\n#BPM 120\r\n", 949);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        vm.Title = "다른 한글 제목";

        var savedPath = Path.Combine(_directory, "saved.bms");
        Assert.True(vm.SaveBms(savedPath), vm.LastErrorMessage);

        Assert.Null(vm.LastWarningMessage);
        Assert.Equal(949, BmsParser.Parse(savedPath).Encoding.CodePage);
    }

    [Fact]
    public void 새로_만든_문서는_UTF8로_저장한다()
    {
        var vm = new MainWindowViewModel();
        vm.Title = "새 차트";

        var savedPath = Path.Combine(_directory, "new.bms");
        Assert.True(vm.SaveBms(savedPath), vm.LastErrorMessage);

        Assert.Equal(65001, BmsParser.Parse(savedPath).Encoding.CodePage);
        Assert.Equal("새 차트", BmsParser.Parse(savedPath).Chart.Header.Title);
    }
}

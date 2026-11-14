using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Services;
using bms_editer.Services.Downmix;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 차트를 열 때 헛도는 일이 없는지 보는 테스트. (알려진 문제 28번)
//
// "느려졌다"는 눈으로는 잘 안 보이고, 보일 때쯤이면 이미 O(n²)이다.
// 알림 횟수는 세기 쉬우니 거기에 못을 박아 둔다.
public sealed class LoadPerformanceTests : IDisposable
{
    private readonly string _directory;

    public LoadPerformanceTests()
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

    private string WriteBigChart(int wavCount, int measureCount)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string Key(int v) => $"{digits[v / 36]}{digits[v % 36]}";

        var sb = new StringBuilder();
        sb.AppendLine("#TITLE big");
        sb.AppendLine("#BPM 150");

        for (var i = 1; i <= wavCount; i++)
            sb.AppendLine($"#WAV{Key(i)} snd{i}.wav");

        for (var m = 1; m <= measureCount; m++)
        {
            var slots = string.Concat(Enumerable.Range(0, 16).Select(k => Key(((m * 16 + k) % wavCount) + 1)));
            sb.AppendLine($"#{m:D3}11:{slots}");
        }

        var path = Path.Combine(_directory, "big.bms");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void 차트를_열_때_키음_목록_알림이_키음_개수만큼_나가지_않는다()
    {
        var path = WriteBigChart(wavCount: 300, measureCount: 100);
        var vm = new MainWindowViewModel();

        var notifications = 0;
        vm.WavList.CollectionChanged += (_, _) => notifications++;

        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);

        Assert.Equal(300, vm.WavList.Count);
        Assert.Equal(1600, vm.Chart.Notes.Count);

        // 예전에는 Clear 한 번 + Add 300번 = 301회였고, 통계·팔레트 창이
        // 그때마다 (레인 수 x 노트 수) 순회를 다시 돌았다(약 337만 회 비교).
        // 지금은 비우기 한 번 + 통째로 갈아끼우기 한 번이면 충분하다.
        Assert.True(notifications <= 2, $"키음 목록 알림이 {notifications}회 나갔다");
    }

    [Fact]
    public void 통째로_갈아끼워도_목록_내용은_같다()
    {
        var path = WriteBigChart(wavCount: 10, measureCount: 4);
        var vm = new MainWindowViewModel();

        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);

        Assert.Equal(10, vm.WavList.Count);
        Assert.Equal("01", vm.WavList[0].Key);
        Assert.Equal("0A", vm.WavList[9].Key);
        Assert.Same(vm.WavList[0], vm.SelectedWavItem);
    }

    [Fact]
    public void 컨트롤_패널은_한_번_열린_뒤에도_집계가_맞는다()
    {
        // 알림을 줄이면서 갱신까지 빠뜨리면 더 나쁘다. 결과가 맞는지 같이 본다.
        var path = WriteBigChart(wavCount: 10, measureCount: 4);
        var vm = new MainWindowViewModel();
        var stats = new ControlPanelViewModel(vm);

        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);

        Assert.Equal(vm.Chart.Notes.Count, stats.TotalCount);
        Assert.NotEmpty(stats.WavStats);
        Assert.All(stats.WavStats, s => Assert.NotEqual("(등록되지 않은 번호)", s.FileName));
    }

    [Fact]
    public void AddWavs_여러_파일을_한번의_알림과_한번의_되돌리기로_추가한다()
    {
        var path = WriteBigChart(wavCount: 2, measureCount: 1);
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);

        var files = new[]
        {
            Path.Combine(_directory, "sample_a.ogg"),
            Path.Combine(_directory, "sample_b.ogg"),
            Path.Combine(_directory, "sample_c.ogg"),
        };
        foreach (var file in files)
            File.WriteAllBytes(file, Array.Empty<byte>());

        var initialCount = vm.WavList.Count;
        var notifications = 0;
        NotifyCollectionChangedAction? lastAction = null;
        vm.WavList.CollectionChanged += (_, e) =>
        {
            notifications++;
            lastAction = e.Action;
        };

        var added = vm.AddWavs(files);

        Assert.Equal(3, added);
        Assert.Equal(1, notifications);
        Assert.Equal(NotifyCollectionChangedAction.Reset, lastAction);
        Assert.Equal(initialCount + 3, vm.WavList.Count);
        Assert.Same(vm.WavList[^1], vm.SelectedWavItem);

        vm.UndoCommand.Execute(null);
        Assert.Equal(initialCount, vm.WavList.Count);
    }

    [Fact]
    public void ReadWithChart_단일_읽기는_개별_Read와_Parse_결과와_일치한다()
    {
        var kbPath = Path.Combine(_directory, "keyboard_sample.bms");
        File.WriteAllText(kbPath, """
            #PLAYER 1
            #TITLE Keyboard Test
            #BPM 140
            #WAV01 kick.ogg
            #WAV02 ln_start.ogg
            #00111:0100
            #00113:0200
            #00213:0200
            """.Replace("\r\n", "\n"), Encoding.UTF8);

        var separateSource = KeyboardChartReader.Read(kbPath);
        var separateParsed = BmsParser.Parse(kbPath);
        var (combinedSource, combinedParsed) = KeyboardChartReader.ReadWithChart(kbPath);

        Assert.Equal(separateSource.Title, combinedSource.Title);
        Assert.Equal(separateSource.Bpm, combinedSource.Bpm);
        Assert.Equal(separateSource.Notes.Count, combinedSource.Notes.Count);
        Assert.Equal(separateSource.WavTexts.Count, combinedSource.WavTexts.Count);

        Assert.Equal(separateParsed.Chart.Notes.Count, combinedParsed.Chart.Notes.Count);
        Assert.Equal(separateParsed.Chart.Header.Title, combinedParsed.Chart.Header.Title);
        Assert.Equal(separateParsed.Chart.WavTable.Count, combinedParsed.Chart.WavTable.Count);
    }

    [Fact]
    public void 헤더_속성_변경은_즉시_Chart_Header에_반영된다()
    {
        var vm = new MainWindowViewModel();

        vm.Title = "New Title";
        vm.Artist = "New Artist";
        vm.Genre = "Electronic";
        vm.Level = "9";
        vm.Player = 2;
        vm.Rank = 1;

        Assert.Equal("New Title", vm.Chart.Header.Title);
        Assert.Equal("New Artist", vm.Chart.Header.Artist);
        Assert.Equal("Electronic", vm.Chart.Header.Genre);
        Assert.Equal("9", vm.Chart.Header.Level);
        Assert.Equal(3, vm.Chart.Header.Player);
        Assert.Equal(1, vm.Chart.Header.Rank);
    }
}

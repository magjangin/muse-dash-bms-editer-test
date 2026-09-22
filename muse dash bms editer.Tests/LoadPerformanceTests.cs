using System;
using System.IO;
using System.Linq;
using System.Text;
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
}

using System;
using System.IO;
using System.Text;
using bms_editer.Models;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// "고친 것이 있는지"(dirty) 추적을 못 박아 두는 테스트. (알려진 문제 13번)
//
// 이 표시가 틀리면 두 방향으로 다 나쁘다. 헐거우면 작업을 잃고,
// 빡빡하면 아무것도 안 고쳤는데 확인창이 떠서 사용자가 확인창 자체를 무시하게 된다.
public sealed class DirtyTrackingTests : IDisposable
{
    private readonly string _directory;

    public DirtyTrackingTests()
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

    private string WriteChart(string content = "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:01\r\n")
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private MainWindowViewModel LoadedViewModel(out string path)
    {
        path = WriteChart();
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        return vm;
    }

    [Fact]
    public void 새_문서는_깨끗하다()
    {
        Assert.False(new MainWindowViewModel().IsDirty);
    }

    [Fact]
    public void 열자마자는_깨끗하다()
    {
        // 예전에는 "내용이 있는지"로만 판단해서, 열기만 해도 확인창이 떴다.
        var vm = LoadedViewModel(out _);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void 노트를_찍으면_더러워진다()
    {
        var vm = LoadedViewModel(out _);
        vm.SelectedWavItem = vm.WavList[0];

        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("11", 2, 0.0));

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void 헤더를_고치면_더러워진다()
    {
        var vm = LoadedViewModel(out _);
        vm.Title = "바뀐 제목";
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void BPM을_고치면_더러워진다()
    {
        var vm = LoadedViewModel(out _);
        vm.Bpm = 150;
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void 노트를_지우면_더러워진다()
    {
        var vm = LoadedViewModel(out _);
        vm.DeleteNotes(vm.Notes);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void 저장하면_다시_깨끗해진다()
    {
        var vm = LoadedViewModel(out var path);
        vm.Title = "바뀐 제목";
        Assert.True(vm.IsDirty);

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void 저장에_실패하면_더러운_채로_남는다()
    {
        var vm = LoadedViewModel(out var path);
        vm.Title = "바뀐 제목";

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(vm.SaveBms(path));
        }

        // 여기서 깨끗해지면 창을 닫을 때 경고 없이 작업이 사라진다.
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void 화면_보기_설정만_바꾸는_것은_문서_변경이_아니다()
    {
        var vm = LoadedViewModel(out _);

        vm.BeatSplit = 32;
        vm.VerticalZoom = 6;
        vm.IsHorizontalView = true;
        vm.IsCircleNoteShape = true;
        vm.FollowPlaybackCursor = false;

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void 제목_표시줄이_파일명과_수정_표시를_보여준다()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal("제목 없음 - bms editer", vm.WindowTitle);

        vm.LoadBms(WriteChart());
        Assert.Equal("chart.bms - bms editer", vm.WindowTitle);

        vm.Title = "바뀐 제목";
        Assert.Equal("*chart.bms - bms editer", vm.WindowTitle);
    }
}

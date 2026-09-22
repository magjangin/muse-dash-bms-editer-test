using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using bms_editer;
using bms_editer.Models;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 되돌리기가 **창에서** 닿는지.
//
// 뷰모델 테스트는 UndoCommand 가 도는 것까지만 본다. 메뉴 머리말이 바인딩에서 조용히 실패하거나
// 버튼이 명령에 안 붙어 있으면 빌드도 예외도 멀쩡한데 눌러도 아무 일이 없다.
// 이 저장소가 그걸로 두 번 당했다(WindowSmokeTests 머리말 참고).
public sealed class UndoUiSmokeTests : IDisposable
{
    private readonly string _directory;

    public UndoUiSmokeTests()
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

    private MainWindowViewModel LoadedChart()
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(
            path,
            "#TITLE 되돌리기\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00113:0100\r\n#00213:0100\r\n",
            new UTF8Encoding(false));

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    // 도구 모음의 되돌리기 / 다시 하기 단추.
    private static (Button Undo, Button Redo) HistoryButtons(Window window)
    {
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        var undo = buttons.Single(b => (b.Content as string) == "↩");
        var redo = buttons.Single(b => (b.Content as string) == "↪");
        return (undo, redo);
    }

    [Fact]
    public void 되돌릴_것이_없으면_단추가_꺼져_있다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var window = new MainWindow { DataContext = LoadedChart() };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var (undo, redo) = HistoryButtons(window);
        Assert.False(undo.IsEffectivelyEnabled, "방금 연 문서인데 되돌리기가 켜져 있다");
        Assert.False(redo.IsEffectivelyEnabled);

        window.Close();
    });

    [Fact]
    public void 편집하면_단추가_켜지고_눌러서_되돌릴_수_있다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var owner = LoadedChart();
        var window = new MainWindow { DataContext = owner };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        owner.SetNoteSelection(owner.Chart.Notes.ToArray());
        owner.DeleteSelectedNotesCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var (undo, redo) = HistoryButtons(window);
        Assert.True(undo.IsEffectivelyEnabled);
        Assert.False(redo.IsEffectivelyEnabled);
        Assert.Empty(owner.Chart.Notes);

        // 화면의 단추로 되돌린다. 뷰모델을 직접 부르면 배선이 끊겨도 통과한다.
        undo.Command!.Execute(undo.CommandParameter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, owner.Chart.Notes.Count);
        Assert.True(redo.IsEffectivelyEnabled);

        window.CaptureRenderedFrame()?.Save(
            Path.Combine(HoldTestSupport.ArtifactDirectory("muse-dash-holds"), "undo.png"));

        window.Close();
    });

    [Fact]
    public void 메뉴에_무엇이_되돌아가는지_적혀_있다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var owner = LoadedChart();
        var window = new MainWindow { DataContext = owner };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        owner.SetNoteSelection(owner.Chart.Notes.ToArray());
        owner.DeleteSelectedNotesCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 닫힌 메뉴의 항목은 아직 그려지지 않아 시각 트리에 없다. 논리 트리로 본다.
        // 머리말이 바인딩에서 조용히 실패하면 빈 칸이 뜬다.
        var headers = window.GetLogicalDescendants()
            .OfType<MenuItem>()
            .Select(m => m.Header as string)
            .Where(h => h is not null)
            .ToList();

        Assert.Contains("되돌리기 (노트 삭제)", headers);
        Assert.Contains("다시 하기", headers);

        window.Close();
    });
}

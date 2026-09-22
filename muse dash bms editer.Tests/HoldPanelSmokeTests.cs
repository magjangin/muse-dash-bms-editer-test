using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using bms_editer;
using bms_editer.Models;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 검사기 패널이 실제 창에 뜨고, 진단이 목록으로 보이는지.
//
// 뷰모델만 보면 "진단 1건"까지는 통과하는데, 바인딩이 빠져 화면에 아무것도 안 뜨는 경우를
// 못 잡는다. 이 프로젝트는 그걸로 두 번 당했다(WindowSmokeTests 머리말 참고).
// 그래서 파일을 실제로 읽어 창을 띄우고 목록을 확인한다.
public sealed class HoldPanelSmokeTests
{
    // 홀드 시작 3개에 끝 2개. 마지막 시작은 짝이 없어 🔴 로 잡혀야 한다.
    private static MainWindowViewModel LoadChartWithBrokenHold()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "chart.bms");
        File.WriteAllText(
            path,
            "#TITLE 홀드검사\r\n#BPM 120\r\n" +
            "#WAV01 1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.48.wav\r\n" +
            "#WAV02 1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.48.wav\r\n" +
            "#00113:0100\r\n" +
            "#00213:0200\r\n" +
            "#00313:0100\r\n",
            new UTF8Encoding(false));

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);
        return owner;
    }

    [Fact]
    public void 불러오면_짝을_읽고_어긋난_곳을_목록으로_보여준다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var owner = LoadChartWithBrokenHold();

        // 파일을 여는 것만으로 짝이 읽혀야 한다. 사용자가 따로 눌러야 하는 버튼은 없다.
        Assert.Single(owner.HoldLinks);
        Assert.True(owner.HasHoldDiagnostics);
        Assert.Equal("홀드 짝 1개 · 🔴 문제 1", owner.HoldSummaryText);

        var diagnostic = Assert.Single(owner.HoldDiagnostics);
        Assert.Equal(HoldDiagnosticKind.OrphanHead, diagnostic.Kind);
        Assert.Equal(3, diagnostic.Note.Measure);
        Assert.Contains("짝(종료 노트)을 찾지 못했습니다", diagnostic.Message);

        var window = new MainWindow { DataContext = owner };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // 진단 목록이 실제로 화면에 떠 있고 내용이 채워졌는지.
        var list = window.GetVisualDescendants()
            .OfType<ListBox>()
            .Single(l => l.ItemsSource is System.Collections.Generic.IReadOnlyList<HoldDiagnostic>);

        Assert.True(list.IsEffectivelyVisible);
        Assert.Single(list.ItemsSource!.Cast<object>());

        // 줄을 고르면 그 노트가 선택돼야 격자가 그 자리로 간다.
        owner.SelectedHoldDiagnostic = diagnostic;
        Assert.Same(diagnostic.Note, Assert.Single(owner.SelectedNotes));

        // 눈으로 확인할 그림을 남긴다. %TEMP%\bms-editer-tests\muse-dash-holds\panel.png
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(
            Path.Combine(HoldTestSupport.ArtifactDirectory("muse-dash-holds"), "panel.png"));

        window.Close();
    });

    [Fact]
    public void 짝이_다_맞으면_검사기가_자리를_차지하지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "chart.bms");
        File.WriteAllText(
            path,
            "#TITLE 정상\r\n#BPM 120\r\n" +
            "#WAV01 1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.48.wav\r\n" +
            "#WAV02 1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.48.wav\r\n" +
            "#00113:0100\r\n#00213:0200\r\n",
            new UTF8Encoding(false));

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);

        Assert.Single(owner.HoldLinks);
        Assert.False(owner.HasHoldDiagnostics);
        Assert.Equal("홀드 짝 1개 · 문제 없음", owner.HoldSummaryText);

        var window = new MainWindow { DataContext = owner };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var lists = window.GetVisualDescendants()
            .OfType<ListBox>()
            .Where(l => l.ItemsSource is System.Collections.Generic.IReadOnlyList<HoldDiagnostic>)
            .ToList();

        Assert.All(lists, list => Assert.False(list.IsEffectivelyVisible));

        window.Close();
    });
}

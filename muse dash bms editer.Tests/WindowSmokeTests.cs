using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using bms_editer.ViewModels;
using bms_editer.Views;
using Xunit;

namespace bms_editer.Tests;

// 창이 실제로 열리고 내용이 그려지는지 보는 테스트.
//
// 왜 필요한가:
// XAML 이 깨져도 빌드는 통과하고 예외도 안 난다. 실제로 두 번 당했다.
//   * 명령을 조상 바인딩으로 끌어왔더니 컴파일된 바인딩에서 조용히 실패해,
//     Command 가 null 인 버튼이 됐다. 눌러도 아무 일이 없었다.
//   * 버튼 스타일이 테마 템플릿에 가려 버튼처럼 보이지도 않았다.
// 둘 다 뷰모델 테스트는 통과했다. 창을 실제로 만들어 봐야 잡힌다.
//
// 렌더링까지 실제로 돈다(HeadlessAppFixture 가 Skia + UseHeadlessDrawing=false).
// 레이아웃이나 바인딩이 터지면 여기서 예외가 난다.
public sealed class WindowSmokeTests
{
    // Avalonia 는 자기 스레드에서만 창을 만들 수 있다. 세션 하나를 어셈블리 단위로 공유한다.
    private static void RunOnUiThread(Action body) =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(WindowSmokeTests).Assembly)
            .Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static MainWindowViewModel LoadedOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "chart.bms");
        File.WriteAllText(
            path,
            "#TITLE 스모크\r\n#BPM 120\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n" +
            "#00111:01020100\r\n#00212:02020000\r\n",
            new UTF8Encoding(false));

        var owner = new MainWindowViewModel();
        Assert.True(owner.LoadBms(path), owner.LastErrorMessage);
        return owner;
    }

    private static void ShowAndDraw(Window window)
    {
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void 통계_창이_열리고_집계를_보여준다() => RunOnUiThread(() =>
    {
        var viewModel = new NoteStatsViewModel(LoadedOwner());
        var window = new NoteStatsWindow(viewModel);

        ShowAndDraw(window);

        // 레인별·키음별 목록이 실제로 채워졌는지.
        Assert.Equal(5, viewModel.TotalCount);
        Assert.NotEmpty(viewModel.Stats);
        Assert.Equal(2, viewModel.WavStats.Count);

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.Equal(2, list.ItemsSource!.Cast<object>().Count());
        Assert.True(list.IsEffectivelyVisible);
    });

    [Fact]
    public void 컨트롤_패널이_열리고_집계를_보여준다() => RunOnUiThread(() =>
    {
        var viewModel = new ControlPanelViewModel(LoadedOwner());
        var window = new ControlPanelWindow(viewModel);

        ShowAndDraw(window);

        // 레인별·키음별 목록이 실제로 채워졌는지.
        Assert.Equal(5, viewModel.TotalCount);
        Assert.NotEmpty(viewModel.Stats);
        Assert.Equal(2, viewModel.WavStats.Count);

        var lists = window.GetVisualDescendants().OfType<ListBox>().ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, list => Assert.True(list.IsEffectivelyVisible));
        Assert.Equal(2, lists[1].ItemsSource!.Cast<object>().Count());
    });

    // 창에서 명령까지 닿는 길이 실제로 이어져 있는지.
    //
    // 예전에 이 자리에서 끊어졌다. 목록 항목 안에서 조상 바인딩으로 명령을 끌어왔더니
    // 컴파일된 바인딩에서 조용히 실패해 Command 가 null 인 버튼이 됐고, 빌드도 예외도
    // 멀쩡했다. 그래서 뷰모델 테스트가 아니라 **창을 실제로 만들어** 버튼을 확인한다.
    [Fact]
    public void 컨트롤_패널의_작업_버튼에_명령이_붙어_있다() => RunOnUiThread(() =>
    {
        var viewModel = new ControlPanelViewModel(LoadedOwner());
        var window = new ControlPanelWindow(viewModel);
        ShowAndDraw(window);

        var buttons = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("action"))
            .ToList();

        Assert.Equal(6, buttons.Count);
        Assert.All(buttons, button => Assert.NotNull(button.Command));
    });

    [Fact]
    public void 컨트롤_패널은_편집을_따라_다시_집계한다() => RunOnUiThread(() =>
    {
        var owner = LoadedOwner();
        var viewModel = new ControlPanelViewModel(owner);
        var window = new ControlPanelWindow(viewModel);
        ShowAndDraw(window);

        Assert.Equal(5, viewModel.TotalCount);

        owner.DeleteNotes(owner.Notes);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, viewModel.TotalCount);
        Assert.Empty(viewModel.WavStats);
    });

    [Fact]
    public void 검색_창이_열린다() => RunOnUiThread(() =>
    {
        var viewModel = new NoteSearchViewModel(LoadedOwner());
        var window = new NoteSearchWindow(viewModel);

        ShowAndDraw(window);

        Assert.NotEmpty(viewModel.Lanes);
    });

    [Fact]
    public void 키음_팔레트_창이_열린다() => RunOnUiThread(() =>
    {
        var viewModel = new WavPaletteViewModel(LoadedOwner());
        var window = new WavPaletteWindow(viewModel);

        ShowAndDraw(window);

        Assert.Equal(2, viewModel.Items.Count);
    });

    [Fact]
    public void 확인창이_열린다() => RunOnUiThread(() =>
    {
        // 3지선다 대화상자는 창 닫기 확인에 쓰인다. 여기서 터지면 저장 안 한 작업이 걸린다.
        var window = new ConfirmWindow();

        ShowAndDraw(window);

        Assert.True(window.IsVisible);
    });

    [Fact]
    public void 우클릭_시_재생이_멈춘다() => RunOnUiThread(() =>
    {
        var owner = LoadedOwner();
        var window = new MainWindow { DataContext = owner };
        ShowAndDraw(window);
        var vm = (MainWindowViewModel)window.DataContext!;

        // 1) 파형 클릭 (X = 100)
        vm.IsPlaying = true;
        window.MouseDown(new Avalonia.Point(100, 100), Avalonia.Input.MouseButton.Right);
        Assert.False(vm.IsPlaying);

        // 2) 일반 모드에서 격자 클릭 (IsEditMode = false)
        vm.IsPlaying = true;
        window.MouseDown(new Avalonia.Point(300, 100), Avalonia.Input.MouseButton.Right);
        Assert.False(vm.IsPlaying);

        // 3) 편집 모드(IsEditMode = true)에서 노트 영역 우클릭
        vm.IsPlaying = true;
        vm.IsEditMode = true;
        window.MouseDown(new Avalonia.Point(260, 120), Avalonia.Input.MouseButton.Right);
        Assert.False(vm.IsPlaying);

        // 4) 스크롤뷰어 빈 공간(레인 오른쪽 X = 800) 우클릭
        vm.IsPlaying = true;
        window.MouseDown(new Avalonia.Point(800, 300), Avalonia.Input.MouseButton.Right);
        Assert.False(vm.IsPlaying);

        // 5) 윈도우 상단 툴바 영역 우클릭
        vm.IsPlaying = true;
        window.MouseDown(new Avalonia.Point(50, 40), Avalonia.Input.MouseButton.Right);
        Assert.False(vm.IsPlaying);
    });

    [Fact]
    public void 팔레트_버튼_클릭_시_연필_아이콘이_활성화된다() => RunOnUiThread(() =>
    {
        var owner = LoadedOwner();
        owner.IsEditMode = false;
        var window = new MainWindow { DataContext = owner };
        ShowAndDraw(window);

        Assert.False(owner.IsEditMode);

        var paletteButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content as string == "🎨");

        paletteButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.True(owner.IsEditMode);
    });
}

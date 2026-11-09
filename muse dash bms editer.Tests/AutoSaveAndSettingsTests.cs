using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 한 곡에 4시간이 드는 도구의 안전장치. 복구가 틀리면 "복구했는데 키음이 다 사라진" 상태가 되고,
// 그건 자동 저장이 없는 것보다 나쁘다. 그래서 경로와 인코딩까지 왕복시켜 본다.
public sealed class AutoSaveAndSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataRoot;
    private readonly string _songFolder;

    public AutoSaveAndSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        _dataRoot = Path.Combine(_root, "data");
        _songFolder = Path.Combine(_root, "song");
        Directory.CreateDirectory(Path.Combine(_songFolder, "1번 씬 wav폴더"));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteChart(Encoding? encoding = null, string title = "곡", string body = "#00113:0100\r\n")
    {
        File.WriteAllBytes(Path.Combine(_songFolder, "1번 씬 wav폴더", "010101_a.wav"), Array.Empty<byte>());

        var path = Path.Combine(_songFolder, "chart.bms");
        File.WriteAllText(
            path,
            $"#TITLE {title}\r\n#BPM 120\r\n#WAV01 1번 씬 wav폴더\\010101_a.wav\r\n{body}",
            encoding ?? new UTF8Encoding(false));
        return path;
    }

    private MainWindowViewModel NewVm() => new() { AutoSave = new AutoSaveStore(_dataRoot) };

    private static void Edit(MainWindowViewModel vm)
    {
        vm.SelectedWavItem = vm.WavList[0];
        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("14", 2, 0.5));
    }

    // 프로세스가 죽은 상황: 뷰모델을 정리하지 않고 잠금만 풀린다.
    private static void SimulateCrash(MainWindowViewModel vm) => vm.AutoSave!.Dispose();

    private AutoSaveEntry FindSingle()
    {
        using var scanner = new AutoSaveStore(_dataRoot);
        return Assert.Single(scanner.FindRecoverable());
    }

    [Fact]
    public async Task 비정상_종료_뒤_복구하면_노트와_키음_경로가_그대로다()
    {
        var path = WriteChart();
        var crashed = NewVm();
        Assert.True(crashed.LoadBms(path), crashed.LastErrorMessage);
        var originalWav = crashed.WavList[0].FilePath;
        Edit(crashed);

        await crashed.AutoSaveNowAsync();
        SimulateCrash(crashed);

        var vm = NewVm();
        Assert.True(vm.RecoverAutoSave(FindSingle()), vm.LastErrorMessage);

        Assert.Equal(2, vm.Chart.Notes.Count);
        Assert.Equal(originalWav, vm.WavList[0].FilePath);
        Assert.False(vm.WavList[0].IsPathGuessed);
        Assert.Equal(path, vm.CurrentFilePath);
        Assert.True(vm.IsDirty, "복구한 문서는 아직 저장되지 않은 상태다");
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public async Task 복구해도_원본_파일은_저장하기_전까지_그대로다()
    {
        var path = WriteChart();
        var before = File.ReadAllBytes(path);
        var crashed = NewVm();
        crashed.LoadBms(path);
        Edit(crashed);
        await crashed.AutoSaveNowAsync();
        SimulateCrash(crashed);

        var vm = NewVm();
        vm.RecoverAutoSave(FindSingle());

        Assert.Equal(before, File.ReadAllBytes(path));
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public async Task 복구한_문서는_원본_인코딩으로_저장된다()
    {
        var cp949 = Encoding.GetEncoding(949);
        var path = WriteChart(cp949, "한글제목");
        var crashed = NewVm();
        crashed.LoadBms(path);
        Assert.Equal(949, crashed.DocumentEncoding.CodePage);
        Edit(crashed);
        await crashed.AutoSaveNowAsync();
        SimulateCrash(crashed);

        var vm = NewVm();
        vm.RecoverAutoSave(FindSingle());

        Assert.Equal(949, vm.DocumentEncoding.CodePage);
        Assert.Equal("한글제목", vm.Title);
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
        Assert.Contains("한글제목", cp949.GetString(File.ReadAllBytes(path)));
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public async Task 저장한_적_없는_문서도_복구된다()
    {
        var wav = Path.Combine(_songFolder, "1번 씬 wav폴더", "010101_a.wav");
        File.WriteAllBytes(wav, Array.Empty<byte>());

        var crashed = NewVm();
        Assert.True(crashed.AddWav(wav), crashed.LastErrorMessage);
        Edit(crashed);
        await crashed.AutoSaveNowAsync();
        SimulateCrash(crashed);

        var vm = NewVm();
        Assert.True(vm.RecoverAutoSave(FindSingle()), vm.LastErrorMessage);

        Assert.Null(vm.CurrentFilePath);
        Assert.Single(vm.Chart.Notes);
        Assert.Equal(Path.GetFullPath(wav), Path.GetFullPath(vm.WavList[0].FilePath));
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public async Task 복구한_뒤에_또_죽어도_복구_지점이_남아_있다()
    {
        var path = WriteChart();
        var crashed = NewVm();
        crashed.LoadBms(path);
        Edit(crashed);
        await crashed.AutoSaveNowAsync();
        SimulateCrash(crashed);

        var recovered = NewVm();
        recovered.RecoverAutoSave(FindSingle());
        SimulateCrash(recovered);

        Assert.Equal(2, FindSingleNotes());

        int FindSingleNotes() => FindSingle().Meta.NoteCount;
    }

    [Fact]
    public async Task 저장하면_자동_저장이_지워져_다음_실행에서_묻지_않는다()
    {
        var path = WriteChart();
        var vm = NewVm();
        vm.LoadBms(path);
        Edit(vm);
        await vm.AutoSaveNowAsync();

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
        vm.AutoSave!.Dispose();

        using var scanner = new AutoSaveStore(_dataRoot);
        Assert.Empty(scanner.FindRecoverable());
    }

    [Fact]
    public async Task 저장_안_함으로_닫으면_다음_실행에서_묻지_않는다()
    {
        var vm = NewVm();
        vm.LoadBms(WriteChart());
        Edit(vm);
        await vm.AutoSaveNowAsync();

        vm.DiscardAutoSave();
        vm.AutoSave!.Dispose();

        using var scanner = new AutoSaveStore(_dataRoot);
        Assert.Empty(scanner.FindRecoverable());
    }

    [Fact]
    public async Task 고친_것이_없으면_자동_저장하지_않는다()
    {
        var vm = NewVm();
        vm.LoadBms(WriteChart());

        await vm.AutoSaveNowAsync();

        Assert.False(Directory.Exists(vm.AutoSave!.AutoSaveRoot));
        vm.AutoSave.Dispose();
    }

    [Fact]
    public void 다른_창이_쓰고_있는_자동_저장은_복구_대상이_아니다()
    {
        using var running = new AutoSaveStore(_dataRoot);
        running.Write(running.CurrentSessionId, "#BPM 120\r\n", Meta());

        using var other = new AutoSaveStore(_dataRoot);
        Assert.Empty(other.FindRecoverable());
    }

    [Fact]
    public void 자동_저장은_최근_다섯_벌만_남긴다()
    {
        using var store = new AutoSaveStore(_dataRoot);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 9; i++)
            store.Write(store.CurrentSessionId, $"#BPM {100 + i}\r\n", Meta(start.AddMinutes(i)));

        var folder = Path.Combine(store.AutoSaveRoot, store.CurrentSessionId);
        var files = Directory.GetFiles(folder, "*.bms").OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.Equal(AutoSaveStore.MaxGenerations, files.Length);
        Assert.Contains("108", File.ReadAllText(files[^1]));
    }

    [Fact]
    public void 문서가_바뀐_뒤에_도착한_자동_저장은_버린다()
    {
        using var store = new AutoSaveStore(_dataRoot);
        var stale = store.CurrentSessionId;
        store.DiscardCurrent();

        store.Write(stale, "#BPM 120\r\n", Meta());

        Assert.False(Directory.Exists(Path.Combine(store.AutoSaveRoot, stale)));
    }

    [Fact]
    public void 저장_전_내용은_세대별로_남고_열었을_때의_원본은_따로_남는다()
    {
        var path = WriteChart(title: "처음");
        var vm = NewVm();
        vm.LoadBms(path);
        var folder = vm.AutoSave!.BackupFolderFor(path);

        for (var i = 0; i < AutoSaveStore.MaxBackups + 3; i++)
        {
            vm.Title = $"고친제목{i}";
            Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
            System.Threading.Thread.Sleep(3);
        }

        Assert.Contains("처음", File.ReadAllText(Path.Combine(folder, "original.bms")));
        Assert.Equal(AutoSaveStore.MaxBackups + 1, Directory.GetFiles(folder, "*.bms").Length);
        vm.AutoSave.Dispose();
    }

    [Fact]
    public void 밖에서_파일을_고치면_알아채고_한_번만_알린다()
    {
        var path = WriteChart();
        var vm = NewVm();
        vm.LoadBms(path);
        Assert.False(vm.HasExternalChange());

        File.AppendAllText(path, "#00213:01\r\n");

        Assert.True(vm.HasExternalChange());
        Assert.True(vm.ShouldNotifyExternalChange());
        Assert.False(vm.ShouldNotifyExternalChange());
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public void 자기가_저장한_것은_밖에서_고친_것으로_치지_않는다()
    {
        var path = WriteChart();
        var vm = NewVm();
        vm.LoadBms(path);
        vm.Title = "고침";

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        Assert.False(vm.HasExternalChange());
        vm.AutoSave!.Dispose();
    }

    private MainWindowViewModel ChartWithNotes(int count)
    {
        var slots = string.Concat(Enumerable.Repeat("01", count));
        var vm = NewVm();
        Assert.True(vm.LoadBms(WriteChart(body: $"#00113:{slots}\r\n")), vm.LastErrorMessage);
        return vm;
    }

    [Fact]
    public async Task 많은_노트의_일괄_삭제는_확인을_받지_못하면_하지_않는다()
    {
        var vm = ChartWithNotes(OwnerObservingViewModel.BulkConfirmThreshold + 1);
        var search = new NoteSearchViewModel(vm);

        await search.DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Equal(OwnerObservingViewModel.BulkConfirmThreshold + 1, vm.Chart.Notes.Count);
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public async Task 많은_노트의_일괄_삭제는_확인하면_진행한다()
    {
        var vm = ChartWithNotes(OwnerObservingViewModel.BulkConfirmThreshold + 1);
        var asked = 0;
        vm.ConfirmAsync = _ => { asked++; return Task.FromResult(true); };

        await new NoteSearchViewModel(vm).DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Equal(1, asked);
        Assert.Empty(vm.Chart.Notes);
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public async Task 적은_노트의_일괄_삭제는_묻지_않는다()
    {
        var vm = ChartWithNotes(OwnerObservingViewModel.BulkConfirmThreshold);
        var asked = 0;
        vm.ConfirmAsync = _ => { asked++; return Task.FromResult(false); };

        await new NoteSearchViewModel(vm).DeleteMatchesCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);
        Assert.Empty(vm.Chart.Notes);
        vm.AutoSave!.Dispose();
    }

    [Fact]
    public void 설정은_저장한_값을_그대로_읽는다()
    {
        var path = Path.Combine(_root, "settings.json");
        var settings = new AppSettings { VerticalZoom = 21.5, IsHorizontalView = true, KeySoundVolume = 0.4, WindowWidth = 900 };
        settings.AddRecentFile(@"C:\a.bms");

        settings.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal(21.5, loaded.VerticalZoom);
        Assert.True(loaded.IsHorizontalView);
        Assert.Equal(0.4, loaded.KeySoundVolume);
        Assert.Equal(900, loaded.WindowWidth);
        Assert.Equal(new[] { @"C:\a.bms" }, loaded.RecentFiles);
    }

    [Fact]
    public void 깨진_설정_파일은_기본값으로_돌아간다()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{ 이건 JSON 이 아니다");

        Assert.Equal(10.0, AppSettings.Load(path).VerticalZoom);
    }

    [Fact]
    public void 설정의_이상한_값은_범위_안으로_들어온다()
    {
        var vm = new MainWindowViewModel();

        vm.ApplySettings(new AppSettings { VerticalZoom = double.NaN, HorizontalZoom = 999, PlaybackSpeed = -1, KeySoundVolume = 7, WavPaletteViewModeIndex = 42 });

        Assert.Equal(10.0, vm.VerticalZoom);
        Assert.Equal(4.0, vm.HorizontalZoom);
        Assert.Equal(0.1, vm.PlaybackSpeed);
        Assert.Equal(1.0, vm.KeySoundVolume);
        Assert.Equal(3, vm.WavPaletteViewModeIndex);
    }

    [Fact]
    public void 최근_파일은_중복_없이_최신순으로_열_개만_남는다()
    {
        var settings = new AppSettings();

        for (var i = 0; i < 15; i++)
            settings.AddRecentFile($@"C:\{i}.bms");
        settings.AddRecentFile(@"c:\10.BMS");

        Assert.Equal(AppSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.Equal(@"c:\10.BMS", settings.RecentFiles[0]);
        Assert.Single(settings.RecentFiles, p => p.Equals(@"C:\10.bms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 키음_음량은_0과_1_사이로_잘린다()
    {
        using var player = new KeySoundPlayer();

        player.Volume = 3.0;
        Assert.Equal(1.0, player.Volume);

        player.Volume = -1.0;
        Assert.Equal(0.0, player.Volume);
    }

    [Fact]
    public void 파일이_없는_키음은_목록에서_표시된다()
    {
        var existing = Path.Combine(_root, "yes.wav");
        File.WriteAllBytes(existing, Array.Empty<byte>());

        Assert.StartsWith("#01", new BmsWavItem { Key = "01", FilePath = existing }.DisplayText);
        Assert.StartsWith("⚠️", new BmsWavItem { Key = "02", FilePath = Path.Combine(_root, "no.wav") }.DisplayText);
    }

    [Theory]
    [InlineData(PhysicalKey.Digit1, 4)]
    [InlineData(PhysicalKey.Digit3, 12)]
    [InlineData(PhysicalKey.Digit8, 32)]
    public void 숫자_키로_격자_분할을_바꾼다(PhysicalKey key, int expected) => HoldTestSupport.RunOnUiThread(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new bms_editer.MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(expected, vm.BeatSplit);
        window.Close();
    });

    [Fact]
    public void E_키로_편집_모드를_오간다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var vm = new MainWindowViewModel();
        var window = new bms_editer.MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.E, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsEditMode);
        window.Close();
    });

    // UI 자동화 클라이언트가 붙어 있으면 NumericUpDown 의 기본 연결이 decimal 을 못 넘겨 예외를 던진다.
    // 차트를 열다가 바인딩이 끊겨 문서가 반쯤만 들어왔다. 실제 exe 를 UI 자동화로 돌려 보다가 잡혔다.
    [Fact]
    public void 창의_숫자_칸은_문제가_되는_접근성_연결을_쓰지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var window = new bms_editer.MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var boxes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
            .OfType<Avalonia.Controls.NumericUpDown>()
            .ToList();

        Assert.NotEmpty(boxes);
        Assert.All(boxes, box => Assert.IsNotType<Avalonia.Automation.Peers.NumericUpDownAutomationPeer>(
            Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(box)));
        window.Close();
    });

    private static AutoSaveMeta Meta(DateTime? at = null) =>
        new(null, "base.bms", at ?? DateTime.UtcNow, 0, Encoding.UTF8.CodePage, false, "t");
}

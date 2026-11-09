using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views;

namespace bms_editer;

// 실제로 앱을 띄울 때만 켜는 것들: 설정 · 최근 파일 · 자동 저장 복구 · 끌어다 놓기 · 외부 변경 감지.
//
// 생성자에 넣지 않은 이유: 헤드리스 창 테스트도 MainWindow 를 만든다.
// 테스트가 사용자의 설정·자동 저장 폴더를 건드리면 안 된다.
public partial class MainWindow
{
    private AppSettings? _settings;
    private bool _isCheckingExternalChange;

    public void EnableDesktopServices()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        _settings = AppSettings.Load();
        vm.ApplySettings(_settings);
        vm.AutoSave = new AutoSaveStore();
        vm.StartAutoSaveTimer();

        RestoreWindowPlacement(_settings);
        RebuildRecentFilesMenu();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);

        Activated += OnWindowActivated;
    }

    // 창이 뜬 직후 한 번. 복구를 먼저 묻고, 복구하지 않았을 때만 실행 인자의 파일을 연다.
    public async Task RunStartupAsync(string? startupPath)
    {
        var recovered = await OfferAutoSaveRecoveryAsync();

        if (!recovered && startupPath is not null)
            await OpenExternalFileAsync(startupPath);
    }

    private async Task<bool> OfferAutoSaveRecoveryAsync()
    {
        if (DataContext is not MainWindowViewModel { AutoSave: { } store } vm)
            return false;

        foreach (var entry in store.FindRecoverable())
        {
            var name = entry.Meta.OriginalPath ?? "제목 없음 (저장한 적 없는 문서)";
            var when = entry.Meta.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            var choice = await ConfirmWindow.ShowThreeWayAsync(
                this,
                "지난번에 앱이 정상적으로 닫히지 않아 저장되지 않은 작업이 남아 있습니다.\n\n"
                + $"파일: {name}\n자동 저장 시각: {when}\n노트 수: {entry.Meta.NoteCount}\n\n"
                + "복구해도 원본 파일은 저장하기 전까지 바뀌지 않습니다. 취소하면 다음 실행 때 다시 묻습니다.",
                confirmText: "복구",
                alternateText: "버리기",
                title: "작업 복구");

            if (choice == ConfirmChoice.Cancel)
                return false;

            if (choice == ConfirmChoice.Alternate)
            {
                store.Delete(entry);
                continue;
            }

            if (!vm.RecoverAutoSave(entry))
            {
                await ConfirmWindow.ShowMessageAsync(this, $"복구하지 못했습니다.\n\n{vm.LastErrorMessage}", "작업 복구");
                return false;
            }

            if (entry.Meta.OriginalPath is { } original && Path.GetDirectoryName(original) is { } folder && Directory.Exists(folder))
                await LoadFolderAudioAndVideoAsync(vm, folder);

            return true;
        }

        return false;
    }

    private void RestoreWindowPlacement(AppSettings settings)
    {
        if (settings.WindowWidth is > 200 and { } width)
            Width = width;

        if (settings.WindowHeight is > 200 and { } height)
            Height = height;

        // 모니터를 뺐으면 저장해 둔 자리가 화면 밖일 수 있다. 그때는 운영체제에 맡긴다.
        if (settings.WindowX is { } x && settings.WindowY is { } y
            && Screens.ScreenFromPoint(new PixelPoint(x, y)) is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }

        if (settings.IsWindowMaximized)
            WindowState = WindowState.Maximized;
    }

    // Closing 때마다 적어 둔다. Closed 뒤에는 창 크기를 믿을 수 없다.
    private void CaptureWindowPlacement()
    {
        if (_settings is null)
            return;

        _settings.IsWindowMaximized = WindowState == WindowState.Maximized;

        // 최대화·최소화 중의 크기를 적으면 다음에 복원 크기가 사라진다.
        if (WindowState != WindowState.Normal)
            return;

        _settings.WindowWidth = ClientSize.Width;
        _settings.WindowHeight = ClientSize.Height;
        _settings.WindowX = Position.X;
        _settings.WindowY = Position.Y;
    }

    private void SaveSettingsIfEnabled()
    {
        if (_settings is null || DataContext is not MainWindowViewModel vm)
            return;

        vm.CaptureSettings(_settings);
        _settings.Save();
    }

    private void RememberRecentFile(string path)
    {
        if (_settings is null)
            return;

        _settings.AddRecentFile(path);
        _settings.Save();
        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();

        var recent = _settings?.RecentFiles ?? new();
        RecentFilesMenu.IsEnabled = recent.Count > 0;

        foreach (var path in recent)
        {
            // Header 를 글자 그대로 주면 파일 이름의 '_' 가 단축키 표시로 읽혀 사라진다.
            var item = new MenuItem { Header = new TextBlock { Text = path } };
            item.Click += async (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    await OpenChartPathAsync(vm, path);
            };

            RecentFilesMenu.Items.Add(item);
        }
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // .bms → 열기 · 폴더 → 폴더 열기 · .ogg → 음원 · .wav → 키음 추가.
    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var paths = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToArray();

        if (paths is not { Length: > 0 })
            return;

        e.Handled = true;

        var first = paths[0];
        if (Directory.Exists(first))
        {
            if (await vm.ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n폴더를 열까요?"))
                await LoadFolderMediaAsync(vm, first);

            return;
        }

        var extension = Path.GetExtension(first);
        if (FolderMedia.ChartExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            await OpenChartPathAsync(vm, first);
        }
        else if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
        {
            if (!await vm.LoadOggAsync(first))
                await ConfirmWindow.ShowMessageAsync(this, $"OGG를 불러오지 못했습니다.\n\n{vm.LastErrorMessage}", "OGG 로드 실패");
        }
        else if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var path in paths.Where(p => string.Equals(Path.GetExtension(p), ".wav", StringComparison.OrdinalIgnoreCase)))
            {
                if (vm.AddWav(path))
                    continue;

                await ConfirmWindow.ShowMessageAsync(this, $"키음을 추가하지 못했습니다.\n\n{vm.LastErrorMessage}", "키음 추가 실패");
                return;
            }
        }
    }

    // 다른 프로그램에서 돌아왔을 때 차트가 밖에서 바뀌었으면 알린다.
    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_isCheckingExternalChange || DataContext is not MainWindowViewModel vm || !vm.ShouldNotifyExternalChange())
            return;

        _isCheckingExternalChange = true;
        try
        {
            var name = Path.GetFileName(vm.CurrentFilePath);
            var warning = vm.IsDirty ? "\n\n다시 열면 에디터에서 고친 내용은 사라집니다." : "";

            if (await ConfirmWindow.ShowAsync(this, $"'{name}' 이(가) 다른 프로그램에서 바뀌었습니다.\n다시 열까요?{warning}", "파일이 바뀜")
                && vm.CurrentFilePath is { } path && !vm.LoadBms(path))
            {
                await ConfirmWindow.ShowMessageAsync(this, $"다시 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");
            }
        }
        finally
        {
            _isCheckingExternalChange = false;
        }
    }

    // 저장 직전 확인. 계속 저장해도 되면 true.
    private async Task<bool> EnsureMayOverwriteAsync(MainWindowViewModel vm, string path)
    {
        if (!string.Equals(path, vm.CurrentFilePath, StringComparison.OrdinalIgnoreCase) || !vm.HasExternalChange())
            return true;

        var choice = await ConfirmWindow.ShowThreeWayAsync(
            this,
            $"'{Path.GetFileName(path)}' 이(가) 이 파일을 연 뒤에 다른 프로그램에서 바뀌었습니다.\n\n"
            + "덮어쓰면 밖에서 고친 내용이 사라지고, 다시 열면 에디터에서 고친 내용이 사라집니다.",
            confirmText: "덮어쓰기",
            alternateText: "다시 열기",
            title: "파일이 바뀜");

        if (choice == ConfirmChoice.Confirm)
            return true;

        if (choice == ConfirmChoice.Alternate && !vm.LoadBms(path))
            await ConfirmWindow.ShowMessageAsync(this, $"다시 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");

        return false;
    }

    private async void OnImportKeysoundsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "키음 표를 가져올 뮤즈 대시 차트 선택",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml" } },
            },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
            return;

        // 표를 통째로 갈아끼우므로 같은 번호의 노트가 다른 소리를 가리키게 될 수 있다.
        if (vm.Chart.Notes.Count > 0
            && !await ConfirmWindow.ShowAsync(this, "지금 키음 표를 통째로 바꿉니다. 노트의 소리가 달라질 수 있습니다.\n되돌리기(Ctrl+Z)로 되돌릴 수 있습니다.\n\n계속할까요?", "키음 표 가져오기"))
        {
            return;
        }

        var count = vm.ImportKeysoundsFrom(path);
        await ConfirmWindow.ShowMessageAsync(
            this,
            count < 0
                ? vm.LastErrorMessage ?? "가져오지 못했습니다."
                : $"키음 {count}개를 가져왔습니다.\n다른 폴더에 저장한다면 씬 WAV 폴더도 같이 복사해 주십시오.",
            "키음 표 가져오기");
    }

    // Ctrl+휠: 마우스가 가리키는 자리를 그대로 둔 채 세로 줌을 바꾼다.
    private void OnEditorWheelTunnel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || DataContext is not MainWindowViewModel vm)
            return;

        var pointer = e.GetPosition(EditorSurface);
        var offset = EditorScrollViewer.Offset;
        var along = vm.IsHorizontalView ? pointer.X : pointer.Y;
        var inView = along - (vm.IsHorizontalView ? offset.X : offset.Y);
        var ratio = along / Math.Max(1.0, GetTimelineLength(vm));

        vm.VerticalZoom = Math.Clamp(
            vm.VerticalZoom * Math.Pow(1.15, e.Delta.Y),
            MainWindowViewModel.MinVerticalZoom,
            MainWindowViewModel.MaxVerticalZoom);

        e.Handled = true;

        // 새 길이는 레이아웃이 끝나야 스크롤에 반영된다.
        Dispatcher.UIThread.Post(() =>
        {
            var target = (ratio * GetTimelineLength(vm)) - inView;
            EditorScrollViewer.Offset = vm.IsHorizontalView
                ? new Vector(Math.Max(0, target), EditorScrollViewer.Offset.Y)
                : new Vector(EditorScrollViewer.Offset.X, Math.Max(0, target));
        }, DispatcherPriority.Background);
    }
}

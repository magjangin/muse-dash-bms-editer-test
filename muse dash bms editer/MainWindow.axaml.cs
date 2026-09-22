using System.Linq;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using bms_editer.Views;
using bms_editer.Views.Controls;

namespace bms_editer;

public partial class MainWindow : Window
{
        // 지금 떠 있는 모드리스 보조 창(검색/통계/키음 팔레트). 종류당 하나만 띄운다.
        private readonly Dictionary<Type, Window> _toolWindows = new();

        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainWindowViewModel();
            DataContext = vm;
            vm.ConfirmAsync = message => ConfirmWindow.ShowAsync(this, message, "확인");
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ScrollToRatioRequested += ScrollEditorToRatio;
            Waveform.ScrubRequested += OnWaveformScrubRequested;
            Closing += OnWindowClosing;
            Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
            AddHandler(PointerPressedEvent, OnWindowPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
            UpdateEditorOrientation();
            UpdateWindowTitle();
        }

        // 창을 닫을 때 저장 안 한 작업이 사라지지 않게 막는다.
        //
        // 새로 만들기·열기에는 확인창이 있었는데 정작 창 닫기에는 없었다.
        // Closing 은 await 할 수 없으므로, 일단 닫기를 취소하고 물어본 뒤
        // 사용자가 정말 닫겠다고 하면 그때 다시 Close() 한다.
        private bool _isClosingConfirmed;

        private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_isClosingConfirmed || DataContext is not MainWindowViewModel vm || !vm.IsDirty)
                return;

            e.Cancel = true;

            var name = vm.CurrentFilePath is { } path ? Path.GetFileName(path) : "제목 없음";
            var choice = await ConfirmWindow.ShowThreeWayAsync(
                this,
                $"'{name}' 의 바뀐 내용을 저장하지 않았습니다.\n\n저장하고 닫을까요?",
                confirmText: "저장하고 닫기",
                alternateText: "저장 안 함",
                title: "종료");

            switch (choice)
            {
                case ConfirmChoice.Cancel:
                    return;

                case ConfirmChoice.Confirm:
                    var savePath = vm.CurrentFilePath ?? await PickSavePathAsync(vm);
                    if (savePath is null)
                        return;

                    if (!vm.SaveBms(savePath))
                    {
                        await ConfirmWindow.ShowMessageAsync(
                            this, $"저장하지 못했습니다. 창을 닫지 않았습니다.\n\n{vm.LastErrorMessage}", "저장 실패");
                        return;
                    }
                    break;
            }

            _isClosingConfirmed = true;
            Close();
        }

        private void UpdateWindowTitle()
        {
            if (DataContext is MainWindowViewModel vm)
                Title = vm.WindowTitle;
        }

        private async void OnLoadOggClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "OGG 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("OGG 오디오") { Patterns = new[] { "*.ogg" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            // 실패해도 이미 물려 있던 음원은 그대로 남는다. 사유만 알려준다.
            if (!await vm.LoadOggAsync(path))
                await ConfirmWindow.ShowMessageAsync(this, $"OGG를 불러오지 못했습니다.\n\n{vm.LastErrorMessage}", "OGG 로드 실패");
        }

        private async void OnLoadVideoClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "비디오 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("비디오 파일") { Patterns = new[] { "*.mp4", "*.webm", "*.mov", "*.avi", "*.mkv" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            vm.LoadVideo(path);
        }

        private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "BMS 폴더 선택",
                AllowMultiple = false,
            });

            var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
            if (folderPath is null || !Directory.Exists(folderPath))
                return;

            // 확인은 **고를 것을 다 고른 뒤에** 받는다. 먼저 물으면, 선택을 취소해도
            // 이미 "버리시겠습니까"에 답한 뒤라 사용자만 헷갈린다.
            if (!await vm.ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n폴더를 열까요?"))
                return;

            await LoadFolderMediaAsync(vm, folderPath);
        }

        private void OnClearVideoClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ClearVideo();
        }

        private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "BMS 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            // 확인은 열 파일을 고른 뒤에 받는다. (폴더 열기와 같은 이유)
            if (!await vm.ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n파일을 열까요?"))
                return;

            if (!vm.LoadBms(path))
                await ConfirmWindow.ShowMessageAsync(this, $"파일을 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");
        }

        // 건반형 BMS(4K/5K/7K)를 열어 뮤즈 대시 두 레인으로 접는다.
        //
        // 여는 것이 아니라 **가져오기**다. 지금 문서의 키음 표를 그대로 쓰기 때문이다.
        // 건반 BMS 의 키음은 그 게임 소리라 뮤즈 대시 노트에 붙일 수 없다.
        private async void OnImportKeyboardChartClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "가져올 건반 BMS 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml", "*.pms" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            var report = await vm.ImportKeyboardChartAsync(path);
            if (report is null)
            {
                // 사용자가 확인 창에서 취소한 경우에는 알릴 것이 없다.
                if (vm.LastErrorMessage is { } message)
                    await ConfirmWindow.ShowMessageAsync(this, message, "가져오기 실패");

                return;
            }

            await ConfirmWindow.ShowMessageAsync(this, vm.DescribeDownmixReport(report), "가져오기 완료");
        }

        private async void OnSaveFileClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var path = vm.CurrentFilePath ?? await PickSavePathAsync(vm);
            if (path is null)
                return;

            await SaveAndReportAsync(vm, path);
        }

        private async void OnSaveFileAsClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var path = await PickSavePathAsync(vm);
            if (path is null)
                return;

            await SaveAndReportAsync(vm, path);
        }

        // 저장은 조용히 실패하면 안 된다. 실패하면 사유까지 보여준다.
        // 저장은 됐지만 알려야 할 것(인코딩을 UTF-8로 물린 경우)도 여기서 보여준다.
        private async Task SaveAndReportAsync(MainWindowViewModel vm, string path)
        {
            if (!vm.SaveBms(path))
            {
                await ConfirmWindow.ShowMessageAsync(this, $"저장하지 못했습니다.\n\n{vm.LastErrorMessage}", "저장 실패");
                return;
            }

            if (vm.LastWarningMessage is { } warning)
                await ConfirmWindow.ShowMessageAsync(this, warning, "저장 완료");
        }

        private async Task<string?> PickSavePathAsync(MainWindowViewModel vm)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "BMS 파일로 저장",
                DefaultExtension = "bms",
                SuggestedFileName = ToSafeFileName(vm.Title),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("BMS 차트 파일") { Patterns = new[] { "*.bms", "*.bme", "*.bml" } },
                },
            });

            return file?.TryGetLocalPath();
        }

        // 곡 제목을 파일명으로 쓸 수 있게 다듬는다.
        //
        // 예전에는 제목을 그대로 넘겨서, `:` `?` `/` 같은 글자가 든 제목이면
        // 저장 대화상자가 걸렸다. 곡 제목에는 흔한 글자들이다.
        private static string ToSafeFileName(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "chart";

            var safe = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();

            // 전부 걸러졌거나 점만 남은 경우.
            return safe.Trim('.').Length == 0 ? "chart" : safe;
        }

        private async Task LoadFolderMediaAsync(MainWindowViewModel vm, string folderPath)
        {
            var bmsPath = FolderMedia.FindBestFile(folderPath, FolderMedia.ChartExtensions);

            // 차트가 없는 폴더를 고르면 아무것도 건드리지 않는다.
            //
            // 예전에는 차트만 건너뛰고 음원·영상을 얹었다. 사용자는 새로 열렸다고 생각하는데
            // 화면의 차트는 옛것이고, CurrentFilePath 도 옛 파일을 가리켜서
            // 그대로 [저장]을 누르면 방금 연 폴더가 아니라 옛 차트가 덮어써졌다.
            if (bmsPath is null)
            {
                await ConfirmWindow.ShowMessageAsync(
                    this,
                    $"이 폴더에 BMS 차트가 없습니다.\n\n{folderPath}\n\n" +
                    "지금 작업 중인 차트를 그대로 두었습니다. 음원·영상도 바꾸지 않았습니다.",
                    "폴더 열기");
                return;
            }

            if (!vm.LoadBms(bmsPath))
            {
                await ConfirmWindow.ShowMessageAsync(this, $"차트를 열지 못했습니다.\n\n{vm.LastErrorMessage}", "열기 실패");
                return;
            }

            var oggPath = FolderMedia.FindBestFile(folderPath, FolderMedia.AudioExtensions);
            if (oggPath is not null && !await vm.LoadOggAsync(oggPath))
                await ConfirmWindow.ShowMessageAsync(this, $"OGG를 불러오지 못했습니다.\n\n{vm.LastErrorMessage}", "OGG 로드 실패");

            var videoPath = FolderMedia.FindBestFile(folderPath, FolderMedia.VideoExtensions);
            if (videoPath is not null)
                vm.LoadVideo(videoPath);
            else
                vm.ClearVideo();
        }

        private async void OnAddWavClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "WAV 파일 선택",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("WAV 오디오") { Patterns = new[] { "*.wav" } },
                },
            });

            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null)
                return;

            // 예전에는 실패해도 아무 표시가 없어서, 키음이 한도를 넘으면 조용히 아무 일도 안 났다.
            if (!vm.AddWav(path))
                await ConfirmWindow.ShowMessageAsync(this, $"키음을 추가하지 못했습니다.\n\n{vm.LastErrorMessage}", "키음 추가 실패");
        }

        private void OnNoteGridKeyDown(object? sender, KeyEventArgs e) => HandleEditorKey(e);

        // 창 전체에서 받는 단축키.
        //
        // 예전에는 Delete·방향키가 **격자에 포커스가 있을 때만** 먹었다. 팔레트나 사이드바를
        // 한 번 만지면 조용히 안 먹었고 안내도 없었다. 이제 창이 받아준다.
        // 격자가 먼저 처리했으면 e.Handled 가 서 있어 두 번 돌지 않는다.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || DataContext is not MainWindowViewModel vm)
                return;

            var focused = FocusManager?.GetFocusedElement();

            // 글자를 치는 중이면 단축키가 아니라 입력이다.
            if (IsWithin<TextBox>(focused))
                return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                HandleControlShortcut(e);
                return;
            }

            if (e.Key == Key.Space)
            {
                // 스페이스는 포커스가 잡힌 버튼·체크박스를 누르는 키이기도 하다. 그쪽이 우선이다.
                if (IsWithin<Button>(focused) || IsWithin<ToggleButton>(focused))
                    return;

                vm.TogglePlaybackCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // 방향키·Delete 는 목록·콤보·슬라이더에서는 그쪽 것이다.
            if (IsWithin<ListBox>(focused) || IsWithin<ComboBox>(focused)
                || IsWithin<Slider>(focused) || IsWithin<NumericUpDown>(focused))
            {
                return;
            }

            HandleEditorKey(e);
        }

        private void HandleControlShortcut(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    OnSaveFileAsClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    OnSaveFileClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.O:
                    OnOpenFileClick(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.N:
                    if (DataContext is MainWindowViewModel vm)
                        vm.NewFileCommand.Execute(null);
                    e.Handled = true;
                    break;

                // Ctrl+Shift+Z 도 다시 하기로 받는다. Ctrl+Y 만 두면 그쪽 버릇이 든 손이 헛돈다.
                case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                case Key.Y:
                    ExecuteIfPossible(v => v.RedoCommand);
                    e.Handled = true;
                    break;

                case Key.Z:
                    ExecuteIfPossible(v => v.UndoCommand);
                    e.Handled = true;
                    break;
            }
        }

        // 되돌릴 것이 없으면 아무 일도 하지 않는다. CanExecute 를 보지 않고 부르면
        // 비어 있는 칸에서도 눌린 것처럼 동작한다.
        private void ExecuteIfPossible(Func<MainWindowViewModel, IRelayCommand> pick)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var command = pick(vm);
            if (command.CanExecute(null))
                command.Execute(null);
        }

        // 격자 편집 키(선택 해제·삭제·이동). 격자에서도 창에서도 같은 규칙을 쓴다.
        private void HandleEditorKey(KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            switch (e.Key)
            {
                case Key.Delete:
                    vm.DeleteSelectedNotesCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Key.Escape:
                    vm.ClearNoteSelection();
                    e.Handled = true;
                    return;
            }

            NoteMoveDirection? direction = e.Key switch
            {
                Key.Up => vm.IsHorizontalView ? NoteMoveDirection.LanePrevious : NoteMoveDirection.TimeForward,
                Key.Down => vm.IsHorizontalView ? NoteMoveDirection.LaneNext : NoteMoveDirection.TimeBackward,
                Key.Left => vm.IsHorizontalView ? NoteMoveDirection.TimeBackward : NoteMoveDirection.LanePrevious,
                Key.Right => vm.IsHorizontalView ? NoteMoveDirection.TimeForward : NoteMoveDirection.LaneNext,
                _ => null
            };

            if (direction is { } d)
            {
                vm.MoveSelectedNotesCommand.Execute(d);
                e.Handled = true;
            }
        }

        // 포커스가 T 안에(또는 T 자신에) 있는지.
        private static bool IsWithin<T>(IInputElement? focused) where T : class
        {
            if (focused is not Visual visual)
                return false;

            foreach (var ancestor in visual.GetSelfAndVisualAncestors())
            {
                if (ancestor is T)
                    return true;
            }

            return false;
        }

        // 검색 결과를 격자에서 바로 확인해야 하므로 모달이 아닌 모드리스로 띄운다.
        private void OnShowNoteSearchClick(object? sender, RoutedEventArgs e) =>
            ShowToolWindow(vm => new NoteSearchWindow(new NoteSearchViewModel(vm)));

        // 편집하는 동안 집계가 따라 움직여야 하므로 검색 창과 같이 모드리스로 띄운다.
        // 숫자만 보는 창이다. 골라서 다루는 쪽은 아래 컨트롤 패널이 맡는다.
        private void OnShowStatsClick(object? sender, RoutedEventArgs e) =>
            ShowToolWindow(vm => new NoteStatsWindow(new NoteStatsViewModel(vm)));

        // 같은 집계를 물려받아 레인/키음별 일괄 선택·미리듣기·일괄 교체까지 잇는 공구함.
        // 여기서 고른 노트를 격자에서 이어 손보므로 역시 모드리스로 띄운다.
        private void OnShowControlPanelClick(object? sender, RoutedEventArgs e) =>
            ShowToolWindow(vm => new ControlPanelWindow(new ControlPanelViewModel(vm)));

        // 사이드바의 좁은 키음 목록 대신 넓은 타일 판에서 고르는 창.
        // 선택이 곧 편집용 붓이므로 팔레트를 열 때 편집 모드(연필 아이콘)를 즉시 활성화한다.
        private void OnShowWavPaletteClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.IsEditMode = true;

            ShowToolWindow(vm => new WavPaletteWindow(new WavPaletteViewModel(vm)));
        }

        // 모드리스 보조 창을 종류당 하나만 띄운다. 이미 떠 있으면 앞으로 가져온다.
        //
        // 보조 창의 뷰모델은 메인 뷰모델의 변경 알림을 구독하므로, 창이 닫힐 때
        // 반드시 해제해야 닫은 창이 계속 살아남지 않는다. 세 창이 같은 절차를
        // 따로 복사해 갖고 있었는데, 하나라도 Dispose 를 빠뜨리면 조용히 새는 자리였다.
        private void ShowToolWindow<TWindow>(Func<MainWindowViewModel, TWindow> create)
            where TWindow : Window
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (_toolWindows.TryGetValue(typeof(TWindow), out var existing))
            {
                existing.Activate();
                return;
            }

            var window = create(vm);
            window.Closed += (_, _) =>
            {
                _toolWindows.Remove(typeof(TWindow));
                (window.DataContext as IDisposable)?.Dispose();
            };

            _toolWindows[typeof(TWindow)] = window;
            window.Show(this);
        }

        private void OnWaveformScrubRequested(object? sender, WaveformScrubRequestedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (e.IsFinal)
                vm.ScrubCommit(e.Ratio);
            else
                vm.ScrubPreview(e.Ratio);
        }

        private bool _isSurfaceScrubbing;

        private void OnEditorSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(EditorSurface);
            if (point.Properties.IsMiddleButtonPressed)
            {
                _isSurfaceScrubbing = true;
                e.Pointer.Capture(EditorSurface);
                ScrubFromEditorSurface(e, isFinal: false);
            }
            else if (IsNonMiddleMouseButtonPressed(point.Properties))
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.StopPlaybackAtCurrentPosition();
            }
        }

        private void OnEditorSurfacePointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isSurfaceScrubbing)
                ScrubFromEditorSurface(e, isFinal: false);
        }

        // 드래그 중에는 커서만 옮기고, 버튼을 뗄 때 한 번만 재생을 옮긴다. (알려진 문제 24번)
        private void OnEditorSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isSurfaceScrubbing)
                return;

            _isSurfaceScrubbing = false;
            e.Pointer.Capture(null);
            ScrubFromEditorSurface(e, isFinal: true);
        }

        private void ScrubFromEditorSurface(PointerEventArgs e, bool isFinal)
        {
            if (DataContext is not MainWindowViewModel vm || vm.OggDurationSeconds <= 0)
                return;

            var timelineLength = GetTimelineLength(vm);
            if (timelineLength <= 0)
                return;

            var position = e.GetPosition(EditorSurface);
            var ratio = vm.IsHorizontalView
                ? position.X / timelineLength
                : 1.0 - (position.Y / timelineLength);

            if (isFinal)
                vm.ScrubCommit(ratio);
            else
                vm.ScrubPreview(ratio);

            e.Handled = true;
        }

        private static bool IsNonMiddleMouseButtonPressed(PointerPointProperties properties) =>
            properties.IsLeftButtonPressed ||
            properties.IsRightButtonPressed ||
            properties.IsXButton1Pressed ||
            properties.IsXButton2Pressed ||
            properties.PointerUpdateKind is PointerUpdateKind.LeftButtonPressed
                or PointerUpdateKind.RightButtonPressed
                or PointerUpdateKind.XButton1Pressed
                or PointerUpdateKind.XButton2Pressed;

        // 재생 중 마우스 우클릭(또는 좌클릭) 시 현재 위치에서 즉시 재생을 멈춘다.
        // Tunnel 단계에서 창 전체로 미리 가로채므로, 자식 컨트롤(노트 격자·파형 등)에서
        // 이벤트를 Handled 처리하거나 에디터 빈 영역을 클릭해도 안정적으로 즉각 정지한다.
        private void OnWindowPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || !vm.IsPlaying)
                return;

            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsMiddleButtonPressed)
                return;

            if (IsNonMiddleMouseButtonPressed(properties))
            {
                vm.StopPlaybackAtCurrentPosition();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.PlaybackPositionSeconds))
            {
                FollowPlaybackCursorIfNeeded();
                if (sender is MainWindowViewModel vm)
                    VideoPreview.SyncTo(vm.PlaybackPositionSeconds);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsHorizontalView))
            {
                UpdateEditorOrientation();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.WindowTitle))
            {
                UpdateWindowTitle();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsPlaying))
            {
                if (sender is not MainWindowViewModel vm)
                    return;

                if (vm.IsPlaying)
                    VideoPreview.PlayFrom(vm.PlaybackPositionSeconds);
                else
                    VideoPreview.PauseAt(vm.PlaybackPositionSeconds);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.VideoFilePath))
            {
                if (sender is not MainWindowViewModel vm)
                    return;

                if (vm.VideoFilePath is { } videoPath)
                    VideoPreview.LoadVideo(videoPath);
                else
                    VideoPreview.ClearVideo();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.PlaybackSpeed))
            {
                if (sender is MainWindowViewModel vm)
                    VideoPreview.SetPlaybackRate(vm.PlaybackSpeed);
            }
        }

        private void UpdateEditorOrientation()
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (vm.IsHorizontalView)
            {
                EditorSurface.Orientation = Avalonia.Layout.Orientation.Vertical;
                Waveform.Width = double.NaN;
                Waveform.Height = 220;
            }
            else
            {
                EditorSurface.Orientation = Avalonia.Layout.Orientation.Horizontal;
                Waveform.Width = 220;
                Waveform.Height = double.NaN;
            }
        }

        // 통계·검색 창에서 고른 노트가 있는 자리로 격자를 옮긴다.
        //
        // 이게 없으면 통계 창에서 키음 번호를 눌러도 화면에서는 아무 일도 안 일어난 것처럼
        // 보인다. 선택은 됐는데 그 노트들이 보이는 구간 밖에 있기 때문이다.
        //
        // 선택 직후에는 아직 레이아웃이 끝나지 않아 Viewport 가 0일 수 있으므로
        // 한 박자 뒤에 옮긴다.
        private void ScrollEditorToRatio(double ratio)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is not MainWindowViewModel vm)
                    return;

                var timelineLength = GetTimelineLength(vm);
                if (timelineLength <= 0)
                    return;

                var offset = EditorScrollViewer.Offset;

                if (vm.IsHorizontalView)
                {
                    var viewport = EditorScrollViewer.Viewport.Width;
                    if (viewport <= 0)
                        return;

                    var target = (ratio * timelineLength) - (viewport * 0.5);
                    var max = Math.Max(0, EditorSurface.Bounds.Width - viewport);
                    EditorScrollViewer.Offset = new Vector(Math.Clamp(target, 0, max), offset.Y);
                }
                else
                {
                    var viewport = EditorScrollViewer.Viewport.Height;
                    if (viewport <= 0)
                        return;

                    var target = ((1.0 - ratio) * timelineLength) - (viewport * 0.5);
                    var max = Math.Max(0, EditorSurface.Bounds.Height - viewport);
                    EditorScrollViewer.Offset = new Vector(offset.X, Math.Clamp(target, 0, max));
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private void FollowPlaybackCursorIfNeeded()
        {
            if (DataContext is not MainWindowViewModel vm ||
                !vm.FollowPlaybackCursor ||
                !vm.IsPlaybackCursorVisible ||
                vm.OggDurationSeconds <= 0)
            {
                return;
            }

            var timelineLength = GetTimelineLength(vm);
            if (timelineLength <= 0)
                return;

            var offset = EditorScrollViewer.Offset;

            if (vm.IsHorizontalView)
            {
                if (EditorScrollViewer.Viewport.Width <= 0)
                    return;

                var cursorX = (vm.PlaybackPositionSeconds / vm.OggDurationSeconds) * timelineLength;
                var leftMargin = EditorScrollViewer.Viewport.Width * 0.25;
                var rightMargin = EditorScrollViewer.Viewport.Width * 0.75;
                var cursorInView = cursorX - offset.X;

                if (cursorInView < leftMargin || cursorInView > rightMargin)
                {
                    var targetX = cursorX - (EditorScrollViewer.Viewport.Width * 0.5);
                    var maxX = Math.Max(0, EditorSurface.Bounds.Width - EditorScrollViewer.Viewport.Width);
                    targetX = Math.Clamp(targetX, 0, maxX);
                    EditorScrollViewer.Offset = new Vector(targetX, offset.Y);
                }
            }
            else
            {
                if (EditorScrollViewer.Viewport.Height <= 0)
                    return;

                var cursorY = (1.0 - (vm.PlaybackPositionSeconds / vm.OggDurationSeconds)) * timelineLength;
                var topMargin = EditorScrollViewer.Viewport.Height * 0.25;
                var bottomMargin = EditorScrollViewer.Viewport.Height * 0.75;
                var cursorInView = cursorY - offset.Y;

                if (cursorInView < topMargin || cursorInView > bottomMargin)
                {
                    var targetY = cursorY - (EditorScrollViewer.Viewport.Height * 0.5);
                    var maxY = Math.Max(0, EditorSurface.Bounds.Height - EditorScrollViewer.Viewport.Height);
                    targetY = Math.Clamp(targetY, 0, maxY);
                    EditorScrollViewer.Offset = new Vector(offset.X, targetY);
                }
            }
        }

        private static double GetTimelineLength(MainWindowViewModel vm)
        {
            var spacingScale = Math.Max(1.0, vm.BeatSplit / (double)Math.Max(1, vm.GridMeasure));
            return vm.OggDurationSeconds > 0
                ? vm.OggDurationSeconds * vm.RowHeight * vm.VerticalZoom * spacingScale / 2.0
                : vm.MeasureCount * vm.RowHeight * vm.VerticalZoom * spacingScale;
        }
    }

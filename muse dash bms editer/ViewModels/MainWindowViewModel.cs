using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public BmsChart Chart { get; } = new();

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (_currentFilePath == value)
                return;

            _currentFilePath = value;
            OnPropertyChanged(nameof(CurrentFilePath));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private string? _currentFilePath;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _gridSyncFlashTimer;
    private readonly DispatcherTimer _playbackSpeedDebounceTimer;
    private OggAudioPlayer? _audioPlayer;
    private DateTimeOffset _playbackStartedAt;
    private double _playbackStartSeconds;
    private double _lastPlaybackPositionSeconds;
    private BmsNote[] _playbackNotes = Array.Empty<BmsNote>();
    private readonly KeySoundPlayer _keySoundPlayer = new();

    // OGG 로딩 및 재생 상태
    [ObservableProperty] private string? _oggFileName;
    [ObservableProperty] private IReadOnlyList<float>? _oggPeaks;
    [ObservableProperty] private IReadOnlyList<float>? _oggOnsets;
    [ObservableProperty] private double _oggDurationSeconds;

    // 음원을 타임라인에서 뒤로 미는 양(ms).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioOffsetSeconds))]
    [NotifyPropertyChangedFor(nameof(PlaybackCursorRatio))]
    private double _audioOffsetMs;

    public double AudioOffsetSeconds => AudioOffsetMs / 1000.0;

    public double PlaybackCursorRatio =>
        OggDurationSeconds <= 0
            ? 0.0
            : Math.Clamp((PlaybackPositionSeconds + AudioOffsetSeconds) / OggDurationSeconds, 0, 1);

    partial void OnAudioOffsetMsChanged(double value)
    {
        Chart.Header.AudioOffsetMs = value;
        MarkDirty();
        FlashGridSync();
    }

    [RelayCommand]
    private void DetectAudioOffset()
    {
        if (TryDetectAudioOffsetMs() is { } detected)
            AudioOffsetMs = Math.Round(detected, 1);
    }

    [RelayCommand]
    private void ResetAudioOffset()
    {
        AudioOffsetMs = 0.0;
    }

    public double? TryDetectAudioOffsetMs()
    {
        var onsets = OggOnsets;
        if (onsets is null || onsets.Count == 0 || Bpm <= 0 || OggDurationSeconds <= 0)
            return null;

        var strongSeconds = new List<double>();
        for (var i = 0; i < onsets.Count; i++)
        {
            if (onsets[i] > 0.45f)
            {
                var sec = OggPeakLoader.GetBucketRatio(i, onsets.Count) * OggDurationSeconds;
                strongSeconds.Add(sec);
            }
        }

        if (strongSeconds.Count < 4)
            return null;

        var step = 240.0 / Bpm / 16.0;
        var best = 0.0;
        var bestScore = double.MaxValue;

        for (var candidate = -step / 2; candidate <= step / 2; candidate += 0.0005)
        {
            var score = 0.0;
            foreach (var sec in strongSeconds)
            {
                var position = (sec + candidate) / step;
                score += Math.Abs(position - Math.Round(position));
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best * 1000.0;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackCursorRatio))]
    private double _playbackPositionSeconds;
    [ObservableProperty] private bool _isPlaybackCursorVisible;
    [ObservableProperty] private bool _isGridSyncFlashVisible;
    [ObservableProperty] private bool _isPlaying;

    // 오른쪽 패널 비디오 프리뷰
    [ObservableProperty] private string? _videoFilePath;
    [ObservableProperty] private string? _videoFileName;

    // HEADER 패널
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private double _bpm = 120.0;

    // 콤보박스 인덱스(0=Single, 1=Couple, 2=Double). 파일의 #PLAYER 값보다 하나 작다.
    [ObservableProperty] private int _player;
    [ObservableProperty] private int _rank = 2;
    [ObservableProperty] private string _level = string.Empty;

    // 격자 패널
    [ObservableProperty] private int _measureCount = 32;
    [ObservableProperty] private int _rowHeight = 16;
    [ObservableProperty] private int _beatSplit = 16;
    [ObservableProperty] private int _gridMeasure = 4;
    [ObservableProperty] private double _verticalZoom = 8.0;
    [ObservableProperty] private double _horizontalZoom = 1.50;
    [ObservableProperty] private double _waveformHorizontalZoom = 1.00;
    [ObservableProperty] private bool _followPlaybackCursor = true;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private bool _lockVerticalPosition;
    [ObservableProperty] private bool _isHorizontalView;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private bool _isCircleNoteShape;

    // 키음 팔레트 창의 보기 모드(0 목록, 1 보통, 2 큰, 3 아주 큰 아이콘)
    [ObservableProperty] private int _wavPaletteViewModeIndex = 2;

    // 재생 배속 (0.1x ~ 1.0x) 및 키음 활성화 여부
    [ObservableProperty] private double _playbackSpeed = 1.0;
    [ObservableProperty] private bool _isKeySoundEnabled = true;

    public string KeySoundToggleText => IsKeySoundEnabled ? "🔊 키음 소리 켜짐" : "🔇 키음 소리 끄기";

    partial void OnIsKeySoundEnabledChanged(bool value) => OnPropertyChanged(nameof(KeySoundToggleText));

    partial void OnPlaybackSpeedChanged(double value)
    {
        if (!IsPlaying)
            return;

        _playbackSpeedDebounceTimer.Stop();
        _playbackSpeedDebounceTimer.Start();
    }

    public MainWindowViewModel()
    {
        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playbackTimer.Tick += (_, _) => UpdatePlaybackPosition();

        _playbackSpeedDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _playbackSpeedDebounceTimer.Tick += (_, _) =>
        {
            _playbackSpeedDebounceTimer.Stop();
            if (IsPlaying)
                PlayFrom(PlaybackPositionSeconds);
        };

        _gridSyncFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _gridSyncFlashTimer.Tick += (_, _) =>
        {
            _gridSyncFlashTimer.Stop();
            IsGridSyncFlashVisible = false;
        };

        // 키음을 넣거나 뺀 것도 저장해야 할 변경이다.
        // 뮤즈 대시는 파일명으로 홀드를 가리므로, 키음 표가 바뀌면 짝도 다시 읽는다.
        //
        // 되돌리기도 여기서 기록한다. 키음만 빼고 노트만 되돌리면, 지운 키음 번호를
        // 가리키는 노트가 되살아나 소리 안 나는 유령 노트가 된다.
        WavList.CollectionChanged += (_, _) =>
        {
            MarkDirty();
            InvalidateWavSnapshot();
            RecordHistory("키음 표 변경");
            RecomputeHolds();
        };

        // 빈 문서의 바닥을 깔아 둔다. 이게 없으면 되돌리기가 비교할 "이전 상태"를 갖지 못해서
        // **첫 편집만 되돌릴 수 없다.** 두 번째부터는 멀쩡해서 알아채기 어려운 종류의 구멍이다.
        ResetEditHistory();
    }

    public string GridDisplay => $"{BeatSplit}/{Math.Max(1, GridMeasure)}";

    partial void OnBeatSplitChanged(int value) => OnPropertyChanged(nameof(GridDisplay));
    partial void OnGridMeasureChanged(int value) => OnPropertyChanged(nameof(GridDisplay));

    // 화면에 묶인 헤더 값이 바뀌면 문서를 고친 것이다.
    partial void OnTitleChanged(string value) => MarkDirty();
    partial void OnArtistChanged(string value) => MarkDirty();
    partial void OnGenreChanged(string value) => MarkDirty();
    partial void OnLevelChanged(string value) => MarkDirty();
    partial void OnPlayerChanged(int value) => MarkDirty();
    partial void OnRankChanged(int value) => MarkDirty();

    partial void OnBpmChanged(double value)
    {
        Chart.Header.Bpm = value;
        MarkDirty();
        InvalidateTimeline();
        UpdateMeasureCountFromAudio();
        FlashGridSync();
    }

    private void FlashGridSync()
    {
        if (OggDurationSeconds <= 0)
            return;

        IsGridSyncFlashVisible = true;
        _gridSyncFlashTimer.Stop();
        _gridSyncFlashTimer.Start();
    }

    // 마디 위치 <-> 시각 변환. 격자·노트·재생이 모두 이걸 통해 계산한다.
    //
    // BPM 변화(#xxx03/#xxx08)와 마디 길이(#xxx02)를 여기서만 다룬다.
    // 예전에는 세 곳이 각자 240/BPM 을 써서, 그런 차트는 화면과 소리가 어긋났다.
    private ChartTimeline? _timeline;

    public ChartTimeline Timeline => _timeline ??= ChartTimeline.FromChart(Chart, Bpm);

    public void InvalidateTimeline()
    {
        _timeline = null;
        OnPropertyChanged(nameof(Timeline));
    }

    // 오디오 길이가 요구하는 마디 수. 음원이 없으면 0.
    private int GetAudioMeasureCount()
    {
        if (OggDurationSeconds <= 0 || Bpm <= 0)
            return 0;

        var totalBeats = OggDurationSeconds * (Bpm / 60.0);
        return Math.Max(1, (int)Math.Ceiling(totalBeats / 4.0));
    }

    // 차트 안의 노트와 보존줄이 요구하는 마디 수.
    private int GetChartMeasureCount()
    {
        var maxMeasure = 0;

        foreach (var note in Chart.Notes)
            maxMeasure = Math.Max(maxMeasure, note.Measure);

        foreach (var raw in Chart.PreservedLines)
        {
            if (raw.IsData)
                maxMeasure = Math.Max(maxMeasure, raw.Measure);
        }

        return maxMeasure + 1;
    }

    // 이 문서가 가질 수 있는 가장 작은 마디 수. 오디오와 차트 중 큰 쪽을 따른다.
    public int MinimumMeasureCount =>
        Math.Max(MinimumMeasureFloor, Math.Max(GetAudioMeasureCount(), GetChartMeasureCount()));

    private const int MinimumMeasureFloor = 32;

    // 오디오가 요구하는 만큼은 반드시 확보하되, **줄이지는 않는다.**
    //
    // 예전에는 오디오 길이 기준으로 무조건 덮어썼다. 200마디 차트를 열고 그보다 짧은
    // OGG를 얹으면 MeasureCount 가 75로 줄어, 75마디 이후 노트는 화면에는 보이는데
    // 배치·이동·복사가 전부 거부됐다. BPM 을 만질 때마다 다시 계산돼서
    // BPM 조율 중에도 튀어나왔다.
    public void UpdateMeasureCountFromAudio()
    {
        var required = MinimumMeasureCount;
        if (MeasureCount < required)
            MeasureCount = required;
        else
            Chart.MeasureCount = MeasureCount;

        OnPropertyChanged(nameof(MinimumMeasureCount));
    }

    // 사용자가 마디 수를 직접 줄이더라도, 이미 노트가 있는 마디까지 잠기면 안 된다.
    partial void OnMeasureCountChanged(int value)
    {
        var floor = MinimumMeasureCount;
        if (value < floor)
        {
            MeasureCount = floor;
            return;
        }

        Chart.MeasureCount = value;
    }

    // 새로 만들기처럼 작업 내용을 버리는 동작 전에 사용자 확인을 받는 콜백. (View가 주입)
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    // 마지막 저장(또는 열기) 이후에 고친 것이 있는지.
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
                return;

            _isDirty = value;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private bool _isDirty;

    // 제목 표시줄. 어떤 파일을 열었고 저장했는지가 여기 말고는 드러나는 곳이 없다.
    public string WindowTitle
    {
        get
        {
            var name = CurrentFilePath is { } path ? System.IO.Path.GetFileName(path) : "제목 없음";
            return $"{(IsDirty ? "*" : "")}{name} - bms editer";
        }
    }

    // 문서를 고쳤다고 표시한다. 노트·헤더·키음이 바뀌는 모든 자리에서 부른다.
    public void MarkDirty() => IsDirty = true;

    public void MarkClean()
    {
        IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
    }

    // 작업 내용을 버리는 동작(새로 만들기·열기) 앞에서 확인을 받는다.
    public async Task<bool> ConfirmDiscardIfNeededAsync(string message)
    {
        if (!IsDirty || ConfirmAsync is not { } confirm)
            return true;

        return await confirm(message);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopPlayback(resetCursor: false);
        _playbackTimer.Stop();
        _gridSyncFlashTimer.Stop();
        _audioPlayer?.Dispose();
        _keySoundPlayer.Dispose();
    }
}

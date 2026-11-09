using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

// 자동 저장과 복구, 그리고 밖에서 고친 파일 감지. (usability_review S-1 · S-3 · S-4 · S-8)
//
// 한 곡에 약 4시간이 드는 도구다. 되돌리기는 편집 실수만 막고, 앱이 꺼지면 같이 사라진다.
public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(60);

    // 앱을 실제로 띄울 때만 넣는다(MainWindow.EnableDesktopServices).
    // 뷰모델 테스트가 사용자의 자동 저장·백업 폴더를 건드리면 안 되므로 기본값은 null 이다.
    public AutoSaveStore? AutoSave { get; set; }

    private DispatcherTimer? _autoSaveTimer;
    private long _documentRevision;
    private long _autoSavedRevision;
    private bool _isAutoSaving;

    // 저장 전 백업에서 "열었을 때의 원본"을 이미 남긴 경로.
    private string? _originalBackedUpPath;

    public void StartAutoSaveTimer()
    {
        _autoSaveTimer ??= new DispatcherTimer { Interval = AutoSaveInterval };
        _autoSaveTimer.Tick -= OnAutoSaveTick;
        _autoSaveTimer.Tick += OnAutoSaveTick;
        _autoSaveTimer.Start();
    }

    private async void OnAutoSaveTick(object? sender, EventArgs e) => await AutoSaveNowAsync();

    // 내용은 UI 스레드에서 만들고(문서를 읽는 동안 편집이 끼면 안 된다) 쓰기만 뒤로 넘긴다.
    public async Task AutoSaveNowAsync()
    {
        if (AutoSave is not { } store || !IsDirty || _isAutoSaving || _documentRevision == _autoSavedRevision)
            return;

        var revision = _documentRevision;
        var sessionId = store.CurrentSessionId;
        string content;
        AutoSaveMeta meta;

        try
        {
            (content, meta) = BuildAutoSaveContent(store);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"자동 저장 준비 실패: {ex.Message}");
            return;
        }

        _isAutoSaving = true;
        try
        {
            await Task.Run(() => store.Write(sessionId, content, meta));
            _autoSavedRevision = revision;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 다음 주기에 다시 시도한다. 자동 저장 실패로 작업을 끊지 않는다.
            System.Diagnostics.Debug.WriteLine($"자동 저장 실패: {ex.Message}");
        }
        finally
        {
            _isAutoSaving = false;
        }
    }

    // 오류 처리기에서 부른다. 이 안에서 또 실패해도 원래 오류를 가리면 안 된다.
    public bool EmergencySave()
    {
        try
        {
            if (AutoSave is not { } store || !IsDirty)
                return false;

            var sessionId = store.CurrentSessionId;
            var (content, meta) = BuildAutoSaveContent(store);
            store.Write(sessionId, content, meta);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"비상 저장 실패: {ex.Message}");
            return false;
        }
    }

    // "저장 안 함"으로 닫을 때. 남겨 두면 다음 실행에서 복구하라고 묻는다.
    public void DiscardAutoSave() => AutoSave?.DiscardCurrent();

    private (string Content, AutoSaveMeta Meta) BuildAutoSaveContent(AutoSaveStore store)
    {
        var basePath = store.BasePathFor(CurrentFilePath);
        var content = BmsWriter.Write(Chart, Title, Artist, Genre, Bpm, Player, Rank, Level, WavList, basePath);
        var meta = new AutoSaveMeta(
            CurrentFilePath,
            basePath,
            DateTime.UtcNow,
            Chart.Notes.Count,
            DocumentEncoding.CodePage,
            DocumentEncoding.GetPreamble().Length > 0,
            Title);

        return (content, meta);
    }

    // 복구한 문서는 "저장 안 된 상태"다. 사용자가 저장하기 전까지 원본 파일은 건드리지 않는다.
    public bool RecoverAutoSave(AutoSaveEntry entry)
    {
        BmsParseResult parsed;

        try
        {
            parsed = BmsParser.Parse(entry.ChartFile, Path.GetDirectoryName(entry.Meta.BasePath));
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            return false;
        }

        ApplyParsedDocument(parsed, entry.Meta.OriginalPath);
        DocumentEncoding = EncodingOf(entry.Meta);
        _fileStamp = FileStampOf(entry.Meta.OriginalPath);
        _externalChangeNotified = _fileStamp;

        AutoSave?.Adopt(entry);
        MarkDirty();
        _autoSavedRevision = _documentRevision;
        return true;
    }

    private static Encoding EncodingOf(AutoSaveMeta meta)
    {
        if (meta.CodePage == Encoding.UTF8.CodePage)
            return new UTF8Encoding(meta.HasBom);

        try
        {
            return Encoding.GetEncoding(meta.CodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new UTF8Encoding(false);
        }
    }

    // ── 밖에서 고친 파일 ─────────────────────────────────────────────────────
    //
    // 곡 도중 BPM 변화를 넣는 화면이 없어서 문서들이 텍스트 편집기로 고치라고 안내한다.
    // 에디터를 띄워 둔 채 고치고 돌아와 Ctrl+S 를 누르면 그 내용이 조용히 사라졌다.

    private readonly record struct FileStamp(DateTime LastWriteUtc, long Length);

    private FileStamp? _fileStamp;
    private FileStamp? _externalChangeNotified;

    private static FileStamp? FileStampOf(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path))
                return null;

            var info = new FileInfo(path);
            return new FileStamp(info.LastWriteTimeUtc, info.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // 파일이 지워진 경우는 바뀐 것으로 치지 않는다. 저장하면 그대로 다시 생긴다.
    public bool HasExternalChange() =>
        _fileStamp is { } known && FileStampOf(CurrentFilePath) is { } current && current != known;

    // 창이 앞으로 올 때 한 번만 알린다. 같은 변경을 창을 오갈 때마다 묻지 않는다.
    public bool ShouldNotifyExternalChange()
    {
        if (!HasExternalChange())
            return false;

        var current = FileStampOf(CurrentFilePath);
        if (current == _externalChangeNotified)
            return false;

        _externalChangeNotified = current;
        return true;
    }

    // ── 설정 ────────────────────────────────────────────────────────────────

    public const double MinVerticalZoom = 1.0;
    public const double MaxVerticalZoom = 32.0;

    private static double Sane(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public void ApplySettings(AppSettings settings)
    {
        VerticalZoom = Sane(settings.VerticalZoom, MinVerticalZoom, MaxVerticalZoom, 10.0);
        HorizontalZoom = Sane(settings.HorizontalZoom, 0.25, 4.0, 2.0);
        WaveformHorizontalZoom = Sane(settings.WaveformHorizontalZoom, 0.25, 4.0, 1.0);
        IsHorizontalView = settings.IsHorizontalView;
        IsCircleNoteShape = settings.IsCircleNoteShape;
        HideUnusedLanes = settings.HideUnusedLanes;
        SnapToGrid = settings.SnapToGrid;
        FollowPlaybackCursor = settings.FollowPlaybackCursor;
        PlaybackSpeed = Sane(settings.PlaybackSpeed, 0.1, 1.0, 1.0);
        IsKeySoundEnabled = settings.IsKeySoundEnabled;
        KeySoundVolume = Sane(settings.KeySoundVolume, 0.0, 1.0, 1.0);
        WavPaletteViewModeIndex = Math.Clamp(settings.WavPaletteViewModeIndex, 0, 3);
    }

    public void CaptureSettings(AppSettings settings)
    {
        settings.VerticalZoom = VerticalZoom;
        settings.HorizontalZoom = HorizontalZoom;
        settings.WaveformHorizontalZoom = WaveformHorizontalZoom;
        settings.IsHorizontalView = IsHorizontalView;
        settings.IsCircleNoteShape = IsCircleNoteShape;
        settings.HideUnusedLanes = HideUnusedLanes;
        settings.SnapToGrid = SnapToGrid;
        settings.FollowPlaybackCursor = FollowPlaybackCursor;
        settings.PlaybackSpeed = PlaybackSpeed;
        settings.IsKeySoundEnabled = IsKeySoundEnabled;
        settings.KeySoundVolume = KeySoundVolume;
        settings.WavPaletteViewModeIndex = WavPaletteViewModeIndex;
    }
}

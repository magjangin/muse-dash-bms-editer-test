using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel
{
    // 마지막 열기/저장이 실패한 이유. 성공하면 null.
    public string? LastErrorMessage { get; private set; }

    // 저장은 됐지만 사용자가 알아야 할 것. 성공하면서도 채워질 수 있다.
    public string? LastWarningMessage { get; private set; }

    // 이 문서를 어떤 인코딩으로 읽었는지. 저장할 때 같은 인코딩으로 되돌려 쓴다.
    // 무조건 UTF-8 로 쓰면 CP949·CP932 차트가 다른 플레이어·에디터에서 깨진다.
    // 새로 만든 문서는 UTF-8(BOM 없음)로 시작한다.
    public Encoding DocumentEncoding { get; private set; } = new UTF8Encoding(false);

    // 지금 음원을 읽는 중인지. 화면에 로딩 표시를 띄우는 데 쓴다.
    [ObservableProperty] private bool _isLoadingOgg;

    // 실패하면 false. 실패해도 이미 물려 있던 음원은 그대로 둔다.
    //
    // 예전에는 catch 가 _audioPlayer(= 기존 플레이어)를 Dispose 하고 파형·길이를 0/null 로
    // 밀어버렸다. 새로 고른 파일이 깨졌을 뿐인데 멀쩡하던 파형과 재생이 같이 사라졌다.
    public async Task<bool> LoadOggAsync(string filePath)
    {
        OggWaveform waveform;
        OggAudioPlayer audioPlayer;

        IsLoadingOgg = true;
        try
        {
            // 디코딩은 5분짜리 곡이면 수 초가 걸린다. UI 스레드에서 하면 그동안 창이 얼어붙는다.
            // 새 음원을 끝까지 다 읽고 나서야 기존 것을 건드린다.
            var decoded = await Task.Run(() => OggDecoder.Decode(filePath));

            waveform = OggPeakLoader.Load(decoded);
            audioPlayer = new OggAudioPlayer(decoded);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"OGG 로드 실패: {ex.Message}");
            return false;
        }
        finally
        {
            IsLoadingOgg = false;
        }

        StopPlayback(resetCursor: true);
        _audioPlayer?.Dispose();
        _audioPlayer = audioPlayer;
        OggDurationSeconds = waveform.DurationSeconds;
        OggPeaks = waveform.Peaks;
        OggOnsets = waveform.Onsets;
        OggFileName = Path.GetFileName(filePath);
        UpdateMeasureCountFromAudio();
        LastErrorMessage = null;
        return true;
    }

    public void LoadVideo(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        VideoFilePath = filePath;
        VideoFileName = Path.GetFileName(filePath);
    }

    public void ClearVideo()
    {
        VideoFilePath = null;
        VideoFileName = null;
    }

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (!await ConfirmDiscardIfNeededAsync("현재 작업 중인 내용이 모두 사라집니다.\n새로 만들까요?"))
            return;

        // 재생 중이던 배경 음원을 먼저 정리한다.
        StopPlayback(resetCursor: true);
        _audioPlayer?.Dispose();
        _audioPlayer = null;
        OggFileName = null;
        OggPeaks = null;
        OggOnsets = null;
        OggDurationSeconds = 0;
        ClearVideo();

        // 곡 길이가 0이 된 뒤에 초기화해야 UpdateMeasureCountFromAudio가 덮어쓰지 않는다.
        ResetDocumentState();

        // 새 차트는 명세서 기준값인 16분할 그리드로 시작한다.
        BeatSplit = 16;
        GridMeasure = 4;
    }

    // 차트/키음/헤더 등 문서 상태를 초기 상태로 되돌린다. (배경 음원·비디오는 대상 아님)
    private void ResetDocumentState()
    {
        Chart.Clear();
        WavList.Clear();
        SelectedWavItem = null;
        _selectedNotes.Clear();
        NotifySelectionChanged();

        PullHeaderFromChart();
        InvalidateTimeline();
        MeasureCount = Chart.MeasureCount;
        CurrentFilePath = null;
        DocumentEncoding = new UTF8Encoding(false);

        NotifyNotesChanged();
        OnPropertyChanged(nameof(SelectedNotes));

        // 빈 문서에서 이전 문서의 편집으로 거슬러 올라가면 안 된다.
        ResetEditHistory();

        // 방금 비운 직후는 "고친 것 없음"이다. 위 알림들이 세운 표시를 여기서 내린다.
        MarkClean();
    }

    // Chart.Header 의 값을 화면에 묶인 프로퍼티로 옮긴다.
    //
    // #PLAYER 는 파일에서 1/2/3 인데 콤보박스는 0부터 시작하는 인덱스라 한 칸 어긋난다.
    // 그 변환을 여기 한 곳에서만 하고, 되돌리는 쪽은 PlayerHeaderValue 가 맡는다.
    private void PullHeaderFromChart()
    {
        Title = Chart.Header.Title;
        Artist = Chart.Header.Artist;
        Genre = Chart.Header.Genre;
        Level = Chart.Header.Level;
        Bpm = Chart.Header.Bpm;
        Player = Math.Clamp(Chart.Header.Player - 1, 0, 2);
        Rank = Math.Clamp(Chart.Header.Rank, 0, 3);
        AudioOffsetMs = Chart.Header.AudioOffsetMs;
    }

    // 실패하면 false. 예전에는 조용히 무시해서 사용자가 성공한 줄 알았다.
    public bool LoadBms(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LastErrorMessage = "파일을 찾을 수 없습니다.";
            return false;
        }

        BmsParseResult parsed;

        try
        {
            // 먼저 다 읽고 나서 지운다. 파싱이 중간에 실패했을 때
            // 작업 중이던 내용까지 같이 날아가지 않도록 순서를 지킨다.
            parsed = BmsParser.Parse(filePath);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"BMS 로드 실패: {ex.Message}");
            return false;
        }

        ResetDocumentState();

        // 노트·보존줄·마디길이·BPM 변화·키음표 등 차트 안의 모든 컬렉션이 여기서 한꺼번에 옮겨진다.
        Chart.ReplaceContentWith(parsed.Chart);
        PullHeaderFromChart();
        InvalidateTimeline();
        MeasureCount = Chart.MeasureCount;

        // 읽어낸 인코딩을 기억해 두었다가 저장할 때 그대로 되돌려 쓴다.
        DocumentEncoding = parsed.Encoding;

        // 한 번에 갈아끼운다. 하나씩 Add 하면 항목마다 알림이 나가고,
        // 통계·팔레트 창이 그때마다 전체 재집계를 돈다. (BulkObservableCollection 주석 참고)
        WavList.ReplaceAll(parsed.WavItems);
        _keySoundPlayer.PreloadAsync(Chart.WavTable.Values);

        if (WavList.Count > 0)
        {
            SelectedWavItem = WavList[0];
        }

        CurrentFilePath = filePath;
        LastErrorMessage = null;

        // UI 렌더링 강제 업데이트 유도
        NotifyNotesChanged();

        // 방금 읽어온 이 상태가 되돌리기의 바닥이다. 여기보다 더 거슬러 올라가면
        // 이전 문서의 노트가 튀어나온다.
        ResetEditHistory();

        // 방금 읽어온 그대로다. 아직 고친 것이 없다.
        MarkClean();
        return true;
    }

    // 어떤 인코딩으로 쓸지 정한다.
    //
    // 기본은 읽어온 인코딩 그대로다(9번). 그런데 CP932 차트에 한글 제목을 넣는 식으로
    // 원본 인코딩이 담지 못하는 글자가 생기면, 그대로 쓸 경우 '?' 로 뭉개져 조용히 사라진다.
    // 인코딩을 지키려다 글자를 잃는 건 본말전도라, 그때만 UTF-8 로 물러나고 사실을 알린다.
    private Encoding ChooseSaveEncoding(string content)
    {
        LastWarningMessage = null;

        if (CanEncodeWithoutLoss(DocumentEncoding, content))
            return DocumentEncoding;

        LastWarningMessage =
            $"원본 인코딩({DocumentEncoding.WebName})으로 담을 수 없는 글자가 있어 UTF-8로 저장했습니다.\n" +
            "그대로 뒀다면 그 글자들이 '?' 로 바뀌어 사라졌을 것입니다.";

        return new UTF8Encoding(false);
    }

    private static bool CanEncodeWithoutLoss(Encoding encoding, string content)
    {
        // 원본 인코딩은 못 담는 글자를 조용히 '?' 로 바꾼다. 예외를 던지게 복제해서 확인한다.
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;

        try
        {
            strict.GetBytes(content);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    // 실패하면 false. 호출한 쪽에서 LastErrorMessage 를 사용자에게 보여준다.
    public bool SaveBms(string filePath)
    {
        try
        {
            var content = BmsWriter.Write(Chart, Title, Artist, Genre, Bpm, Player, Rank, Level, WavList, filePath);
            var encoding = ChooseSaveEncoding(content);

            // 원본을 바로 덮어쓰지 않는다. 쓰다 말면 되돌릴 방법이 없다. (SafeFileWriter 주석 참고)
            SafeFileWriter.WriteAllText(filePath, content, encoding);
            DocumentEncoding = encoding;
            CurrentFilePath = filePath;
            LastErrorMessage = null;
            MarkClean();
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"BMS 저장 실패: {ex.Message}");
            return false;
        }
    }
}

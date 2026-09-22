using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Downmix;

namespace bms_editer.ViewModels;

// 건반형 BMS(4K/5K/7K) 가져오기 — 뮤즈 대시 두 레인으로 접는다.
//
// 이 기능이 만들어 주는 것은 **리듬 뼈대**이지 완성된 채보가 아니다.
// 건반 BMS 에는 하트·장애물·보스 같은 뮤즈 대시 노트 종류 정보가 없어서
// 나오는 것은 전부 일반 노트와 홀드뿐이다. 종류는 사람이 얹는다.
//
// 되돌리기가 받쳐 준다. 결과가 마음에 안 들면 Ctrl+Z 한 번이면 원래대로다.
public sealed partial class MainWindowViewModel
{
    // 등록된 일반 노트와 홀드 정의의 완전성을 검사한다.
    public bool HasMuseDashKeysounds =>
        MuseDashKeyResolver.Resolve(Chart.WavTable, Chart.Notes, out _) is not null;

    // 다른 뮤즈 대시 차트에서 `#WAV` 표를 통째로 가져온다.
    //
    // 키음 파일 자체는 옮기지 않는다. 적힌 경로(`1번 씬 wav폴더\...`)는 그 차트 폴더 기준이라,
    // 새 차트를 다른 폴더에 저장하려면 **씬 wav 폴더도 같이 복사**해야 게임에서 소리가 난다.
    public int ImportKeysoundsFrom(string museDashBmsPath)
    {
        BmsParseResult parsed;
        try
        {
            parsed = BmsParser.Parse(museDashBmsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastErrorMessage = $"그 차트를 읽지 못했습니다.\n\n{ex.Message}";
            return -1;
        }

        if (MuseDashKeyResolver.Resolve(parsed.Chart.WavTable, parsed.Chart.Notes, out var reason) is null)
        {
            LastErrorMessage = $"그 차트에서도 뮤즈 대시 키음을 찾지 못했습니다.\n\n{reason}";
            return -1;
        }

        Chart.WavTable.Clear();
        foreach (var (key, value) in parsed.Chart.WavTable)
            Chart.WavTable[key] = value;

        // 한 번에 갈아끼운다. 하나씩 넣으면 항목마다 알림이 나가 통계·팔레트가 매번 재집계한다.
        WavList.ReplaceAll(parsed.WavItems);
        SelectedWavItem = WavList.FirstOrDefault();

        LastErrorMessage = null;
        return WavList.Count;
    }

    // 실패하면 null 을 돌려주고 LastErrorMessage 에 사유를 담는다.
    public async Task<DownmixReport?> ImportKeyboardChartAsync(string filePath)
    {
        KeyboardChart source;
        BmsParseResult parsedSource;
        try
        {
            source = KeyboardChartReader.Read(filePath);
            parsedSource = BmsParser.Parse(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            LastErrorMessage = ex.Message;
            return null;
        }
        if (parsedSource.Chart.HasConditionalBlocks)
        {
            LastErrorMessage = "조건 분기가 있는 BMS는 먼저 분기를 확정해야 변환할 수 있습니다.";
            return null;
        }
        if (source.Notes.Count == 0)
        {
            LastErrorMessage =
                "그 파일에서 건반 노트를 찾지 못했습니다.\n\n" +
                "1P 건반 채널(11~19)에 노트가 있는 BMS 인지 확인하십시오.";
            return null;
        }

        // 원본 WAV는 BGM에서도 참조할 수 있으므로 보존하고, 뮤즈 대시 노트 정의는 빈 번호에 새로 적는다.
        var targetWavs = new Dictionary<string, string>(parsedSource.Chart.WavTable);
        var keys = MuseDashKeyResolver.CreateDefinitions(
            targetWavs, WavKey.WidthOf(parsedSource.Chart), out var keyFailure);

        if (keys is null)
        {
            LastErrorMessage = keyFailure;
            return null;
        }

        if (!await ConfirmOverwriteForImportAsync())
            return null;

        var result = DownmixEngine.Convert(source, keys);
        if (result.Notes.Count == 0)
        {
            LastErrorMessage = "변환 결과가 비었습니다. 건반 노트가 전부 걸러졌습니다.";
            return null;
        }

        ReplaceDocumentWithDownmix(filePath, parsedSource, targetWavs, result);

        // 원본 폴더의 음원·영상도 같이 연다. 원본 차트는 그 음원에 맞춰 만든 것이라
        // 열자마자 뼈대를 곡에 맞춰 들어 볼 수 있다. (폴더 열기와 같은 규칙: FolderMedia)
        var media = await OpenCompanionMediaAsync(Path.GetDirectoryName(filePath)!);

        LastErrorMessage = null;
        return result.Report with
        {
            AudioFile = media.Audio,
            VideoFile = media.Video,
            MediaWarning = media.Warning,
        };
    }

    // 곡 폴더에서 음원과 영상을 찾아 연다.
    //
    // 음원을 못 열어도 가져오기는 실패가 아니다 — 노트는 이미 들어왔다. 사유만 돌려준다.
    // 영상이 없으면 지금 물린 영상을 내린다. 문서가 통째로 바뀌었으니 옛 곡의 영상이 남으면 안 된다.
    private async Task<(string? Audio, string? Video, string? Warning)> OpenCompanionMediaAsync(string folder)
    {
        string? audio = null;
        string? warning = null;

        var audioPath = FolderMedia.FindBestFile(folder, FolderMedia.AudioExtensions);
        if (audioPath is not null)
        {
            if (await LoadOggAsync(audioPath))
                audio = Path.GetFileName(audioPath);
            else
                warning = $"음원 {Path.GetFileName(audioPath)} 을(를) 열지 못했습니다. {LastErrorMessage}";
        }

        var videoPath = FolderMedia.FindBestFile(folder, FolderMedia.VideoExtensions);
        if (videoPath is not null)
            LoadVideo(videoPath);
        else
            ClearVideo();

        return (audio, videoPath is null ? null : Path.GetFileName(videoPath), warning);
    }

    // 변환은 **덮어쓰기**다. 기존 노트 위에 얹으면 어느 것이 변환 결과인지 알 수 없고,
    // 자리가 겹쳐 저장할 때 한쪽이 조용히 사라진다.
    private async Task<bool> ConfirmOverwriteForImportAsync()
    {
        if (Chart.Notes.Count == 0)
            return true;

        if (ConfirmAsync is not { } confirm)
        {
            LastErrorMessage = "확인 창을 띄울 수 없어 가져오기를 멈췄습니다.";
            return false;
        }

        return await confirm(
            $"지금 문서의 노트 {Chart.Notes.Count}개를 모두 지우고 변환 결과로 채웁니다.\n\n" +
            "되돌리기(Ctrl+Z)로 되돌릴 수 있습니다.\n\n계속할까요?");
    }

    // 원본에서 딸려 온 건반 줄. 보존줄에 남겨 두면 저장할 때 건반 채널이 그대로 따라 나온다.
    private static readonly Regex KeyboardLines =
        new(@"^#(?:\d{3}(?:[1256][1-9]):|LN(?:OBJ|TYPE)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 문서를 변환 결과로 통째로 갈아 끼운다. 되돌리기 한 칸에 전부 담기도록 한 번에 한다.
    private void ReplaceDocumentWithDownmix(
        string sourcePath,
        BmsParseResult parsedSource,
        IReadOnlyDictionary<string, string> targetWavs,
        DownmixResult result)
    {
        var folder = Path.GetDirectoryName(sourcePath)!;

        _history.UpdateBaselineDocument(TakeSnapshot("").Document!);
        _isRestoringHistory = true;
        try
        {
            _selectedNotes.Clear();
            Chart.ReplaceContentWith(parsedSource.Chart);
            Chart.PreservedLines.RemoveAll(line => KeyboardLines.IsMatch(line.Text));

            Chart.WavTable.Clear();
            foreach (var (key, text) in targetWavs)
                Chart.WavTable[key] = Path.Combine(folder, text);

            var originalWavs = parsedSource.WavItems.GroupBy(w => w.Key).ToDictionary(g => g.Key, g => g.Last());
            WavList.ReplaceAll(targetWavs.Select(pair => originalWavs.TryGetValue(pair.Key, out var original)
                ? CloneWav(original)
                : new BmsWavItem
                {
                    Key = pair.Key,
                    SourceText = pair.Value,
                    FilePath = Path.Combine(folder, pair.Value),
                }));

            SelectedWavItem = WavList.FirstOrDefault();
            InvalidateWavSnapshot();
            PullHeaderFromChart();
            InvalidateTimeline();
            MeasureCount = Math.Max(result.MeasureCount, Chart.MeasureCount);

            Chart.Notes.Clear();
            Chart.Notes.AddRange(result.Notes);

            // 마디 수는 줄이지 않고 필요한 만큼만 늘린다.
            UpdateMeasureCountFromAudio();
        }
        finally
        {
            _isRestoringHistory = false;
        }

        NotifyNotesChanged("건반 BMS 가져오기");
        NotifySelectionChanged();
    }

    // 가져오기 결과를 사람이 읽을 한 덩어리로. 창 쪽에서 그대로 띄운다.
    public string DescribeDownmixReport(DownmixReport report)
    {
        var lines = new System.Collections.Generic.List<string>
        {
            $"건반 노트 {report.SourceNotes}개 → 뮤즈 대시 노트 {report.CreatedNotes}개",
            $"지상 {report.GroundNotes}개 · 공중 {report.AirNotes}개 · 홀드 {report.CreatedHolds}쌍",
            $"씬 {report.Scene} 노트 정의를 자동으로 만들었습니다. 효과음 WAV 파일은 별도이고, "
                + "미리듣기에는 그 씬의 WAV 폴더가 필요합니다.",
            DescribeCompanionMedia(report),
            "",
            // 배분을 무엇으로 정했는지 같이 적는다. 게임 배치를 따랐는지 번호로 짐작했는지가
            // 결과를 읽는 기준이 된다.
            (report.LaneSource is { } game ? $"레인 배분 ({game} 화면 배치): " : "레인 배분 (채널 번호 순서): ")
                + string.Join(" · ", report.LaneMapping),
        };

        // 한쪽으로 쏠려서 배치를 손댔으면 어디를 손댔는지 밝힌다. 원본과 달라진 자리다.
        if (report.RebalancedChannels is { Count: > 0 } rebalanced)
        {
            lines.Add($"· 한쪽으로 쏠려서 채널 {string.Join(" · ", rebalanced)} 을(를) "
                      + "지상·공중 번갈아로 돌렸습니다.");
        }

        if (report.MovedToOtherLane > 0)
            lines.Add($"· 자리가 겹쳐 반대 레인으로 옮긴 노트 {report.MovedToOtherLane}개");

        if (report.DroppedTooDense > 0)
            lines.Add($"· 같은 자리에 셋 이상이라 버린 노트 {report.DroppedTooDense}개 (손이 둘뿐입니다)");

        if (report.DroppedOverlappingHold > 0)
            lines.Add($"· 다른 홀드와 겹쳐 버린 홀드 {report.DroppedOverlappingHold}개");

        if (report.DroppedZeroLengthHold > 0)
            lines.Add($"· 길이가 0이라 버린 홀드 {report.DroppedZeroLengthHold}개");

        foreach (var warning in report.Warnings)
            lines.Add("· " + warning);

        lines.Add("");
        lines.Add(HoldSummaryText);
        lines.Add("");
        lines.Add("이건 리듬 뼈대입니다. 하트·장애물·보스 같은 종류는 건반 BMS 에 없어서 "
                  + "전부 일반 노트와 홀드로만 나옵니다. 마음에 안 들면 Ctrl+Z 로 되돌리십시오"
                  + "(같이 연 음원·영상은 그대로 남습니다).");

        return string.Join("\n", lines.Where(l => l is not null));
    }

    private static string DescribeCompanionMedia(DownmixReport report)
    {
        var video = report.VideoFile is { } name ? $" 영상 {name} 은(는) 열었습니다." : "";

        if (report.MediaWarning is { } warning)
            return warning + video;

        if (report.AudioFile is { } audio)
        {
            return report.VideoFile is { } withVideo
                ? $"음원 {audio} · 영상 {withVideo} 을(를) 원본 폴더에서 같이 열었습니다."
                : $"음원 {audio} 을(를) 원본 폴더에서 같이 열었습니다.";
        }

        return "원본 폴더에 OGG 음원이 없어 차트만 가져왔습니다." + video;
    }
}

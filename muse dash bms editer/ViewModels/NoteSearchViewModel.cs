using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using bms_editer.Models;
using bms_editer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

// 검색/삭제/교체 창의 "열" 목록 한 칸. (레인 1개 = 토글 버튼 1개)
public sealed partial class LaneFilterItem : ObservableObject
{
    public string LaneId { get; }
    public string Header { get; }

    [ObservableProperty] private bool _isIncluded = true;

    public LaneFilterItem(LaneDefinition lane)
    {
        LaneId = lane.Id;
        Header = lane.Header;
    }
}

// 조건에 맞는 노트를 한 번에 찾아 선택/삭제하거나 키음 번호를 바꾸는 창의 상태 모델.
//
// 대상 노트 필터는 세 그룹(선택 상태 / 노트 종류 / 표시 여부)으로 나뉘며,
// 노트는 각 그룹에서 최소 하나씩 일치해야 결과에 포함된다.
// 그룹 안의 두 항목을 모두 끄면 그 그룹은 아무것도 통과시키지 않는다.
public sealed partial class NoteSearchViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;

    // BmsParser가 2자리/3자리 키음 배치를 모두 지원하므로,
    // 이 차트가 실제로 쓰는 자릿수에 맞춰 번호 범위의 기본값과 최댓값을 정한다.
    //
    // **매번 다시 본다.** 예전에는 창을 만들 때 한 번 정하고 굳혀서, 창을 띄워둔 채
    // 3자리 차트를 열면 번호 범위가 2자리에 머물러 3자리 키음을 아예 찾을 수 없었다.
    private int KeyWidth => _owner.WavKeyWidth;

    private int MaxKeyValue => WavKey.MaxValue(KeyWidth);

    public NoteSearchViewModel(MainWindowViewModel owner)
    {
        _owner = owner;
        foreach (var lane in owner.Chart.Lanes)
        {
            Lanes.Add(new LaneFilterItem(lane));
        }

        _wavKeyFrom = ToBase36(1);
        _wavKeyTo = new string('Z', KeyWidth);
        _replacementWavKey = ToBase36(1);
    }

    public ObservableCollection<LaneFilterItem> Lanes { get; } = new();

    // "롱"·"숨기기" 필터를 쓸 수 있는지.
    //
    // BmsParser 는 편집 대상인 건반 채널(11~18)만 노트로 만들고 전부 Normal 로 둔다.
    // 롱노트(51~59)·숨김(31~39) 채널은 원문 보존으로 빠지므로, 이 두 필터는
    // 어떤 노트에도 해당되지 않는다. 눌러도 아무 일이 없는 버튼을 켜 두면
    // "조건을 잘못 넣었나" 하고 사용자만 헤매므로, 해당되는 노트가 생기기 전까지는 잠근다.
    public bool AreNoteTypeFiltersUsable =>
        _owner.Chart.Notes.Any(n => n.Type != NoteType.Normal);

    public string NoteTypeFilterHint => AreNoteTypeFiltersUsable
        ? "노트 종류로 거릅니다"
        : "이 에디터는 아직 롱노트·숨김 노트를 편집 대상으로 읽지 않습니다. 해당되는 노트가 없어 잠겨 있습니다";

    // 대상 노트 - 선택 상태
    [ObservableProperty] private bool _includeSelected = true;
    [ObservableProperty] private bool _includeUnselected = true;

    // 대상 노트 - 노트 종류
    [ObservableProperty] private bool _includeNormal = true;
    [ObservableProperty] private bool _includeLong = true;

    // 대상 노트 - 표시 여부
    [ObservableProperty] private bool _includeHidden = true;
    [ObservableProperty] private bool _includeVisible = true;

    [ObservableProperty] private int _measureFrom;
    [ObservableProperty] private int _measureTo = 999;

    [ObservableProperty] private string _wavKeyFrom = string.Empty;
    [ObservableProperty] private string _wavKeyTo = string.Empty;

    [ObservableProperty] private string _replacementWavKey = string.Empty;

    // 복사할 때 옮길 마디 수. 음수면 앞쪽으로 복사한다.
    [ObservableProperty] private int _copyMeasureOffset = 1;

    [ObservableProperty] private string _statusMessage = "조건을 정한 뒤 아래 작업 버튼을 누르세요.";

    // 조건에 맞는 노트를 차트 순서 그대로 모아 돌려준다.
    public IReadOnlyList<BmsNote> FindMatches()
    {
        var laneIds = Lanes
            .Where(lane => lane.IsIncluded)
            .Select(lane => lane.LaneId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (laneIds.Count == 0)
            return Array.Empty<BmsNote>();

        var measureLow = Math.Min(MeasureFrom, MeasureTo);
        var measureHigh = Math.Max(MeasureFrom, MeasureTo);

        // 번호 범위는 base-36 값으로 비교한다. 입력이 잘못돼 있으면 그쪽 끝을 열어 둔다.
        var keyLow = TryParseBase36(WavKeyFrom, out var parsedLow) ? parsedLow : 0;
        var keyHigh = TryParseBase36(WavKeyTo, out var parsedHigh) ? parsedHigh : MaxKeyValue;
        if (keyLow > keyHigh)
            (keyLow, keyHigh) = (keyHigh, keyLow);

        var selected = new HashSet<BmsNote>(_owner.SelectedNotes);
        var matches = new List<BmsNote>();

        foreach (var note in _owner.Chart.Notes)
        {
            if (!laneIds.Contains(note.LaneId))
                continue;

            if (note.Measure < measureLow || note.Measure > measureHigh)
                continue;

            if (!TryParseBase36(note.WavKey, out var noteKey) || noteKey < keyLow || noteKey > keyHigh)
                continue;

            var isSelected = selected.Contains(note);
            if (!(isSelected ? IncludeSelected : IncludeUnselected))
                continue;

            var isLong = note.Type is NoteType.LongStart or NoteType.LongEnd;
            if (!(isLong ? IncludeLong : IncludeNormal))
                continue;

            var isHidden = note.Type == NoteType.Invisible;
            if (!(isHidden ? IncludeHidden : IncludeVisible))
                continue;

            matches.Add(note);
        }

        return matches;
    }

    [RelayCommand]
    private void SelectMatches()
    {
        var matches = FindMatches();
        _owner.SetNoteSelection(matches, NoteSelectionSource.Search);
        StatusMessage = matches.Count == 0
            ? "조건에 맞는 노트가 없습니다."
            : $"{matches.Count}개를 선택했습니다.";
    }

    [RelayCommand]
    private void DeleteMatches()
    {
        var matches = FindMatches();
        var removed = _owner.DeleteNotes(matches);
        StatusMessage = removed == 0
            ? "조건에 맞는 노트가 없습니다."
            : $"{removed}개를 삭제했습니다.";
    }

    // 조건에 맞는 노트를 지정한 마디 수만큼 옮긴 자리에 복제한다.
    // 후렴 패턴을 뒤쪽 마디로 옮겨 붙일 때 쓴다.
    [RelayCommand]
    private void CopyMatches()
    {
        if (CopyMeasureOffset == 0)
        {
            StatusMessage = "옮길 마디 수가 0이면 제자리라 복사할 수 없습니다.";
            return;
        }

        var matches = FindMatches();
        if (matches.Count == 0)
        {
            StatusMessage = "조건에 맞는 노트가 없습니다.";
            return;
        }

        var result = _owner.CopyNotesByMeasureOffset(matches, CopyMeasureOffset);

        var skipped = new List<string>();
        if (result.Blocked > 0)
            skipped.Add($"이미 노트가 있는 자리 {result.Blocked}개");
        if (result.OutOfRange > 0)
            skipped.Add($"마디 범위 밖 {result.OutOfRange}개");

        var tail = skipped.Count > 0 ? $" (건너뜀: {string.Join(", ", skipped)})" : string.Empty;
        var direction = CopyMeasureOffset > 0 ? "뒤" : "앞";
        var distance = Math.Abs(CopyMeasureOffset);

        StatusMessage = result.Copied == 0
            ? $"복사된 노트가 없습니다.{tail}"
            : $"{result.Copied}개를 {distance}마디 {direction}로 복사하고 선택했습니다.{tail}";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _owner.ClearNoteSelection();
        StatusMessage = "선택을 해제했습니다.";
    }

    [RelayCommand]
    private void ReplaceWavKey()
    {
        if (!TryParseBase36(ReplacementWavKey, out var parsed) || parsed == 0 || parsed > MaxKeyValue)
        {
            StatusMessage = $"바꿀 번호는 {ToBase36(1)} ~ {new string('Z', KeyWidth)} 사이여야 합니다.";
            return;
        }

        var normalizedKey = ToBase36(parsed);
        var matches = FindMatches();
        var changed = _owner.ReplaceWavKey(matches, normalizedKey);
        ReplacementWavKey = normalizedKey;

        if (matches.Count == 0)
        {
            StatusMessage = "조건에 맞는 노트가 없습니다.";
            return;
        }

        var unregistered = _owner.Chart.WavTable.ContainsKey(normalizedKey)
            ? string.Empty
            : $" (경고: #WAV{normalizedKey}가 등록돼 있지 않습니다)";

        StatusMessage = changed == 0
            ? $"{matches.Count}개가 이미 {normalizedKey}입니다.{unregistered}"
            : $"{changed}개를 {normalizedKey}(으)로 바꿨습니다.{unregistered}";
    }

    [RelayCommand]
    private void IncludeAllLanes() => SetAllLanes(true);

    [RelayCommand]
    private void ExcludeAllLanes() => SetAllLanes(false);

    [RelayCommand]
    private void InvertLanes()
    {
        foreach (var lane in Lanes)
        {
            lane.IsIncluded = !lane.IsIncluded;
        }
    }

    private void SetAllLanes(bool isIncluded)
    {
        foreach (var lane in Lanes)
        {
            lane.IsIncluded = isIncluded;
        }
    }

    // base-36 키 변환 규칙은 Services/WavKey 한 곳에 있다.
    private static bool TryParseBase36(string? text, out int value) => WavKey.TryParse(text, out value);

    // 차트가 쓰는 자릿수(2자리 또는 3자리)에 맞춰 키 문자열을 만든다.
    private string ToBase36(int value) => WavKey.Format(value, KeyWidth);
}

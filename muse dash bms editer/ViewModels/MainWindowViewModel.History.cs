using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

// 되돌리기 / 다시 하기. (known_issues.md C 항목)
//
// 왜 이게 "속도 기능"인가:
// 되돌릴 수 없으면 일괄 작업을 과감하게 못 쓴다. 대신 확인창을 읽고, 파일 사본을 만들고,
// 결과를 눈으로 다시 본다. 실측에서 그 방어 비용이 20~30분이었다(authoring_time.md).
//
// 담는 범위는 **노트와 키음 표**다. 헤더(제목·BPM)나 화면 설정(줌·보기 방향)은 담지 않는다.
// 노트를 지웠다가 되돌렸더니 BPM 까지 옛날 값으로 돌아가면 그게 더 놀랍다.
public sealed partial class MainWindowViewModel
{
    private readonly EditHistory _history = new();

    // 되돌리는 중에는 기록하지 않는다. 이 표시가 없으면 되돌리기가 그 자체로 한 칸을 쌓아
    // 되돌리기를 두 번 눌러야 한 칸 움직이고, 다시 하기는 영영 비활성이 된다.
    private bool _isRestoringHistory;

    // 키음 표 스냅샷 캐시. 키음은 노트에 비해 거의 안 바뀌는데(실측 차트는 628개 고정)
    // 노트를 한 번 찍을 때마다 628개를 복제하면 메모리와 시간이 아깝다.
    // WavList 가 바뀔 때 무효화한다. (BmsWavItem 의 속성을 제자리에서 고치는 코드는 없다)
    private IReadOnlyList<BmsWavItem>? _wavSnapshotCache;
    private IReadOnlyDictionary<string, string>? _wavTableSnapshotCache;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    // 메뉴에 "되돌리기 (노트 삭제)" 처럼 무엇이 취소되는지 같이 보여준다.
    // 무엇이 되돌아갈지 모르면 누르기를 망설이게 된다.
    public string UndoMenuHeader =>
        _history.UndoLabel is { } label ? $"되돌리기 ({label})" : "되돌리기";

    public string RedoMenuHeader =>
        _history.RedoLabel is { } label ? $"다시 하기 ({label})" : "다시 하기";

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var restoreDocument = _history.UndoLabel == "건반 BMS 가져오기";
        if (_history.Undo() is { } snapshot)
            ApplySnapshot(snapshot, restoreDocument);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var restoreDocument = _history.RedoLabel == "건반 BMS 가져오기";
        if (_history.Redo() is { } snapshot)
            ApplySnapshot(snapshot, restoreDocument);
    }

    // 문서를 새로 열거나 비운 직후. 이전 문서의 편집으로 거슬러 올라가면 안 된다.
    private void ResetEditHistory()
    {
        InvalidateWavSnapshot();
        _history.Reset(TakeSnapshot("불러오기"));
        NotifyHistoryChanged();
    }

    // 편집이 끝날 때마다 NotifyNotesChanged 가 부른다.
    private void RecordHistory(string label)
    {
        if (_isRestoringHistory)
            return;

        _history.Record(TakeSnapshot(label));
        NotifyHistoryChanged();
    }

    private void InvalidateWavSnapshot()
    {
        _wavSnapshotCache = null;
        _wavTableSnapshotCache = null;
    }

    // 노트 -> 자리 번호. 선택을 번호로 옮길 때 쓴다.
    // 노트가 바뀔 때만 다시 만든다(NotifyNotesChanged). 드래그 선택은 포인터가 움직일 때마다
    // 오므로, 그때마다 전체를 훑으면 큰 차트에서 끌리는 느낌이 난다.
    private Dictionary<BmsNote, int>? _noteIndexCache;

    private Dictionary<BmsNote, int> GetNoteIndexes()
    {
        if (_noteIndexCache is { } cached)
            return cached;

        var map = new Dictionary<BmsNote, int>(Chart.Notes.Count);
        for (var i = 0; i < Chart.Notes.Count; i++)
            map[Chart.Notes[i]] = i;

        return _noteIndexCache = map;
    }

    // 선택은 노트 참조가 아니라 **자리 번호**로 담는다. 되돌리면 노트가 복제본으로 바뀌어
    // 예전 참조는 어느 목록에도 없게 되기 때문이다.
    private List<int> CurrentSelectionIndices()
    {
        var indexOf = GetNoteIndexes();
        var selected = new List<int>(_selectedNotes.Count);
        foreach (var note in _selectedNotes)
        {
            if (indexOf.TryGetValue(note, out var index))
                selected.Add(index);
        }

        return selected;
    }

    // 선택만 바뀌었을 때. 칸을 쌓지 않고 지금 칸의 선택만 따라가게 한다.
    private void TrackSelectionForHistory()
    {
        if (_isRestoringHistory)
            return;

        _history.UpdateBaselineSelection(CurrentSelectionIndices(), SelectedWavItem?.Key);
    }

    private EditSnapshot TakeSnapshot(string label)
    {
        var selected = CurrentSelectionIndices();

        _wavSnapshotCache ??= WavList.Select(CloneWav).ToArray();
        _wavTableSnapshotCache ??= new Dictionary<string, string>(Chart.WavTable);

        var document = new BmsChart();
        document.ReplaceContentWith(Chart);
        document.Notes.Clear();
        document.Header.Title = Title;
        document.Header.Artist = Artist;
        document.Header.Genre = Genre;
        document.Header.Bpm = Bpm;
        return new EditSnapshot(
            Chart.Notes.Select(CloneNote).ToArray(),
            _wavSnapshotCache,
            _wavTableSnapshotCache,
            selected,
            SelectedWavItem?.Key,
            label, document);
    }

    private void ApplySnapshot(EditSnapshot snapshot, bool restoreDocument = false)
    {
        _isRestoringHistory = true;
        try
        {
            if (restoreDocument && snapshot.Document is { } document)
            {
                Chart.ReplaceContentWith(document);
                PullHeaderFromChart();
                InvalidateTimeline();
                MeasureCount = Chart.MeasureCount;
            }
            // 스냅샷은 다시 쓰일 수 있다(되돌리기 → 다시 하기 → 되돌리기). 담긴 것을 그대로
            // 문서에 넣으면 그 뒤의 편집이 스냅샷 안의 노트를 제자리에서 고쳐 버린다. 그래서 또 복제한다.
            Chart.Notes.Clear();
            Chart.Notes.AddRange(snapshot.Notes.Select(CloneNote));

            Chart.WavTable.Clear();
            foreach (var (key, value) in snapshot.WavTable)
                Chart.WavTable[key] = value;

            WavList.ReplaceAll(snapshot.Wavs.Select(CloneWav));
            InvalidateWavSnapshot();

            // 키음 선택은 참조가 아니라 번호로 되살린다. 목록이 통째로 바뀌었으므로
            // 예전 항목을 그대로 두면 어느 목록에도 없는 것을 가리킨다.
            SelectedWavItem = snapshot.SelectedWavKey is { } wavKey
                ? WavList.FirstOrDefault(w => string.Equals(w.Key, wavKey, StringComparison.OrdinalIgnoreCase))
                : null;

            _selectedNotes.Clear();
            foreach (var index in snapshot.SelectedNoteIndices)
            {
                if (index >= 0 && index < Chart.Notes.Count)
                    _selectedNotes.Add(Chart.Notes[index]);
            }

            NotifyNotesChanged();
            NotifySelectionChanged();
        }
        finally
        {
            _isRestoringHistory = false;
        }

        // 되돌린 것도 "파일과 달라진 상태"다. 되돌려서 원본과 같아졌는지까지는 보지 않는다.
        MarkDirty();
        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoMenuHeader));
        OnPropertyChanged(nameof(RedoMenuHeader));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private static BmsNote CloneNote(BmsNote note) => new()
    {
        Measure = note.Measure,
        LaneId = note.LaneId,
        Position = note.Position,
        WavKey = note.WavKey,
        Type = note.Type,
        BranchId = note.BranchId,
        SourceLineOrder = note.SourceLineOrder,
    };

    private static BmsWavItem CloneWav(BmsWavItem item) => new()
    {
        Key = item.Key,
        FilePath = item.FilePath,
        SourceText = item.SourceText,
        IsPathGuessed = item.IsPathGuessed,
    };
}

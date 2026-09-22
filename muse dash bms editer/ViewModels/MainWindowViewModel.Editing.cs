using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel
{
    public BulkObservableCollection<BmsWavItem> WavList { get; } = new();
    [ObservableProperty] private BmsWavItem? _selectedWavItem;

    private IReadOnlyList<BmsNote>? _notesCache;
    public IReadOnlyList<BmsNote> Notes => _notesCache ??= Chart.Notes.ToArray();

    private readonly HashSet<BmsNote> _selectedNotes = new();
    private IReadOnlyList<BmsNote> _selectedNotesCache = Array.Empty<BmsNote>();
    public IReadOnlyList<BmsNote> SelectedNotes => _selectedNotesCache;

    // 시각 순으로 정렬한 노트. 재생 중 "지금 울릴 노트"를 이진 탐색으로 찾는 데 쓴다.
    // 노트가 바뀔 때만 다시 만든다. (NotifyNotesChanged 참고)
    private BmsNote[]? _sortedNotesCache;

    private BmsNote[] GetSortedNotes() =>
        _sortedNotesCache ??= Chart.Notes.OrderBy(n => n.Measure + n.Position).ToArray();

    // 노트가 바뀌었다고 알린다. 화면 갱신과 정렬 캐시 무효화가 늘 짝이어야 해서 한곳에 모은다.
    //
    // label 은 되돌리기 메뉴에 "되돌리기 (노트 삭제)" 로 보이는 이름이다.
    private void NotifyNotesChanged(string label = "편집")
    {
        _sortedNotesCache = null;
        _notesCache = Chart.Notes.ToArray();
        _noteIndexCache = null;
        MarkDirty();

        // 모든 편집 경로가 여기를 지난다. 홀드 짝과 되돌리기 기록도 여기서 한 번에 처리한다.
        // (삭제·이동·키음 교체·복제 어느 것이든 짝을 깨고, 되돌릴 거리가 된다)
        RecordHistory(label);
        RecomputeHolds();
        OnPropertyChanged(nameof(Notes));
    }

    private void NotifySelectionChanged()
    {
        _selectedNotesCache = _selectedNotes.ToArray();

        // 선택은 되돌리기 칸을 쌓지 않지만, 지금 칸이 들고 있는 선택은 따라가야 한다.
        // 그래야 되돌렸을 때 "지우기 직전에 고른 것"이 돌아온다.
        TrackSelectionForHistory();
        OnPropertyChanged(nameof(SelectedNotes));
    }

    [RelayCommand]
    private void SelectNotes(NoteSelectionArgs args)
    {
        if (!args.Additive)
        {
            SetNoteSelection(args.Notes);
            return;
        }

        // 이미 고른 것에 더한다. 이미 들어 있는 것을 다시 고르면 빼서, 잘못 잡은 것을 되돌릴 수 있다.
        foreach (var note in args.Notes)
        {
            if (!_selectedNotes.Add(note))
                _selectedNotes.Remove(note);
        }

        NotifySelectionChanged();
    }

    // 격자 밖(검색/삭제/교체 창, 통계 창 등)에서 선택 집합을 통째로 교체한다.
    // source 는 격자가 강조 색을 고르는 데만 쓴다. 선택 자체의 의미는 같다.
    public void SetNoteSelection(IEnumerable<BmsNote> notes, NoteSelectionSource source = NoteSelectionSource.Grid)
    {
        _selectedNotes.Clear();
        foreach (var note in notes)
        {
            _selectedNotes.Add(note);
        }
        NotifySelectionChanged();

        // 격자 밖에서 고른 선택은 화면 밖에 있기 쉽다. 거기로 스크롤해 준다.
        if (source != NoteSelectionSource.Grid)
            RequestScrollToSelection();
    }

    // 선택한 자리로 격자를 스크롤해 달라고 뷰에 알린다. 0~1 비율로 넘긴다.
    //
    // 통계 창에서 키음 번호를 눌러도, 검색 창에서 조건 선택을 해도, 그 노트들이
    // 지금 보이는 구간 밖에 있으면 **화면에서는 아무 일도 안 일어난 것처럼 보인다.**
    // 선택은 됐는데 어디가 선택됐는지 알 길이 없어서 기능이 없는 줄 알게 된다.
    public event Action<double>? ScrollToRatioRequested;

    private void RequestScrollToSelection()
    {
        if (_selectedNotes.Count == 0)
            return;

        // 여러 개를 골랐으면 가장 앞선 노트를 기준으로 삼는다.
        var first = _selectedNotes.MinBy(n => n.Measure + n.Position);
        if (first is null)
            return;

        var measurePosition = first.Measure + first.Position;

        var ratio = OggDurationSeconds > 0
            ? Timeline.SecondsAt(measurePosition) / OggDurationSeconds
            : measurePosition / Math.Max(1, MeasureCount);

        ScrollToRatioRequested?.Invoke(Math.Clamp(ratio, 0, 1));
    }

    [RelayCommand]
    public void ClearNoteSelection() => SetNoteSelection(Array.Empty<BmsNote>());

    // 선택 여부와 관계없이 지정한 노트들을 지우고, 실제로 지워진 개수를 돌려준다.
    public int DeleteNotes(IReadOnlyList<BmsNote> notes)
    {
        if (notes.Count == 0) return 0;

        var targetList = ReferenceEquals(notes, Chart.Notes) ? notes.ToArray() : notes;
        var removed = 0;
        foreach (var note in targetList)
        {
            if (!Chart.Notes.Remove(note)) continue;

            _selectedNotes.Remove(note);
            removed++;
        }

        if (removed > 0)
        {
            NotifyNotesChanged("노트 삭제");
            NotifySelectionChanged();
        }

        return removed;
    }

    // 지정한 노트들을 마디 단위로 옮긴 자리에 복제한다.
    //
    // 이미 노트가 있는 자리와 마디 범위 밖은 건너뛴다. 겹쳐 두면 저장할 때
    // BmsWriter 가 같은 슬롯을 덮어써서 한쪽이 조용히 사라지므로 아예 만들지 않는다.
    // 만든 노트를 선택 상태로 두어 결과를 바로 확인하고 이어서 손볼 수 있게 한다.
    public NoteCopyResult CopyNotesByMeasureOffset(IReadOnlyList<BmsNote> notes, int measureOffset)
    {
        if (notes.Count == 0 || measureOffset == 0)
            return new NoteCopyResult(0, 0, 0);

        // 원본뿐 아니라 이번에 새로 만든 것까지 함께 봐야 복제본끼리 겹치는 것도 막힌다.
        var occupied = new HashSet<(string Lane, int Measure, long Slot)>();
        foreach (var existing in Chart.Notes)
            occupied.Add(ToSlotKey(existing.LaneId, existing.Measure, existing.Position));

        var created = new List<BmsNote>();
        var blocked = 0;
        var outOfRange = 0;

        foreach (var note in notes)
        {
            var targetMeasure = note.Measure + measureOffset;
            if (targetMeasure < 0 || targetMeasure >= MeasureCount)
            {
                outOfRange++;
                continue;
            }

            if (!occupied.Add(ToSlotKey(note.LaneId, targetMeasure, note.Position)))
            {
                blocked++;
                continue;
            }

            created.Add(new BmsNote
            {
                Measure = targetMeasure,
                LaneId = note.LaneId,
                Position = note.Position,
                WavKey = note.WavKey,
                Type = note.Type,
            });
        }

        if (created.Count > 0)
        {
            Chart.Notes.AddRange(created);
            SetNoteSelection(created, NoteSelectionSource.Search);
            NotifyNotesChanged("마디 복제");
        }

        return new NoteCopyResult(created.Count, blocked, outOfRange);
    }

    // 마디 내 위치를 0.0001 단위로 끊어 슬롯 키를 만든다.
    // BMS 위치는 최대 1/1920(≈0.00052) 간격이라 서로 다른 자리가 같은 칸에 들어가지 않고,
    // double 오차로 미세하게 다른 같은 자리는 하나로 묶인다.
    private static (string, int, long) ToSlotKey(string laneId, int measure, double position) =>
        (laneId, measure, (long)Math.Round(position * 10000));

    // 지정한 노트들의 키음 번호를 한꺼번에 바꾸고, 실제로 바뀐 개수를 돌려준다.
    public int ReplaceWavKey(IReadOnlyList<BmsNote> notes, string wavKey)
    {
        if (notes.Count == 0 || string.IsNullOrWhiteSpace(wavKey)) return 0;

        var changed = 0;
        foreach (var note in notes)
        {
            if (note.WavKey == wavKey) continue;

            note.WavKey = wavKey;
            changed++;
        }

        if (changed > 0)
            NotifyNotesChanged("키음 교체");

        return changed;
    }

    [RelayCommand]
    private void DeleteSelectedNotes()
    {
        if (_selectedNotes.Count == 0) return;

        // 바로 옆에 있는 DeleteNotes 가 같은 일을 한다. 루프를 두 벌 두면 한쪽만 고치게 된다.
        DeleteNotes(SelectedNotes);
    }

    // 선택한 노트를 통째로 옮긴다. **하나라도 못 가면 아무것도 옮기지 않는다.**
    //
    // 예전에는 노트마다 따로 판단해서, 앞이 막히면 막힌 것만 제자리에 남고 나머지는
    // 움직였다. 선택의 상대 간격이 무너져서 패턴 모양이 깨졌다(6번). 게다가
    // 자리 검사가 선택된 노트를 통째로 제외해서, 남은 노트 위로 겹쳐 올라갈 수도 있었다.
    // 겹치면 저장할 때 BmsWriter 가 같은 슬롯을 덮어써 한쪽이 조용히 사라진다(5번).
    [RelayCommand]
    private void MoveSelectedNotes(NoteMoveDirection direction)
    {
        if (_selectedNotes.Count == 0) return;

        var split = Math.Max(1, BeatSplit);
        var lanes = Chart.Lanes;

        // "수직위치 고정"이 켜져 있으면 시간 위치는 건드리지 않는다. 레인만 옮긴다.
        // (세로 뷰에서 세로축이 곧 시간축이라 체크박스 이름이 이렇게 붙어 있다)
        if (LockVerticalPosition && direction is NoteMoveDirection.TimeForward or NoteMoveDirection.TimeBackward)
            return;

        // 1단계: 옮길 자리를 전부 미리 구한다. 하나라도 못 구하면 그만둔다.
        var moves = new List<(BmsNote Note, int Measure, double Position, string LaneId)>(_selectedNotes.Count);

        foreach (var note in _selectedNotes)
        {
            var target = direction switch
            {
                NoteMoveDirection.TimeForward => OffsetInTime(note, 1, split),
                NoteMoveDirection.TimeBackward => OffsetInTime(note, -1, split),
                NoteMoveDirection.LanePrevious => OffsetInLane(note, -1, lanes),
                NoteMoveDirection.LaneNext => OffsetInLane(note, 1, lanes),
                _ => null,
            };

            if (target is not { } t)
                return;

            moves.Add((note, t.Measure, t.Position, t.LaneId));
        }

        // 2단계: 선택 밖의 노트와 부딪히는지, 옮긴 것끼리 겹치는지 확인한다.
        var occupied = new HashSet<(string, int, long)>();
        foreach (var note in Chart.Notes)
        {
            if (!_selectedNotes.Contains(note))
                occupied.Add(ToSlotKey(note.LaneId, note.Measure, note.Position));
        }

        foreach (var move in moves)
        {
            if (!occupied.Add(ToSlotKey(move.LaneId, move.Measure, move.Position)))
                return;
        }

        // 3단계: 확인이 끝났으니 한꺼번에 옮긴다.
        foreach (var (note, measure, position, laneId) in moves)
        {
            note.Measure = measure;
            note.Position = position;
            note.LaneId = laneId;
        }

        NotifyNotesChanged("노트 이동");
    }

    // 노트를 시간축으로 격자 한 칸만큼 **옮긴다.** 격자에 붙이지 않는다.
    //
    // 예전에는 Math.Round((Measure + Position) * split) 으로 위치를 다시 계산해서,
    // 현재 격자로 표현할 수 없는 노트는 옮기는 순간 자리가 바뀌었다.
    // 12분할로 찍은 3잇단음을 16분할 상태에서 건드리면 잇단음이 뭉개졌다.
    private (int Measure, double Position, string LaneId)? OffsetInTime(BmsNote note, int steps, int split)
    {
        var total = note.Measure + note.Position + ((double)steps / split);
        if (total < 0)
            return null;

        // 부동소수 오차로 마디 경계에서 한 칸 밀리지 않도록 여유를 두고 자른다.
        var measure = (int)Math.Floor(total + PositionEpsilon);
        var position = total - measure;
        if (position < PositionEpsilon)
            position = 0.0;

        if (measure < 0 || measure >= MeasureCount)
            return null;

        return (measure, position, note.LaneId);
    }

    private static (int Measure, double Position, string LaneId)? OffsetInLane(
        BmsNote note, int steps, IReadOnlyList<LaneDefinition> lanes)
    {
        var currentIndex = -1;
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].Id == note.LaneId)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex == -1)
            return null;

        var newIndex = currentIndex + steps;
        if (newIndex < 0 || newIndex >= lanes.Count)
            return null;

        return (note.Measure, note.Position, lanes[newIndex].Id);
    }

    // 마디 내 위치를 같은 자리로 볼 허용오차. ToSlotKey 의 1/10000 눈금과 짝이다.
    private const double PositionEpsilon = 0.0001;

    // 이 차트가 쓰는 키 자릿수. 규칙은 WavKey 한 곳에 있고, 검색 창과 컨트롤 패널도
    // 번호 범위의 최댓값을 여기서 받아 간다.
    public int WavKeyWidth => WavKey.WidthOf(Chart);

    // 비어 있는 다음 키음 번호. **차트가 쓰는 자릿수에 맞춰서** 만든다.
    //
    // 예전에는 항상 2자리만 만들었다. 기존 키가 전부 3자리인 차트에서는 2자리 "01" 이
    // "비어 있는" 키로 보이는데, 저장할 때 BmsWriter 가 폭을 3으로 맞추면서 "001" 이 되어
    // 기존 001번 키음을 조용히 덮어썼다. 001을 쓰던 노트가 전부 다른 소리로 바뀌었다.
    private string GetNextWavKey()
    {
        var width = WavKeyWidth;
        var limit = WavKey.MaxValue(width);

        // 00 / 000 은 "빈 자리"를 뜻하므로 1부터 시작한다.
        for (var value = 1; value <= limit; value++)
        {
            var key = WavKey.Format(value, width);
            if (!Chart.WavTable.ContainsKey(key))
                return key;
        }

        throw new InvalidOperationException("WAV 키 한도를 초과했습니다.");
    }

    // 실패하면 false. 예전에는 예외를 삼켜서 [키음 추가]가 조용히 아무 일도 안 했다.
    public bool AddWav(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LastErrorMessage = "파일을 찾을 수 없습니다.";
            return false;
        }

        try
        {
            var key = GetNextWavKey();
            Chart.WavTable[key] = filePath;
            _keySoundPlayer.Preload(filePath);
            var item = new BmsWavItem { Key = key, FilePath = filePath };
            WavList.Add(item);
            SelectedWavItem = item;
            LastErrorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"WAV 추가 실패: {ex.Message}");
            return false;
        }
    }

    // 이 번호를 쓰는 노트 개수.
    public int CountNotesUsingWavKey(string key) =>
        Chart.Notes.Count(n => string.Equals(n.WavKey, key, StringComparison.OrdinalIgnoreCase));

    // 키음을 지운다. 그 번호를 쓰는 노트가 있으면 먼저 확인을 받는다.
    //
    // 예전에는 테이블에서만 지우고 노트는 그대로 뒀다. 남은 노트는 소리가 나지 않는
    // 유령 노트가 되고, 저장하면 #WAV 정의가 없는 번호를 가리키는 파일이 나온다.
    // 게다가 새 키음을 추가하면 비어 있는 그 번호가 다시 쓰여서,
    // 유령 노트들이 갑자기 엉뚱한 소리를 내기 시작한다.
    [RelayCommand]
    private async Task RemoveWavAsync()
    {
        if (SelectedWavItem is null) return;

        var key = SelectedWavItem.Key;
        var usedBy = CountNotesUsingWavKey(key);

        if (usedBy > 0)
        {
            if (ConfirmAsync is not { } confirm)
                return;

            var proceed = await confirm(
                $"#WAV{key} 를 쓰는 노트가 {usedBy}개 있습니다.\n\n" +
                "지우면 그 노트들은 소리가 나지 않는 유령 노트가 됩니다.\n" +
                "나중에 키음을 추가하면 이 번호가 다시 쓰여서, 그 노트들이 엉뚱한 소리를 내게 됩니다.\n\n" +
                "그래도 지울까요?");

            if (!proceed)
                return;
        }

        Chart.WavTable.Remove(key);
        WavList.Remove(SelectedWavItem);
        SelectedWavItem = null;
    }

    [RelayCommand]
    private void PlaceNote(NotePlacementArgs args)
    {
        if (IsPlaying)
            StopPlaybackAtCurrentPosition();

        if (SelectedWavItem is null) return;

        var wavKey = SelectedWavItem.Key;

        var existing = Chart.Notes.Find(n => 
            n.Measure == args.Measure && 
            n.LaneId == args.LaneId && 
            Math.Abs(n.Position - args.Position) < 0.0001);

        if (existing is not null)
        {
            existing.WavKey = wavKey;
        }
        else
        {
            var note = new BmsNote
            {
                Measure = args.Measure,
                LaneId = args.LaneId,
                Position = args.Position,
                WavKey = wavKey,
                Type = NoteType.Normal
            };
            Chart.Notes.Add(note);
        }

        PlayWavSound(wavKey);
        NotifyNotesChanged("노트 배치");
    }

    // 우클릭한 자리에서 가장 가까운 노트 하나를 지운다.
    //
    // 예전에는 허용오차가 0.005 로 고정이었다. 배치 허용오차(0.0001)의 50배라
    // 192분할처럼 촘촘하게 찍어두면 엉뚱한 옆 노트가 지워졌다. 게다가 Find 는
    // "가장 가까운"이 아니라 "처음 찾은" 것을 골라서 어느 게 지워질지 예측할 수 없었다.
    [RelayCommand]
    private void RemoveNote(NotePlacementArgs args)
    {
        if (IsPlaying)
            StopPlaybackAtCurrentPosition();

        // 격자 한 칸의 절반. 격자로 표현 못 하는 자리(잇단음)의 노트도 집을 수 있으면서,
        // 옆 칸까지 넘어가지는 않는 폭이다.
        var tolerance = Math.Max(PositionEpsilon, 0.5 / Math.Max(1, BeatSplit));

        BmsNote? nearest = null;
        var nearestDistance = double.MaxValue;

        foreach (var note in Chart.Notes)
        {
            if (note.Measure != args.Measure || note.LaneId != args.LaneId)
                continue;

            var distance = Math.Abs(note.Position - args.Position);
            if (distance > tolerance || distance >= nearestDistance)
                continue;

            nearest = note;
            nearestDistance = distance;
        }

        if (nearest is null)
            return;

        Chart.Notes.Remove(nearest);
        _selectedNotes.Remove(nearest);
        NotifyNotesChanged("노트 삭제");
        NotifySelectionChanged();
    }
}

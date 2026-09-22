using System;
using System.Collections.Generic;
using bms_editer.Models;

namespace bms_editer.Services;

// 되돌리기 한 칸. 편집 직후 문서의 상태를 통째로 담는다.
//
// 왜 명령별 역연산이 아니라 통째 스냅샷인가:
// 편집 경로마다 "되돌리는 방법"을 따로 적으면, 새 편집 기능이 생길 때마다 그걸 같이 적어야 한다.
// 이 저장소가 반복해서 데인 실패 모양이 그것이다(같은 일을 두 곳에 적었다가 한쪽만 고침).
// 노트 편집은 전부 MainWindowViewModel.NotifyNotesChanged 하나를 지나가므로,
// 거기서 통째로 떠 두면 **빠뜨릴 경로가 없다.**
//
// 값은 전부 복제본이다. BmsNote 는 가변 클래스여서(MoveSelectedNotes 가 Measure 를,
// ReplaceWavKey 가 WavKey 를 제자리에서 고친다) 참조만 담으면 되돌릴 것이 같이 바뀐다.
public sealed record EditSnapshot(
    IReadOnlyList<BmsNote> Notes,
    IReadOnlyList<BmsWavItem> Wavs,
    IReadOnlyDictionary<string, string> WavTable,
    IReadOnlyList<int> SelectedNoteIndices,
    string? SelectedWavKey,
    string Label,
    BmsChart? Document = null);

// 되돌리기 / 다시 하기 칸.
//
// Baseline 은 "지금 화면에 있는 상태"다. 편집이 끝날 때마다 이전 Baseline 을 되돌리기 칸에 밀어 넣고
// 새 상태를 Baseline 으로 삼는다. 되돌리기는 그 반대다.
public sealed class EditHistory
{
    // 칸 수 상한. 한 칸이 노트 수에 비례하므로(650노트 차트에서 약 40KB) 무한히 쌓으면 메모리를 먹는다.
    // 100칸이면 실측 규모에서 10MB 안쪽이고, 그보다 더 거슬러 올라갈 일은 파일을 다시 여는 쪽이 낫다.
    public const int MaxDepth = 100;

    private readonly List<EditSnapshot> _undo = new();
    private readonly List<EditSnapshot> _redo = new();

    public EditSnapshot? Baseline { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    // 되돌리면 취소되는 작업의 이름. 지금 상태를 만든 작업이다.
    public string? UndoLabel => CanUndo ? Baseline?.Label : null;

    // 다시 하면 되살아나는 작업의 이름.
    public string? RedoLabel => CanRedo ? _redo[^1].Label : null;

    // 지금 상태의 "무엇이 선택돼 있었나"만 갱신한다.
    //
    // 선택은 문서를 고치는 일이 아니라 되돌리기 칸을 쌓지 않는다. 그런데 되돌렸을 때
    // 선택까지 돌아와야 어디가 되돌아간 건지 보인다. 그래서 칸은 쌓지 않고 지금 칸의
    // 선택만 따라가게 둔다. (이게 없으면 "지우기 직전에 고른 것"이 아니라
    // 그보다 한참 전의 선택이 되살아난다)
    public void UpdateBaselineSelection(IReadOnlyList<int> selectedNoteIndices, string? selectedWavKey)
    {
        if (Baseline is { } baseline)
        {
            Baseline = baseline with
            {
                SelectedNoteIndices = selectedNoteIndices,
                SelectedWavKey = selectedWavKey,
            };
        }
    }

    // 문서를 새로 열거나 비울 때. 이전 문서의 편집으로 거슬러 올라가면 안 된다.
    public void Reset(EditSnapshot current)
    {
        _undo.Clear();
        _redo.Clear();
        Baseline = current;
    }

    // 편집이 끝났다. next 는 편집 **후**의 상태다.
    public void Record(EditSnapshot next)
    {
        if (Baseline is { } previous)
        {
            _undo.Add(previous);
            if (_undo.Count > MaxDepth)
                _undo.RemoveAt(0);
        }

        // 새로 편집했으면 다시 할 미래는 사라진다.
        _redo.Clear();
        Baseline = next;
    }

    public void UpdateBaselineDocument(BmsChart document)
    {
        if (Baseline is { } baseline)
            Baseline = baseline with { Document = document };
    }

    public EditSnapshot? Undo()
    {
        if (_undo.Count == 0)
            return null;

        var target = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        if (Baseline is { } current)
            _redo.Add(current);

        Baseline = target;
        return target;
    }

    public EditSnapshot? Redo()
    {
        if (_redo.Count == 0)
            return null;

        var target = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        if (Baseline is { } current)
        {
            _undo.Add(current);
            if (_undo.Count > MaxDepth)
                _undo.RemoveAt(0);
        }

        Baseline = target;
        return target;
    }
}

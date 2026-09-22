using System.Collections.Generic;

namespace bms_editer.Models;

public enum NoteType
{
    Normal,
    LongStart,
    LongEnd,
    Invisible,
    Mine,
}

public sealed class BmsNote
{
    public int Measure { get; set; }
    public string LaneId { get; set; } = string.Empty;

    // 마디 내 상대 위치 (0.0 ~ 1.0)
    public double Position { get; set; }

    // WAV 테이블을 가리키는 base-36 키 (예: "01", "A3")
    public string WavKey { get; set; } = string.Empty;

    public NoteType Type { get; set; } = NoteType.Normal;
    public int BranchId { get; set; } = 0;
    public int SourceLineOrder { get; set; } = 0;
}

public record NotePlacementArgs(string LaneId, int Measure, double Position);

// 마디 단위 복제 결과. 건너뛴 이유를 나눠서 알려줘야 사용자가 왜 덜 복사됐는지 안다.
public readonly record struct NoteCopyResult(int Copied, int Blocked, int OutOfRange);

// Additive: 기존 선택에 더할지, 통째로 갈아끼울지.
// Ctrl/Shift 를 누른 채 드래그하면 떨어져 있는 구간을 함께 고를 수 있다.
public record NoteSelectionArgs(IReadOnlyList<BmsNote> Notes, bool Additive = false);

// 지금 선택을 누가 만들었는지.
//
// Grid 는 격자에서 손으로 끌어 고른 것이고, Search 는 검색 창처럼 격자 밖에서
// 고른 것이다. 격자 밖에서 고른 선택은 화면 밖에 있기 쉬워서, 그때만 격자를
// 그 자리로 스크롤한다. (MainWindowViewModel.SetNoteSelection 참고)
public enum NoteSelectionSource
{
    Grid,
    Search,
}

public enum NoteMoveDirection
{
    TimeForward,
    TimeBackward,
    LanePrevious,
    LaneNext,
}

using System;
using System.Collections.Generic;

namespace bms_editer.Models;

// 짝지어진 홀드 한 개. 노트 두 개를 가리키기만 한다.
//
// 게임 파서는 짝이 맞은 끝 노트를 자기 목록에서 지운다(안 지우면 끝 지점에 유령 단타가 스폰된다).
// 에디터가 그걸 따라 하면 안 된다. BmsWriter 는 Chart.Notes 의 노트 하나당 슬롯 하나를 찍으므로
// 끝 노트를 목록에서 빼는 순간 저장한 파일에서도 사라진다.
// 그래서 홀드는 노트를 고쳐 만드는 것이 아니라, 노트 위에 얹는 파생 정보로 둔다.
// (docs/specifications/hold_pairing_spec.md §3)
//
// Kind 는 프로파일 규칙이 붙인 이름이다("Hold", "Sandbag", "Gate", "Fairy" ...).
// 게임마다 짝을 이루는 오브젝트 종류가 달라서 열거형으로 못 박지 않는다.
public readonly record struct HoldLink(BmsNote Head, BmsNote Tail, string Kind);

public enum HoldDiagnosticKind
{
    // 짝이 되는 끝이 없는 시작.
    OrphanHead,

    // 짝이 되는 시작이 없는 끝.
    OrphanTail,

    // 끝이 오기 전에 같은 레인에서 시작이 또 나와, 앞의 시작이 버려졌다.
    ReplacedHead,

    // 순서로 짝짓는 게임에서, 파일명의 "시작/끝" 표식과 순서가 처음 어긋난 자리.
    // 그 뒤의 짝은 전부 뒤집혀 있으므로 첫 자리 하나만 알린다.
    ParityBreak,

    // 끝 노트 하나를 시작 여러 개가 같이 쓴다.
    SharedTail,

    // 시작과 끝이 같은 자리다.
    ZeroLength,
}

public enum HoldDiagnosticSeverity
{
    Error,
    Warning,
}

public sealed record HoldDiagnostic(
    HoldDiagnosticKind Kind,
    HoldDiagnosticSeverity Severity,
    BmsNote Note,
    string HoldKind,
    string Message)
{
    // 목록에 보이는 한 줄. 어디인지가 먼저 보여야 격자에서 찾아갈 수 있다.
    public string DisplayText =>
        $"{(Severity == HoldDiagnosticSeverity.Error ? "🔴" : "🟡")} {Note.Measure:000}마디 · {Note.LaneId} · {Message}";
}

public sealed record HoldPairingResult(IReadOnlyList<HoldLink> Links, IReadOnlyList<HoldDiagnostic> Diagnostics)
{
    public static HoldPairingResult Empty { get; } =
        new(Array.Empty<HoldLink>(), Array.Empty<HoldDiagnostic>());
}

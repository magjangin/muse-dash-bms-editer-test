using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;

namespace bms_editer.Services.Holds;

// 짝을 고르는 방식들. 전부 실제 게임 모드 코드를 옮긴 것이다.
//
// 비슷해 보여도 합치면 안 된다. 시작 두 개 뒤에 끝 하나가 오는 같은 배치를 두고
//   * NearestUnconsumedTail 은 앞 시작이 끝을 가져가고 뒤 시작이 고아가 되고,
//   * NearestTailShared 는 두 시작이 끝 하나를 같이 쓰고,
//   * ReplacePending 은 뒤 시작이 끝을 가져가고 앞 시작이 버려진다.
// 에디터가 게임과 다르게 짝지으면 화면의 몸통이 게임과 다른 곳을 가리킨다.
internal static class HoldPolicies
{
    // 같은 자리로 볼 허용오차. 위치는 index/count 로 계산돼 같은 칸이면 값도 같지만,
    // 서로 다른 분할의 줄에서 온 노트끼리는 부동소수 끝자리가 흔들릴 수 있다.
    private const double TimeEpsilon = 1e-9;

    public static void Apply(
        HoldRule rule,
        IReadOnlyList<HoldMember> ordered,
        List<HoldLink> links,
        List<HoldDiagnostic> diagnostics)
    {
        switch (rule.Policy)
        {
            case HoldPairingPolicy.NearestUnconsumedTail:
                NearestUnconsumedTail(rule, ordered, links, diagnostics);
                break;
            case HoldPairingPolicy.NearestTailShared:
                NearestTailShared(rule, ordered, links, diagnostics);
                break;
            case HoldPairingPolicy.FifoQueue:
                FifoQueue(rule, ordered, links, diagnostics);
                break;
            case HoldPairingPolicy.ReplacePending:
                ReplacePending(rule, ordered, links, diagnostics);
                break;
            case HoldPairingPolicy.Alternate:
                Alternate(rule, ordered, links, diagnostics);
                break;
        }
    }

    private static void NearestUnconsumedTail(
        HoldRule rule, IReadOnlyList<HoldMember> ordered, List<HoldLink> links, List<HoldDiagnostic> diagnostics)
    {
        var tails = ordered.Where(m => m.Role == HoldRole.Tail).ToList();
        var consumed = new HashSet<BmsNote>();

        foreach (var head in ordered.Where(m => m.Role == HoldRole.Head))
        {
            HoldMember? found = null;
            foreach (var candidate in tails)
            {
                // 같은 자리의 끝은 짝이 아니다. 게임 코드가 "시작보다 뒤"만 본다.
                if (consumed.Contains(candidate.Note) || candidate.Time <= head.Time + TimeEpsilon)
                    continue;

                found = candidate;
                break;
            }

            if (found is not { } tail)
            {
                diagnostics.Add(OrphanHead(rule, head.Note));
                continue;
            }

            consumed.Add(tail.Note);
            links.Add(new HoldLink(head.Note, tail.Note, rule.Kind));
        }

        foreach (var tail in tails)
        {
            if (!consumed.Contains(tail.Note))
                diagnostics.Add(OrphanTail(rule, tail.Note));
        }
    }

    private static void NearestTailShared(
        HoldRule rule, IReadOnlyList<HoldMember> ordered, List<HoldLink> links, List<HoldDiagnostic> diagnostics)
    {
        var tails = ordered.Where(m => m.Role == HoldRole.Tail).ToList();
        var usage = new Dictionary<BmsNote, int>();

        foreach (var head in ordered.Where(m => m.Role == HoldRole.Head))
        {
            HoldMember? found = null;
            foreach (var candidate in tails)
            {
                if (candidate.Time <= head.Time + TimeEpsilon)
                    continue;

                found = candidate;
                break;
            }

            if (found is not { } tail)
            {
                diagnostics.Add(OrphanHead(rule, head.Note));
                continue;
            }

            usage[tail.Note] = usage.TryGetValue(tail.Note, out var count) ? count + 1 : 1;
            links.Add(new HoldLink(head.Note, tail.Note, rule.Kind));
        }

        foreach (var tail in tails)
        {
            if (!usage.TryGetValue(tail.Note, out var count))
            {
                diagnostics.Add(OrphanTail(rule, tail.Note));
            }
            else if (count > 1)
            {
                diagnostics.Add(new HoldDiagnostic(
                    HoldDiagnosticKind.SharedTail,
                    HoldDiagnosticSeverity.Warning,
                    tail.Note,
                    rule.Kind,
                    $"끝 노트 하나를 {KindName(rule.Kind)} 시작 {count}개가 같이 씁니다. " +
                    "게임은 모두 이 끝에 붙이지만, 의도한 배치인지 확인하십시오."));
            }
        }
    }

    private static void FifoQueue(
        HoldRule rule, IReadOnlyList<HoldMember> ordered, List<HoldLink> links, List<HoldDiagnostic> diagnostics)
    {
        var open = new Queue<HoldMember>();

        foreach (var member in ordered)
        {
            if (member.Role == HoldRole.Head)
            {
                open.Enqueue(member);
                continue;
            }

            if (open.Count == 0)
            {
                diagnostics.Add(OrphanTail(rule, member.Note));
                continue;
            }

            // 게임 코드는 먼저 꺼내고 나서 자리를 확인한다. 끝이 시작보다 앞서지 않으면
            // 꺼낸 시작은 되돌려 놓지 않고 버린다. 그래서 둘 다 고아가 된다.
            var start = open.Dequeue();
            if (member.Time <= start.Time + TimeEpsilon)
            {
                diagnostics.Add(OrphanHead(rule, start.Note));
                diagnostics.Add(OrphanTail(rule, member.Note));
                continue;
            }

            links.Add(new HoldLink(start.Note, member.Note, rule.Kind));
        }

        foreach (var left in open)
            diagnostics.Add(OrphanHead(rule, left.Note));
    }

    private static void ReplacePending(
        HoldRule rule, IReadOnlyList<HoldMember> ordered, List<HoldLink> links, List<HoldDiagnostic> diagnostics)
    {
        HoldMember? pending = null;

        foreach (var member in ordered)
        {
            if (member.Role == HoldRole.Head)
            {
                if (pending is { } replaced)
                {
                    diagnostics.Add(new HoldDiagnostic(
                        HoldDiagnosticKind.ReplacedHead,
                        HoldDiagnosticSeverity.Error,
                        replaced.Note,
                        rule.Kind,
                        $"끝이 오기 전에 같은 레인에서 {KindName(rule.Kind)} 시작이 또 나왔습니다. " +
                        $"게임은 이 시작을 버리고 다음 시작으로 짝을 맞춥니다. {rule.OrphanHeadOutcome}".TrimEnd()));
                }

                pending = member;
                continue;
            }

            if (pending is not { } head)
            {
                diagnostics.Add(OrphanTail(rule, member.Note));
                continue;
            }

            AddLink(rule, head.Note, member, links, diagnostics);
            pending = null;
        }

        if (pending is { } left)
            diagnostics.Add(OrphanHead(rule, left.Note));
    }

    private static void Alternate(
        HoldRule rule, IReadOnlyList<HoldMember> ordered, List<HoldLink> links, List<HoldDiagnostic> diagnostics)
    {
        HoldMember? open = null;
        var breakReported = false;

        foreach (var member in ordered)
        {
            if (open is not { } head)
            {
                if (!breakReported && member.Label == HoldRole.Tail)
                {
                    diagnostics.Add(ParityBreak(rule, member.Note, labelSaysTail: true));
                    breakReported = true;
                }

                open = member;
                continue;
            }

            if (!breakReported && member.Label == HoldRole.Head)
            {
                diagnostics.Add(ParityBreak(rule, member.Note, labelSaysTail: false));
                breakReported = true;
            }

            AddLink(rule, head.Note, member, links, diagnostics);
            open = null;
        }

        if (open is { } left)
            diagnostics.Add(OrphanHead(rule, left.Note));
    }

    private static void AddLink(
        HoldRule rule, BmsNote head, HoldMember tail, List<HoldLink> links, List<HoldDiagnostic> diagnostics)
    {
        if (tail.Time <= head.Measure + head.Position + TimeEpsilon)
        {
            diagnostics.Add(new HoldDiagnostic(
                HoldDiagnosticKind.ZeroLength,
                HoldDiagnosticSeverity.Warning,
                tail.Note,
                rule.Kind,
                $"{KindName(rule.Kind)} 시작과 끝이 같은 자리에 있습니다. 길이 0짜리가 됩니다."));
        }

        links.Add(new HoldLink(head, tail.Note, rule.Kind));
    }

    private static HoldDiagnostic OrphanHead(HoldRule rule, BmsNote note) =>
        new(HoldDiagnosticKind.OrphanHead, HoldDiagnosticSeverity.Error, note, rule.Kind,
            $"{KindName(rule.Kind)} 시작의 짝이 되는 끝 노트가 없습니다. {rule.OrphanHeadOutcome}".TrimEnd());

    private static HoldDiagnostic OrphanTail(HoldRule rule, BmsNote note) =>
        new(HoldDiagnosticKind.OrphanTail, HoldDiagnosticSeverity.Error, note, rule.Kind,
            $"{KindName(rule.Kind)} 끝 노트의 짝이 되는 시작이 없습니다. {rule.OrphanTailOutcome}".TrimEnd());

    // 게임은 파일명 표식이 아니라 순서로 짝짓는다. 표식과 순서가 처음 어긋난 자리를 알린다.
    // 그 뒤로는 짝이 전부 뒤집혀 있으니 뒤쪽을 잔뜩 늘어놓아 봐야 원인은 이 한 곳이다.
    private static HoldDiagnostic ParityBreak(HoldRule rule, BmsNote note, bool labelSaysTail) =>
        new(HoldDiagnosticKind.ParityBreak, HoldDiagnosticSeverity.Error, note, rule.Kind,
            $"파일명은 '{(labelSaysTail ? "끝" : "시작")}'인데 순서상 {(labelSaysTail ? "시작" : "끝")}으로 짝지어집니다. " +
            "게임은 파일명이 아니라 순서로 짝을 맞추므로, 이 앞에서 하나가 어긋나 여기부터 뒤의 짝이 전부 뒤집혔을 가능성이 큽니다.");

    internal static string KindName(string kind) => kind.ToUpperInvariant() switch
    {
        "HOLD" => "홀드",
        "SANDBAG" => "샌드백",
        "GATE" => "게이트",
        "FAIRY" => "페어리",
        "DOUBLE" => "더블",
        "SPAM" => "연타",
        _ => kind,
    };
}

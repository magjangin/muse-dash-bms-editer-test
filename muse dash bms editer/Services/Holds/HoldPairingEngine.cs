using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;

namespace bms_editer.Services.Holds;

// 게임 프로파일의 규칙대로 노트에서 홀드 짝을 읽어낸다.
//
// Chart.Notes 를 바꾸지 않는다. 결과는 언제든 버리고 다시 만들 수 있는 파생 정보다.
// 편집할 때마다 통째로 다시 돈다. 부분 갱신은 캐시가 어긋나는 버그를 부르고,
// 실측 규모(수백 노트)에서는 전체 재계산이 눈에 띄지 않는다.
// (docs/specifications/hold_pairing_spec.md §3 불변식 I-3, §7)
public static class HoldPairingEngine
{
    public static HoldPairingResult Pair(
        IReadOnlyList<BmsNote> notes,
        IReadOnlyDictionary<string, string> wavTexts,
        GameProfile? profile)
    {
        if (profile is null || profile.IsNone || profile.HoldRules.Count == 0 || notes.Count == 0)
            return HoldPairingResult.Empty;

        var links = new List<HoldLink>();
        var diagnostics = new List<HoldDiagnostic>();

        foreach (var rule in profile.HoldRules)
        {
            var channels = rule.Channels.Count > 0
                ? new HashSet<string>(rule.Channels, StringComparer.OrdinalIgnoreCase)
                : null;

            var members = new List<HoldMember>();
            for (var index = 0; index < notes.Count; index++)
            {
                var note = notes[index];
                if (channels is not null && !channels.Contains(note.LaneId))
                    continue;

                var (role, label) = HoldRoleClassifier.Classify(profile, rule, note, wavTexts);
                if (role == HoldRole.None)
                    continue;

                members.Add(new HoldMember(note, role, label, index));
            }

            // 조건 블록(#RANDOM)의 갈래끼리는 절대 섞지 않는다. 한 번에 한 갈래만 연주되기 때문이다.
            var groups = members.GroupBy(m => (
                m.Note.BranchId,
                Lane: rule.Scope == HoldScope.Channel ? m.Note.LaneId.ToUpperInvariant() : ""));

            foreach (var group in groups)
            {
                var ordered = group
                    .OrderBy(m => m.Time)
                    .ThenBy(m => TieRank(rule, m))
                    .ThenBy(m => m.Index)
                    .ToList();

                HoldPolicies.Apply(rule, ordered, links, diagnostics);
            }
        }

        return new HoldPairingResult(links, diagnostics);
    }

    // 같은 자리에 시작과 끝이 겹칠 때의 순서. 줄을 세우는 방식만 시작을 앞에 둔다
    // (게임 코드가 그렇게 정렬한다). 나머지는 파일에 적힌 순서를 따른다.
    private static int TieRank(HoldRule rule, HoldMember member) =>
        rule.Policy == HoldPairingPolicy.FifoQueue && member.Role == HoldRole.Head ? 0 : 1;
}

internal enum HoldRole
{
    None,
    Head,
    Tail,

    // 순서로 짝짓는 규칙에서 "짝 수열에 들어가는 노트". 시작인지 끝인지는 순서가 정한다.
    Member,
}

internal readonly record struct HoldMember(BmsNote Note, HoldRole Role, HoldRole Label, int Index)
{
    public double Time => Note.Measure + Note.Position;
}

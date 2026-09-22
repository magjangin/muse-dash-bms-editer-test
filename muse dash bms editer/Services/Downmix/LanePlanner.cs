using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;

namespace bms_editer.Services.Downmix;

// 건반 채널 하나하나를 뮤즈 대시의 어느 레인으로 보낼지 정한다.
//
// 세 단계를 거친다. 뒤의 것이 앞의 것을 덮는다.
//   1. 채널 번호로 짐작한 좌우 (그 게임을 모를 때의 차선책)
//   2. 그 게임의 실제 화면 배치 (프로파일의 `downmix`)
//   3. 그렇게 갈랐는데 한쪽으로 쏠리면, 무거운 레인부터 **번갈아**로 돌린다
//
// 3번이 필요한 이유: 배치를 옳게 따라도 **원본 채보가 한쪽 손에 몰려 있으면** 결과가 기운다.
// 실측 20곡에서 스크래치·게이트 레인 하나가 곡 노트의 40%를 넘기도 했다. 뮤즈 대시는 손이
// 둘뿐이라 한쪽만 쉴 새 없이 두드리는 채보가 되고, 그건 원본에 없던 난이도다.
public static class LanePlanner
{
    // 지상 비율이 이 범위를 벗어나면 쏠린 것으로 본다. 45~55%.
    private const double BalancedMargin = 0.05;

    public static LanePlan Create(KeyboardChart source)
    {
        var layout = source.SourceProfile?.Downmix is { IsEmpty: false } known ? known : null;
        var assignment = GuessByChannelNumber(source.Channels);

        if (layout is not null)
            ApplyGameLayout(assignment, layout);

        var rebalanced = Rebalance(assignment, CountByChannel(source));

        return new LanePlan(
            assignment,
            layout is null ? null : source.SourceProfile!.DisplayName,
            rebalanced);
    }

    // 왼쪽 절반은 지상, 오른쪽 절반은 공중. 가운데 하나가 남으면 번갈아 보낸다.
    //
    // 왼손/오른손을 위아래로 옮기는 것이라, 건반에서 좌우로 오가던 트릴이
    // 뮤즈 대시에서 지상·공중 번갈아 치기가 된다. 같은 레인 연타로 뭉개지지 않는다.
    //
    // 단 이것은 **채널 번호로 짐작한 자리**다. 그 게임의 실제 화면 배치를 아는 프로파일이 있으면
    // 그쪽이 옳다 — 건볼트의 `14` 는 번호로는 가운데지만 게임에서는 오른손 안쪽 레인이다.
    private static Dictionary<string, LaneAssignment> GuessByChannelNumber(IReadOnlyList<string> channels)
    {
        var map = new Dictionary<string, LaneAssignment>(StringComparer.OrdinalIgnoreCase);

        // 비어 있는 레인이 있어도 실제 건반 번호의 좌우 위치를 유지한다.
        var layout = channels.Contains("18") || channels.Contains("19")
            ? new[] { "11", "12", "13", "14", "15", "18", "19" }
            : channels.Contains("15")
                ? new[] { "11", "12", "13", "14", "15" }
                : new[] { "11", "12", "13", "14" };

        for (var i = 0; i < layout.Length; i++)
        {
            var isMiddle = layout.Length % 2 == 1 && i == layout.Length / 2;
            map[layout[i]] = isMiddle
                ? LaneAssignment.Alternating
                : i < layout.Length / 2 ? LaneAssignment.Ground : LaneAssignment.Air;
        }

        map["16"] = LaneAssignment.Ground;
        return map;
    }

    private static void ApplyGameLayout(Dictionary<string, LaneAssignment> map, DownmixLayout layout)
    {
        foreach (var channel in layout.Ground)
            map[channel] = LaneAssignment.Ground;
        foreach (var channel in layout.Air)
            map[channel] = LaneAssignment.Air;
        foreach (var channel in layout.Alternating)
            map[channel] = LaneAssignment.Alternating;
    }

    // 쏠린 쪽에서 가장 무거운 레인을 하나씩 **번갈아**로 돌린다.
    //
    // 왜 레인을 통째로 반대편으로 옮기지 않고 번갈아로 돌리는가:
    // 통째로 옮기면 그 레인의 연타가 **반대쪽에서 똑같이 뭉친다.** 번갈아로 돌리면 한 레인의 연타가
    // 지상·공중을 오가는 패턴이 되는데, 그게 뮤즈 대시에서 실제로 치는 모양이다.
    //
    // 고른 것이 왜 효과가 큰지: 번갈아는 그 레인의 노트를 **절반씩 양쪽에 나눈다.** 그래서 무거운
    // 레인일수록 옮겨지는 양이 크다. 한 번 돌릴 때마다 다시 재고, 더 나아지지 않으면 멈춘다.
    private static IReadOnlyList<string> Rebalance(
        Dictionary<string, LaneAssignment> map, IReadOnlyDictionary<string, int> counts)
    {
        var moved = new List<string>();

        while (true)
        {
            var (ground, air) = Expected(map, counts);
            var total = ground + air;
            if (total == 0)
                break;

            var gap = Math.Abs((ground / total) - 0.5);
            if (gap <= BalancedMargin)
                break;

            var heavy = ground > air ? LaneAssignment.Ground : LaneAssignment.Air;

            string? best = null;
            var bestGap = gap;

            // 무거운 것부터 본다. 같은 값이면 채널 번호 순서 — 같은 차트를 두 번 변환하면
            // 같은 결과가 나와야 한다.
            var candidates = map
                .Where(pair => pair.Value == heavy)
                .OrderByDescending(pair => counts.GetValueOrDefault(pair.Key))
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();

            foreach (var (channel, assignment) in candidates)
            {
                if (counts.GetValueOrDefault(channel) == 0)
                    continue;

                map[channel] = LaneAssignment.Alternating;
                var (g, a) = Expected(map, counts);
                map[channel] = assignment;

                var candidateGap = Math.Abs((g / (g + a)) - 0.5);
                if (candidateGap >= bestGap)
                    continue;

                best = channel;
                bestGap = candidateGap;
            }

            // 더 고를 것이 없다. 한 레인에만 노트가 있는 차트라면 여기서 멈춘다.
            if (best is null)
                break;

            map[best] = LaneAssignment.Alternating;
            moved.Add(best);
        }

        return moved;
    }

    // 번갈아는 절반씩 양쪽으로 간다. 실제 배치 전이라 **예상치**다.
    private static (double Ground, double Air) Expected(
        IReadOnlyDictionary<string, LaneAssignment> map, IReadOnlyDictionary<string, int> counts)
    {
        double ground = 0;
        double air = 0;

        foreach (var (channel, count) in counts)
        {
            switch (map.GetValueOrDefault(channel, LaneAssignment.Ground))
            {
                case LaneAssignment.Ground:
                    ground += count;
                    break;
                case LaneAssignment.Air:
                    air += count;
                    break;
                default:
                    ground += count / 2.0;
                    air += count / 2.0;
                    break;
            }
        }

        return (ground, air);
    }

    private static Dictionary<string, int> CountByChannel(KeyboardChart source)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in source.Notes)
            counts[note.Channel] = counts.GetValueOrDefault(note.Channel) + 1;

        return counts;
    }
}

public enum LaneAssignment
{
    Ground,
    Air,
    Alternating,
}

// 채널을 어디로 보낼지와, 그렇게 정한 이유.
public sealed record LanePlan(
    IReadOnlyDictionary<string, LaneAssignment> Assignment,

    // 배치를 가져온 게임 이름. 채널 번호로 짐작했으면 null.
    string? Source,

    // 쏠림을 줄이려고 번갈아로 돌린 채널. 바꾼 것이 없으면 비어 있다.
    IReadOnlyList<string> Rebalanced)
{
    // 쏠림 교정으로 바뀐 채널은 표시를 붙인다. 안 붙이면 "게임 배치"라고 적힌 줄에
    // 게임에 없던 번갈아가 섞여 보인다.
    public IReadOnlyList<string> Describe(IReadOnlyList<string> channels) =>
        channels.Select(c => Assignment.GetValueOrDefault(c, LaneAssignment.Ground) switch
        {
            LaneAssignment.Ground => $"{c} → 지상",
            LaneAssignment.Air => $"{c} → 공중",
            _ when Rebalanced.Contains(c, StringComparer.OrdinalIgnoreCase) => $"{c} → 번갈아(쏠림 교정)",
            _ => $"{c} → 번갈아",
        }).ToArray();
}

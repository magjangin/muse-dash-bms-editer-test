using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;

namespace bms_editer.Services.Downmix;

public sealed record DownmixReport(
    int SourceNotes,
    int CreatedNotes,
    int CreatedHolds,
    int MovedToOtherLane,
    int DroppedTooDense,
    int DroppedOverlappingHold,
    int DroppedZeroLengthHold,

    // 어느 레인에 몇 개가 갔는지. 한쪽으로 쏠렸는지를 이 둘로 본다.
    int GroundNotes,
    int AirNotes,
    string Scene,
    IReadOnlyList<string> LaneMapping,
    IReadOnlyList<string> Warnings,

    // 레인 배분을 어느 게임의 화면 배치에서 가져왔는지. 번호 순서로 갈랐으면 null.
    string? LaneSource = null,

    // 쏠림을 줄이려고 번갈아로 돌린 채널.
    IReadOnlyList<string>? RebalancedChannels = null,

    // 가져오기가 원본 폴더에서 같이 연 음원·영상 파일 이름. 엔진은 채우지 않는다(뷰모델 몫).
    string? AudioFile = null,
    string? VideoFile = null,

    // 음원을 찾았는데 열지 못했을 때의 사유. 노트는 이미 들어왔으므로 가져오기 실패는 아니다.
    string? MediaWarning = null);

public sealed record DownmixResult(IReadOnlyList<BmsNote> Notes, int MeasureCount, DownmixReport Report);

// 건반형 채보(4K/5K/7K)를 뮤즈 대시의 두 레인으로 접는다.
//
// 뮤즈 대시는 손이 둘(지상·공중)뿐이다. 건반 게임의 3~5 동시치기를 그대로 옮기면
// 칠 수 없는 패턴이 되므로, 같은 자리에 둘까지만 남긴다.
//
// 결과는 **초안**이다. 건반 BMS 에는 하트·장애물·보스 같은 뮤즈 대시 노트 종류 정보가
// 아예 없어서, 나오는 것은 전부 일반 노트와 홀드뿐이다. 종류는 사람이 얹어야 한다.
public static class DownmixEngine
{
    // 자동 변환 출력: 지상 13, 공중 14.
    public const string AirLane = "14";
    public const string GroundLane = "13";

    // 같은 자리로 볼 허용오차. 위치는 slot/count 로 나오므로 분할이 다른 줄끼리
    // 부동소수 끝자리가 흔들릴 수 있다.
    private const double TimeEpsilon = 1e-9;

    public static DownmixResult Convert(KeyboardChart source, DownmixKeys keys)
    {
        var plan = LanePlanner.Create(source);
        var items = BuildItems(source, plan.Assignment, out var droppedZeroLength);

        var notes = new List<BmsNote>();
        var holdIntervals = new Dictionary<string, List<(double Start, double End)>>
        {
            [GroundLane] = new(),
            [AirLane] = new(),
        };
        var occupiedSlots = new Dictionary<string, HashSet<long>>
        {
            [GroundLane] = new(),
            [AirLane] = new(),
        };

        var moved = 0;
        var droppedDense = 0;
        var droppedOverlap = 0;
        var holds = 0;

        foreach (var item in items.OrderBy(i => i.Start).ThenBy(i => i.Order))
        {
            var preferred = item.Lane;
            var alternate = preferred == GroundLane ? AirLane : GroundLane;

            var placed = false;
            foreach (var lane in new[] { preferred, alternate })
            {
                if (!CanPlace(item, lane, holdIntervals, occupiedSlots))
                    continue;

                Place(item, lane, keys, notes, holdIntervals, occupiedSlots);

                if (lane != preferred)
                    moved++;
                if (item.IsHold)
                    holds++;

                placed = true;
                break;
            }

            if (placed)
                continue;

            // 두 레인 모두 막혔다. 건반 게임의 3중 이상 동시치기이거나, 이미 홀드가 지나는 구간이다.
            if (item.IsHold)
                droppedOverlap++;
            else
                droppedDense++;
        }

        var measureCount = Math.Max(source.MeasureCount, notes.Count == 0 ? 1 : notes.Max(n => n.Measure) + 1);

        var ground = notes.Count(n => n.LaneId == GroundLane);

        var report = new DownmixReport(
            source.Notes.Count,
            notes.Count,
            holds,
            moved,
            droppedDense,
            droppedOverlap,
            droppedZeroLength,
            ground,
            notes.Count - ground,
            keys.Scene,
            plan.Describe(source.Channels),
            source.Warnings,
            plan.Source,
            plan.Rebalanced);

        return new DownmixResult(notes, measureCount, report);
    }

    private static List<DownmixItem> BuildItems(
        KeyboardChart source,
        IReadOnlyDictionary<string, LaneAssignment> laneOf,
        out int droppedZeroLength)
    {
        droppedZeroLength = 0;

        var items = new List<DownmixItem>();
        var alternateFlip = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var openHold = new Dictionary<string, KeyboardNote>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        string LaneFor(string channel)
        {
            var assignment = laneOf[channel];
            if (assignment == LaneAssignment.Ground)
                return GroundLane;
            if (assignment == LaneAssignment.Air)
                return AirLane;

            var flip = alternateFlip.GetValueOrDefault(channel);
            alternateFlip[channel] = !flip;
            return flip ? AirLane : GroundLane;
        }

        foreach (var note in source.Notes.OrderBy(n => n.Time).ThenBy(n => n.Channel, StringComparer.Ordinal))
        {
            switch (note.Kind)
            {
                case KeyboardNoteKind.LongStart:
                    openHold[note.Channel] = note;
                    break;

                case KeyboardNoteKind.LongEnd when openHold.Remove(note.Channel, out var start):
                    if (note.Time <= start.Time + TimeEpsilon)
                    {
                        droppedZeroLength++;
                        break;
                    }

                    items.Add(new DownmixItem(LaneFor(start.Channel), start, note, order++));
                    break;

                case KeyboardNoteKind.LongEnd:
                    // 시작 없는 끝. 읽는 쪽이 이미 경고를 남겼다.
                    break;

                default:
                    items.Add(new DownmixItem(LaneFor(note.Channel), note, null, order++));
                    break;
            }
        }

        return items;
    }

    private static bool CanPlace(
        DownmixItem item,
        string lane,
        IReadOnlyDictionary<string, List<(double Start, double End)>> holdIntervals,
        IReadOnlyDictionary<string, HashSet<long>> occupiedSlots)
    {
        // 그 레인을 홀드가 지나는 중이면 아무것도 올릴 수 없다. 한 손으로 둘을 못 친다.
        foreach (var (start, end) in holdIntervals[lane])
        {
            if (item.Start < end - TimeEpsilon && item.End > start + TimeEpsilon)
                return false;
        }

        if (occupiedSlots[lane].Contains(SlotKey(item.Start)))
            return false;

        return !item.IsHold || !occupiedSlots[lane].Contains(SlotKey(item.End));
    }

    private static void Place(
        DownmixItem item,
        string lane,
        DownmixKeys keys,
        List<BmsNote> notes,
        IReadOnlyDictionary<string, List<(double Start, double End)>> holdIntervals,
        IReadOnlyDictionary<string, HashSet<long>> occupiedSlots)
    {
        var ground = lane == GroundLane;

        if (!item.IsHold)
        {
            notes.Add(Note(lane, item.Head, ground ? keys.GroundNormal : keys.AirNormal));
            occupiedSlots[lane].Add(SlotKey(item.Start));
            return;
        }

        // 뮤즈 대시는 같은 채널에서 나온 **순서**로 시작·끝을 가른다.
        // 시작과 끝을 붙여서 내려주고, 사이에 다른 홀드가 끼지 않게 위에서 겹침을 막았다.
        notes.Add(Note(lane, item.Head, ground ? keys.GroundHoldStart : keys.AirHoldStart));
        notes.Add(Note(lane, item.Tail!, ground ? keys.GroundHoldEnd : keys.AirHoldEnd));

        occupiedSlots[lane].Add(SlotKey(item.Start));
        occupiedSlots[lane].Add(SlotKey(item.End));
        holdIntervals[lane].Add((item.Start, item.End));
    }

    private static BmsNote Note(string lane, KeyboardNote from, string wavKey) => new()
    {
        LaneId = lane,
        Measure = from.Measure,
        Position = from.Position,
        WavKey = wavKey,

        // 뮤즈 대시 노트는 전부 Normal 로 둔다. 홀드인지는 키음 UID 가 정하고,
        // 짝은 그 위에 얹는 파생 정보다(BmsParser 가 읽는 방식과 같게 맞춘다).
        Type = NoteType.Normal,
    };

    // 마디 안 위치를 0.0001 단위로 끊는다. (MainWindowViewModel.ToSlotKey 와 같은 규칙)
    private static long SlotKey(double time) => (long)Math.Round(time * 10000);

    private sealed record DownmixItem(string Lane, KeyboardNote Head, KeyboardNote? Tail, int Order)
    {
        public bool IsHold => Tail is not null;

        public double Start => Head.Time;

        public double End => Tail?.Time ?? Head.Time;
    }
}

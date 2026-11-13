using System;
using System.Collections.Generic;

namespace bms_editer.Services;

public readonly record struct AudioAttack(double Seconds, float Strength);

public enum GridFitAdvice
{
    // 음원이 없거나 어택이 너무 적어 판단하지 않는다.
    None,

    // 어택이 격자에서 한쪽으로 오래 밀린 구간이 없다. 파형을 보고 맞춰도 된다.
    Waveform,

    // 어택이 여러 구간 연속으로 격자 한쪽에 크게 밀려 있다. 파형을 믿지 말고 배속을 낮춰 소리로 맞춰야 한다.
    Listen,
}

// "이 곡은 파형을 보고 맞춰도 되나, 소리로 들어야 하나"를 자동으로 고른다.
//
// 왜 필요한가: With(164 BPM)는 0.3배속으로 들으면 격자에 맞는데, 파형만 보면 어긋나 보였다.
// 이런 곡에서 파형에 맞추려고 오프셋이나 노트를 옮기면 오히려 틀어진다.
// 사용자는 "얼마나 벗어났는지"가 아니라 "어느 쪽으로 맞추라"는 안내만 원했다. 숫자는 보여 주지 않는다.
//
// 무엇을 보나: 8마디 구간마다 어택들이 16분 격자선 기준으로 **한쪽으로 몰려 있는지**(원형 평균).
// 몰림이 뚜렷하고(R ≥ 0.5) 25ms 이상 같은 쪽으로 밀린 구간이 3개 이상 이어지면 "소리".
//
// ❌ 먼저 해 본 것: "어택 중 격자 ±15ms 안에 드는 비율"(0.70 미만이면 소리). 피아노 루바토나 스윙·셋잇단처럼
//    원래 16분 격자 밖에 소리가 많은 곡(your reality 0.23, 아기상어 0.20)까지 "소리"로 걸렀다. 사용자가 확인한
//    정답은 With 와 You're Under Arrest 두 곡뿐이었다. 그런 곡은 구간마다 몰림 방향이 오락가락할 뿐
//    한쪽으로 오래 밀리지 않는다. 그래서 "흩어짐"이 아니라 "같은 쪽으로 오래 밀림"을 본다.
//
// 보정(2026-11-09, H:\muse dash hwa\hwa 11곡, 차트 BPM·오프셋 그대로, 구간별 몰림 ms / R):
//   With                 -12 → 7구간째부터 -41/.82 -38/.55 -43/.67 -40/.87 -42/.69 -41/.56  → 소리(6연속)
//   You're Under Arrest  1, 1 → 31/.91 35/.71 31/.63 31/.90 35/.70 40/.84 44/.75          → 소리(7연속)
//   your reality         21 58 -24 0 38 -52 -16 18 67 -38 (방향이 오락가락)                   → 파형
//   아기상어             30 43 -46 -7 -8 1                                                    → 파형
//   CHOP! 최대 -19, Star Trace 꾸준히 +10~16, 꺾어 마지막 한 구간 26, 나머지 15ms 이하       → 파형
// 알려진 한계: BPM 이 조금 틀려(예: 0.3) 몰림이 구간마다 계속 돌아가는 경우는 같은 쪽으로 오래 머물지 않아
// 잡지 않는다. 그렇게 넓히면 아기상어 같은 곡까지 걸렸다.
public static class GridFitAdvisor
{
    // 어택으로 칠 봉우리: ±40ms 안에서 가장 높고, 곡 전체 봉우리 높이 중 상위 쪽.
    private const double PeakHalfSeconds = 0.040;
    private const double StrongPeakQuantile = 0.60;

    private const int SectionMeasures = 8;
    private const int MinAttacksPerSection = 12;

    public const double MinSectionConsistency = 0.5;
    public const double MinShiftSeconds = 0.025;
    public const int MinShiftedSectionRun = 3;

    private const int MinAttacks = 16;

    public readonly record struct SectionShift(int Section, double ShiftSeconds, double Consistency, int AttackCount);

    public readonly record struct Report(int LongestShiftedRun, int AttackCount, IReadOnlyList<SectionShift> Sections);

    public static GridFitAdvice Advise(Report report)
    {
        if (report.AttackCount < MinAttacks)
            return GridFitAdvice.None;

        return report.LongestShiftedRun >= MinShiftedSectionRun ? GridFitAdvice.Listen : GridFitAdvice.Waveform;
    }

    public static GridFitAdvice Advise(IReadOnlyList<AudioAttack> attacks, ChartTimeline timeline, double audioOffsetSeconds) =>
        Advise(Measure(attacks, timeline, audioOffsetSeconds));

    public static AudioAttack[] FindAttacks(OggAudioData data) =>
        FindAttacks(PreciseBpmDetector.BuildOnsetCurve(data));

    public static AudioAttack[] FindAttacks(float[] curve)
    {
        if (curve.Length < 3)
            return [];

        var hop = PreciseBpmDetector.HopSeconds;
        var half = Math.Max(1, (int)Math.Round(PeakHalfSeconds / hop));

        var candidates = new List<(int Index, float Value)>();
        for (var i = 1; i < curve.Length - 1; i++)
        {
            var value = curve[i];
            if (value <= 0)
                continue;

            var isPeak = true;
            for (var j = Math.Max(0, i - half); j < i && isPeak; j++)
                isPeak = curve[j] < value;
            for (var j = i + 1; j <= Math.Min(curve.Length - 1, i + half) && isPeak; j++)
                isPeak = curve[j] <= value;

            if (isPeak)
                candidates.Add((i, value));
        }

        if (candidates.Count == 0)
            return [];

        var values = new float[candidates.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = candidates[i].Value;
        Array.Sort(values);
        var floor = values[(int)Math.Floor((values.Length - 1) * StrongPeakQuantile)];

        var attacks = new List<AudioAttack>();
        foreach (var (index, value) in candidates)
        {
            // 칸 i 는 [i·hop, (i+1)·hop). 에너지가 뛴 칸의 가운데를 어택 시각으로 본다.
            if (value >= floor)
                attacks.Add(new AudioAttack((index + 0.5) * hop, value));
        }

        return attacks.ToArray();
    }

    // audioOffsetSeconds: 음원을 타임라인에서 뒤로 미는 양. 음원 t 초는 타임라인 t + offset 초에 그려진다.
    public static Report Measure(IReadOnlyList<AudioAttack> attacks, ChartTimeline timeline, double audioOffsetSeconds)
    {
        var sums = new SortedDictionary<int, (double Re, double Im, double Weight, double UnitWeighted, int Count)>();

        foreach (var attack in attacks)
        {
            var chartSeconds = attack.Seconds + audioOffsetSeconds;
            if (chartSeconds <= 0)
                continue;

            // 가장 가까운 16분 격자선과 그 칸의 길이. 마디 길이(#xxx02)·변속도 타임라인이 처리한다.
            var position = timeline.MeasurePositionAt(chartSeconds);
            var measure = (int)Math.Floor(position);
            var split = Math.Max(1, (int)Math.Round(16 * timeline.GetMeasureLength(measure)));
            var nearest = measure + (Math.Round((position - measure) * split) / split);
            var lineSeconds = timeline.SecondsAt(nearest);
            var unit = timeline.SecondsAt(nearest + (1.0 / split)) - lineSeconds;
            if (unit <= 0)
                continue;

            // 격자 칸 한 개를 한 바퀴로 보는 위상. 격자선 바로 위면 0.
            var phase = 2.0 * Math.PI * (chartSeconds - lineSeconds) / unit;
            var weight = attack.Strength;
            var section = measure / SectionMeasures;
            sums.TryGetValue(section, out var s);
            sums[section] = (
                s.Re + (weight * Math.Cos(phase)),
                s.Im + (weight * Math.Sin(phase)),
                s.Weight + weight,
                s.UnitWeighted + (weight * unit),
                s.Count + 1);
        }

        var sections = new List<SectionShift>();
        var longest = 0;
        var run = 0;
        var runSign = 0;
        var previousSection = int.MinValue;

        foreach (var (index, s) in sums)
        {
            if (s.Count < MinAttacksPerSection || s.Weight <= 0)
            {
                run = 0;
                previousSection = index;
                continue;
            }

            var consistency = Math.Sqrt((s.Re * s.Re) + (s.Im * s.Im)) / s.Weight;
            var shift = Math.Atan2(s.Im, s.Re) / (2.0 * Math.PI) * (s.UnitWeighted / s.Weight);
            sections.Add(new SectionShift(index, shift, consistency, s.Count));

            var sign = Math.Sign(shift);
            var shifted = consistency >= MinSectionConsistency && Math.Abs(shift) >= MinShiftSeconds;
            if (shifted && index == previousSection + 1 && run > 0 && sign == runSign)
                run++;
            else
                run = shifted ? 1 : 0;

            runSign = sign;
            longest = Math.Max(longest, run);
            previousSection = index;
        }

        return new Report(longest, attacks.Count, sections);
    }
}

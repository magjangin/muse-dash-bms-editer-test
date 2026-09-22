using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;

namespace bms_editer.Services;

// "마디 위치 <-> 시각(초)" 변환을 맡는 한 곳.
//
// 왜 따로 두는가:
// 예전에는 격자·노트·재생이 각자 `240.0 / Bpm` 을 써서 시각을 계산했다. 셋 다 단일 BPM
// 하나만 봤기 때문에
//   * #xxx02(박자 변경)가 섞인 차트는 그 마디부터 격자가 소리와 어긋나고,
//   * #xxx03/#xxx08(BPM 변화)이 있는 차트는 변화 지점 이후가 통째로 어긋났다.
// 파일은 보존줄로 살아남으니 저장해도 안 상하지만, "열어보면 틀리게 보이는" 상태라
// 그걸 믿고 씽크를 맞추는 순간 진짜로 상한다.
//
// 규칙을 한 곳에 두고 격자·노트·재생이 모두 여기에 물어보게 한다.
public sealed class ChartTimeline
{
    // 무한 루프 방지용 상한. 규격상 마디는 세 자리지만 여유를 둔다.
    private const int MaxMeasure = 10000;

    private readonly double _baseBpm;
    private readonly Dictionary<int, double> _measureLengths;

    // 마디별 BPM 변화. 변화가 있는 마디만 담는다(대부분의 마디에는 없다).
    private readonly Dictionary<int, List<BpmChange>> _changesByMeasure;

    // 마디 경계의 누적 시각과 그 순간의 BPM. 필요한 만큼만 늘려 가며 채운다.
    private readonly List<double> _measureStartSeconds = new() { 0.0 };
    private readonly List<double> _bpmAtMeasureStart;

    public ChartTimeline(double baseBpm, IReadOnlyDictionary<int, double> measureLengths, IEnumerable<BpmChange> bpmChanges)
    {
        _baseBpm = baseBpm > 0 ? baseBpm : 120.0;
        _measureLengths = measureLengths.ToDictionary(kv => kv.Key, kv => kv.Value);
        _bpmAtMeasureStart = new List<double> { _baseBpm };

        _changesByMeasure = bpmChanges
            .Where(c => c.Bpm > 0)
            .GroupBy(c => c.Measure)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Position).ToList());

        HasBpmChanges = _changesByMeasure.Count > 0;
        HasMeasureLengthChanges = _measureLengths.Count > 0;
    }

    // BPM 도 마디 길이도 바뀌지 않는 보통의 차트.
    public static ChartTimeline Uniform(double bpm) =>
        new(bpm, new Dictionary<int, double>(), Array.Empty<BpmChange>());

    public static ChartTimeline FromChart(BmsChart chart, double baseBpm) =>
        new(baseBpm, chart.MeasureLengths, chart.BpmChanges);

    // 둘 다 false 면 예전의 단순 계산(240/BPM)과 결과가 완전히 같다.
    public bool HasBpmChanges { get; }
    public bool HasMeasureLengthChanges { get; }

    public double BaseBpm => _baseBpm;

    // 마디 하나의 길이 배율. 1.0 이 4/4다. (#xxx02)
    public double GetMeasureLength(int measure) =>
        _measureLengths.TryGetValue(measure, out var length) && length > 0 ? length : 1.0;

    // 마디 위치(마디 + 마디 안 0~1)를 곡 시작부터의 초로 바꾼다.
    public double SecondsAt(double measurePosition)
    {
        if (measurePosition <= 0)
            return 0;

        var measure = (int)Math.Floor(measurePosition);
        var position = measurePosition - measure;

        EnsureBoundary(measure);

        var seconds = _measureStartSeconds[measure];
        var bpm = _bpmAtMeasureStart[measure];
        var cursor = 0.0;

        if (_changesByMeasure.TryGetValue(measure, out var changes))
        {
            foreach (var change in changes)
            {
                var at = Math.Clamp(change.Position, 0.0, 1.0);
                if (at >= position)
                    break;

                if (at > cursor)
                {
                    seconds += SpanSeconds(measure, at - cursor, bpm);
                    cursor = at;
                }

                bpm = change.Bpm;
            }
        }

        return seconds + SpanSeconds(measure, position - cursor, bpm);
    }

    // 곡 시작부터의 초를 마디 위치로 되돌린다. 클릭한 자리를 마디로 옮길 때 쓴다.
    public double MeasurePositionAt(double seconds)
    {
        if (seconds <= 0)
            return 0;

        var measure = 0;
        while (measure < MaxMeasure)
        {
            EnsureBoundary(measure + 1);
            if (_measureStartSeconds[measure + 1] > seconds)
                break;

            measure++;
        }

        var remaining = seconds - _measureStartSeconds[measure];
        var bpm = _bpmAtMeasureStart[measure];
        var cursor = 0.0;

        if (_changesByMeasure.TryGetValue(measure, out var changes))
        {
            foreach (var change in changes)
            {
                var at = Math.Clamp(change.Position, 0.0, 1.0);
                if (at > cursor)
                {
                    var span = SpanSeconds(measure, at - cursor, bpm);
                    if (span > remaining)
                        break;

                    remaining -= span;
                    cursor = at;
                }

                bpm = change.Bpm;
            }
        }

        // 남은 시간을 지금 BPM 으로 마디 비율로 되돌린다.
        var wholeMeasureSeconds = SpanSeconds(measure, 1.0, bpm);
        if (wholeMeasureSeconds <= 0)
            return measure + cursor;

        return measure + Math.Min(1.0, cursor + (remaining / wholeMeasureSeconds));
    }

    // 마디 안에서 measureFraction 만큼이 차지하는 시간. BPM 이 일정한 구간에서만 쓴다.
    private double SpanSeconds(int measure, double measureFraction, double bpm)
    {
        if (measureFraction <= 0 || bpm <= 0)
            return 0;

        // 4/4 한 마디 = 4박. 마디 길이 배율이 그 박 수를 늘리고 줄인다.
        var beats = 4.0 * GetMeasureLength(measure) * measureFraction;
        return beats * 60.0 / bpm;
    }

    // 아직 안 구한 마디 경계까지 이어서 채운다.
    private void EnsureBoundary(int measure)
    {
        while (_measureStartSeconds.Count <= measure)
        {
            var previous = _measureStartSeconds.Count - 1;
            var start = _measureStartSeconds[previous];
            var bpm = _bpmAtMeasureStart[previous];
            var cursor = 0.0;

            if (_changesByMeasure.TryGetValue(previous, out var changes))
            {
                foreach (var change in changes)
                {
                    var at = Math.Clamp(change.Position, 0.0, 1.0);
                    if (at > cursor)
                    {
                        start += SpanSeconds(previous, at - cursor, bpm);
                        cursor = at;
                    }

                    bpm = change.Bpm;
                }
            }

            start += SpanSeconds(previous, 1.0 - cursor, bpm);

            _measureStartSeconds.Add(start);
            _bpmAtMeasureStart.Add(bpm);
        }
    }
}

using System;
using System.Threading.Tasks;

namespace bms_editer.Services;

// 소수점까지 재는 BPM 측정. iBMSC 방식(IbmscBpmDetector)과 나란히 보여 주려고 만들었다.
//
// 1) 어택 곡선: 모노 표본을 다섯 대역(<150, 150~600, 600~2.5k, 2.5k~8k, >8kHz)으로 나눠
//    5ms 마다 log 에너지가 늘어난 양만 더한다. 대역을 나누는 이유는 마스터링된 곡의 전대역 음량이
//    거의 평평해서, 한 줄로 보면 킥·하이햇 어택이 묻히기 때문이다.
// 2) BPM 후보마다 어택 곡선과 복소 지수(cos + sin)의 내적 **크기**를 본다. iBMSC 처럼 cos 만 쓰면
//    박이 곡 0초에서 시작하지 않는 곡은 위상이 어긋나 엉뚱한 BPM 이 뽑힌다(With: 164 → 123).
// 3) 같은 곡에서 2배·4배 박도 함께 울리므로 점수 = |X(b)| + ½|X(2b)| + ¼|X(4b)|. 이걸 안 하면
//    반 박(82 같은 값)이나 엇박 성분이 이긴다.
// 4) 60~199.99 를 0.05 간격으로 훑고, 가장 좋은 곳 ±0.06 을 0.002 간격으로 다시 훑는다.
//
// 리듬게임에 쓰는 곡은 200 BPM 미만으로 본다(사용자 기준). 그래서 범위를 그 안으로 묶는다.
//
// 검증(2026-11-09, H:\muse dash hwa\hwa 11곡): 차트 BPM 과 모두 ±0.35 안. 정수 차트 대비
// 대부분 ±0.01, your reality 104.76(차트 105), 아기상어 114.66(차트 115).
public static class PreciseBpmDetector
{
    public const double MinBpm = 60.0;
    public const double MaxBpm = 199.99;

    // 어택 곡선 한 칸의 길이. GridFitAdvisor 도 같은 곡선을 쓴다.
    public const double HopSeconds = 0.005;
    private const double CoarseStep = 0.05;
    private const double FineSpan = 0.06;
    private const double FineStep = 0.002;
    private const double DetrendSeconds = 0.5;

    private static readonly (double? Low, double? High)[] Bands =
    [
        (null, 150.0),
        (150.0, 600.0),
        (600.0, 2500.0),
        (2500.0, 8000.0),
        (8000.0, null),
    ];

    // 고르지 못하면(무음·너무 짧음) 0.
    public static double Detect(OggAudioData data) => Detect(BuildOnsetCurve(data));

    public static double Detect(float[] onsets)
    {
        if (onsets.Length < 4)
            return 0.0;

        var total = 0.0;
        foreach (var value in onsets)
            total += value;
        if (total <= 0)
            return 0.0;

        var coarseCount = (int)Math.Floor((MaxBpm - MinBpm) / CoarseStep) + 1;
        var coarse = new double[coarseCount];
        Parallel.For(0, coarseCount, i => coarse[i] = Score(onsets, MinBpm + (i * CoarseStep)));

        var bestIndex = 0;
        for (var i = 1; i < coarseCount; i++)
        {
            if (coarse[i] > coarse[bestIndex])
                bestIndex = i;
        }

        var center = MinBpm + (bestIndex * CoarseStep);
        var fineCount = (int)Math.Round(2 * FineSpan / FineStep) + 1;
        var fine = new double[fineCount];
        Parallel.For(0, fineCount, i => fine[i] = Score(onsets, center - FineSpan + (i * FineStep)));

        var bestFine = 0;
        for (var i = 1; i < fineCount; i++)
        {
            if (fine[i] > fine[bestFine])
                bestFine = i;
        }

        return Math.Clamp(center - FineSpan + (bestFine * FineStep), MinBpm, MaxBpm);
    }

    private static double Score(float[] onsets, double bpm) =>
        Magnitude(onsets, bpm) + (0.5 * Magnitude(onsets, bpm * 2)) + (0.25 * Magnitude(onsets, bpm * 4));

    // |Σ o[n]·e^(-i·2π·(bpm/60)·n·hop)|. 회전자를 곱해 나가서 cos/sin 을 매번 부르지 않는다.
    private static double Magnitude(float[] onsets, double bpm)
    {
        var step = -2.0 * Math.PI * (bpm / 60.0) * HopSeconds;
        var stepCos = Math.Cos(step);
        var stepSin = Math.Sin(step);
        double rotCos = 1, rotSin = 0, re = 0, im = 0;

        for (var n = 0; n < onsets.Length; n++)
        {
            re += onsets[n] * rotCos;
            im += onsets[n] * rotSin;

            var nextCos = (rotCos * stepCos) - (rotSin * stepSin);
            rotSin = (rotCos * stepSin) + (rotSin * stepCos);
            rotCos = nextCos;

            // 수만 번 곱하면 길이가 1 에서 조금씩 벗어난다. 가끔 되돌린다.
            if ((n & 4095) == 4095)
            {
                var length = Math.Sqrt((rotCos * rotCos) + (rotSin * rotSin));
                rotCos /= length;
                rotSin /= length;
            }
        }

        return Math.Sqrt((re * re) + (im * im));
    }

    // 5ms 칸마다 다섯 대역의 log 에너지 증가량 합(느린 변화는 뺌). 칸 i 는 [i·hop, (i+1)·hop) 구간이다.
    public static float[] BuildOnsetCurve(OggAudioData data)
    {
        var channels = Math.Max(1, data.Channels);
        var frames = data.FrameCount;
        var sampleRate = data.SampleRate;
        if (frames <= 0 || sampleRate <= 0)
            return [];

        var hop = Math.Max(1, (int)Math.Round(sampleRate * HopSeconds));
        var count = (int)(frames / hop);
        if (count <= 0)
            return [];

        var mono = new float[count * hop];
        var pcm = data.Pcm16;
        var stride = channels * sizeof(short);
        for (var i = 0; i < mono.Length; i++)
        {
            var sum = 0;
            var offset = i * stride;
            for (var c = 0; c < channels; c++)
                sum += (short)(pcm[offset + (c * 2)] | (pcm[offset + (c * 2) + 1] << 8));
            mono[i] = sum / (32768f * channels);
        }

        var total = new double[count];
        Parallel.For(0, Bands.Length, () => new double[count], (b, _, local) =>
        {
            var (low, high) = Bands[b];
            var sections = new System.Collections.Generic.List<Biquad>();
            if (low is { } l)
                sections.Add(MakeButterworth2(sampleRate, l, highPass: true));
            if (high is { } h)
                sections.Add(MakeButterworth2(sampleRate, h, highPass: false));

            var previous = double.NaN;
            var states = new double[sections.Count * 2];
            for (var frame = 0; frame < count; frame++)
            {
                var energy = 0.0;
                for (var k = 0; k < hop; k++)
                {
                    double value = mono[(frame * hop) + k];
                    for (var s = 0; s < sections.Count; s++)
                    {
                        var q = sections[s];
                        var output = (q.B0 * value) + states[s * 2];
                        states[s * 2] = (q.B1 * value) - (q.A1 * output) + states[(s * 2) + 1];
                        states[(s * 2) + 1] = (q.B2 * value) - (q.A2 * output);
                        value = output;
                    }

                    energy += value * value;
                }

                var log = Math.Log((energy / hop) + 1e-10);
                if (!double.IsNaN(previous))
                    local[frame] += Math.Max(0.0, log - previous);
                previous = log;
            }

            return local;
        },
        local =>
        {
            lock (total)
            {
                for (var i = 0; i < count; i++)
                    total[i] += local[i];
            }
        });

        // 0.5초 이동 평균을 빼서 느린 음량 변화를 지운다. 남은 것은 어택뿐이다.
        var window = Math.Max(1, (int)(DetrendSeconds / HopSeconds));
        var prefix = new double[count + 1];
        for (var i = 0; i < count; i++)
            prefix[i + 1] = prefix[i] + total[i];

        var onsets = new float[count];
        for (var i = 0; i < count; i++)
        {
            var from = Math.Max(0, i - (window / 2));
            var to = Math.Min(count, i + ((window + 1) / 2));
            var mean = (prefix[to] - prefix[from]) / window;
            onsets[i] = (float)Math.Max(0.0, total[i] - mean);
        }

        return onsets;
    }

    private readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2);

    // 2차 버터워스(Q = 1/√2). RBJ 쿡북 계수.
    private static Biquad MakeButterworth2(int sampleRate, double cutoffHz, bool highPass)
    {
        var w0 = 2.0 * Math.PI * Math.Min(cutoffHz, sampleRate * 0.45) / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2.0 * Math.Sqrt(0.5));
        var a0 = 1.0 + alpha;
        var b0 = highPass ? (1.0 + cos) / 2.0 : (1.0 - cos) / 2.0;
        var b1 = highPass ? -(1.0 + cos) : 1.0 - cos;
        return new Biquad(b0 / a0, b1 / a0, b0 / a0, -2.0 * cos / a0, (1.0 - alpha) / a0);
    }
}

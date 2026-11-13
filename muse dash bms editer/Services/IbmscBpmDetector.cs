using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace bms_editer.Services;

// iBMSC MD 의 "BPM 자동 측정"(iBMSC MD-dotnet/src/x.cs)을 그대로 옮긴 것.
//
// iBMSC 와 같은 숫자를 보려고 옮긴 것이라 동작을 고치지 않았다. 알려진 성질:
//   - 왼쪽 채널만 쓴다.
//   - 50ms(44.1kHz 스테레오 기준 2205 표본) 칸의 평균 음량 → 늘어난 양만 남긴 어택 곡선.
//   - BPM 후보마다 어택 곡선에 cos 만 곱해 더한다(sin 없음). 그래서 박이 곡 0초 근처에서
//     시작해야 점수가 나오고, 위상이 어긋난 곡은 엉뚱한 BPM(예: 3/4배)이 뽑힌다.
//   - 모든 점수가 0 이하면 0 을 돌려준다.
//   - 저음 모드의 필터는 Accord LowPassFilter(250Hz)·HighPassFilter(50Hz)(1차 RC). 원본이 모노 배열을
//     Signal.FromArray(array, channels) 로 넘겨, 스테레오면 짝/홀 표본을 다른 채널로 오해한 채 거른다.
//     이 오해까지 그대로 둔다(같은 숫자가 나와야 하므로).
//
// 원본과 다른 점 두 가지(사용자 요청):
//   - 범위: 원본은 60~400 을 훑지만 여기서는 60~199.99 만 본다. 리듬게임에 쓰는 곡은 200 BPM 미만으로
//     본다. 200 이상(예: With 의 205·246)이 뽑혀 봐야 쓸 데가 없다.
//   - 간격: 원본은 정수 BPM 만 훑지만 여기서는 0.01 간격으로 훑는다. 점수 식(cos 만 쓰는 것)은 그대로다.
//     정수 사이 값은 원본에 없던 값이므로, 원본 iBMSC 는 이 값의 반올림이 아니라 정수 중 최고를 내놓는다.
//     **소수점은 믿을 만하지 않다.** cos 만 쓰니 박의 시작 위상이 템포 쪽으로 새어 나와, 완벽한 120 BPM
//     클릭도 120.30 이 나온다. 정수 원본에서는 반올림에 가려 안 보였을 뿐이다. 소수점은 PreciseBpmDetector 를 보라.
public static class IbmscBpmDetector
{
    private const double MinBpm = 60.0;
    private const double MaxBpm = 199.99;
    private const double Step = 0.01;

    // fullBand=true  : iBMSC 의 x.a(A_0: true)  — 필터 없이
    // fullBand=false : iBMSC 의 x.a(A_0: false) — 저음(50~250Hz) 필터를 거쳐서
    // 고르지 못하면 0.
    public static double Detect(OggAudioData data, bool fullBand)
    {
        var channels = Math.Max(1, data.Channels);
        var length = data.FrameCount;
        if (length <= 0 || data.SampleRate <= 0)
            return 0;

        // array[i] = ptr3[i * channels] / 32768f — 왼쪽 채널만.
        var pcm = data.Pcm16;
        var array = new float[length];
        var stride = channels * sizeof(short);
        for (long i = 0; i < length; i++)
        {
            var offset = (int)(i * stride);
            array[i] = (short)(pcm[offset] | (pcm[offset + 1] << 8)) / 32768f;
        }

        float sampleRate = data.SampleRate;
        if (!fullBand)
            ApplyAccordFilters(array, channels, sampleRate);

        var window = (int)(sampleRate / 44100f * (channels / 2f) * 2205f);
        if (window <= 0)
            return 0;

        return PickBpm(BuildEnvelope(array, window), sampleRate, window);
    }

    // 원본 a(float[], float, int, int): 칸마다 Σ(|x|·2) / 칸 길이, 그다음 최댓값으로 나눈다.
    private static float[] BuildEnvelope(float[] samples, int window)
    {
        var envelope = new float[(int)Math.Ceiling((float)samples.Length / window)];
        var index = 0;
        for (var start = 0; start < samples.Length; start += window)
        {
            var sum = 0f;
            for (var i = start; i < start + window && i < samples.Length; i++)
            {
                var value = Math.Abs(samples[i]);
                if (!(value > 1f))
                    sum += value * 2f;
            }

            envelope[index++] = sum / window;
        }

        var max = 0f;
        foreach (var value in envelope)
            max = Math.Max(max, value);

        // 원본은 무음이면 0 으로 나눠 NaN 이 된다. 여기서는 그대로 두면 아래 비교가 모두 거짓이라 0 이 나온다.
        if (max > 0f)
        {
            for (var i = 0; i < envelope.Length; i++)
                envelope[i] /= max;
        }

        return envelope;
    }

    // 원본 a(float[], float, int): 어택 곡선과 cos 의 내적이 가장 큰 BPM(여기서는 0.01 간격).
    private static double PickBpm(float[] envelope, float sampleRate, int window)
    {
        var flux = new List<float>(Math.Max(0, envelope.Length - 1));
        for (var i = 1; i < envelope.Length; i++)
            flux.Add(Math.Max(envelope[i] - envelope[i - 1], 0f));

        if (flux.Count == 0)
            return 0.0;

        var values = flux.ToArray();
        var scale = 1.0 / values.Length;
        var framesPerSecond = sampleRate / window;
        var count = (int)Math.Round((MaxBpm - MinBpm) / Step) + 1;
        var scores = new double[count];

        // 후보가 1만 4천 개라 나눠 돌린다. 고르는 순서(작은 BPM 부터, 같으면 앞의 것)는 아래에서 원본대로 지킨다.
        Parallel.For(0, count, i =>
        {
            var omega = Math.PI * 2.0 * (BpmAt(i) / 60.0) / framesPerSecond;
            var stepCos = Math.Cos(omega);
            var stepSin = Math.Sin(omega);
            double rotCos = 1.0, rotSin = 0.0, score = 0.0;

            for (var n = 0; n < values.Length; n++)
            {
                score += values[n] * rotCos;
                var nextCos = (rotCos * stepCos) - (rotSin * stepSin);
                rotSin = (rotCos * stepSin) + (rotSin * stepCos);
                rotCos = nextCos;

                if ((n & 4095) == 4095)
                {
                    var len = Math.Sqrt((rotCos * rotCos) + (rotSin * rotSin));
                    rotCos /= len;
                    rotSin /= len;
                }
            }

            scores[i] = score * scale;
        });

        // 원본은 best = 0 에서 시작해 "더 클 때만" 바꾼다. 모든 점수가 0 이하면 0 이 나온다.
        var best = 0.0;
        var result = 0.0;
        for (var i = 0; i < count; i++)
        {
            if (best < scores[i])
            {
                best = scores[i];
                result = BpmAt(i);
            }
        }

        return result;
    }

    private static double BpmAt(int index) => Math.Round(MinBpm + (index * Step), 2);

    // Accord.Audio.Filters.LowPassFilter(250) → HighPassFilter(50) 을 Signal.FromArray(array, channels) 로 건 것과 같다.
    // 배열을 channels 채널 인터리브로 보고 채널마다 따로 거른다. 꼬리의 남는 표본(길이 % channels)은 건드리지 않는다.
    private static void ApplyAccordFilters(float[] array, int channels, float sampleRate)
    {
        var perChannel = array.Length / channels;
        if (perChannel <= 0)
            return;

        var dt = 1f / sampleRate;
        var lowRc = 1f / (2f * MathF.PI * 250f);
        var lowAlpha = dt / (lowRc + dt);
        var highRc = 1f / (2f * MathF.PI * 50f);
        var highAlpha = highRc / (highRc + dt);

        var low = new float[array.Length];
        for (var c = 0; c < channels; c++)
        {
            low[c] = array[c];
            for (var i = 1; i < perChannel; i++)
            {
                var at = (i * channels) + c;
                low[at] = low[at - channels] + (lowAlpha * (array[at] - low[at - channels]));
            }
        }

        for (var c = 0; c < channels; c++)
        {
            var previousIn = low[c];
            array[c] = low[c];
            for (var i = 1; i < perChannel; i++)
            {
                var at = (i * channels) + c;
                var input = low[at];
                array[at] = highAlpha * (array[at - channels] + input - previousIn);
                previousIn = input;
            }
        }
    }
}

using System;
using NVorbis;

namespace bms_editer.Services;

public sealed record OggWaveform(float[] Peaks, float[] Onsets, double DurationSeconds);

// OGG Vorbis 파일을 디코딩해서 파형 표시용 피크와 박자 확인용 어택 값을 다운샘플링한다.
public static class OggPeakLoader
{
    // 버킷 index 가 대응하는 시각의 비율(0~1).
    //
    // Load() 가 버킷을 `frameIndex * peakCount / totalFrames` 로 담으므로,
    // peaks[i] · onsets[i] 는 정확히 `i * DurationSeconds / count` 시점을 가리킨다.
    // 그리는 쪽은 반드시 이 규칙을 써야 한다.
    //
    // 이 한 줄을 각자 다시 쓰다가 세 번 어긋났다.
    //   f86a58c — 버킷 크기를 정수로 절삭해서 2분 지점 53ms
    //   e19c25b — 재생 커서를 벽시계로 잡아서 약 12ms
    //   (27번)  — 온셋 마커만 i/(count-1) 이라 곡 끝에서 12~15ms
    // 그래서 규칙을 담은 곳 옆에 두고 한 군데서만 정의한다.
    public static double GetBucketRatio(int index, int count) =>
        count <= 0 ? 0.0 : (double)index / count;

    // 화면 칸 하나가 덮는 시간 구간 [startRatio, endRatio) 에 걸리는 버킷 범위 [From, To).
    // GetBucketRatio 를 뒤집은 규칙이다. 그리는 쪽은 "이 칸에 어느 버킷을 보여줄까"를
    // 반드시 여기에 물어야 한다.
    //
    // 네 번째였다. OggWaveformControl 이 파형 막대를 그릴 때만 i/(Count-1) 로 되짚어서,
    // 같은 배열을 쓰는 온셋 마커와 최대 17ms 벌어졌다. 게다가 그 배율 오차(0.002~0.008%)의
    // 부호가 세로 줌에 따라 뒤집혀서, 줌을 만지면 파형이 격자 밑에서 미끄러졌다.
    //
    // 최소 한 칸은 반드시 돌려준다. 화면 칸이 버킷보다 촘촘하면 같은 버킷을 여러 칸이
    // 나눠 갖고, 성기면 여러 버킷이 한 칸에 묶인다. 묶인 쪽에서 **최댓값**을 골라야
    // 킥처럼 순간적으로 튀는 피크가 화면에서 통째로 사라지지 않는다.
    // (예전에는 최근접 버킷 하나만 집어 와서 실제로 사라졌다)
    public static (int From, int To) GetBucketRange(double startRatio, double endRatio, int count)
    {
        if (count <= 0)
            return (0, 0);

        var from = Math.Clamp((int)Math.Floor(startRatio * count), 0, count - 1);
        var to = Math.Clamp((int)Math.Ceiling(endRatio * count), from + 1, count);
        return (from, to);
    }

    // peaksPerSecond: 초당 샘플 개수. 곡 길이에 비례해서 정하므로
    // 짧은 곡/긴 곡 모두 화면에서 비슷한 밀도로 보인다(고정 총 개수 대비 개선).
    // 이미 풀어놓은 PCM 에서 피크를 뽑는다.
    //
    // 예전에는 여기서도 VorbisReader 를 따로 열어 곡 전체를 다시 디코딩했다.
    // OggAudioPlayer 가 하는 일과 똑같은 일을 한 번 더 한 셈이라 로딩이 두 배 걸렸다.
    public static OggWaveform Load(OggAudioData data, double peaksPerSecond = 80.0)
    {
        var durationSeconds = data.DurationSeconds;
        var peakCount = Math.Clamp((int)(durationSeconds * peaksPerSecond), 32, 20000);

        var channels = data.Channels;
        var totalFrames = data.FrameCount;

        var peaks = new float[peakCount];
        var energies = new float[peakCount];

        if (totalFrames <= 0 || channels <= 0)
            return new OggWaveform(peaks, BuildOnsets(energies), durationSeconds);

        // 버킷 크기를 정수 프레임 수로 고정하면(예: 44100/80 = 551.25 → 551) 피크 배열이
        // 곡 전체를 덮지 못한 채 타임라인 전체 폭에 늘어나 그려져서, 재생 시간에 비례하는
        // 어긋남이 쌓인다(44.1kHz 기준 약 0.045%, 2분 지점에서 50ms 이상).
        // 그래서 프레임 절대 위치로 버킷을 직접 계산해, peaks[i]가 항상
        // i * DurationSeconds / peakCount 시점에 정확히 대응하도록 한다.
        long frameIndex = 0;
        var bucketIndex = 0;
        var frameInBucket = 0;
        double sumSquares = 0;
        var maxInBucket = 0f;

        void FlushBucket()
        {
            if (frameInBucket == 0)
                return;

            var rms = ToRms(sumSquares, frameInBucket);
            energies[bucketIndex] = rms;
            peaks[bucketIndex] = ToDisplayAmplitude(rms, maxInBucket);
            sumSquares = 0;
            frameInBucket = 0;
            maxInBucket = 0f;
        }

        var pcm = data.Pcm16;
        var bytesPerFrame = channels * sizeof(short);

        for (; frameIndex < totalFrames; frameIndex++)
        {
            var bucket = (int)Math.Min(peakCount - 1, frameIndex * peakCount / totalFrames);
            if (bucket != bucketIndex)
            {
                FlushBucket();
                bucketIndex = bucket;
            }

            var frameStart = frameIndex * bytesPerFrame;
            var value = 0f;

            for (var c = 0; c < channels; c++)
            {
                var offset = (int)frameStart + (c * sizeof(short));
                var sample = (short)(pcm[offset] | (pcm[offset + 1] << 8));

                // short.MinValue 의 절댓값은 short 범위를 넘으므로 float 로 올려 나눈다.
                value = Math.Max(value, Math.Abs(sample / 32768f));
            }

            sumSquares += (double)value * value;
            maxInBucket = Math.Max(maxInBucket, value);
            frameInBucket++;
        }

        FlushBucket();

        return new OggWaveform(peaks, BuildOnsets(energies), durationSeconds);
    }

    private static float ToRms(double sumSquares, int frameCount) =>
        (float)Math.Sqrt(sumSquares / Math.Max(1, frameCount));

    // 파형 칸을 꽉 채우지 않도록 표시 게인을 낮게 유지한다.
    private static float ToDisplayAmplitude(float rms, float peak)
    {
        var amplitude = (rms * 0.75f) + (peak * 0.30f);
        return MathF.Min(1f, amplitude);
    }

    private static float[] BuildOnsets(float[] energies)
    {
        var onsets = new float[energies.Length];
        var maxOnset = 0f;

        for (var i = 1; i < energies.Length; i++)
        {
            var previousFloor = MathF.Max(energies[i - 1] * 0.92f, GetLocalAverage(energies, i - 8, i));
            var onset = MathF.Max(0f, energies[i] - previousFloor);
            onsets[i] = onset;
            maxOnset = MathF.Max(maxOnset, onset);
        }

        if (maxOnset <= 0)
            return onsets;

        for (var i = 0; i < onsets.Length; i++)
            onsets[i] = MathF.Min(1f, onsets[i] / maxOnset);

        return onsets;
    }

    private static float GetLocalAverage(float[] values, int start, int end)
    {
        start = Math.Clamp(start, 0, values.Length);
        end = Math.Clamp(end, start, values.Length);
        if (start == end)
            return 0f;

        var sum = 0f;
        for (var i = start; i < end; i++)
            sum += values[i];

        return sum / (end - start);
    }
}

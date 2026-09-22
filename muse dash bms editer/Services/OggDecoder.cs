using System;
using NVorbis;

namespace bms_editer.Services;

// 디코딩한 OGG 한 곡. 재생과 파형이 이걸 나눠 쓴다.
public sealed record OggAudioData(byte[] Pcm16, int SampleRate, int Channels, double DurationSeconds)
{
    public int BlockAlign => Math.Max(1, Channels * sizeof(short));

    public long FrameCount => BlockAlign > 0 ? Pcm16.Length / BlockAlign : 0;
}

// OGG Vorbis 를 PCM16 으로 푸는 곳.
//
// 예전에는 OggPeakLoader 와 OggAudioPlayer 가 **각각** VorbisReader 를 열어
// 곡 전체를 따로 디코딩했다. 5분짜리면 같은 일을 두 번 하느라 두 배의 시간이 걸렸고,
// 둘 다 UI 스레드에서 동기로 돌아 그동안 창이 얼어붙었다.
// 이제 한 번만 풀고 그 결과를 나눠 쓴다.
public static class OggDecoder
{
    public static OggAudioData Decode(string filePath)
    {
        using var reader = new VorbisReader(filePath);

        var channels = Math.Max(1, reader.Channels);
        var sampleRate = reader.SampleRate;
        var durationSeconds = reader.TotalTime.TotalSeconds;

        // TotalSamples 는 granule 값에서 오는 "예상치"다. 맞으면 재할당이 없고,
        // 어긋나면 아래에서 늘리거나 줄인다.
        var expectedSamples = reader.TotalSamples * channels;
        var capacity = expectedSamples > 0 && expectedSamples < int.MaxValue / sizeof(short)
            ? (int)expectedSamples * sizeof(short)
            : 0;

        var bytes = new byte[Math.Max(capacity, 4096)];
        var byteOffset = 0;
        var floatBuffer = new float[4096 * channels];

        int samplesRead;
        while ((samplesRead = reader.ReadSamples(floatBuffer, 0, floatBuffer.Length)) > 0)
        {
            var needed = byteOffset + (samplesRead * sizeof(short));

            // granule 값이 어긋난 파일에서 디코더가 예상보다 더 내보내는 일이 실제로 있다.
            // 예전에는 고정 크기 배열에 bytes[byteOffset++] 로 그냥 써서
            // IndexOutOfRangeException 이 났고, LoadOgg 가 삼켜 "로드 실패"로만 보였다.
            if (needed > bytes.Length)
                Array.Resize(ref bytes, Math.Max(needed, bytes.Length * 2));

            for (var i = 0; i < samplesRead; i++)
            {
                var sample = (short)(Math.Clamp(floatBuffer[i], -1f, 1f) * short.MaxValue);
                bytes[byteOffset++] = (byte)(sample & 0xff);
                bytes[byteOffset++] = (byte)((sample >> 8) & 0xff);
            }
        }

        // 예상보다 적게 나왔으면 남는 꼬리를 잘라낸다. 안 자르면 곡 끝에 무음이 붙는다.
        if (byteOffset != bytes.Length)
            Array.Resize(ref bytes, byteOffset);

        // 길이도 실제로 받아낸 표본으로 다시 잡는다. 파형·격자·재생이 모두 이 값을 기준으로 삼는다.
        var blockAlign = channels * sizeof(short);
        if (sampleRate > 0 && blockAlign > 0)
        {
            var actualSeconds = (double)bytes.Length / blockAlign / sampleRate;
            if (actualSeconds > 0)
                durationSeconds = actualSeconds;
        }

        return new OggAudioData(bytes, sampleRate, channels, durationSeconds);
    }
}

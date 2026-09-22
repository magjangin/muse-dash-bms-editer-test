using System;
using System.IO;

namespace bms_editer.Services;

public sealed record PcmAudioData(short[] Samples, int SampleRate, int Channels)
{
    public int FrameCount => Samples.Length / 2;
}

public static class WavDecoder
{
    public const int TargetSampleRate = 44100;
    public const int TargetChannels = 2;

    public static PcmAudioData? Decode(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var ext = Path.GetExtension(filePath);
        if (string.Equals(ext, ".ogg", StringComparison.OrdinalIgnoreCase))
            return DecodeOgg(filePath);

        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 12)
                return null;

            var riff = reader.ReadBytes(4);
            if (riff[0] != 'R' || riff[1] != 'I' || riff[2] != 'F' || riff[3] != 'F')
                return null;

            reader.ReadInt32(); // fileSize

            var wave = reader.ReadBytes(4);
            if (wave[0] != 'W' || wave[1] != 'A' || wave[2] != 'V' || wave[3] != 'E')
                return null;

            short audioFormat = 0;
            short channels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            byte[]? dataBytes = null;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkIdBytes = reader.ReadBytes(4);
                var chunkId = System.Text.Encoding.ASCII.GetString(chunkIdBytes);
                var chunkSize = reader.ReadInt32();

                if (chunkSize < 0)
                    break;

                var safeChunkSize = (int)Math.Min((long)chunkSize, stream.Length - stream.Position);

                if (chunkId == "fmt ")
                {
                    audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // byteRate
                    reader.ReadInt16(); // blockAlign
                    bitsPerSample = reader.ReadInt16();

                    var readBytes = 16;

                    // WAVE_FORMAT_EXTENSIBLE (0xFFFE) 처리
                    if (audioFormat == -2 || audioFormat == unchecked((short)0xFFFE))
                    {
                        var extraSize = reader.ReadInt16();
                        readBytes += 2;
                        if (extraSize >= 22)
                        {
                            reader.ReadInt16(); // validBitsPerSample
                            reader.ReadInt32(); // channelMask
                            var subFormatTag = reader.ReadInt16(); // 첫 2바이트가 GUID 포맷 (1 = PCM, 3 = Float)
                            reader.ReadBytes(14); // 나머지 14바이트 GUID
                            readBytes += 22;
                            audioFormat = subFormatTag;
                        }
                    }

                    var remaining = safeChunkSize - readBytes;
                    if (remaining > 0)
                        reader.ReadBytes(remaining);
                }
                else if (chunkId == "data")
                {
                    dataBytes = reader.ReadBytes(safeChunkSize);
                    break;
                }
                else
                {
                    reader.ReadBytes(safeChunkSize);
                }

                // RIFF 청크 2바이트 워드 패딩 처리
                if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
                {
                    stream.Position++;
                }
            }

            if (dataBytes is null || channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                return null;

            // PCM(1) 또는 IEEE Float(3) 지원
            if (audioFormat != 1 && audioFormat != 3)
                return null;

            var sourceFrames = ExtractFrames(dataBytes, audioFormat, channels, bitsPerSample);
            if (sourceFrames is null || sourceFrames.Length == 0)
                return null;

            var resampled = ResampleToStereo44100(sourceFrames, channels, sampleRate);
            return new PcmAudioData(resampled, TargetSampleRate, TargetChannels);
        }
        catch
        {
            return null;
        }
    }

    private static PcmAudioData? DecodeOgg(string filePath)
    {
        try
        {
            var data = OggDecoder.Decode(filePath);
            if (data.Pcm16.Length == 0 || data.SampleRate <= 0 || data.Channels <= 0)
                return null;

            var sampleCount = data.Pcm16.Length / sizeof(short);
            var shorts = new short[sampleCount];
            Buffer.BlockCopy(data.Pcm16, 0, shorts, 0, data.Pcm16.Length);

            var frameCount = sampleCount / data.Channels;
            var sourceFrames = new float[frameCount * data.Channels];
            for (var i = 0; i < sampleCount; i++)
            {
                sourceFrames[i] = shorts[i] / 32768f;
            }

            var resampled = ResampleToStereo44100(sourceFrames, data.Channels, data.SampleRate);
            return new PcmAudioData(resampled, TargetSampleRate, TargetChannels);
        }
        catch
        {
            return null;
        }
    }

    private static float[]? ExtractFrames(byte[] data, short format, short channels, short bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0) return null;

        var totalSamples = data.Length / bytesPerSample;
        var frames = totalSamples / channels;
        var result = new float[frames * channels];

        if (format == 1) // PCM
        {
            if (bitsPerSample == 8)
            {
                for (var i = 0; i < result.Length; i++)
                    result[i] = (data[i] - 128) / 128f;
            }
            else if (bitsPerSample == 16)
            {
                for (var i = 0; i < result.Length; i++)
                {
                    var offset = i * 2;
                    short s = (short)(data[offset] | (data[offset + 1] << 8));
                    result[i] = s / 32768f;
                }
            }
            else if (bitsPerSample == 24)
            {
                for (var i = 0; i < result.Length; i++)
                {
                    var offset = i * 3;
                    var sample24 = (data[offset] << 8) | (data[offset + 1] << 16) | (data[offset + 2] << 24);
                    result[i] = sample24 / 2147483648f;
                }
            }
            else if (bitsPerSample == 32)
            {
                for (var i = 0; i < result.Length; i++)
                {
                    var offset = i * 4;
                    var s = BitConverter.ToInt32(data, offset);
                    result[i] = s / 2147483648f;
                }
            }
            else
            {
                return null;
            }
        }
        else if (format == 3) // IEEE Float
        {
            if (bitsPerSample == 32)
            {
                for (var i = 0; i < result.Length; i++)
                    result[i] = BitConverter.ToSingle(data, i * 4);
            }
            else
            {
                return null;
            }
        }

        return result;
    }

    private static short[] ResampleToStereo44100(float[] source, int sourceChannels, int sourceSampleRate)
    {
        var sourceFrames = source.Length / sourceChannels;
        if (sourceFrames == 0) return Array.Empty<short>();

        int targetFrames;
        if (sourceSampleRate == TargetSampleRate)
        {
            targetFrames = sourceFrames;
        }
        else
        {
            targetFrames = (int)Math.Round((double)sourceFrames * TargetSampleRate / sourceSampleRate);
        }

        var output = new short[targetFrames * TargetChannels];

        for (var i = 0; i < targetFrames; i++)
        {
            float l;
            float r;

            if (sourceSampleRate == TargetSampleRate)
            {
                if (sourceChannels == 1)
                {
                    l = r = source[i];
                }
                else
                {
                    l = source[i * sourceChannels];
                    r = source[i * sourceChannels + 1];
                }
            }
            else
            {
                var srcPos = (double)i * sourceSampleRate / TargetSampleRate;
                var idx0 = (int)Math.Floor(srcPos);
                var idx1 = Math.Min(idx0 + 1, sourceFrames - 1);
                var frac = (float)(srcPos - idx0);

                if (sourceChannels == 1)
                {
                    var s0 = source[idx0];
                    var s1 = source[idx1];
                    l = r = s0 + (s1 - s0) * frac;
                }
                else
                {
                    var l0 = source[idx0 * sourceChannels];
                    var l1 = source[idx1 * sourceChannels];
                    var r0 = source[idx0 * sourceChannels + 1];
                    var r1 = source[idx1 * sourceChannels + 1];

                    l = l0 + (l1 - l0) * frac;
                    r = r0 + (r1 - r0) * frac;
                }
            }

            output[i * 2] = (short)(Math.Clamp(l, -1f, 1f) * short.MaxValue);
            output[i * 2 + 1] = (short)(Math.Clamp(r, -1f, 1f) * short.MaxValue);
        }

        return output;
    }
}

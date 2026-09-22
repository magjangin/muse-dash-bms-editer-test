using System;
using System.IO;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

public sealed class KeySoundPlayerTests
{
    private static string CreateSyntheticWavFile(int sampleRate, short channels, short bitsPerSample, short format, int frameCount)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_sound_{Guid.NewGuid():N}.wav");
        using var stream = File.Create(tempFile);
        using var writer = new BinaryWriter(stream);

        var bytesPerSample = bitsPerSample / 8;
        var blockAlign = (short)(channels * bytesPerSample);
        var byteRate = sampleRate * blockAlign;
        var dataSize = frameCount * blockAlign;

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // chunk size
        writer.Write(format);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        for (var i = 0; i < frameCount; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 440.0 * i / sampleRate);
            for (var c = 0; c < channels; c++)
            {
                if (bitsPerSample == 16)
                {
                    writer.Write((short)(value * 32767));
                }
                else if (bitsPerSample == 8)
                {
                    writer.Write((byte)((value * 127) + 128));
                }
                else if (bitsPerSample == 24)
                {
                    var s24 = (int)(value * 8388607);
                    writer.Write((byte)(s24 & 0xFF));
                    writer.Write((byte)((s24 >> 8) & 0xFF));
                    writer.Write((byte)((s24 >> 16) & 0xFF));
                }
                else if (bitsPerSample == 32 && format == 3)
                {
                    writer.Write(value);
                }
            }
        }

        return tempFile;
    }

    [Fact]
    public void WavDecoder_16비트_스테레오_WAV_디코딩이_정상_동작한다()
    {
        var path = CreateSyntheticWavFile(44100, 2, 16, 1, 4410);
        try
        {
            var pcm = WavDecoder.Decode(path);
            Assert.NotNull(pcm);
            Assert.Equal(44100, pcm.SampleRate);
            Assert.Equal(2, pcm.Channels);
            Assert.Equal(4410, pcm.FrameCount);
            Assert.Equal(8820, pcm.Samples.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WavDecoder_모노_음원을_44100Hz_스테레오로_확장_변환한다()
    {
        var path = CreateSyntheticWavFile(22050, 1, 16, 1, 2205); // 0.1초
        try
        {
            var pcm = WavDecoder.Decode(path);
            Assert.NotNull(pcm);
            Assert.Equal(44100, pcm.SampleRate);
            Assert.Equal(2, pcm.Channels);
            // 22050Hz 0.1초 -> 44100Hz 약 4410 프레임
            Assert.InRange(pcm.FrameCount, 4400, 4420);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WavDecoder_24비트_및_Float_WAV도_정상_디코딩한다()
    {
        var path24 = CreateSyntheticWavFile(44100, 2, 24, 1, 1000);
        var pathFloat = CreateSyntheticWavFile(48000, 2, 32, 3, 4800);
        try
        {
            var pcm24 = WavDecoder.Decode(path24);
            Assert.NotNull(pcm24);
            Assert.Equal(1000, pcm24.FrameCount);

            var pcmFloat = WavDecoder.Decode(pathFloat);
            Assert.NotNull(pcmFloat);
            Assert.Equal(44100, pcmFloat.SampleRate);
            Assert.InRange(pcmFloat.FrameCount, 4400, 4420);
        }
        finally
        {
            if (File.Exists(path24)) File.Delete(path24);
            if (File.Exists(pathFloat)) File.Delete(pathFloat);
        }
    }

    [Fact]
    public void KeySoundPlayer_사전_로드_및_다중_재생이_예외_없이_동작한다()
    {
        using var player = new KeySoundPlayer();
        var path1 = CreateSyntheticWavFile(44100, 2, 16, 1, 1000);
        var path2 = CreateSyntheticWavFile(44100, 2, 16, 1, 1000);

        try
        {
            player.Preload(path1);
            player.Preload(path2);

            // 화음 동시 발음 시뮬레이션
            player.Play(path1);
            player.Play(path2);

            player.StopAll();
        }
        finally
        {
            if (File.Exists(path1)) File.Delete(path1);
            if (File.Exists(path2)) File.Delete(path2);
        }
    }
}

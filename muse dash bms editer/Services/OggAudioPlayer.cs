using System;
using System.Runtime.InteropServices;
using NVorbis;

namespace bms_editer.Services;

public sealed partial class OggAudioPlayer : IDisposable
{
    private readonly byte[] _pcmBytes;
    private readonly int _sampleRate;
    private readonly short _channels;
    private readonly short _bitsPerSample = 16;
    private IntPtr _waveOut;
    private GCHandle _dataHandle;
    private GCHandle _headerHandle;
    private WaveHeader _header;
    private bool _prepared;

    // 디코딩은 OggDecoder 가 한 번만 한다. 파형 계산도 같은 결과를 나눠 쓴다.
    public OggAudioPlayer(OggAudioData data)
    {
        _sampleRate = data.SampleRate;
        _channels = (short)data.Channels;
        DurationSeconds = data.DurationSeconds;
        _pcmBytes = data.Pcm16;
    }

    public double DurationSeconds { get; }

    private int BlockAlign => _channels * _bitsPerSample / 8;

    // 장치가 실제로 재생한 시간(초). 재생 중이 아니거나 장치가 위치 조회를
    // 지원하지 않으면 null.
    //
    // 벽시계로 커서를 움직이면 두 가지로 어긋난다. 첫째로 waveOutWrite 가
    // 돌아온 뒤에도 실제 출력까지 드라이버 버퍼링 지연이 있어서 커서가 그만큼
    // 앞서 나가고, 둘째로 사운드 장치는 시스템 시계와 다른 크리스털로 돌아가
    // 시간이 갈수록 미세하게 벌어진다. 장치에 직접 물어보면 둘 다 사라진다.
    public double? GetPlayedSeconds()
    {
        if (_waveOut == IntPtr.Zero || _sampleRate <= 0)
            return null;

        var mmTime = new MmTime { Type = TIME_SAMPLES };
        if (WaveOutGetPosition(_waveOut, ref mmTime, Marshal.SizeOf<MmTime>()) != 0)
            return null;

        // 요청한 단위를 장치가 못 맞추면 실제로 채워준 단위가 Type 에 담겨 온다.
        return mmTime.Type switch
        {
            TIME_SAMPLES => mmTime.Value / (double)_sampleRate,
            TIME_BYTES => mmTime.Value / (double)(_sampleRate * Math.Max(1, BlockAlign)),
            _ => null,
        };
    }

    public void Play(double startSeconds, double speed = 1.0)
    {
        Stop();

        if (_pcmBytes.Length == 0)
            return;

        var speedClamped = Math.Clamp(speed, 0.05, 4.0);
        var targetSampleRate = Math.Max(1000, (int)Math.Round(_sampleRate * speedClamped));

        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = _channels,
            SamplesPerSec = targetSampleRate,
            BitsPerSample = _bitsPerSample,
        };
        format.BlockAlign = (short)(format.Channels * format.BitsPerSample / 8);
        format.AvgBytesPerSec = format.SamplesPerSec * format.BlockAlign;

        ThrowIfFailed(WaveOutOpen(out _waveOut, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0));

        var startByte = SecondsToByteOffset(startSeconds, format.BlockAlign);
        var byteCount = _pcmBytes.Length - startByte;
        if (byteCount <= 0)
            return;

        _dataHandle = GCHandle.Alloc(_pcmBytes, GCHandleType.Pinned);
        _header = new WaveHeader
        {
            Data = _dataHandle.AddrOfPinnedObject() + startByte,
            BufferLength = byteCount,
        };
        _headerHandle = GCHandle.Alloc(_header, GCHandleType.Pinned);

        ThrowIfFailed(WaveOutPrepareHeader(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>()));
        _prepared = true;
        ThrowIfFailed(WaveOutWrite(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>()));
    }

    public void Stop()
    {
        if (_waveOut == IntPtr.Zero)
            return;

        WaveOutReset(_waveOut);

        if (_prepared && _headerHandle.IsAllocated)
            WaveOutUnprepareHeader(_waveOut, _headerHandle.AddrOfPinnedObject(), Marshal.SizeOf<WaveHeader>());

        WaveOutClose(_waveOut);
        _waveOut = IntPtr.Zero;
        _prepared = false;

        if (_headerHandle.IsAllocated)
            _headerHandle.Free();

        if (_dataHandle.IsAllocated)
            _dataHandle.Free();
    }

    public void Dispose() => Stop();

    private int SecondsToByteOffset(double seconds, int blockAlign)
    {
        var clampedSeconds = Math.Clamp(seconds, 0, DurationSeconds);
        var byteOffset = (int)(clampedSeconds * _sampleRate * blockAlign);
        return Math.Clamp(byteOffset - (byteOffset % blockAlign), 0, _pcmBytes.Length);
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != 0)
            throw new InvalidOperationException($"waveOut error: {result}");
    }

    [LibraryImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static partial int WaveOutOpen(out IntPtr hWaveOut, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static partial int WaveOutPrepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static partial int WaveOutWrite(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static partial int WaveOutReset(IntPtr hWaveOut);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static partial int WaveOutUnprepareHeader(IntPtr hWaveOut, IntPtr header, int headerSize);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static partial int WaveOutClose(IntPtr hWaveOut);

    [LibraryImport("winmm.dll", EntryPoint = "waveOutGetPosition")]
    private static partial int WaveOutGetPosition(IntPtr hWaveOut, ref MmTime mmTime, int size);

    private const uint TIME_SAMPLES = 0x0002;
    private const uint TIME_BYTES = 0x0004;

    // MMTIME. wType 뒤에 오는 union 은 smpte 구조체가 가장 커서 8바이트라
    // 전체 12바이트다. 우리는 union 의 첫 4바이트(sample/cb)만 읽는다.
    [StructLayout(LayoutKind.Sequential)]
    private struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint UnionTail;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }
}

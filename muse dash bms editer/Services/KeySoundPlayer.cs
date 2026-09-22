using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace bms_editer.Services;

// 키음(WAV/OGG) 다중 믹싱 재생기.
//
// 1. PCM 사전 디코딩 및 메모리 캐싱으로 디스크 I/O 없이 즉시 발음.
// 2. 여러 키음(화음 또는 빠른 연속타)이 동시에 들어와도 앞 소리가 잘리지 않고
//    합산(Clamping Mix)되어 함께 울리는 폴리포닉(다중 채널) 믹싱 지원.
public sealed unsafe partial class KeySoundPlayer : IDisposable
{
    private sealed class Voice
    {
        public short[] Samples { get; }
        public int Position { get; set; }

        public Voice(short[] samples)
        {
            Samples = samples;
            Position = 0;
        }
    }

    private readonly ConcurrentDictionary<string, PcmAudioData?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Voice> _activeVoices = new();
    private readonly object _lock = new();

    private const int BufferFrames = 1764; // 40ms at 44100Hz
    private const int BufferCount = 3;
    private const int SampleRate = 44100;
    private const short Channels = 2;
    private const short BitsPerSample = 16;
    private const int BlockAlign = Channels * (BitsPerSample / 8);

    private IntPtr _waveOut = IntPtr.Zero;
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private readonly IntPtr[] _headerPtrs = new IntPtr[BufferCount];
    private Thread? _playbackThread;
    private readonly AutoResetEvent _wakeEvent = new(false);
    private volatile bool _isDisposed;

    public KeySoundPlayer()
    {
        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(BufferFrames * BlockAlign);
            _headerPtrs[i] = Marshal.AllocHGlobal(sizeof(WaveHeader));
            var hdr = (WaveHeader*)_headerPtrs[i];
            hdr->Data = _buffers[i];
            hdr->BufferLength = BufferFrames * BlockAlign;
            hdr->BytesRecorded = 0;
            hdr->User = IntPtr.Zero;
            hdr->Flags = WHDR_DONE;
            hdr->Loops = 0;
            hdr->Next = IntPtr.Zero;
            hdr->Reserved = IntPtr.Zero;
        }

        StartAudioWorker();
    }

    public void Preload(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        _cache.GetOrAdd(filePath, WavDecoder.Decode);
    }

    public void PreloadAsync(IEnumerable<string> filePaths)
    {
        Task.Run(() =>
        {
            foreach (var path in filePaths)
            {
                if (_isDisposed) break;
                Preload(path);
            }
        });
    }

    public void Play(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || _isDisposed)
            return;

        var pcm = _cache.GetOrAdd(filePath, WavDecoder.Decode);
        if (pcm is null || pcm.Samples.Length == 0)
        {
            // 디코딩 실패 시 OS 기본 PlaySound 로 시도
            PlaySoundFallback(filePath);
            return;
        }

        lock (_lock)
        {
            _activeVoices.Add(new Voice(pcm.Samples));
        }

        _wakeEvent.Set();
    }

    private static void PlaySoundFallback(string filePath)
    {
        try
        {
            PlaySound(filePath, IntPtr.Zero, 0x0001 | 0x0002); // SND_ASYNC | SND_NODEFAULT
        }
        catch
        {
            // 무시
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            _activeVoices.Clear();
        }
    }

    private void StartAudioWorker()
    {
        _playbackThread = new Thread(AudioLoop)
        {
            IsBackground = true,
            Name = "KeySoundPlayer_Mixer"
        };
        _playbackThread.Start();
    }

    private void AudioLoop()
    {
        var shortBuffer = new short[BufferFrames * Channels];
        var byteBuffer = new byte[BufferFrames * BlockAlign];
        var currentBufferIndex = 0;
        var silentBuffersCount = 0;

        while (!_isDisposed)
        {
            bool hasVoices;
            lock (_lock)
            {
                hasVoices = _activeVoices.Count > 0;
            }

            if (!hasVoices)
            {
                if (silentBuffersCount >= BufferCount)
                {
                    // 활성 보이스가 없고 잔여 버퍼도 모두 재생 완료된 경우 대기
                    CloseWaveOut();
                    _wakeEvent.WaitOne(500);
                    continue;
                }
            }

            if (!EnsureWaveOutOpen())
            {
                Thread.Sleep(30);
                continue;
            }

            var hdr = (WaveHeader*)_headerPtrs[currentBufferIndex];

            // 현재 버퍼가 재생 완료될 때까지 대기
            if ((hdr->Flags & WHDR_DONE) == 0 && (hdr->Flags & WHDR_PREPARED) != 0)
            {
                Thread.Sleep(3);
                continue;
            }

            // 믹싱 버퍼 초기화
            Array.Clear(shortBuffer, 0, shortBuffer.Length);

            lock (_lock)
            {
                for (var v = _activeVoices.Count - 1; v >= 0; v--)
                {
                    var voice = _activeVoices[v];
                    var remainingSamples = voice.Samples.Length - voice.Position;
                    var samplesToCopy = Math.Min(remainingSamples, shortBuffer.Length);

                    for (var i = 0; i < samplesToCopy; i++)
                    {
                        var mixed = shortBuffer[i] + voice.Samples[voice.Position + i];
                        shortBuffer[i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    }

                    voice.Position += samplesToCopy;
                    if (voice.Position >= voice.Samples.Length)
                    {
                        _activeVoices.RemoveAt(v);
                    }
                }

                hasVoices = _activeVoices.Count > 0;
            }

            if (hasVoices)
            {
                silentBuffersCount = 0;
            }
            else
            {
                silentBuffersCount++;
            }

            // short[] -> byte[] 변환 후 네이티브 버퍼에 복사
            Buffer.BlockCopy(shortBuffer, 0, byteBuffer, 0, byteBuffer.Length);
            Marshal.Copy(byteBuffer, 0, _buffers[currentBufferIndex], byteBuffer.Length);

            // waveOutWrite
            if ((hdr->Flags & WHDR_PREPARED) != 0)
            {
                WaveOutUnprepareHeader(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));
            }

            hdr->Data = _buffers[currentBufferIndex];
            hdr->BufferLength = byteBuffer.Length;
            hdr->BytesRecorded = 0;
            hdr->Flags = 0;

            WaveOutPrepareHeader(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));
            WaveOutWrite(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));

            currentBufferIndex = (currentBufferIndex + 1) % BufferCount;
        }

        CloseWaveOut();
    }

    private bool EnsureWaveOutOpen()
    {
        if (_waveOut != IntPtr.Zero)
            return true;

        try
        {
            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = Channels,
                SamplesPerSec = SampleRate,
                BitsPerSample = BitsPerSample,
                BlockAlign = (short)BlockAlign,
                AvgBytesPerSec = SampleRate * BlockAlign,
                Size = 0,
            };

            var res = WaveOutOpen(out _waveOut, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0);
            if (res != 0 || _waveOut == IntPtr.Zero)
                return false;

            for (var i = 0; i < BufferCount; i++)
            {
                var hdr = (WaveHeader*)_headerPtrs[i];
                hdr->Flags = WHDR_DONE;
            }

            return true;
        }
        catch
        {
            _waveOut = IntPtr.Zero;
            return false;
        }
    }

    private void CloseWaveOut()
    {
        if (_waveOut == IntPtr.Zero)
            return;

        try
        {
            WaveOutReset(_waveOut);

            for (var i = 0; i < BufferCount; i++)
            {
                var hdr = (WaveHeader*)_headerPtrs[i];
                if ((hdr->Flags & WHDR_PREPARED) != 0)
                {
                    WaveOutUnprepareHeader(_waveOut, _headerPtrs[i], sizeof(WaveHeader));
                }
            }

            WaveOutClose(_waveOut);
        }
        catch
        {
            // 무시
        }
        finally
        {
            _waveOut = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _wakeEvent.Set();

        try
        {
            _playbackThread?.Join(300);
        }
        catch
        {
            // 무시
        }

        CloseWaveOut();

        for (var i = 0; i < BufferCount; i++)
        {
            if (_buffers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffers[i]);
                _buffers[i] = IntPtr.Zero;
            }

            if (_headerPtrs[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headerPtrs[i]);
                _headerPtrs[i] = IntPtr.Zero;
            }
        }

        _wakeEvent.Dispose();
        _cache.Clear();
    }

    private const int WHDR_DONE = 0x00000001;
    private const int WHDR_PREPARED = 0x00000002;

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

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PlaySound(string pszSound, IntPtr hmod, int fdwSound);
}

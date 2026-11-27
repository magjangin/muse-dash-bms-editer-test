using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // Lazy 로 감싼다. 미리 읽기와 재생이 같은 파일을 동시에 집어도 디코딩은 한 번만 돈다.
    private readonly ConcurrentDictionary<string, Lazy<PcmAudioData?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    // 지금 백그라운드에서 읽는 중인 재생 요청. 같은 파일이 연달아 와도 한 번만 울린다.
    private readonly ConcurrentDictionary<string, byte> _pendingPlays = new(StringComparer.OrdinalIgnoreCase);

    // 파일이 없다고 확인한 시각 + 다시 볼 때까지의 여유. (값은 Environment.TickCount64 기준)
    private readonly ConcurrentDictionary<string, long> _missingUntil = new(StringComparer.OrdinalIgnoreCase);

    // 없는 파일을 이만큼 지나서 다시 확인한다. 나중에 폴더를 넣어도 앱을 다시 켤 필요가 없고(U-13),
    // 키음 폴더를 빠뜨린 차트에서 노트마다 디스크를 두드리지도 않는다.
    private const long MissingRetryMilliseconds = 2000;
    private readonly List<Voice> _activeVoices = new();
    private readonly object _lock = new();

    private const int MaxActiveVoices = 64;

    // 약 23ms. 버퍼 세 개가 앞서 나가 있으므로 이 값이 곧 키음이 늦게 들리는 양이다.
    // 예전에는 40ms 라 노트보다 최대 120ms 늦게 들렸다.
    private const int BufferFrames = 1024;
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
    private int _disposed;
    private int _cacheGeneration;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    // 256 이 원음. 믹서 스레드가 읽으므로 한 번에 읽히는 정수로 둔다.
    private volatile int _gain = 256;

    // 0.0 ~ 1.0
    public double Volume
    {
        get => _gain / 256.0;
        set => _gain = (int)Math.Round(Math.Clamp(value, 0.0, 1.0) * 256);
    }

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
        GetOrLoad(filePath);
    }

    // 디코딩 결과를 경로별로 기억한다. 단 **파일이 없어서** 못 읽은 것은 기억하지 않는다.
    //
    // 예전에는 "파일 없음"도 null 로 기억해서, 차트를 연 뒤(또는 건반 BMS 가져오기 뒤)
    // 씬 WAV 폴더를 넣어도 앱을 다시 켜기 전까지 그 키음이 영영 안 들렸다(usability_review U-13).
    //
    // 파일은 있는데 디코딩이 실패한 것도 **영구히** 기억하지 않는다(v0.1.8). 실패의 흔한 원인이
    // "다른 프로그램이 쥐고 있음"(DAW 가 다시 내보내는 중, OneDrive·백신이 잠깐 잠금)이라,
    // 예전에는 그 순간 한 번 실패하면 차트를 다시 열 때까지 그 키음이 안 들렸다.
    // 대신 없는 파일과 같이 잠깐(MissingRetryMilliseconds) 쉬었다가 다시 읽는다.
    public PcmAudioData? GetOrLoad(string filePath)
    {
        if (_cache.TryGetValue(filePath, out var cached))
            return cached.Value;

        if (!File.Exists(filePath))
            return null;

        var lazy = _cache.GetOrAdd(filePath, static path => new Lazy<PcmAudioData?>(() => WavDecoder.Decode(path)));
        var value = lazy.Value;
        if (value is null)
        {
            // 같은 Lazy 일 때만 뺀다. 그사이 ClearCache 뒤 새로 들어온 항목을 지우면 안 된다.
            _cache.TryRemove(KeyValuePair.Create(filePath, lazy));
            _missingUntil[filePath] = Environment.TickCount64 + MissingRetryMilliseconds;
        }

        return value;
    }

    // 다른 차트를 열 때 이전 곡의 키음 PCM 이 메모리에 남지 않도록 비운다.
    public void ClearCache()
    {
        Interlocked.Increment(ref _cacheGeneration);
        StopAll();
        _cache.Clear();
        _missingUntil.Clear();
    }

    public void PreloadAsync(IEnumerable<string> filePaths)
    {
        var generation = Volatile.Read(ref _cacheGeneration);
        var snapshot = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Task.Run(() =>
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };

            // 키음이 수백 개인 차트가 흔하다. 하나씩 읽으면 재생을 눌렀을 때까지 다 못 읽는다.
            Parallel.ForEach(snapshot, options, (path, state) =>
            {
                if (IsDisposed || Volatile.Read(ref _cacheGeneration) != generation)
                {
                    state.Stop();
                    return;
                }

                Preload(path);
            });
        });
    }

    // UI 스레드에서 부른다. **여기서 디스크를 건드리지 않는다.**
    //
    // 예전에는 처음 듣는 키음이면 재생 타이머 안에서 파일을 읽고 디코딩했다. 그동안 커서가 멈췄다.
    // 파일이 없는 키음은 노트가 지나갈 때마다 File.Exists 와 PlaySound 로 디스크를 다시 봤다.
    public void Play(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || IsDisposed)
            return;

        if (_cache.TryGetValue(filePath, out var entry) && entry.IsValueCreated)
        {
            PlayDecoded(filePath, entry.Value);
            return;
        }

        if (_missingUntil.TryGetValue(filePath, out var retryAt) && Environment.TickCount64 < retryAt)
            return;

        var generation = Volatile.Read(ref _cacheGeneration);
        if (_pendingPlays.TryAdd(filePath, 0))
            Task.Run(() => LoadAndPlay(filePath, generation));
    }

    private void LoadAndPlay(string filePath, int generation)
    {
        try
        {
            if (IsDisposed || Volatile.Read(ref _cacheGeneration) != generation)
                return;

            var pcm = GetOrLoad(filePath);
            if (IsDisposed || Volatile.Read(ref _cacheGeneration) != generation)
                return;

            if (pcm is null && !File.Exists(filePath))
            {
                _missingUntil[filePath] = Environment.TickCount64 + MissingRetryMilliseconds;
                return;
            }

            PlayDecoded(filePath, pcm);
        }
        finally
        {
            _pendingPlays.TryRemove(filePath, out _);
        }
    }

    private void PlayDecoded(string filePath, PcmAudioData? pcm)
    {
        if (pcm is null || pcm.Samples.Length == 0)
        {
            // 디코딩 실패 시 OS 기본 PlaySound 로 시도. 드문 경우지만 파일을 건드리므로 UI 스레드 밖에서.
            Task.Run(() => PlaySoundFallback(filePath));
            return;
        }

        if (IsDisposed)
            return;

        lock (_lock)
        {
            if (_activeVoices.Count >= MaxActiveVoices)
                _activeVoices.RemoveAt(0);
            _activeVoices.Add(new Voice(pcm.Samples));
        }

        try
        {
            _wakeEvent.Set();
        }
        catch (ObjectDisposedException)
        {
            // 뒤에서 읽던 키음이 창을 닫는 순간과 겹친 경우. 울릴 곳이 없으니 그냥 끝낸다.
        }
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
            Name = "KeySoundPlayer_Mixer",

            // 믹서가 밀리면 소리가 끊긴다. 일은 짧으니 우선순위를 올려도 다른 쪽에 부담이 없다.
            Priority = ThreadPriority.AboveNormal,
        };
        _playbackThread.Start();
    }

    private void AudioLoop()
    {
        var shortBuffer = new short[BufferFrames * Channels];
        var currentBufferIndex = 0;
        var silentBuffersCount = 0;

        while (!IsDisposed)
        {
            // 백그라운드 스레드의 예외는 프로세스를 그대로 끝낸다. 소리만 포기하고 앱은 살린다.
            try
            {
                MixOneBuffer(shortBuffer, ref currentBufferIndex, ref silentBuffersCount);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"키음 믹서 오류: {ex}");
                StopAll();
                CloseWaveOut();
                Thread.Sleep(100);
            }
        }

        CloseWaveOut();
    }

    private void MixOneBuffer(short[] shortBuffer, ref int currentBufferIndex, ref int silentBuffersCount)
    {
        bool hasVoices;
        lock (_lock)
        {
            hasVoices = _activeVoices.Count > 0;
        }

        if (!hasVoices && silentBuffersCount >= BufferCount && AreAllHeadersDone())
        {
            // 활성 보이스가 없고 잔여 버퍼도 모두 재생 완료된 경우 대기
            CloseWaveOut();
            _wakeEvent.WaitOne(500);
            return;
        }

        if (!EnsureWaveOutOpen())
        {
            // 장치를 못 열면 보이스가 소비되지 않고 쌓이다가, 장치가 돌아오는 순간 한꺼번에 터진다.
            StopAll();
            Thread.Sleep(30);
            return;
        }

        var hdr = (WaveHeader*)_headerPtrs[currentBufferIndex];

        // 현재 버퍼가 재생 완료될 때까지 대기 (드라이버 스레드가 갱신하므로 Volatile 읽기)
        var flags = Volatile.Read(ref hdr->Flags);
        if ((flags & WHDR_DONE) == 0 && (flags & WHDR_PREPARED) != 0)
        {
            Thread.Sleep(3);
            return;
        }

        // 믹싱 버퍼 초기화
        Array.Clear(shortBuffer, 0, shortBuffer.Length);

        var gain = _gain;

        bool mixedAnyVoice;
        lock (_lock)
        {
            mixedAnyVoice = _activeVoices.Count > 0;
            for (var v = _activeVoices.Count - 1; v >= 0; v--)
            {
                var voice = _activeVoices[v];
                var remainingSamples = voice.Samples.Length - voice.Position;
                var samplesToCopy = Math.Min(remainingSamples, shortBuffer.Length);

                if (gain == 0)
                {
                    // 음소거 상태: 샘플 연산 건너뛰고 위치만 전진
                    voice.Position += samplesToCopy;
                }
                else if (gain == 256)
                {
                    // 기본 볼륨(100%): 곱셈 및 비트 쉬프트 생략 Fast Path
                    for (var i = 0; i < samplesToCopy; i++)
                    {
                        var mixed = shortBuffer[i] + voice.Samples[voice.Position + i];
                        shortBuffer[i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    }
                    voice.Position += samplesToCopy;
                }
                else
                {
                    // 볼륨 스케일링
                    for (var i = 0; i < samplesToCopy; i++)
                    {
                        var mixed = shortBuffer[i] + ((voice.Samples[voice.Position + i] * gain) >> 8);
                        shortBuffer[i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    }
                    voice.Position += samplesToCopy;
                }

                if (voice.Position >= voice.Samples.Length)
                {
                    _activeVoices.RemoveAt(v);
                }
            }
        }

        silentBuffersCount = mixedAnyVoice ? 0 : silentBuffersCount + 1;

        // short[] 를 중간 byte[] 없이 네이티브 버퍼에 바로 복사
        Marshal.Copy(shortBuffer, 0, _buffers[currentBufferIndex], shortBuffer.Length);

        // waveOutWrite
        if ((Volatile.Read(ref hdr->Flags) & WHDR_PREPARED) != 0)
        {
            WaveOutUnprepareHeader(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));
        }

        hdr->Data = _buffers[currentBufferIndex];
        hdr->BufferLength = BufferFrames * BlockAlign;
        hdr->BytesRecorded = 0;
        hdr->Flags = 0;

        var prepareRes = WaveOutPrepareHeader(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader));
        var writeRes = prepareRes == 0
            ? WaveOutWrite(_waveOut, _headerPtrs[currentBufferIndex], sizeof(WaveHeader))
            : prepareRes;

        if (writeRes != 0)
        {
            // 장치 분리·독점 등으로 쓰기가 실패하면 WHDR_DONE 이 영영 오지 않으므로 장치를 닫고 다음 루프에서 재오픈한다.
            StopAll();
            CloseWaveOut();
            Thread.Sleep(50);
            return;
        }

        currentBufferIndex = (currentBufferIndex + 1) % BufferCount;
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

    private bool AreAllHeadersDone()
    {
        for (var i = 0; i < BufferCount; i++)
        {
            var hdr = (WaveHeader*)_headerPtrs[i];
            var flags = Volatile.Read(ref hdr->Flags);
            if ((flags & WHDR_PREPARED) != 0 && (flags & WHDR_DONE) == 0)
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _wakeEvent.Set();

        try
        {
            _playbackThread?.Join();
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

using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using bms_editer.Models;

namespace bms_editer.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void Play()
    {
        PlayFrom(IsPlaybackCursorVisible ? PlaybackPositionSeconds : 0);
    }

    [RelayCommand]
    private void Stop()
    {
        StopPlayback(resetCursor: false);
    }

    // 스페이스바 한 키로 재생/정지를 오간다. 편집하면서 가장 자주 하는 동작이다.
    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying)
            StopPlayback(resetCursor: false);
        else
            Play();
    }

    // 드래그하는 동안에는 커서만 옮긴다.
    //
    // 예전에는 마우스가 움직일 때마다 PlayFrom 을 불렀다. 초당 100번 넘게 들어오는
    // 이벤트마다 오디오 장치를 닫았다 다시 열고(딸깍거림) 노트 전체를 다시 정렬했다.
    public void ScrubPreview(double ratio)
    {
        if (OggDurationSeconds <= 0)
            return;

        StopPlayback(resetCursor: false);
        PlaybackPositionSeconds = Math.Clamp(ratio, 0, 1) * OggDurationSeconds;
        IsPlaybackCursorVisible = true;
    }

    // 버튼을 뗄 때 한 번만 실제로 재생을 옮긴다.
    public void ScrubCommit(double ratio)
    {
        if (_audioPlayer is null || OggDurationSeconds <= 0)
            return;

        PlayFrom(Math.Clamp(ratio, 0, 1) * OggDurationSeconds);
    }

    public void StopPlaybackAtCurrentPosition() => StopPlayback(resetCursor: false);

    // 재생을 시작한다. 오디오 장치를 열 수 없으면(장치 없음·다른 앱이 독점) false.
    //
    // 예전에는 OggAudioPlayer 가 던진 예외를 아무도 받지 않아서, 재생 버튼 한 번에
    // 앱이 그대로 죽고 편집 중이던 내용이 전부 사라졌다.
    private bool PlayFrom(double seconds)
    {
        if (_audioPlayer is null)
            return false;

        var startSeconds = Math.Clamp(seconds, 0, OggDurationSeconds);
        _playbackNotes = GetSortedNotes();

        try
        {
            _audioPlayer.Play(startSeconds, PlaybackSpeed);
        }
        catch (Exception ex)
        {
            StopPlayback(resetCursor: false);
            LastErrorMessage = $"재생을 시작하지 못했습니다.\n\n{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"재생 실패: {ex.Message}");
            return false;
        }

        _playbackStartSeconds = startSeconds;
        _playbackStartedAt = DateTimeOffset.UtcNow;
        PlaybackPositionSeconds = startSeconds;
        _lastPlaybackPositionSeconds = startSeconds;
        IsPlaybackCursorVisible = true;
        IsPlaying = true;
        _playbackTimer.Start();
        LastErrorMessage = null;
        return true;
    }

    private void UpdatePlaybackPosition()
    {
        if (_audioPlayer is null)
            return;

        // 장치가 실제로 재생한 위치를 기준으로 삼는다. 벽시계를 쓰면 출력 지연만큼
        // 커서가 처음부터 앞서 나가고, 장치 클럭과도 시간이 갈수록 벌어져서
        // 화면은 맞아 보이는데 소리와는 안 맞는 상태가 된다.
        // 장치가 위치 조회를 지원하지 않을 때만 예전처럼 벽시계로 되돌아간다.
        var playedSeconds = _audioPlayer.GetPlayedSeconds();
        var currentSec = playedSeconds is { } played
            ? _playbackStartSeconds + played
            : _playbackStartSeconds + (DateTimeOffset.UtcNow - _playbackStartedAt).TotalSeconds * PlaybackSpeed;

        PlaybackPositionSeconds = currentSec;

        PlayNotesInTimeRange(_lastPlaybackPositionSeconds + AudioOffsetSeconds, currentSec + AudioOffsetSeconds);
        _lastPlaybackPositionSeconds = currentSec;

        if (PlaybackPositionSeconds >= OggDurationSeconds)
            StopPlayback(resetCursor: false);
    }

    private void PlayNotesInTimeRange(double start, double end)
    {
        if (!IsKeySoundEnabled || Bpm <= 0 || _playbackNotes.Length == 0) return;

        // 시각 계산은 Timeline 이 맡는다. 예전에는 여기서 240/BPM 을 직접 써서,
        // BPM 이 바뀌거나 4/4가 아닌 마디가 있는 차트는 키음이 엉뚱한 때에 울렸다.
        var timeline = Timeline;

        // 시작 지점 이상인 첫 노트를 이진 탐색으로 찾는다.
        var low = 0;
        var high = _playbackNotes.Length - 1;
        var startIndex = _playbackNotes.Length;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var noteSec = timeline.SecondsAt(_playbackNotes[mid].Measure + _playbackNotes[mid].Position);

            if (noteSec >= start)
            {
                startIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        for (var i = startIndex; i < _playbackNotes.Length; i++)
        {
            var note = _playbackNotes[i];
            if (timeline.SecondsAt(note.Measure + note.Position) >= end)
                break;

            PlayWavSound(note.WavKey);
        }
    }

    private void StopPlayback(bool resetCursor)
    {
        _playbackTimer.Stop();
        _audioPlayer?.Stop();
        _playbackNotes = Array.Empty<BmsNote>();
        IsPlaying = false;

        if (resetCursor)
        {
            PlaybackPositionSeconds = 0;
            IsPlaybackCursorVisible = false;
        }
        else
        {
            PlaybackPositionSeconds = Math.Clamp(PlaybackPositionSeconds, 0, OggDurationSeconds);
        }
    }

    public void PlayWavSound(string key)
    {
        if (Chart.WavTable.TryGetValue(key, out var path))
            _keySoundPlayer.Play(path);
    }

    [RelayCommand]
    private void TestPlayWav()
    {
        if (SelectedWavItem is not null)
        {
            PlayWavSound(SelectedWavItem.Key);
        }
    }

    [RelayCommand]
    private void ToggleKeySound()
    {
        IsKeySoundEnabled = !IsKeySoundEnabled;
    }
}

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
        if (_audioPlayer is null)
        {
            UserMessageRequested?.Invoke("재생할 음원이 없습니다. 먼저 OGG 를 여십시오.");
            return;
        }

        PlayFrom(IsPlaybackCursorVisible ? PlaybackPositionSeconds : 0);
    }

    // 사용자에게 알려야 할 일. 예전에는 재생에 실패해도 LastErrorMessage 에 담기만 하고
    // 띄우는 곳이 없어서, 스페이스를 눌러도 아무 반응이 없는 것처럼 보였다.
    public event Action<string>? UserMessageRequested;

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
        _ = GetSortedNotesWithSeconds();

        try
        {
            _audioPlayer.Play(startSeconds, PlaybackSpeed);
        }
        catch (Exception ex)
        {
            StopPlayback(resetCursor: false);
            LastErrorMessage = $"재생을 시작하지 못했습니다.\n\n{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"재생 실패: {ex.Message}");
            UserMessageRequested?.Invoke(LastErrorMessage);
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

        // 장치가 다 틀었으면 곡 끝이다. 위치 값은 드라이버에 따라 마지막 몇 샘플이 모자랄 수 있다.
        var finished = _audioPlayer.IsPlaybackFinished;
        if (finished)
            currentSec = Math.Max(currentSec, OggDurationSeconds);

        PlaybackPositionSeconds = currentSec;

        PlayNotesInTimeRange(_lastPlaybackPositionSeconds + AudioOffsetSeconds, currentSec + AudioOffsetSeconds);
        _lastPlaybackPositionSeconds = currentSec;

        if (finished || PlaybackPositionSeconds >= OggDurationSeconds)
            StopPlayback(resetCursor: false);
    }

    private void PlayNotesInTimeRange(double start, double end)
    {
        if (!IsKeySoundEnabled || Bpm <= 0)
            return;

        var (notes, noteSeconds) = GetSortedNotesWithSeconds();
        if (notes.Length == 0)
            return;

        // 시작 지점 이상인 첫 노트를 미리 계산해 둔 시각 배열에서 이진 탐색으로 찾는다.
        var low = 0;
        var high = noteSeconds.Length - 1;
        var startIndex = noteSeconds.Length;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (noteSeconds[mid] >= start)
            {
                startIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        for (var i = startIndex; i < notes.Length; i++)
        {
            if (noteSeconds[i] >= end)
                break;

            PlayWavSound(notes[i].WavKey);
        }
    }

    private void StopPlayback(bool resetCursor)
    {
        // 재생을 멈췄는데 긴 키음이 끝까지 남아 울리면 멈춘 것처럼 들리지 않는다.
        // 재생 중일 때만 끊는다. 키음 팔레트에서 미리 듣는 소리까지 끊으면 안 된다.
        if (IsPlaying)
            _keySoundPlayer.StopAll();

        _playbackTimer.Stop();
        _playbackSpeedDebounceTimer.Stop();
        _audioPlayer?.Stop();
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

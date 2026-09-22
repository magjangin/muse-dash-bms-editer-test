using System.Collections.Generic;
using bms_editer.Models;

namespace bms_editer.Services.Downmix;

// 건반형 BMS(4K/5K/7K)에서 읽어낸 노트 하나.
//
// 뮤즈 대시 차트를 읽는 BmsParser 와 따로 두는 이유:
// BmsParser 는 **원문 보존**이 목적이고, 다운믹서는 원본 게임이 노트로 읽는 것만 필요하다.
// 목적이 반대라서 섞지 않는다.
public sealed record KeyboardNote(
    // 원본 BMS 의 건반 채널(11~19, 16=스크래치).
    string Channel,
    int Measure,
    double Position,
    KeyboardNoteKind Kind,

    // 원본에 적혀 있던 키음 번호. 게임에 따라 **이 값 자체가** 롱노트 시작/끝을 뜻한다
    // (스타트레일 02/03 · 건볼트 02/19). 그 판정은 게임 프로파일이 한다.
    string WavKey = "")
{
    public double Time => Measure + Position;
}

public enum KeyboardNoteKind
{
    Normal,
    LongStart,
    LongEnd,
}

public sealed record KeyboardChart(
    IReadOnlyList<KeyboardNote> Notes,

    // 이 차트에 실제로 쓰인 건반 채널. 왼쪽에서 오른쪽 순서로 담는다.
    IReadOnlyList<string> Channels,
    string Title,
    double Bpm,
    int MeasureCount,

    // 키음 번호 -> `#WAV` 에 적힌 글자. 파일명으로 종류를 가리는 게임(스타게이저)이 쓴다.
    IReadOnlyDictionary<string, string> WavTexts,

    // 이 차트가 어느 게임 것으로 보이는지(경로로 추정). 못 찾으면 null.
    GameProfile? SourceProfile,

    // 읽다가 건너뛴 것들. 사용자에게 왜 노트 수가 다른지 설명하는 데 쓴다.
    IReadOnlyList<string> Warnings);

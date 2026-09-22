using System;
using System.Collections.Generic;
using System.Linq;

namespace bms_editer.Models;

// 게임 하나가 "어떤 노트가 홀드의 시작이고 끝인가"를 정하는 규칙.
//
// 규칙은 게임마다 다르고 추측으로 맞출 수 있는 것이 아니다. 같은 "홀드 시작/끝"이라도
// 한 게임은 끝 노트를 한 번만 쓰고, 다른 게임은 여러 시작이 끝 하나를 같이 쓰고,
// 또 다른 게임은 대기 칸이 하나뿐이라 새 시작이 앞 시작을 버린다.
// 그래서 각 값은 게임 모드의 파서 소스를 읽고 그대로 옮겨 적는다. 값을 고치기 전에
// Source 에 적힌 코드를 먼저 확인할 것. (docs/specifications/game_profiles.md)
public sealed class GameProfile
{
    // 콤보박스의 "고르지 않음" 칸. null 을 목록에 넣으면 선택 표시가 흔들려서 빈 프로파일을 둔다.
    public static GameProfile None { get; } = new() { Id = "", DisplayName = "(프로파일 없음)" };

    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ProfileVerification Verification { get; set; } = ProfileVerification.Assumed;

    // 규칙을 어디서 옮겨 적었는지. 사람이 확인하러 갈 자리다.
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";

    // 차트 경로의 폴더 이름에 이 글자가 들어 있으면 이 게임으로 추정한다.
    public List<string> PathHints { get; set; } = new();

    public List<HoldRule> HoldRules { get; set; } = new();

    // 파일명 앞자리 UID 로 노트 종류를 정하는 게임만 쓴다.
    public UidTable? UidTable { get; set; }

    // 이 게임의 노트가 화면 어디에 놓이는가. 다운믹서가 두 레인으로 접을 때만 쓴다.
    public DownmixLayout? Downmix { get; set; }

    public bool IsNone => string.IsNullOrEmpty(Id);

    public override string ToString() => DisplayName;
}

// 건반형 채보를 뮤즈 대시 두 레인(지상·공중)으로 접을 때 쓰는 그 게임의 화면 배치.
//
// 채널 번호만 보고 "왼쪽 절반은 지상"으로 가르면 게임의 의미와 어긋난다. 예를 들어 건볼트의
// `14` 는 번호로는 가운데지만 게임에서는 **오른손 안쪽 레인**이다. 그래서 값은 추측하지 않고
// 게임 모드 파서에서 옮겨 적는다(`Source`). 옮겨 적을 근거가 없으면 이 칸을 통째로 비워 두고,
// 다운믹서는 번호 순서로 가르는 기본 규칙을 쓴다.
//
// 여기 적힌 채널이 곧 **그 게임이 노트로 읽는 채널**이다. 목록에 없는 채널은 게임도 무시하므로
// 가져올 때 같이 버린다(예: 건볼트는 `13` 을 노트로 읽지 않는다).
public sealed class DownmixLayout
{
    // 어느 코드에서 옮겨 적었는지. 사람이 확인하러 갈 자리다.
    public string Source { get; set; } = "";

    public List<string> Ground { get; set; } = new();
    public List<string> Air { get; set; } = new();

    // 한쪽으로 몰기 곤란한 레인. 나올 때마다 지상·공중을 번갈아 보낸다.
    public List<string> Alternating { get; set; } = new();

    public bool IsEmpty => Ground.Count == 0 && Air.Count == 0 && Alternating.Count == 0;

    public IEnumerable<string> Channels => Ground.Concat(Air).Concat(Alternating);

    public bool Reads(string channel) =>
        Channels.Contains(channel, StringComparer.OrdinalIgnoreCase);
}

public enum ProfileVerification
{
    // 짝 맞추는 규칙을 게임 코드로 확인하지 못했다. 모양만 흉내 낸 것이다.
    Assumed,

    // 게임 모드의 파서 소스로 규칙을 확인했다. 실제로 채보를 만들어 플레이해 보지는 않았다.
    Source,

    // 실제로 채보를 만들어 게임에서 확인했다.
    Playtested,
}

public sealed class HoldRule
{
    public string Kind { get; set; } = "Hold";

    // 이 규칙이 보는 채널. 비어 있으면 모든 편집 채널.
    public List<string> Channels { get; set; } = new();

    public HoldRoleRule Role { get; set; } = new();
    public HoldPairingPolicy Policy { get; set; } = HoldPairingPolicy.NearestUnconsumedTail;
    public HoldScope Scope { get; set; } = HoldScope.Channel;

    // 검사기 메시지에 붙는 "게임에서는 어떻게 되는가". 게임 로그를 읽으러 가지 않게 하려는 문장이다.
    public string OrphanHeadOutcome { get; set; } = "";
    public string OrphanTailOutcome { get; set; } = "";
}

// 시작과 끝을 고르는 방식. 전부 실제 게임 모드 코드에 있던 것이다.
public enum HoldPairingPolicy
{
    // 시작마다, 그 뒤에 있는 아직 안 쓴 끝 중 가장 가까운 것 하나. 끝은 한 번만 쓴다.
    NearestUnconsumedTail,

    // 시작마다, 그 뒤에 있는 끝 중 가장 가까운 것. 끝을 소비하지 않아 여러 시작이 같은 끝을 쓸 수 있다.
    NearestTailShared,

    // 시작을 줄 세우고 끝이 올 때마다 가장 오래 기다린 시작과 짝짓는다.
    // 같은 자리면 시작이 먼저이고, 끝이 시작보다 앞서지 않으면 둘 다 버린다.
    FifoQueue,

    // 대기 칸이 하나뿐이다. 끝이 오기 전에 시작이 또 오면 앞 시작을 버리고 바꿔 끼운다.
    ReplacePending,

    // 파일명의 시작/끝 표식을 보지 않고, 같은 종류가 나온 순서로 홀수=시작, 짝수=끝.
    Alternate,
}

public enum HoldScope
{
    // 채널(레인)마다 따로 짝짓는다.
    Channel,

    // 채널을 가리지 않고 한 줄로 짝짓는다. 게임이 여러 채널을 레인 하나로 모으는 경우다.
    AllChannels,
}

public enum HoldRoleSource
{
    // 슬롯 코드 값 자체로 시작/끝을 정한다.
    KeyValue,

    // #WAV 파일명에 든 키워드로 시작/끝을 정한다.
    FileNameKeyword,

    // #WAV 파일명 앞자리 UID 로 종류만 정하고, 시작/끝은 순서로 정한다(Alternate).
    FileNameUid,

    // #WAV 파일명(끝 표식을 뗀 이름)으로 종류만 정하고, 시작/끝은 순서로 정한다(Alternate).
    FileNameBase,
}

public sealed class HoldRoleRule
{
    public HoldRoleSource Type { get; set; }

    // KeyValue
    public List<string> HeadKeys { get; set; } = new();
    public List<string> TailKeys { get; set; } = new();

    // FileNameKeyword. 모두 부분 일치다.
    public string RequiredKeyword { get; set; } = "";
    public List<string> HeadKeywords { get; set; } = new();
    public List<string> TailKeywords { get; set; } = new();

    // 한 파일명이 시작·끝 키워드를 모두 담으면 무엇으로 볼지. 게임마다 검사 순서가 다르다.
    public bool TailFirst { get; set; }

    // FileNameBase
    public List<string> BaseNames { get; set; } = new();
    public List<string> StripSuffixes { get; set; } = new();

    // Alternate 전용: 파일명에 적힌 "시작/끝" 표식.
    // 게임은 이 표식을 읽지 않고 순서로만 짝짓는다. 하지만 표식과 순서가 처음 어긋난 자리가
    // 곧 수열이 뒤집힌 원인이라, 검사기가 그 자리를 지목하는 데 쓴다.
    public List<string> HeadLabels { get; set; } = new();
    public List<string> TailLabels { get; set; } = new();
}

// 파일명 앞자리 UID 를 읽어 노트 종류를 정하는 표. 게임 파서의 분기 순서를 그대로 옮긴다.
public sealed class UidTable
{
    public string Pattern { get; set; } = "^([0-9]{6})";

    // 앞자리 코드. 여기에 걸리면 타입 자리보다 우선한다.
    public int PrefixLength { get; set; } = 4;
    public Dictionary<string, string> PrefixTypes { get; set; } = new();

    // 타입 자리. 0부터 센 위치와 길이로 적는다. "몇 번째 자리" 표현은 한 자리 단위인지
    // 두 자리 묶음 단위인지 헷갈리기 때문이다.
    public int TypeOffset { get; set; } = 2;
    public int TypeLength { get; set; } = 2;
    public Dictionary<string, string> TypeCodes { get; set; } = new();

    // 종류는 정하지 않지만 "UID 로 해석됐다"고 보는 코드. 이게 걸리면 이름 추정을 하지 않는다.
    public int WideOffset { get; set; } = 2;
    public int WideLength { get; set; } = 4;
    public List<string> ResolvedWideCodes { get; set; } = new();
    public List<string> ResolvedTypeCodes { get; set; } = new();

    // 무엇보다 먼저 보는 앞자리. 다른 규칙과 겹치는 코드를 여기서 가로챈다.
    public Dictionary<string, string> FinalPrefixes { get; set; } = new();

    // UID 해석과 무관하게 종류를 덮어쓰는 파일명 표식.
    public List<string> BlockingKeywords { get; set; } = new();
    public string BlockingType { get; set; } = "";

    // UID 로 종류가 정해지지 않았을 때만 쓰는 이름 추정. 순서대로 보고 처음 걸린 것으로 정한다.
    public List<UidNameFallback> NameFallbacks { get; set; } = new();
}

public sealed class UidNameFallback
{
    public List<string> Keywords { get; set; } = new();
    public string UidPrefix { get; set; } = "";
    public string UidTypeCode { get; set; } = "";
    public string Type { get; set; } = "";
}

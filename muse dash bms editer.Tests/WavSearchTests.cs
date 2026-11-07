using bms_editer.Models;
using Xunit;

namespace bms_editer.Tests;

// 키음 팔레트의 검색(여러 낱말, 초성)과 표시용 이름.
public sealed class WavSearchTests
{
    private static BmsWavItem Item(string key, string fileName) =>
        new() { Key = key, FilePath = @"C:\wav\" + fileName };

    [Theory]
    [InlineData("000201_하트 지상 스피드1_dt1.48.wav", "하트 지상 스피드1")]
    [InlineData("010107_1번 씬 보스 원거리1 액션 시작_dt0.wav", "1번 씬 보스 원거리1 액션 시작")]
    [InlineData("kick.wav", "kick")]
    [InlineData("0102.wav", "0102")]
    public void CleanName_StripsCodeDeltaAndExtension(string fileName, string expected)
        => Assert.Equal(expected, WavSearch.CleanName(fileName));

    [Theory]
    [InlineData("하트", true)]
    [InlineData("하트 지상", true)]
    [InlineData("지상 하트", true)]
    [InlineData("하트 공중", false)]
    [InlineData("ㅎㅌ", true)]
    [InlineData("ㅎㅌ ㅈㅅ", true)]
    [InlineData("하ㅌ", true)]
    [InlineData("ㅎㅅ", false)]
    [InlineData("000201", true)]
    [InlineData("DT1.48", true)]
    [InlineData("#001", false)]
    public void Matches_UsesAllWordsAndInitials(string query, bool expected)
        => Assert.Equal(expected, WavSearch.Matches(Item("001", "000201_하트 지상 스피드1_dt1.48.wav"), query));

    [Theory]
    // 속도는 UID 끝 두 자리에서 읽는다(파일명에 "스피드N" 이 없어도).
    [InlineData("010201_홀드 지상 끝 노트_dt1.47.wav", 1, "홀드", "지상", 1)]
    [InlineData("011003_일반 노트1 지상 노멀_dt1.48.wav", 1, "일반", "지상", 3)]
    [InlineData("011604_라이더 공중_dt1.48.wav", 1, "라이더", "공중", 1)]
    [InlineData("011605_라이더 공중_dt1.48.wav", 1, "라이더", "공중", 2)]
    [InlineData("000202_하트 지상 스피드2_dt1.48.wav", 0, "하트", "지상", 2)]
    [InlineData("011301_빅노트1 지상_dt1.48.wav", 1, "빅노트", "지상", 1)]
    [InlineData("010601_보스 발사체1 보스 액션 사용 지상_boss_dt0.8.wav", 1, "보스", "지상", 1)]
    // 보스 연출·씬 전환은 UID 에 속도가 없다.
    [InlineData("020101_2번 씬 보스 등장_dt0.wav", 2, "보스", null, null)]
    [InlineData("000403_3번 씬 전환_dt0.wav", 0, "전환", null, null)]
    // UID 에 속도가 없을 때만 파일명의 "스피드N" 을 본다.
    [InlineData("010107_보스 액션 스피드2_dt0.wav", 1, "보스", null, 2)]
    // 둘이 어긋나면 게임이 읽는 UID 를 따른다.
    [InlineData("000201_하트 지상 스피드2_dt1.48.wav", 0, "하트", "지상", 1)]
    public void Traits_ReadsSceneKindPositionAndSpeed(
        string fileName, int scene, string kind, string? position, int? speed)
        => Assert.Equal(new WavTraits(scene, kind, position, speed), WavSearch.Traits(fileName));

    [Fact]
    public void Traits_IsEmptyWithoutLeadingCode()
        => Assert.Equal(default, WavSearch.Traits("kick.wav"));

    [Theory]
    [InlineData("010201_홀드 지상 끝 노트_dt1.47.wav", "1번 씬 · 홀드 지상 끝 노트")]
    [InlineData("020101_2번 씬 보스 등장_dt0.wav", "2번 씬 보스 등장")]
    [InlineData("000201_하트 지상 스피드1_dt1.48.wav", "하트 지상 스피드1")]
    [InlineData("010601_보스 발사체1 보스 액션 사용 지상_boss_dt0.8.wav", "1번 씬 · 보스 발사체1 보스 액션 사용 지상")]
    [InlineData("kick.wav", "kick")]
    public void DisplayName_AddsSceneOnlyWhenNameLacksIt(string fileName, string expected)
        => Assert.Equal(expected, WavSearch.DisplayName(fileName));

    [Fact]
    public void Matches_FindsByKey()
        => Assert.True(WavSearch.Matches(Item("0A", "x.wav"), "0a"));

    [Fact]
    public void Matches_FindsByDisplayNameScenePrefix()
        => Assert.True(WavSearch.Matches(Item("01", "010201_홀드 지상 끝 노트_dt1.47.wav"), "1번 씬 홀드"));
}

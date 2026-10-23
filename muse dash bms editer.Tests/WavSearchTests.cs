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
    [InlineData("010201_홀드 지상 끝 노트_dt1.47.wav", 1, "홀드", "지상", null)]
    [InlineData("011604_라이더 공중_dt1.48.wav", 1, "라이더", "공중", null)]
    [InlineData("000201_하트 지상 스피드2_dt1.48.wav", 0, "하트", "지상", 2)]
    [InlineData("011301_빅노트1 지상_dt1.48.wav", 1, "빅노트", "지상", null)]
    [InlineData("010601_보스 발사체1 보스 액션 사용 지상_boss_dt0.8.wav", 1, "보스", "지상", null)]
    [InlineData("020101_2번 씬 보스 등장_dt0.wav", 2, "보스", null, null)]
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
}

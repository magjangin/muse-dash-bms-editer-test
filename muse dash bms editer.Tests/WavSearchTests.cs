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

    [Fact]
    public void Matches_FindsByKey()
        => Assert.True(WavSearch.Matches(Item("0A", "x.wav"), "0a"));
}

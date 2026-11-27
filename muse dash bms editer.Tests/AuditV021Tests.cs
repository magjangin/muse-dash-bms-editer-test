using System;
using System.Collections.Generic;
using bms_editer.Models;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// v0.2.1 심층 개선 및 최적화 회귀 테스트
public sealed class AuditV021Tests
{
    // ── ChartTimeline: HasBpmChanges=false Fast Path 검증 ───────────────────────

    [Theory]
    [InlineData(120.0, 0.0, 0.0)]
    [InlineData(120.0, 1.0, 2.0)]
    [InlineData(120.0, 2.5, 5.0)]
    [InlineData(180.0, 3.0, 4.0)]
    public void ChartTimeline_Uniform_FastPath_MatchesExpectedSeconds(double bpm, double measurePos, double expectedSeconds)
    {
        var timeline = ChartTimeline.Uniform(bpm);

        Assert.False(timeline.HasBpmChanges);
        Assert.False(timeline.HasMeasureLengthChanges);

        var seconds = timeline.SecondsAt(measurePos);
        Assert.True(Math.Abs(expectedSeconds - seconds) < 1e-6);

        var recoveredPos = timeline.MeasurePositionAt(seconds);
        Assert.True(Math.Abs(measurePos - recoveredPos) < 1e-6);
    }

    [Fact]
    public void ChartTimeline_NonUniform_DoesNotUseFastPath()
    {
        var bpmChanges = new List<BpmChange> { new(0, 0.5, 240.0) };
        var timeline = new ChartTimeline(120.0, new Dictionary<int, double>(), bpmChanges);

        Assert.True(timeline.HasBpmChanges);

        var sec = timeline.SecondsAt(1.0);
        Assert.True(sec > 0);
    }

    // ── BmsChart: WavTable 및 BpmTable 대소문자 무시 사전 검증 ───────────────────────

    [Fact]
    public void BmsChart_WavTable_CaseInsensitive()
    {
        var chart = new BmsChart();
        chart.WavTable["0a"] = "test.wav";

        Assert.True(chart.WavTable.ContainsKey("0A"));
        Assert.Equal("test.wav", chart.WavTable["0A"]);
    }

    [Fact]
    public void BmsChart_BpmTable_CaseInsensitive()
    {
        var chart = new BmsChart();
        chart.BpmTable["0a"] = 185.0;

        Assert.True(chart.BpmTable.ContainsKey("0A"));
        Assert.Equal(185.0, chart.BpmTable["0A"]);
    }
}

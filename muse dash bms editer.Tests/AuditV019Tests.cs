using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using Xunit;

namespace bms_editer.Tests;

// v0.1.9 감사에서 고친 것들의 회귀 테스트. 모두 임시 폴더에서만 돈다(사용자 데이터 폴더를 건드리지 않음).
public sealed class AuditV019Tests : IDisposable
{
    private readonly string _tempDir;

    public AuditV019Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bms-audit019-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                // ReadOnly 해제 후 삭제
                foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 임시 폴더 정리 실패 무시
        }
    }

    // ── SafeFileWriter: ReadOnly 속성의 파일이나 .bak 가 있어도 안전하게 저장 ─────────────

    [Fact]
    public void SafeFileWriter_Overwrites_ReadOnlyOriginalAndBak()
    {
        var chart = Path.Combine(_tempDir, "test.bms");
        var bak = chart + SafeFileWriter.BackupExtension;

        File.WriteAllText(chart, "version1");
        File.WriteAllText(bak, "oldbak");

        // 두 파일 모두 ReadOnly 설정
        File.SetAttributes(chart, FileAttributes.ReadOnly);
        File.SetAttributes(bak, FileAttributes.ReadOnly);

        SafeFileWriter.WriteAllText(chart, "version2", Encoding.UTF8);

        Assert.Equal("version2", File.ReadAllText(chart));
        Assert.Equal("version1", File.ReadAllText(bak));
        Assert.False((File.GetAttributes(chart) & FileAttributes.ReadOnly) != 0);
    }

    // ── AppSettings: ReadOnly 설정 파일 저장 및 0바이트 파일 로드 방어 ──────────────────

    [Fact]
    public void Settings_Save_Overwrites_ReadOnlyFile()
    {
        var file = Path.Combine(_tempDir, "settings.json");
        var settings = new AppSettings { VerticalZoom = 15.0 };
        settings.Save(file);

        File.SetAttributes(file, FileAttributes.ReadOnly);

        settings.VerticalZoom = 20.0;
        settings.Save(file);

        var loaded = AppSettings.Load(file);
        Assert.Equal(20.0, loaded.VerticalZoom);
    }

    [Fact]
    public void Settings_Load_HandlesEmptyFile_WithoutException()
    {
        var file = Path.Combine(_tempDir, "empty_settings.json");
        File.WriteAllText(file, "   ");

        var loaded = AppSettings.Load(file);
        Assert.NotNull(loaded);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    // ── ChartTimeline: 극단적 시간값 방어 및 마디 내 BPM 변속 탐색 검증 ────────────────

    [Fact]
    public void ChartTimeline_ExtremeSeconds_DoesNotCrash()
    {
        var timeline = ChartTimeline.Uniform(140.0);

        Assert.Equal(0.0, timeline.MeasurePositionAt(-100.0));
        Assert.Equal(0.0, timeline.MeasurePositionAt(0.0));
        Assert.Equal(0.0, timeline.MeasurePositionAt(double.NaN));
        Assert.Equal(0.0, timeline.MeasurePositionAt(double.NegativeInfinity));

        // 매우 큰 초값
        var largeMeasure = timeline.MeasurePositionAt(100000.0);
        Assert.True(double.IsFinite(largeMeasure) && largeMeasure > 0);
    }

    [Fact]
    public void ChartTimeline_BpmChanges_Consistency_RoundTrip()
    {
        var bpmChanges = new List<BpmChange>
        {
            new(0, 0.5, 240.0),
            new(1, 0.25, 120.0),
            new(1, 0.75, 180.0)
        };

        var timeline = new ChartTimeline(120.0, new Dictionary<int, double>(), bpmChanges);

        // 0마디 0.5위치 초 계산 후 되돌리기
        var sec05 = timeline.SecondsAt(0.5);
        var pos05 = timeline.MeasurePositionAt(sec05);
        Assert.True(Math.Abs(0.5 - pos05) < 1e-4);

        // 1마디 0.75위치 초 계산 후 되돌리기
        var sec175 = timeline.SecondsAt(1.75);
        var pos175 = timeline.MeasurePositionAt(sec175);
        Assert.True(Math.Abs(1.75 - pos175) < 1e-4);
    }

    // ── BmsWriter: NaN 오프셋 방어 검증 ──────────────────────────────────────────────

    [Fact]
    public void BmsWriter_NanAudioOffset_IsNotWritten()
    {
        var chart = new BmsChart();
        chart.Header.AudioOffsetMs = double.NaN;

        var output = BmsWriter.Write(
            chart, "Title", "Artist", "Genre", 120.0, 0, 1, "1",
            Array.Empty<BmsWavItem>(), Path.Combine(_tempDir, "test.bms"));

        Assert.DoesNotContain("#BMSEDITER_OFFSET", output);
    }

    [Fact]
    public void BmsWriter_FiniteAudioOffset_IsWritten()
    {
        var chart = new BmsChart();
        chart.Header.AudioOffsetMs = 125.5;

        var output = BmsWriter.Write(
            chart, "Title", "Artist", "Genre", 120.0, 0, 1, "1",
            Array.Empty<BmsWavItem>(), Path.Combine(_tempDir, "test.bms"));

        Assert.Contains("#BMSEDITER_OFFSET 125.5", output);
    }
}

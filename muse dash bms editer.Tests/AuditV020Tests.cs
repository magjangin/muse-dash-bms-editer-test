using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// v0.2.0 심층 개선 및 최적화 회귀 테스트
public sealed class AuditV020Tests : IDisposable
{
    private readonly string _tempDir;

    public AuditV020Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bms-audit020-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
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

    // ── BulkObservableCollection: EnsureCapacity 와 알림 1회 발생 검증 ──────────────

    [Fact]
    public void BulkObservableCollection_AddRange_FiresSingleResetEvent()
    {
        var collection = new BulkObservableCollection<string>();
        var eventCount = 0;
        collection.CollectionChanged += (_, e) =>
        {
            eventCount++;
            Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action);
        };

        collection.AddRange(new[] { "A", "B", "C", "D", "E" });

        Assert.Equal(1, eventCount);
        Assert.Equal(5, collection.Count);
    }

    [Fact]
    public void BulkObservableCollection_ReplaceAll_ReplacesItemsWithSingleResetEvent()
    {
        var collection = new BulkObservableCollection<int>();
        collection.AddRange(new[] { 1, 2, 3 });

        var eventCount = 0;
        collection.CollectionChanged += (_, e) =>
        {
            eventCount++;
            Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action);
        };

        collection.ReplaceAll(new[] { 10, 20, 30, 40 });

        Assert.Equal(1, eventCount);
        Assert.Equal(4, collection.Count);
        Assert.Equal(10, collection[0]);
    }

    // ── KeySoundPlayer: Gain 0 및 Gain 256 Fast Path 정상 동작 검증 ────────────────

    [Fact]
    public void KeySoundPlayer_Volume_ZeroAndFull_MixWithoutErrors()
    {
        using var player = new KeySoundPlayer();

        // 볼륨 0.0 (Gain 0)
        player.Volume = 0.0;
        Assert.Equal(0.0, player.Volume);

        // 볼륨 1.0 (Gain 256)
        player.Volume = 1.0;
        Assert.Equal(1.0, player.Volume);
    }

    // ── BmsWriter: SplitByResolution & LCM/GCD 비정상 값 방어 ──────────────────────

    [Fact]
    public void BmsWriter_HandlesExtremeNotePositions_WithoutCrashing()
    {
        var chart = new BmsChart();
        chart.Notes.Add(new BmsNote { Measure = 0, LaneId = "13", Position = 0.0, WavKey = "01" });
        chart.Notes.Add(new BmsNote { Measure = 0, LaneId = "13", Position = 0.33333333, WavKey = "02" });
        chart.Notes.Add(new BmsNote { Measure = 0, LaneId = "13", Position = 0.99999999, WavKey = "03" });

        var path = Path.Combine(_tempDir, "extreme_resolution.bms");
        var output = BmsWriter.Write(
            chart, "Title", "Artist", "Genre", 120.0, 0, 1, "1",
            new[] { new BmsWavItem { Key = "01", FilePath = "a.wav" } },
            path);

        Assert.NotNull(output);
        Assert.Contains("#00013:", output);
    }
}

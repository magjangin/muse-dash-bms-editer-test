using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Holds;
using bms_editer.ViewModels;
using bms_editer.Views.Controls;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

// 실제 건반 차트 하나를 **에디터가 하는 순서 그대로** 통과시킨다.
//
// 변환 규칙은 DownmixTests 가 합성 차트로 못 박아 두었다. 여기서 보는 것은 그 다음이다 —
// 가져온 결과가 **격자에 실제로 그려지는지**(픽셀), **저장하면 건반 채널이 안 남는지**,
// 다시 열어도 짝이 맞는지, 그리고 **Ctrl+Z 한 번에 사라지는지**.
// (known_issues.md 확인 기록 40·41번)
//
// 그림은 %TEMP%\bms-editer-tests\muse-dash-downmix 에 남는다. 사람이 눈으로 볼 몫이다.
public sealed class DownmixRealChartTests : IDisposable
{
    // 스타트레일 커스텀 차트. 키음 값 02/03 으로 롱노트를 적는 게임이라 홀드까지 나온다.
    private const string SourceChart = @"H:\Sixtar Gate STARTRAIL custom mode\hwa\rise\hwa2.bms";

    private const double LaneWidth = 40;
    private const double MeasureHeight = 64; // RowHeight 16 × VerticalZoom 1 × (BeatSplit 16 / GridMeasure 4)

    private readonly string _directory;
    private readonly ITestOutputHelper _output;

    public DownmixRealChartTests(ITestOutputHelper output)
    {
        _output = output;
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    [Fact]
    public async Task 실제_차트를_가져오면_격자에_그려지고_저장까지_남는다()
    {
        if (!File.Exists(SourceChart))
        {
            _output.WriteLine($"(이 컴퓨터에 {SourceChart} 가 없음 — 건너뜀)");
            return;
        }

        var vm = new MainWindowViewModel();
        var report = await vm.ImportKeyboardChartAsync(SourceChart);

        Assert.NotNull(report);
        Assert.Null(vm.LastErrorMessage);

        // ── 1. 두 레인에만 들어간다 ──────────────────────────────────────
        Assert.All(vm.Chart.Notes, note => Assert.Contains(note.LaneId, new[] { "13", "14" }));
        var ground = vm.Chart.Notes.Count(n => n.LaneId == "13");
        var air = vm.Chart.Notes.Count(n => n.LaneId == "14");
        Assert.True(ground > 0 && air > 0, $"한쪽 레인이 비었습니다 (지상 {ground} · 공중 {air})");

        // ── 2. 결과 창 숫자가 실제 문서와 맞는다 ─────────────────────────
        Assert.Equal(vm.Chart.Notes.Count, report!.CreatedNotes);
        Assert.Equal(report.CreatedHolds, vm.HoldLinks.Count);
        Assert.NotEmpty(vm.HoldLinks);

        // ── 3. 짝이 깨진 자리가 없다 ─────────────────────────────────────
        Assert.False(vm.HasHoldDiagnostics, vm.HoldSummaryText);

        // ── 3-1. 같은 폴더의 음원이 같이 열린다 ──────────────────────────
        // 원본 차트는 그 음원에 맞춰 만든 것이다. 가져오자마자 곡에 맞춰 들어 볼 수 있어야 한다.
        Assert.Equal("music.ogg", report.AudioFile);
        Assert.Equal("music.ogg", vm.OggFileName);
        Assert.True(vm.OggDurationSeconds > 60, $"음원 길이가 이상합니다: {vm.OggDurationSeconds}초");

        // 사용자가 보게 될 결과 창 그대로. 문구가 바뀌면 여기서 먼저 보인다.
        _output.WriteLine(vm.DescribeDownmixReport(report));
        _output.WriteLine("");

        _output.WriteLine(
            $"{Path.GetFileName(Path.GetDirectoryName(SourceChart))}: 노트 {vm.Chart.Notes.Count} " +
            $"(지상 {ground} · 공중 {air}) · 홀드 {vm.HoldLinks.Count}쌍 · " +
            $"옮김 {report.MovedToOtherLane} · 버림 {report.DroppedTooDense} · 짝 문제 0");

        // ── 4. 격자에 정말로 그려지는가 (픽셀) ───────────────────────────
        // 몸통이 한 픽셀 이상 보이도록 충분히 긴 홀드 중 가장 앞의 것을 고른다.
        var longEnough = vm.HoldLinks.Where(l => At(l.Tail) - At(l.Head) >= 0.25).ToArray();
        var link = (longEnough.Length > 0 ? longEnough : vm.HoldLinks.ToArray())
            .OrderBy(l => At(l.Head))
            .First();

        var measureCount = (int)Math.Ceiling(At(link.Tail)) + 2;
        var notes = vm.Chart.Notes.ToArray();
        var links = vm.HoldLinks.ToArray();

        HoldTestSupport.RunOnUiThread(() =>
        {
            var grid = Grid(notes, links, measureCount, rowHeight: 16);
            var snapshot = GridSnapshot.Capture(grid);
            snapshot.Save(Artifact("rise-hold.png"));

            // 세로 보기는 아래가 0마디다. 마디 m 은 y = (전체 마디 − m) × 마디높이.
            var laneCenter = LaneCenter(link.Head.LaneId);
            var middle = (measureCount - ((At(link.Head) + At(link.Tail)) / 2)) * MeasureHeight;
            var pixel = snapshot.At(laneCenter, middle);

            Assert.True(
                HoldTestSupport.LooksLikeHoldBody(pixel, link.Kind),
                $"홀드 몸통이 안 보입니다. 레인 {link.Head.LaneId} · " +
                $"{At(link.Head):0.###}~{At(link.Tail):0.###}마디 가운데 픽셀 = {pixel}");

            // 사람이 훑어볼 전체 그림. 마디당 8px 로 줄여 한 장에 담는다.
            var overview = Grid(notes, links, Math.Max(measureCount, vm.MeasureCount), rowHeight: 2);
            GridSnapshot.Capture(overview).Save(Artifact("rise-overview.png"));
        });

        // ── 5. 저장하면 건반 채널이 안 남는다 ────────────────────────────
        var savedPath = Path.Combine(_directory, "rise-converted.bms");
        Assert.True(vm.SaveBms(savedPath), vm.LastErrorMessage);

        // `13`·`14` 는 제외한다. 건반 BMS 에서도 쓰는 번호지만 **변환 뒤에는 뮤즈 대시의 지상·공중**이다.
        // 남으면 안 되는 것은 나머지 건반 자리(`11` `12` `15~19`)와 2P(`21~29` `61~69`), 롱노트 채널(`51~59`)이다.
        var forbidden = new[] { "11", "12", "15", "16", "17", "18", "19" }
            .Concat(new[] { 2, 5, 6 }.SelectMany(tens => Enumerable.Range(1, 9).Select(one => $"{tens}{one}")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var savedLines = BmsParser.ReadLinesForAnalysis(savedPath);
        var leftover = savedLines
            .Where(line =>
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#LNOBJ", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("#LNTYPE", StringComparison.OrdinalIgnoreCase))
                    return true;

                var match = Regex.Match(trimmed, @"^#\d{3}([0-9A-Za-z]{2}):");
                return match.Success && forbidden.Contains(match.Groups[1].Value);
            })
            .ToArray();
        Assert.True(leftover.Length == 0, "건반 줄이 남았습니다:\n" + string.Join("\n", leftover.Take(5)));

        // ── 6. 다시 열어도 짝이 그대로다 ─────────────────────────────────
        var reopened = new MainWindowViewModel();
        Assert.True(reopened.LoadBms(savedPath), reopened.LastErrorMessage);
        Assert.Equal(vm.Chart.Notes.Count, reopened.Chart.Notes.Count);
        Assert.Equal(vm.HoldLinks.Count, reopened.HoldLinks.Count);
        Assert.False(reopened.HasHoldDiagnostics, reopened.HoldSummaryText);

        // ── 7. Ctrl+Z 한 번이면 없던 일이 된다 ───────────────────────────
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
        Assert.Empty(vm.HoldLinks);
    }

    private static double At(BmsNote note) => note.Measure + note.Position;

    // 기본 레인은 16·11·12·13·14·15·18 순이다.
    private static double LaneCenter(string laneId)
    {
        var lanes = LaneDefinition.CreateDefault();
        var index = lanes.ToList().FindIndex(l => l.Id == laneId);
        Assert.True(index >= 0, $"레인 {laneId} 를 찾지 못했습니다");
        return LaneWidth * (index + 0.5);
    }

    private static NoteGridControl Grid(BmsNote[] notes, HoldLink[] links, int measureCount, double rowHeight) => new()
    {
        Lanes = LaneDefinition.CreateDefault(),
        Notes = notes,
        HoldLinks = links,
        MeasureCount = measureCount,
        RowHeight = rowHeight,
        VerticalZoom = 1,
        HorizontalZoom = 1,
        LaneWidth = LaneWidth,
        BeatSplit = 16,
        GridMeasure = 4,
        Bpm = 120,
    };

    private static string Artifact(string name) =>
        Path.Combine(HoldTestSupport.ArtifactDirectory("muse-dash-downmix"), name);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services.Downmix;
using bms_editer.Services.Holds;
using bms_editer.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

// 건반형 BMS(4K/5K/7K) → 뮤즈 대시 두 레인 변환.
//
// 가장 중요한 것은 **만들어 낸 홀드의 짝이 맞는가**이다.
// 뮤즈 대시는 같은 채널에서 나온 순서로 시작·끝을 가르므로, 서로 다른 건반 레인의 롱노트가
// 한 레인으로 접히면서 겹치면 head·head·tail·tail 이 되어 짝이 통째로 뒤집힌다.
// 그래서 변환 결과를 **짝 검사기에 그대로 넣어** 문제 0건인지 본다.
public sealed class DownmixTests : IDisposable
{
    private readonly string _directory;
    private readonly ITestOutputHelper _output;

    public DownmixTests(ITestOutputHelper output)
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

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // 스타트레일 폴더 아래에 적는다. 경로의 폴더 이름으로 게임을 알아보기 때문이다. (GameProfileCatalog.DetectFromPath)
    private string WriteInStartrail(string name, string content)
    {
        var gameDir = Path.Combine(
            _directory, "Sixtar Gate STARTRAIL custom mode", "hwa", Path.GetFileNameWithoutExtension(name));
        Directory.CreateDirectory(gameDir);

        var path = Path.Combine(gameDir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // 뮤즈 대시 키음만 채운 템플릿. 실제 차트의 파일명 모양을 그대로 쓴다.
    private const string MuseDashWavTable =
        "#WAV01 1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav\r\n" +
        "#WAV02 1번 씬 wav폴더\\011004_일반 노트1 공중 노멀_dt1.48.wav\r\n" +
        "#WAV03 1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.48.wav\r\n" +
        "#WAV04 1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.48.wav\r\n" +
        "#WAV05 1번 씬 wav폴더\\010204_홀드 공중 시작 노트_dt1.48.wav\r\n" +
        "#WAV06 1번 씬 wav폴더\\010204_홀드 공중 끝 노트_dt1.48.wav\r\n";

    private MainWindowViewModel MuseDashTemplate()
    {
        var path = Write("template.bms", "#TITLE 템플릿\r\n#BPM 120\r\n" + MuseDashWavTable);
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        Assert.Empty(vm.Chart.Notes);
        return vm;
    }

    private static DownmixKeys TemplateKeys() =>
        new("01", GroundNormal: "01", AirNormal: "02",
            GroundHoldStart: "03", GroundHoldEnd: "04",
            AirHoldStart: "05", AirHoldEnd: "06");

    private static KeyboardNote Note(string channel, int measure, double position, KeyboardNoteKind kind = KeyboardNoteKind.Normal) =>
        new(channel, measure, position, kind);

    private static KeyboardChart Chart(IReadOnlyList<KeyboardNote> notes, params string[] channels) =>
        new(notes, channels, "테스트", 120, notes.Max(n => n.Measure) + 1,
            new Dictionary<string, string>(), null, Array.Empty<string>());

    // ── 읽기 ────────────────────────────────────────────────────────────

    [Fact]
    public void 건반_채널을_읽는다()
    {
        var path = Write("4k.bms",
            "#TITLE 사키\r\n#BPM 155\r\n#WAV01 kick.wav\r\n" +
            "#00111:01000100\r\n" +
            "#00112:00010001\r\n" +
            "#00113:01010000\r\n" +
            "#00114:00000101\r\n");

        var chart = KeyboardChartReader.Read(path);

        Assert.Equal("사키", chart.Title);
        Assert.Equal(155, chart.Bpm);
        Assert.Equal(new[] { "11", "12", "13", "14" }, chart.Channels);
        Assert.Equal(8, chart.Notes.Count);
        Assert.All(chart.Notes, n => Assert.Equal(KeyboardNoteKind.Normal, n.Kind));
    }

    [Fact]
    public void 롱노트_채널_51_59는_읽지_않는다()
    {
        // 원본 게임은 롱노트를 시작·끝 노트로 적고 51~59 는 읽지 않는다.
        // 여기서 읽으면 게임에 없던 홀드가 생긴다. 건너뛴 수는 알린다.
        var path = WriteInStartrail("ln.bms",
            "#TITLE ln\r\n#BPM 120\r\n#LNTYPE 1\r\n#WAV01 a.wav\r\n" +
            "#00111:0100\r\n" +
            "#00151:0100\r\n" +
            "#00251:0100\r\n");

        var chart = KeyboardChartReader.Read(path);

        Assert.Equal("startrail", chart.SourceProfile?.Id);
        var note = Assert.Single(chart.Notes);
        Assert.Equal(KeyboardNoteKind.Normal, note.Kind);
        Assert.Contains(chart.Warnings, w => w.Contains("51~59") && w.Contains("2개"));
    }

    [Fact]
    public void LNOBJ는_롱노트로_해석하지_않는다()
    {
        // #LNOBJ 도 원본 게임이 읽지 않는다. 그 키가 찍힌 자리는 그냥 다른 소리의 노트다.
        var path = WriteInStartrail("lnobj.bms",
            "#TITLE lnobj\r\n#BPM 120\r\n#LNOBJ ZZ\r\n#WAV01 a.wav\r\n#WAVZZ end.wav\r\n" +
            "#00111:01000000\r\n" +
            "#00211:ZZ000000\r\n");

        var chart = KeyboardChartReader.Read(path);

        Assert.Equal(2, chart.Notes.Count);
        Assert.All(chart.Notes, n => Assert.Equal(KeyboardNoteKind.Normal, n.Kind));
    }

    [Fact]
    public void 이피_채널은_건너뛰고_알린다()
    {
        var path = Write("2p.bms",
            "#TITLE 2p\r\n#BPM 120\r\n#WAV01 a.wav\r\n" +
            "#00111:0100\r\n" +
            "#00121:0100\r\n");

        var chart = KeyboardChartReader.Read(path);

        Assert.Single(chart.Notes);
        Assert.Contains(chart.Warnings, w => w.Contains("2P"));
    }

    // ── 레인 배분 ────────────────────────────────────────────────────────

    [Fact]
    public void 왼손은_지상_오른손은_공중으로_간다()
    {
        // 4K: 11·12(왼쪽) → 지상(13), 13·14(오른쪽) → 공중(14).
        var source = Chart(new[]
        {
            Note("11", 1, 0.0),
            Note("12", 2, 0.0),
            Note("13", 3, 0.0),
            Note("14", 4, 0.0),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        var byMeasure = result.Notes.ToDictionary(n => n.Measure, n => n.LaneId);
        Assert.Equal(DownmixEngine.GroundLane, byMeasure[1]);
        Assert.Equal(DownmixEngine.GroundLane, byMeasure[2]);
        Assert.Equal(DownmixEngine.AirLane, byMeasure[3]);
        Assert.Equal(DownmixEngine.AirLane, byMeasure[4]);
    }

    [Fact]
    public void 칠키의_가운데_건반은_번갈아_간다()
    {
        // 7K(11~15·18·19)에서 가운데는 14. 같은 레인으로 몰면 연타가 된다.
        var source = Chart(new[]
        {
            Note("14", 1, 0.0),
            Note("14", 2, 0.0),
            Note("14", 3, 0.0),
            Note("14", 4, 0.0),
        }, "11", "12", "13", "14", "15", "18", "19");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        var lanes = result.Notes.OrderBy(n => n.Measure).Select(n => n.LaneId).ToArray();
        Assert.Equal(
            new[] { DownmixEngine.GroundLane, DownmixEngine.AirLane, DownmixEngine.GroundLane, DownmixEngine.AirLane },
            lanes);
    }

    // ── 쏠림 교정 ────────────────────────────────────────────────────────
    //
    // 배치를 옳게 따라도 원본 채보가 한쪽 손에 몰려 있으면 결과가 기운다.
    // 뮤즈 대시는 손이 둘뿐이라 한쪽만 쉴 새 없이 두드리는 채보가 되고, 그건 원본에 없던 난이도다.

    [Fact]
    public void 한쪽으로_쏠리면_무거운_레인을_번갈아로_돌린다()
    {
        // 왼쪽 `11` 에 8개, 오른쪽 `14` 에 1개. 그대로 접으면 89%가 지상이다.
        var notes = Enumerable.Range(0, 8)
            .Select(i => Note("11", 1, i / 8.0))
            .Append(Note("14", 2, 0))
            .ToArray();

        var result = DownmixEngine.Convert(Chart(notes, "11", "14"), TemplateKeys());

        Assert.Contains("11", result.Report.RebalancedChannels!);
        Assert.Equal(9, result.Notes.Count);

        // 반반에 가깝게 갈린다. 버린 노트는 없다.
        Assert.InRange(result.Report.GroundNotes, 4, 5);
        Assert.InRange(result.Report.AirNotes, 4, 5);
        Assert.Equal(0, result.Report.DroppedTooDense);
    }

    [Fact]
    public void 이미_고르면_배치를_손대지_않는다()
    {
        var notes = new[] { Note("11", 1, 0), Note("14", 1, 0), Note("11", 2, 0), Note("14", 2, 0) };

        var result = DownmixEngine.Convert(Chart(notes, "11", "14"), TemplateKeys());

        Assert.Empty(result.Report.RebalancedChannels!);
        Assert.Equal(2, result.Report.GroundNotes);
        Assert.Equal(2, result.Report.AirNotes);
    }

    [Fact]
    public void 같은_차트는_두_번_돌려도_같은_결과가_나온다()
    {
        // 무엇을 번갈아로 돌릴지 고를 때 후보가 여럿이면 순서가 흔들릴 수 있다.
        // 무거운 것 먼저, 같으면 채널 번호 순으로 못 박아 두었다.
        var notes = new[]
        {
            Note("11", 1, 0), Note("11", 1, 0.5),
            Note("12", 2, 0), Note("12", 2, 0.5),
            Note("14", 3, 0),
        };

        var first = DownmixEngine.Convert(Chart(notes, "11", "12", "14"), TemplateKeys());
        var second = DownmixEngine.Convert(Chart(notes, "11", "12", "14"), TemplateKeys());

        Assert.Equal(first.Report.RebalancedChannels, second.Report.RebalancedChannels);
        Assert.Equal(
            first.Notes.Select(n => (n.Measure, n.Position, n.LaneId)),
            second.Notes.Select(n => (n.Measure, n.Position, n.LaneId)));
    }

    // ── 동시치기 ─────────────────────────────────────────────────────────

    [Fact]
    public void 같은_자리의_셋째_노트는_버린다()
    {
        // 건반은 3중 동시치기가 나오지만 뮤즈 대시는 손이 둘뿐이다.
        var source = Chart(new[]
        {
            Note("11", 1, 0.0),
            Note("12", 1, 0.0),
            Note("13", 1, 0.0),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        Assert.Equal(2, result.Notes.Count);
        Assert.Equal(1, result.Report.DroppedTooDense);

        // 남은 둘은 서로 다른 레인이다.
        Assert.Equal(2, result.Notes.Select(n => n.LaneId).Distinct().Count());
    }

    [Fact]
    public void 한쪽에_몰린_동시치기는_반대_레인으로_옮긴다()
    {
        // 왼손 둘이 동시에 눌렸다. 둘 다 지상으로 보내면 하나가 버려진다.
        // 반대쪽이 비어 있으면 옮겨서 살린다 — 뮤즈 대시에서 지상+공중 동시는 정상 패턴이다.
        var source = Chart(new[]
        {
            Note("11", 1, 0.0),
            Note("12", 1, 0.0),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        Assert.Equal(2, result.Notes.Count);
        Assert.Equal(0, result.Report.DroppedTooDense);
        Assert.Equal(1, result.Report.MovedToOtherLane);
        Assert.Equal(2, result.Notes.Select(n => n.LaneId).Distinct().Count());
    }

    // ── 홀드 ─────────────────────────────────────────────────────────────

    [Fact]
    public void 롱노트는_시작과_끝_두_노트로_나온다()
    {
        var source = Chart(new[]
        {
            Note("11", 1, 0.0, KeyboardNoteKind.LongStart),
            Note("11", 3, 0.0, KeyboardNoteKind.LongEnd),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        Assert.Equal(2, result.Notes.Count);
        Assert.Equal(1, result.Report.CreatedHolds);

        var keys = TemplateKeys();
        Assert.Equal(keys.GroundHoldStart, result.Notes[0].WavKey);
        Assert.Equal(keys.GroundHoldEnd, result.Notes[1].WavKey);
        Assert.All(result.Notes, n => Assert.Equal(DownmixEngine.GroundLane, n.LaneId));
    }

    [Fact]
    public void 한_레인에서_겹치는_홀드는_하나만_남긴다()
    {
        // 서로 다른 건반 레인의 롱노트가 한 레인으로 접히면서 겹치면,
        // 그대로 두면 시작·시작·끝·끝 순서가 되어 짝이 통째로 뒤집힌다.
        var source = Chart(new[]
        {
            Note("11", 1, 0.0, KeyboardNoteKind.LongStart),
            Note("11", 4, 0.0, KeyboardNoteKind.LongEnd),
            Note("12", 2, 0.0, KeyboardNoteKind.LongStart),
            Note("12", 5, 0.0, KeyboardNoteKind.LongEnd),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        // 반대쪽이 비어 있으므로 둘 다 살아남되, **서로 다른 레인**으로 갈라져야 한다.
        Assert.Equal(2, result.Report.CreatedHolds);
        Assert.Equal(1, result.Report.MovedToOtherLane);
        Assert.Equal(0, result.Report.DroppedOverlappingHold);

        var lanes = result.Notes.Select(n => n.LaneId).Distinct().ToArray();
        Assert.Equal(2, lanes.Length);
    }

    [Fact]
    public void 두_레인이_다_막히면_홀드를_버린다()
    {
        // 겹치는 롱노트 셋. 지상·공중에 하나씩 가고 나면 셋째는 갈 곳이 없다.
        var source = Chart(new[]
        {
            Note("11", 1, 0.0, KeyboardNoteKind.LongStart),
            Note("11", 6, 0.0, KeyboardNoteKind.LongEnd),
            Note("12", 2, 0.0, KeyboardNoteKind.LongStart),
            Note("12", 7, 0.0, KeyboardNoteKind.LongEnd),
            Note("13", 3, 0.0, KeyboardNoteKind.LongStart),
            Note("13", 8, 0.0, KeyboardNoteKind.LongEnd),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        Assert.Equal(2, result.Report.CreatedHolds);
        Assert.Equal(1, result.Report.DroppedOverlappingHold);

        // 남은 둘은 같은 레인에서 겹치지 않는다.
        foreach (var lane in new[] { DownmixEngine.GroundLane, DownmixEngine.AirLane })
        {
            var times = result.Notes
                .Where(n => n.LaneId == lane)
                .Select(n => n.Measure + n.Position)
                .OrderBy(t => t)
                .ToArray();

            // 홀드 하나뿐이면 시작·끝 두 개만 있어야 한다.
            Assert.True(times.Length <= 2, $"{lane} 레인에 홀드가 둘 이상 겹쳐 들어갔습니다");
        }
    }

    [Fact]
    public void 길이가_0인_롱노트는_버린다()
    {
        var source = Chart(new[]
        {
            Note("11", 1, 0.0, KeyboardNoteKind.LongStart),
            Note("11", 1, 0.0, KeyboardNoteKind.LongEnd),
        }, "11", "12", "13", "14");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        Assert.Empty(result.Notes);
        Assert.Equal(1, result.Report.DroppedZeroLengthHold);
    }

    // ── 통합: 변환 결과가 짝 검사기를 통과하는가 ────────────────────────

    [Fact]
    public void 변환_결과는_홀드_짝_검사를_통과한다()
    {
        var profile = GameProfileCatalog.Default.Find("muse_dash")!;
        var wavTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["01"] = "1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav",
            ["02"] = "1번 씬 wav폴더\\011004_일반 노트1 공중 노멀_dt1.48.wav",
            ["03"] = "1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.48.wav",
            ["04"] = "1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.48.wav",
            ["05"] = "1번 씬 wav폴더\\010204_홀드 공중 시작 노트_dt1.48.wav",
            ["06"] = "1번 씬 wav폴더\\010204_홀드 공중 끝 노트_dt1.48.wav",
        };

        // 롱노트와 일반 노트가 뒤섞인 건반 채보.
        var notes = new List<KeyboardNote>();
        for (var m = 1; m <= 20; m++)
        {
            notes.Add(Note("11", m, 0.0, m % 4 == 1 ? KeyboardNoteKind.LongStart : KeyboardNoteKind.Normal));
            if (m % 4 == 3)
                notes.Add(Note("11", m, 0.5, KeyboardNoteKind.LongEnd));

            notes.Add(Note("14", m, 0.25));
            notes.Add(Note("12", m, 0.75));
        }

        var source = Chart(notes, "11", "12", "13", "14");
        var result = DownmixEngine.Convert(source, TemplateKeys());

        // 홀드가 0개면 아래 검사가 통째로 공회전한다. 먼저 못 박는다.
        Assert.Equal(5, result.Report.CreatedHolds);
        Assert.True(result.Notes.Count > 60, $"변환된 노트가 {result.Notes.Count}개뿐입니다");

        var pairing = HoldPairingEngine.Pair(result.Notes, wavTexts, profile);
        var errors = pairing.Diagnostics
            .Where(d => d.Severity == HoldDiagnosticSeverity.Error)
            .Select(d => d.DisplayText)
            .ToArray();

        Assert.True(errors.Length == 0, string.Join("\n", errors));
        Assert.Equal(result.Report.CreatedHolds, pairing.Links.Count);
    }

    // ── 게임 프로파일로 롱노트 찾기 ──────────────────────────────────────

    // 스타트레일·건볼트는 롱노트를 채널이 아니라 **키음 값**에 적는다.
    // 그 규칙은 Profiles/*.json 에 이미 있고, 다운믹서는 그것을 빌려 쓴다.
    [Fact]
    public void 스타트레일_차트의_키음값_롱노트를_찾아낸다()
    {
        // 경로의 폴더 이름으로 게임을 알아본다. (GameProfileCatalog.DetectFromPath)
        var gameDir = Path.Combine(_directory, "Sixtar Gate STARTRAIL custom mode", "hwa", "곡");
        Directory.CreateDirectory(gameDir);

        var path = Path.Combine(gameDir, "hwa2.bms");
        File.WriteAllText(
            path,
            "#TITLE st\r\n#BPM 120\r\n#WAV01 tap.wav\r\n#WAV02 hold_start.wav\r\n#WAV03 hold_end.wav\r\n" +
            "#00111:0200\r\n" +   // 키음 02 = 롱노트 시작
            "#00211:0300\r\n" +   // 키음 03 = 끝
            "#00311:0100\r\n",    // 그냥 단타
            new UTF8Encoding(false));

        var chart = KeyboardChartReader.Read(path);

        Assert.NotNull(chart.SourceProfile);
        Assert.Equal("startrail", chart.SourceProfile!.Id);

        var kinds = chart.Notes.OrderBy(n => n.Measure).Select(n => n.Kind).ToArray();
        Assert.Equal(
            new[] { KeyboardNoteKind.LongStart, KeyboardNoteKind.LongEnd, KeyboardNoteKind.Normal },
            kinds);

        // 변환하면 홀드 한 쌍이 나온다.
        var result = DownmixEngine.Convert(chart, TemplateKeys());
        Assert.Equal(1, result.Report.CreatedHolds);
    }

    [Fact]
    public void 건볼트_차트의_키음값_롱노트를_찾아낸다()
    {
        // 건볼트는 02 = 시작, 19 = 끝.
        var gameDir = Path.Combine(_directory, "GUNVOLT RECORDS Cychronicle", "hwa", "곡");
        Directory.CreateDirectory(gameDir);

        var path = Path.Combine(gameDir, "hwa2.bms");
        File.WriteAllText(
            path,
            "#TITLE gv\r\n#BPM 120\r\n#WAV01 tap.wav\r\n#WAV02 s.wav\r\n#WAV19 e.wav\r\n" +
            "#00111:0200\r\n" +
            "#00311:1900\r\n",
            new UTF8Encoding(false));

        var chart = KeyboardChartReader.Read(path);

        Assert.Equal("gunvolt", chart.SourceProfile?.Id);
        Assert.Equal(KeyboardNoteKind.LongStart, chart.Notes[0].Kind);
        Assert.Equal(KeyboardNoteKind.LongEnd, chart.Notes[1].Kind);
    }

    [Fact]
    public void 모르는_게임이면_키음값을_해석하지_않는다()
    {
        // 프로파일이 안 잡히면 키음 02·03 은 그냥 다른 소리일 뿐이다.
        // 함부로 롱노트로 읽으면 없는 홀드를 만들어 낸다.
        var path = Write("plain.bms",
            "#TITLE plain\r\n#BPM 120\r\n#WAV02 a.wav\r\n#WAV03 b.wav\r\n" +
            "#00111:0200\r\n#00211:0300\r\n");

        var chart = KeyboardChartReader.Read(path);

        Assert.Null(chart.SourceProfile);
        Assert.All(chart.Notes, n => Assert.Equal(KeyboardNoteKind.Normal, n.Kind));
    }

    // ── 이 컴퓨터의 실제 건반 차트로 ─────────────────────────────────────

    // 합성 데이터만으로는 "야생 BMS" 의 모양을 못 담는다. 이 컴퓨터에 있는 다른 리듬게임
    // 커스텀 채보로 실제로 돌려 보고, 변환 결과가 짝 검사를 통과하는지 본다.
    // 차트를 못 찾는 컴퓨터에서는 한 줄만 남기고 지나간다.
    [Fact]
    public void 실제_건반_차트를_변환해도_짝이_맞는다()
    {
        var roots = new[]
        {
            @"H:\steam\steamapps\common\GUNVOLT RECORDS Cychronicle\hwa",
            @"H:\Sixtar Gate STARTRAIL custom mode\hwa",
        };

        var charts = roots
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.bms", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        if (charts.Length == 0)
        {
            _output.WriteLine("(이 컴퓨터에서 건반 차트를 찾지 못함 — 건너뜀)");
            return;
        }

        var profile = GameProfileCatalog.Default.Find("muse_dash")!;
        var wavTexts = TemplateWavTexts();
        var converted = 0;

        foreach (var path in charts)
        {
            var source = KeyboardChartReader.Read(path);
            if (source.Notes.Count == 0)
            {
                _output.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(path))}: 건반 노트 없음 — 건너뜀");
                continue;
            }

            var result = DownmixEngine.Convert(source, TemplateKeys());
            var pairing = HoldPairingEngine.Pair(result.Notes, wavTexts, profile);
            var errors = pairing.Diagnostics.Count(d => d.Severity == HoldDiagnosticSeverity.Error);

            var ground = result.Notes.Count(n => n.LaneId == DownmixEngine.GroundLane);
            var air = result.Notes.Count - ground;

            _output.WriteLine(
                $"{Path.GetFileName(Path.GetDirectoryName(path)),-24} 건반 {source.Notes.Count,4} → " +
                $"노트 {result.Notes.Count,4} (지상 {ground,4} · 공중 {air,4} · 홀드 {result.Report.CreatedHolds,3}) · " +
                $"옮김 {result.Report.MovedToOtherLane,3} · 버림 {result.Report.DroppedTooDense,3} · 짝 문제 {errors}");

            Assert.Equal(0, errors);
            converted++;
        }

        Assert.True(converted > 0, "변환된 차트가 하나도 없습니다");
    }

    private static Dictionary<string, string> TemplateWavTexts() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["01"] = "1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav",
        ["02"] = "1번 씬 wav폴더\\011004_일반 노트1 공중 노멀_dt1.48.wav",
        ["03"] = "1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.48.wav",
        ["04"] = "1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.48.wav",
        ["05"] = "1번 씬 wav폴더\\010204_홀드 공중 시작 노트_dt1.48.wav",
        ["06"] = "1번 씬 wav폴더\\010204_홀드 공중 끝 노트_dt1.48.wav",
    };

    // ── 뷰모델까지 ───────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task 가져오면_문서가_채워지고_되돌릴_수_있다()
    {
        var vm = MuseDashTemplate();

        // 스타트레일 규칙: 키음 02 = 롱노트 시작, 03 = 끝.
        var path = WriteInStartrail("source.bms",
            "#TITLE 원본\r\n#BPM 120\r\n#LNTYPE 1\r\n#WAV01 a.wav\r\n#WAV02 s.wav\r\n#WAV03 e.wav\r\n" +
            "#00111:01010101\r\n" +
            "#00113:01000100\r\n" +
            "#00211:0200\r\n" +
            "#00311:0300\r\n");

        var report = await vm.ImportKeyboardChartAsync(path);

        Assert.NotNull(report);
        Assert.NotEmpty(vm.Chart.Notes);
        Assert.Equal("01", report!.Scene);
        Assert.Equal(1, report.CreatedHolds);

        // 변환 결과도 짝이 맞아야 한다.
        Assert.False(vm.HasHoldDiagnostics, vm.HoldSummaryText);
        Assert.Single(vm.HoldLinks);

        // 그리고 한 번에 되돌아가야 한다.
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
    }

    [Fact]
    public async System.Threading.Tasks.Task 키음이_없는_문서도_자동_변환한다()
    {
        var vm = new MainWindowViewModel();
        var path = Write("source2.bms", "#TITLE 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:0100\r\n");

        var report = await vm.ImportKeyboardChartAsync(path);

        Assert.NotNull(report);
        Assert.Null(vm.LastErrorMessage);
        Assert.Single(vm.Chart.Notes);
        Assert.Equal("13", vm.Chart.Notes[0].LaneId);
        Assert.True(vm.HasMuseDashKeysounds);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
        Assert.Empty(vm.Chart.WavTable);
        vm.RedoCommand.Execute(null);
        Assert.Single(vm.Chart.Notes);
        Assert.True(vm.HasMuseDashKeysounds);
    }

    // 스냅샷이 헤더를 Chart.Header(= 마지막으로 연 파일의 값)에서 떠서, 화면에서 고친 레벨·RANK·PLAYER 는
    // 되돌리면 엉뚱한 값으로 돌아갔다. 제목·BPM 만 화면 값을 따로 챙기고 있었다.
    [Fact]
    public async System.Threading.Tasks.Task 가져오기를_되돌리면_화면에서_고친_헤더가_모두_돌아온다()
    {
        var vm = new MainWindowViewModel { Title = "내 차트", Level = "7", Rank = 3, Player = 0 };
        var path = Write("header.bms",
            "#TITLE 원본\r\n#BPM 150\r\n#PLAYLEVEL 12\r\n#RANK 1\r\n#PLAYER 3\r\n#WAV01 a.wav\r\n#00111:0100\r\n");

        Assert.NotNull(await vm.ImportKeyboardChartAsync(path));
        Assert.Equal("12", vm.Level);
        Assert.Equal(1, vm.Rank);
        Assert.Equal(2, vm.Player);

        vm.UndoCommand.Execute(null);
        Assert.Equal("내 차트", vm.Title);
        Assert.Equal("7", vm.Level);
        Assert.Equal(3, vm.Rank);
        Assert.Equal(0, vm.Player);

        vm.RedoCommand.Execute(null);
        Assert.Equal("12", vm.Level);
        Assert.Equal(1, vm.Rank);
        Assert.Equal(2, vm.Player);
    }

    // ── 곡 폴더째 가져오기 ───────────────────────────────────────────────
    //
    // 원본 차트는 그 폴더의 음원에 맞춰 만든 것이다. 음원·영상을 같이 열어야 뼈대를 곡에 맞춰 들어 볼 수 있다.

    private string SongFolder(string name, string chart, params string[] extraFiles)
    {
        var folder = Path.Combine(_directory, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "chart.bms"), chart, new UTF8Encoding(false));
        foreach (var file in extraFiles)
            File.WriteAllText(Path.Combine(folder, file), "깨진 파일");

        return Path.Combine(folder, "chart.bms");
    }

    [Fact]
    public async System.Threading.Tasks.Task 가져오면_같은_폴더의_영상도_열고_음원이_없으면_알린다()
    {
        // LoadVideo 는 파일이 있는지만 본다. 내용은 영상 컨트롤이 읽는다.
        var path = SongFolder("영상만", "#TITLE 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:0100\r\n", "video.mp4");
        var vm = new MainWindowViewModel();

        var report = await vm.ImportKeyboardChartAsync(path);

        Assert.NotNull(report);
        Assert.Null(report!.AudioFile);
        Assert.Equal("video.mp4", report.VideoFile);
        Assert.Equal("video.mp4", vm.VideoFileName);
        Assert.Contains("OGG 음원이 없어 차트만 가져왔습니다", vm.DescribeDownmixReport(report));
    }

    [Fact]
    public async System.Threading.Tasks.Task 음원을_못_열어도_가져오기는_성공한다()
    {
        // 노트는 이미 들어왔다. 음원이 깨졌다는 이유로 변환 결과까지 버리면 안 된다.
        var path = SongFolder("깨진 음원", "#TITLE 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:0100\r\n", "music.ogg");
        var vm = new MainWindowViewModel();

        var report = await vm.ImportKeyboardChartAsync(path);

        Assert.NotNull(report);
        Assert.Single(vm.Chart.Notes);
        Assert.Null(report!.AudioFile);
        Assert.Contains("music.ogg", report.MediaWarning);
        Assert.Null(vm.LastErrorMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task 자동변환은_타이밍과_홀드를_저장하고_문서전체를_되돌린다()
    {
        var vm = new MainWindowViewModel { Title = "이전", Bpm = 99 };
        // 홀드는 스타트레일 규칙(키음 02 = 시작, 03 = 끝)으로 만든다.
        // 51 줄은 게임이 읽지 않는 찌꺼기다. 노트가 되지도, 저장에 남지도 않아야 한다.
        var source = WriteInStartrail("timed.bms",
            "#TITLE 변환곡\n#BPM 155\n#BPM01 180\n#WAV01 kick.wav\n#WAV02 s.wav\n#WAV03 e.wav\n#LNTYPE 1\n" +
            "#00102:0.5\n#00208:01\n#00101:01\n" +
            "#00111:01\n#00212:01\n#00313:01\n#00414:01\n" +
            "#00511:02\n#00611:03\n#00651:01\n");
        var report = await vm.ImportKeyboardChartAsync(source);
        Assert.NotNull(report);
        Assert.Equal(155, vm.Bpm);
        Assert.Equal("변환곡", vm.Title);
        Assert.Equal(0.5, vm.Chart.MeasureLengths[1]);
        Assert.Single(vm.Chart.BpmChanges);
        Assert.All(vm.Chart.Notes, n => Assert.Contains(n.LaneId, new[] { "13", "14" }));
        Assert.Single(vm.HoldLinks);
        Assert.Equal("kick.wav", Path.GetFileName(vm.Chart.WavTable["01"]));
        var output = Path.Combine(_directory, "converted.bms");
        Assert.True(vm.SaveBms(output), vm.LastErrorMessage);
        var text = File.ReadAllText(output);
        Assert.DoesNotContain("#00651:", text);
        Assert.DoesNotContain("#LNTYPE", text);
        Assert.Contains("#00101:01", text);
        var reopened = new MainWindowViewModel();
        Assert.True(reopened.LoadBms(output));
        Assert.Single(reopened.HoldLinks);
        Assert.False(reopened.HasHoldDiagnostics);
        Assert.All(reopened.Chart.Notes, n => Assert.Contains(n.LaneId, new[] { "13", "14" }));
        vm.UndoCommand.Execute(null);
        Assert.Equal("이전", vm.Title);
        Assert.Equal(99, vm.Bpm);
        Assert.Empty(vm.Chart.BpmChanges);
        Assert.Empty(vm.Chart.Notes);
        vm.RedoCommand.Execute(null);
        Assert.Equal(155, vm.Bpm);
        Assert.Single(vm.HoldLinks);
    }

    [Fact]
    public void 빈_건반이_있어도_레인번호_배분은_유지한다()
    {
        // `12` 가 비어 있어도 `13` 은 4K 배치에서 오른쪽(공중)이다. 쓰인 채널만 세어 자리를 당기면 안 된다.
        // 양쪽 수가 같아 재배분이 끼어들지 않는다(→ 아래 쏠림 테스트).
        var source = Chart(new[] { Note("11", 1, 0), Note("13", 1, 0) }, "11", "13");

        var result = DownmixEngine.Convert(source, TemplateKeys());

        Assert.Equal("13", result.Notes.Single(n => n.WavKey == TemplateKeys().GroundNormal).LaneId);
        Assert.Equal("14", result.Notes.Single(n => n.WavKey == TemplateKeys().AirNormal).LaneId);
        Assert.Empty(result.Report.RebalancedChannels!);
    }

    // ── 게임의 화면 배치를 따르기 ────────────────────────────────────────
    //
    // 채널 번호로 좌우를 짐작하는 것은 그 게임의 화면을 모를 때의 차선책이다.
    // 프로파일이 배치를 알고 있으면 그쪽이 옳다. (Profiles/*.json 의 downmix)

    private static KeyboardChart Chart(IReadOnlyList<KeyboardNote> notes, GameProfile profile, params string[] channels) =>
        new(notes, channels, "테스트", 120, notes.Max(n => n.Measure) + 1,
            new Dictionary<string, string>(), profile, Array.Empty<string>());

    [Fact]
    public void 건볼트는_번호가_아니라_게임의_좌우를_따른다()
    {
        // 번호로만 보면 `14` 는 가운데라 번갈아 가지만, 건볼트에서 `14` 는 **오른손 안쪽 레인**이다.
        var profile = GameProfileCatalog.Default.Find("gunvolt")!;
        var notes = new[] { Note("16", 1, 0), Note("14", 2, 0) };

        var result = DownmixEngine.Convert(Chart(notes, profile, "16", "14"), TemplateKeys());

        var byMeasure = result.Notes.ToDictionary(n => n.Measure, n => n.LaneId);
        Assert.Equal(DownmixEngine.GroundLane, byMeasure[1]);
        Assert.Equal(DownmixEngine.AirLane, byMeasure[2]);
        Assert.Equal(profile.DisplayName, result.Report.LaneSource);
        Assert.Empty(result.Report.RebalancedChannels!);
    }

    [Fact]
    public void 스타게이저는_위가_공중_아래가_지상이다()
    {
        // 스타게이저는 좌우가 아니라 4방향이다. 16=위 · 13=아래.
        var profile = GameProfileCatalog.Default.Find("stargazer")!;
        var notes = new[] { Note("16", 1, 0), Note("13", 2, 0) };

        var result = DownmixEngine.Convert(Chart(notes, profile, "16", "13"), TemplateKeys());

        Assert.Equal(new[] { "14", "13" }, result.Notes.Select(n => n.LaneId));
    }

    [Fact]
    public void 프로파일이_없으면_배분의_출처를_적지_않는다()
    {
        var result = DownmixEngine.Convert(Chart(new[] { Note("11", 1, 0) }, "11"), TemplateKeys());
        Assert.Null(result.Report.LaneSource);
    }

    [Fact]
    public void 게임이_노트로_안_읽는_채널은_버리고_알린다()
    {
        // 건볼트는 `13` 을 노트 채널로 읽지 않는다(ChannelToLaneMap 에 없다).
        // 그 줄을 노트로 만들면 **게임에 없던 노트**가 생긴다.
        var folder = Path.Combine(_directory, "GUNVOLT RECORDS Cychronicle", "테스트곡");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "hwa2.bms");
        File.WriteAllText(path,
            "#TITLE 건볼트\r\n#BPM 120\r\n#WAV01 a.wav\r\n" +
            "#00116:01010101\r\n" +
            "#00113:01010101\r\n",
            new UTF8Encoding(false));

        var source = KeyboardChartReader.Read(path);

        Assert.Equal(4, source.Notes.Count);
        Assert.All(source.Notes, n => Assert.Equal("16", n.Channel));
        Assert.Contains(source.Warnings, w => w.Contains("13 번에서 노트 4개"));
    }

    [Fact]
    public async System.Threading.Tasks.Task 건반_노트가_없는_파일은_거절한다()
    {
        var vm = MuseDashTemplate();
        var path = Write("empty.bms", "#TITLE 빈파일\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00101:0100\r\n");

        var report = await vm.ImportKeyboardChartAsync(path);

        Assert.Null(report);
        Assert.Contains("건반 노트를 찾지 못했습니다", vm.LastErrorMessage!);
    }
}

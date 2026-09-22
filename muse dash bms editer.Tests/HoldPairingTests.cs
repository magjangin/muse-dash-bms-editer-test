using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Holds;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

// 홀드·샌드백 짝 읽기.
//
// 뮤즈 대시는 파일명의 "시작/끝" 글자를 보지 않는다. 같은 채널에서 같은 종류가 나온 순서로
// 홀수=시작, 짝수=끝이다. 그래서 중간에 하나를 끼워 넣거나 빠뜨리면 그 뒤의 짝이 전부 뒤집힌다.
// 이 테스트가 못 박는 것은 "에디터가 게임과 같은 방식으로 짝짓는가"이다.
// 규칙 출처는 Profiles/muse_dash.json 의 source 필드를 볼 것.
public sealed class HoldPairingTests
{
    private readonly ITestOutputHelper _output;

    public HoldPairingTests(ITestOutputHelper output)
    {
        _output = output;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static GameProfile Profile =>
        GameProfileCatalog.Default.Find("muse_dash")
        ?? throw new InvalidOperationException("muse_dash 프로파일이 exe 안에 없습니다.");

    // 키음 표와 노트를 한 번에 만든다. 노트마다 (레인, 마디, 파일명)만 적는다.
    private static (List<BmsNote> Notes, Dictionary<string, string> Wavs) Build(
        params (string Lane, int Measure, string FileName)[] items)
    {
        var notes = new List<BmsNote>();
        var wavs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Length; i++)
        {
            var key = (i + 1).ToString("000");
            wavs[key] = items[i].FileName;
            notes.Add(new BmsNote
            {
                LaneId = items[i].Lane,
                Measure = items[i].Measure,
                Position = 0,
                WavKey = key,
            });
        }

        return (notes, wavs);
    }

    // 실제 차트의 파일명 모양 그대로. 폴더가 앞에 붙어 있어도 규칙이 떼어내야 한다.
    private static string Hold(int scene, bool air, string label) =>
        $"{scene}번 씬 wav폴더\\{scene:00}02{(air ? "04" : "01")}_홀드 {(air ? "공중" : "지상")} {label} 노트_dt1.48.wav";

    [Fact]
    public void 프로파일이_exe_안에서_읽힌다()
    {
        var profile = Profile;

        Assert.Equal("뮤즈 대시", profile.DisplayName);
        Assert.Equal(ProfileVerification.Playtested, profile.Verification);

        var kinds = profile.HoldRules.Select(r => r.Kind).ToArray();
        Assert.Equal(new[] { "Hold", "Sandbag" }, kinds);

        // 실제 차트가 쓰는 채널이다. 여기가 비면 홀드가 하나도 안 잡힌다.
        foreach (var rule in profile.HoldRules)
        {
            Assert.Equal(HoldPairingPolicy.Alternate, rule.Policy);
            Assert.Equal(new[] { "13", "14", "15", "18" }, rule.Channels);
        }

        Assert.Empty(GameProfileCatalog.Default.LoadErrors);
    }

    [Fact]
    public void 홀드는_나온_순서로_짝지어진다()
    {
        var (notes, wavs) = Build(
            ("13", 1, Hold(1, false, "시작")),
            ("13", 2, Hold(1, false, "끝")),
            ("13", 5, Hold(1, false, "시작")),
            ("13", 6, Hold(1, false, "끝")));

        var result = HoldPairingEngine.Pair(notes, wavs, Profile);

        Assert.Equal(2, result.Links.Count);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.Links[0].Head.Measure);
        Assert.Equal(2, result.Links[0].Tail.Measure);
        Assert.All(result.Links, link => Assert.Equal("Hold", link.Kind));
    }

    [Fact]
    public void 홀드가_홀수개면_마지막_시작이_고아로_남는다()
    {
        var (notes, wavs) = Build(
            ("13", 1, Hold(1, false, "시작")),
            ("13", 2, Hold(1, false, "끝")),
            ("13", 5, Hold(1, false, "시작")));

        var result = HoldPairingEngine.Pair(notes, wavs, Profile);

        Assert.Single(result.Links);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(HoldDiagnosticKind.OrphanHead, diagnostic.Kind);
        Assert.Equal(HoldDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(5, diagnostic.Note.Measure);
    }

    // 문서(authoring_time.md)가 지목한 바로 그 사고다.
    // 끝 노트 하나가 빠지면 그 뒤의 짝이 전부 뒤집히는데, 게임은 로그 한 줄만 남긴다.
    [Fact]
    public void 중간에_끝이_빠지면_어긋난_첫_자리를_지목한다()
    {
        var (notes, wavs) = Build(
            ("13", 1, Hold(1, false, "시작")),
            // 여기 있어야 할 "끝"이 없다.
            ("13", 5, Hold(1, false, "시작")),
            ("13", 6, Hold(1, false, "끝")),
            ("13", 9, Hold(1, false, "시작")),
            ("13", 10, Hold(1, false, "끝")));

        var result = HoldPairingEngine.Pair(notes, wavs, Profile);

        // 어긋난 자리는 한 번만 알린다. 뒤를 전부 늘어놓아 봐야 원인은 이 한 곳이다.
        var parity = Assert.Single(result.Diagnostics, d => d.Kind == HoldDiagnosticKind.ParityBreak);
        Assert.Equal(5, parity.Note.Measure);

        _output.WriteLine(parity.DisplayText);
    }

    [Fact]
    public void 채널이_다르면_수열이_섞이지_않는다()
    {
        var (notes, wavs) = Build(
            ("13", 1, Hold(1, false, "시작")),
            ("14", 2, Hold(1, true, "시작")),
            ("13", 3, Hold(1, false, "끝")),
            ("14", 4, Hold(1, true, "끝")));

        var result = HoldPairingEngine.Pair(notes, wavs, Profile);

        Assert.Equal(2, result.Links.Count);
        Assert.Empty(result.Diagnostics);
        Assert.All(result.Links, link => Assert.Equal(link.Head.LaneId, link.Tail.LaneId));
    }

    // 씬 전환(0004xx)은 타입 자리가 "04"라 샌드백과 겹친다. 게임은 앞자리를 먼저 본다.
    // 순서가 뒤바뀌면 씬 전환이 샌드백 수열에 끼어들어 그 뒤의 샌드백이 전부 뒤집힌다.
    [Fact]
    public void 씬_전환은_샌드백_수열에_끼어들지_않는다()
    {
        var (notes, wavs) = Build(
            ("13", 1, "씬 전환 wav 폴더\\000410_10번 씬 전환_dt0.wav"),
            ("13", 2, Hold(1, false, "시작")),
            ("13", 3, "씬 전환 wav 폴더\\000412_12번 씬 전환_dt0.wav"),
            ("13", 4, Hold(1, false, "끝")));

        var result = HoldPairingEngine.Pair(notes, wavs, Profile);

        var link = Assert.Single(result.Links);
        Assert.Equal(2, link.Head.Measure);
        Assert.Equal(4, link.Tail.Measure);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void 하트는_홀드가_아니다()
    {
        // 0002xx(하트)는 타입 자리가 "02"라 홀드와 겹친다. 앞자리가 우선한다.
        var (notes, wavs) = Build(
            ("13", 1, "10번 씬 wav폴더\\000201_하트 지상_dt1.48.wav"),
            ("13", 2, "10번 씬 wav폴더\\000204_하트 공중_dt1.48.wav"));

        var result = HoldPairingEngine.Pair(notes, wavs, Profile);

        Assert.Empty(result.Links);
        Assert.Empty(result.Diagnostics);
    }

    // 이 컴퓨터에 실제 차트가 있으면 그것으로도 돌려 본다.
    //
    // 이 차트들은 게임에서 정상으로 플레이되는 것이라, 에디터가 게임과 같은 방식으로
    // 짝짓는다면 오류가 한 건도 나오면 안 된다. 기대값을 손으로 적는 대신 그 사실을 오라클로 쓴다.
    // 차트를 못 찾는 컴퓨터에서는 한 줄만 남기고 지나간다.
    [Fact]
    public void 실제_차트에서는_짝이_전부_맞는다()
    {
        var charts = FindRealCharts();
        if (charts.Count == 0)
        {
            _output.WriteLine("(이 컴퓨터에서 실제 차트를 찾지 못함 — 건너뜀)");
            return;
        }

        foreach (var path in charts)
        {
            var parsed = BmsParser.Parse(path);
            var wavs = parsed.WavItems.ToDictionary(
                w => w.Key,
                w => string.IsNullOrEmpty(w.SourceText) ? w.FileName : w.SourceText,
                StringComparer.OrdinalIgnoreCase);

            var result = HoldPairingEngine.Pair(parsed.Chart.Notes, wavs, Profile);
            var errors = result.Diagnostics.Where(d => d.Severity == HoldDiagnosticSeverity.Error).ToArray();

            _output.WriteLine(
                $"{Path.GetFileName(Path.GetDirectoryName(path))}: 노트 {parsed.Chart.Notes.Count} · 짝 {result.Links.Count} · 문제 {errors.Length}");
            foreach (var error in errors.Take(5))
                _output.WriteLine("    " + error.DisplayText);

            Assert.Empty(errors);
        }

        Assert.Contains(charts, path => HoldPairingEngine
            .Pair(BmsParser.Parse(path).Chart.Notes, WavTextsOf(path), Profile).Links.Count > 0);
    }

    private static Dictionary<string, string> WavTextsOf(string path) =>
        BmsParser.Parse(path).WavItems.ToDictionary(
            w => w.Key,
            w => string.IsNullOrEmpty(w.SourceText) ? w.FileName : w.SourceText,
            StringComparer.OrdinalIgnoreCase);

    // 게임 폴더는 컴퓨터마다 다르다. 환경변수로 덮어쓸 수 있게 두고, 없으면 알려진 자리를 본다.
    private static IReadOnlyList<string> FindRealCharts()
    {
        var root = Environment.GetEnvironmentVariable("MUSE_DASH_CHARTS");
        var candidates = string.IsNullOrWhiteSpace(root)
            ? new[] { @"H:\muse dash hwa\hwa" }
            : new[] { root };

        foreach (var directory in candidates)
        {
            if (!Directory.Exists(directory))
                continue;

            var found = Directory
                .EnumerateFiles(directory, "*.bms", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

            if (found.Length > 0)
                return found;
        }

        return Array.Empty<string>();
    }
}

using System.Collections.Generic;
using System.IO;
using Avalonia;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// 홀드가 격자에 "머리에서 꼬리까지 한 줄로" 실제로 그려지는지 픽셀로 본다.
//
// 좌표 계산 함수만 테스트하면, 그리는 순서가 바뀌어 몸통이 노트에 덮이거나 바인딩이 빠져
// 아무것도 안 그려져도 통과한다. 그래서 컨트롤을 실제로 그려서 읽는다.
// 그림은 %TEMP%\bms-editer-tests\muse-dash-holds 에 남는다.
// (폴더 이름이 muse-dash- 로 시작하는 이유: 원본 bms editer 저장소의 테스트가 같은 %TEMP% 아래
//  holds 폴더에 그림을 남겨서, 이름이 같으면 어느 쪽 결과인지 구별이 안 된다.)
public sealed class HoldRenderingTests
{
    // RowHeight 16 × VerticalZoom 1 × (BeatSplit 16 / GridMeasure 4) = 마디당 64px. 레인 폭 40px.
    private const double LaneWidth = 40;

    // 기본 레인은 16·11·12·13·14·15·18 순이다. 뮤즈 대시가 쓰는 13은 네 번째 칸.
    private const double Lane13Center = LaneWidth * 3.5;
    private const double Lane14Center = LaneWidth * 4.5;

    private static BmsNote Note(string lane, double at, string key = "001")
    {
        var measure = (int)System.Math.Floor(at);
        return new BmsNote { LaneId = lane, Measure = measure, Position = at - measure, WavKey = key };
    }

    private static NoteGridControl Grid(BmsNote[] notes, HoldLink[] links, bool horizontal = false) => new()
    {
        Lanes = LaneDefinition.CreateDefault(),
        Notes = notes,
        HoldLinks = links,
        MeasureCount = 4,
        RowHeight = 16,
        VerticalZoom = 1,
        HorizontalZoom = 1,
        LaneWidth = LaneWidth,
        BeatSplit = 16,
        GridMeasure = 4,
        Bpm = 120,
        IsHorizontalView = horizontal,
    };

    private static string Artifact(string name) => Path.Combine(HoldTestSupport.ArtifactDirectory("muse-dash-holds"), name);

    [Fact]
    public void 세로_보기에서_머리부터_꼬리까지_레인_가운데에_몸통이_그려진다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var head = Note("13", 1.0);
        var tail = Note("13", 3.0);
        var snapshot = GridSnapshot.Capture(Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Hold") }));
        snapshot.Save(Artifact("vertical.png"));

        // 세로 보기는 아래가 0마디라 머리가 아래(y=192), 꼬리가 위(y=64).
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 128), "Hold"),
            $"몸통 한가운데 {snapshot.At(Lane13Center, 128)}");
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 72), "Hold"), "꼬리 바로 아래까지 이어져야 한다");
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 184), "Hold"), "머리 바로 위부터 시작해야 한다");

        // 몸통은 꼬리를 넘지 않고, 다른 레인에는 번지지 않는다.
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 52), "Hold"), "꼬리 너머로 삐져나왔다");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 204), "Hold"), "머리 너머로 삐져나왔다");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane14Center, 128), "Hold"), "옆 레인에 번졌다");
    });

    [Fact]
    public void 가로_보기에서도_몸통이_레인을_따라_그려진다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var head = Note("13", 1.0);
        var tail = Note("13", 3.0);
        var snapshot = GridSnapshot.Capture(Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Hold") }, horizontal: true));
        snapshot.Save(Artifact("horizontal.png"));

        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(128, Lane13Center), "Hold"));
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(128, Lane14Center), "Hold"), "옆 레인에 번졌다");
    });

    // 몸통을 마디 비례로 늘리면 BPM 이 바뀌는 구간에서 끝이 꼬리 노트와 어긋난다.
    // 양 끝을 노트와 같은 시간축으로 구하는지 못 박는다.
    [Fact]
    public void BPM이_바뀌는_구간을_지나도_몸통_끝이_꼬리_노트와_만난다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var head = Note("13", 1.0);
        var tail = Note("13", 3.0);
        var grid = Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Hold") });
        grid.DurationSeconds = 16;
        grid.Timeline = new ChartTimeline(120, new Dictionary<int, double>(), new[] { new BpmChange(2, 0, 240) });

        var snapshot = GridSnapshot.Capture(grid);
        snapshot.Save(Artifact("bpm-change.png"));

        // 2마디부터 BPM 이 두 배라, 3마디 꼬리는 마디 비례 자리가 아니라 더 아래에 온다.
        Assert.True(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 360), "Hold"), "꼬리 바로 아래까지 이어져야 한다");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 336), "Hold"), "몸통이 꼬리 노트 너머로 늘어났다");
    });

    // 샌드백은 홀드와 색이 달라야 섞여 보이지 않는다.
    [Fact]
    public void 샌드백은_홀드와_다른_색으로_그려진다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var head = Note("13", 1.0);
        var tail = Note("13", 3.0);
        var snapshot = GridSnapshot.Capture(Grid(new[] { head, tail }, new[] { new HoldLink(head, tail, "Sandbag") }));
        snapshot.Save(Artifact("sandbag.png"));

        var pixel = snapshot.At(Lane13Center, 128);
        Assert.True(HoldTestSupport.LooksLikeHoldBody(pixel, "Sandbag"), $"샌드백 몸통 {pixel}");
        Assert.False(HoldTestSupport.LooksLikeHoldBody(pixel, "Hold"), "홀드와 색이 같으면 구별이 안 된다");
    });

    // 짝이 없으면 아무것도 칠하지 않는다. 이게 없으면 "전부 칠하는" 구현도 위 테스트를 통과한다.
    [Fact]
    public void 짝이_없으면_몸통을_그리지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var a = Note("13", 1.0);
        var b = Note("13", 3.0);
        var snapshot = GridSnapshot.Capture(Grid(new[] { a, b }, System.Array.Empty<HoldLink>()));

        Assert.False(HoldTestSupport.LooksLikeHoldBody(snapshot.At(Lane13Center, 128), "Hold"));
    });

    // 좌표만 보는 순수 함수인데도 UI 스레드에서 돌린다.
    //
    // NoteGridControl 을 처음 건드리는 쪽이 정적 브러시를 만드는데, 그 브러시들은 만든 스레드만
    // 쓸 수 있다. 이 테스트가 xunit 스레드에서 먼저 정적 초기화를 돌리면, 뒤따르는 렌더 테스트가
    // 전부 "다른 스레드가 이 개체를 소유합니다"로 죽는다. 실제로 그렇게 5개가 한꺼번에 깨졌다.
    [Fact]
    public void 몸통_사각형은_레인_가운데에서_머리부터_꼬리까지다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var vertical = NoteGridControl.ComputeHoldBodyRect(40, 40, headPos: 192, tailPos: 64, isHorizontalView: false);
        var horizontal = NoteGridControl.ComputeHoldBodyRect(40, 40, headPos: 64, tailPos: 192, isHorizontalView: true);

        var width = 40 * NoteGridControl.HoldBodyWidthRatio;
        Assert.Equal(new Rect(40 + ((40 - width) / 2), 64, width, 128), vertical);
        Assert.Equal(new Rect(64, 40 + ((40 - width) / 2), 128, width), horizontal);
    });
}

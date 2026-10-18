using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Views.Controls;
using Xunit;

namespace bms_editer.Tests;

// UID 로 노트 그림을 고르고, 격자가 그 그림을 그리는지 본다.
//
// 픽셀 다듬기는 화면 없이 바이트 배열로 확인하고, 그리기는 컨트롤을 실제로 그려 픽셀을 읽는다.
// 그림은 %TEMP%\bms-editer-tests\muse-dash-icons 에 남는다.
public sealed class NoteIconTests
{
    [Theory]
    [InlineData("010501_1번 씬 몬스터_dt0.wav", "010501")]
    [InlineData(@"C:\charts\wav\011204.wav", "011204")]
    [InlineData("C:/charts/wav/011613_hammer.ogg", "011613")]
    [InlineData("01050.wav", null)]
    [InlineData("boss_in.wav", null)]
    [InlineData("a10501.wav", null)]
    [InlineData("", null)]
    public void UID_는_파일명_앞_여섯_자리_숫자다(string text, string? expected) =>
        Assert.Equal(expected, NoteIconCatalog.UidOf(text));

    [Theory]
    [InlineData("031107", NoteDirection.Up)]
    [InlineData("021210", NoteDirection.Up)]
    [InlineData("031113", NoteDirection.Down)]
    [InlineData("081116", NoteDirection.Down)]
    [InlineData("031101", NoteDirection.None)]
    [InlineData("031104", NoteDirection.None)]
    [InlineData("0311", NoteDirection.None)]
    public void 끝_두_자리로_위_아래_등장_방향을_안다(string uid, NoteDirection expected) =>
        Assert.Equal(expected, NoteIconCatalog.DirectionOf(uid));

    [Fact]
    public void 가로로_넓은_0112와_꼬리_달린_0106만_키운다()
    {
        Assert.True(NoteIconCatalog.ScaleFor("011201") > 1.0);
        Assert.True(NoteIconCatalog.ScaleFor("011204") > 1.0);
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("011101"));
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("011607"));
        Assert.True(NoteIconCatalog.ScaleFor("010601") > 1.0, "0106 은 꼬리 선 때문에 구슬이 작아서 키운다");
    }

    [Fact]
    public void 복선의_공중만_어느_씬이든_조금_키운다()
    {
        Assert.True(NoteIconCatalog.ScaleFor("010504") > 1.0);
        Assert.True(NoteIconCatalog.ScaleFor("030510") > 1.0);
        Assert.True(NoteIconCatalog.ScaleFor("050516") > 1.0);
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("010501"));
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("030507"));
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("031004"));
        Assert.Equal(NoteIconCatalog.ScaleFor("010504"), NoteIconCatalog.ScaleFor("020504"));
    }

    [Fact]
    public void 배율은_씬이_다르면_번지지_않는다()
    {
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("020601"));
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("021201"));
        Assert.True(NoteIconCatalog.ScaleFor("021101") > 1.0);
        Assert.True(NoteIconCatalog.ScaleFor("020504") > 1.0);
        Assert.Equal(1.0, NoteIconCatalog.ScaleFor("020501"));
    }

    // premultiplied BGRA 한 픽셀을 쓴다.
    private static void Put(byte[] bgra, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        var i = ((y * width) + x) * 4;
        bgra[i + 0] = (byte)(b * a / 255);
        bgra[i + 1] = (byte)(g * a / 255);
        bgra[i + 2] = (byte)(r * a / 255);
        bgra[i + 3] = a;
    }

    // 본체(밝은 파랑 10x10)와 그 아래 그림자(가로 띠)를 가진 그림을 만든다.
    private static (byte[] Pixels, int Width, int Height) WithShadow(byte r, byte g, byte b, byte a, int shadowWidth = 50, int shadowRows = 8)
    {
        const int w = 60, h = 40;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < 10; y++)
            for (var x = 25; x < 35; x++)
                Put(pixels, w, x, y, 100, 150, 255, 255);
        for (var y = 25; y < 25 + shadowRows; y++)
            for (var x = (w - shadowWidth) / 2; x < (w + shadowWidth) / 2; x++)
                Put(pixels, w, x, y, r, g, b, a);
        return (pixels, w, h);
    }

    [Fact]
    public void 그림자는_지우고_본체만_남긴다()
    {
        var (pixels, w, h) = WithShadow(29, 7, 62, 78);

        var cleaned = NoteIconPixels.Clean(pixels, w, h, w * 4);

        Assert.NotNull(cleaned);
        Assert.Equal(10, cleaned!.Value.Width);
        Assert.Equal(10, cleaned.Value.Height);
    }

    [Fact]
    public void 해머는_가는_줄을_지우고_굵은_몸통만_남긴다()
    {
        // 몸통(30x30)에서 비스듬히 뻗어 나간 두 줄 굵기 선.
        const int w = 200, h = 120;
        var pixels = new byte[w * h * 4];
        for (var y = 60; y < 90; y++)
            for (var x = 10; x < 40; x++)
                Put(pixels, w, x, y, 100, 150, 255, 255);
        for (var x = 40; x < 190; x++)
        {
            var y = 60 - ((x - 40) / 3);
            Put(pixels, w, x, y, 80, 20, 120, 255);
            Put(pixels, w, x, y + 1, 80, 20, 120, 255);
        }

        var cleaned = NoteIconPixels.Clean(pixels, w, h, w * 4, bodyOnly: true);

        Assert.NotNull(cleaned);
        Assert.InRange(cleaned!.Value.Width, 30, 36);
        Assert.InRange(cleaned.Value.Height, 30, 36);
    }

    [Fact]
    public void 해머_몸통이_기울어져_있으면_가로로_눕힌다()
    {
        // 가로 12 x 세로 36 막대를 30도 기울여 그린다. 눕히면 가로가 세로보다 훨씬 길어야 한다.
        const int w = 120, h = 120;
        var pixels = new byte[w * h * 4];
        var angle = 30 * Math.PI / 180;
        for (var y = -18; y < 18; y++)
        {
            for (var x = -6; x < 6; x++)
            {
                var px = (int)Math.Round(60 + (x * Math.Cos(angle)) - (y * Math.Sin(angle)));
                var py = (int)Math.Round(60 + (x * Math.Sin(angle)) + (y * Math.Cos(angle)));
                Put(pixels, w, px, py, 100, 150, 255, 255);
                Put(pixels, w, px + 1, py, 100, 150, 255, 255);
            }
        }

        var tilted = NoteIconPixels.Clean(pixels, w, h, w * 4);
        var upright = NoteIconPixels.Clean(pixels, w, h, w * 4, bodyOnly: true);

        Assert.NotNull(tilted);
        Assert.NotNull(upright);
        Assert.True(tilted!.Value.Width > 25, "기울어진 채로는 가로가 넓다");
        Assert.InRange(upright!.Value.Height, 10, 18);
        Assert.InRange(upright.Value.Width, 32, 42);
    }

    [Theory]
    [InlineData(26, 9, 62, 58)]   // 공중 노트의 그림자: 알파가 더 낮다
    [InlineData(28, 9, 61, 54)]
    [InlineData(86, 98, 255, 68)] // 공중 노트 몸 아래로 이어지는 푸른 막대
    public void 그림자색이_달라도_가장_많은_반투명_색을_그림자로_본다(byte r, byte g, byte b, byte a)
    {
        var (pixels, w, h) = WithShadow(r, g, b, a);

        var cleaned = NoteIconPixels.Clean(pixels, w, h, w * 4);

        Assert.NotNull(cleaned);
        Assert.Equal(10, cleaned!.Value.Height);
    }

    [Fact]
    public void 몸이_반투명이어도_같은_값이_수백_개_겹치지_않으면_지우지_않는다()
    {
        // 고스트처럼 반투명한 몸. 알파가 100 이하여도 픽셀마다 값이 조금씩 다르다.
        const int w = 40, h = 40;
        var pixels = new byte[w * h * 4];
        for (var y = 5; y < 35; y++)
            for (var x = 5; x < 35; x++)
                Put(pixels, w, x, y, (byte)(180 + (x % 7)), (byte)(80 + (y % 11)), (byte)(170 + ((x + y) % 13)), (byte)(60 + ((x * y) % 40)));

        var cleaned = NoteIconPixels.Clean(pixels, w, h, w * 4);

        Assert.NotNull(cleaned);
        Assert.Equal(30, cleaned!.Value.Width);
        Assert.Equal(30, cleaned.Value.Height);
    }

    [Fact]
    public void 그림자가_멀리_떨어져_있어도_본체_크기에_영향을_주지_않는다()
    {
        // 공중 노트: 본체 아래 한참 떨어진 곳에 그림자. 지우지 않으면 그림 높이가 그만큼 늘어난다.
        const int w = 60, h = 200;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < 30; y++)
            for (var x = 10; x < 50; x++)
                Put(pixels, w, x, y, 255, 120, 220, 255);
        for (var y = 170; y < 185; y++)
            for (var x = 5; x < 55; x++)
                Put(pixels, w, x, y, 26, 9, 62, 58);

        var cleaned = NoteIconPixels.Clean(pixels, w, h, w * 4);

        Assert.NotNull(cleaned);
        Assert.Equal(30, cleaned!.Value.Height);
    }

    [Fact]
    public void 줄에_매달린_긴_그림은_머리만_남긴다()
    {
        const int w = 40, h = 200;
        var pixels = new byte[w * h * 4];

        // 가운데 2px 줄이 위에서 아래까지, 맨 아래 30x30 이 머리.
        for (var y = 0; y < 170; y++)
            for (var x = 19; x < 21; x++)
                Put(pixels, w, x, y, 120, 40, 200, 255);
        for (var y = 170; y < 200; y++)
            for (var x = 5; x < 35; x++)
                Put(pixels, w, x, y, 255, 120, 220, 255);

        var cleaned = NoteIconPixels.Clean(pixels, w, h, w * 4);

        Assert.NotNull(cleaned);
        // 줄 몇 픽셀이 머리 위에 남는 것은 괜찮다. 중요한 것은 세로가 가로의 2배를 넘지 않는 것.
        Assert.True(cleaned!.Value.Height <= cleaned.Value.Width * 2, $"{cleaned.Value.Width}x{cleaned.Value.Height}");
    }

    [Fact]
    public void 아무것도_안_보이는_그림은_null이다()
    {
        var cleaned = NoteIconPixels.Clean(new byte[10 * 10 * 4], 10, 10, 40);
        Assert.Null(cleaned);
    }

    // 폴더에 그림을 만들어 두고 카탈로그가 UID 로 찾아 키음 번호에 붙이는지 본다.
    private static string MakeIconFolder(params (string Uid, Color Color)[] icons)
    {
        var folder = Path.Combine(Path.GetTempPath(), "bms-editer-tests", "muse-dash-icons", "icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        foreach (var (uid, color) in icons)
        {
            var size = new PixelSize(40, 30);
            using var target = new RenderTargetBitmap(size, new Vector(96, 96));
            using (var context = target.CreateDrawingContext())
                context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 40, 30));
            target.Save(Path.Combine(folder, uid + ".png"));
        }

        return folder;
    }

    [Fact]
    public void 카탈로그는_UID_로_그림을_찾아_키음_번호에_붙인다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var folder = MakeIconFolder(("011101", Colors.Red));
        var catalog = new NoteIconCatalog(folder);

        var icons = catalog.Build(new Dictionary<string, string>
        {
            ["01"] = @"C:\x\011101_몬스터.wav",   // 그림 있음
            ["02"] = @"C:\x\011201_카메라.wav",    // UID 는 있으나 그림 파일이 없음
            ["03"] = @"C:\x\bgm.ogg",              // UID 가 없음
        });

        Assert.Single(icons);
        Assert.True(icons.ContainsKey("01"));
        Assert.False(icons.ContainsKey("02"));
        Assert.False(icons.ContainsKey("03"));
    });

    [Fact]
    public void 폴더가_없으면_빈_표를_돌려준다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var catalog = new NoteIconCatalog(Path.Combine(Path.GetTempPath(), "bms-editer-no-such-folder", Guid.NewGuid().ToString("N")));
        Assert.Empty(catalog.Build(new Dictionary<string, string> { ["01"] = "011101.wav" }));
    });

    [Fact]
    public void 깨진_그림_한_장은_무시한다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var folder = MakeIconFolder(("011101", Colors.Red));
        File.WriteAllText(Path.Combine(folder, "011102.png"), "이건 PNG 가 아니다");

        var icons = new NoteIconCatalog(folder).Build(new Dictionary<string, string>
        {
            ["01"] = "011101.wav",
            ["02"] = "011102.wav",
        });

        Assert.True(icons.ContainsKey("01"));
        Assert.False(icons.ContainsKey("02"));
    });

    private const double LaneWidth = 40;
    private const double Lane13Center = LaneWidth * 1.5 * 3.5; // 가로 줌 1.5, 기본 레인 16·11·12·13 중 네 번째

    private static NoteGridControl Grid(BmsNote[] notes, IReadOnlyDictionary<string, NoteIcon>? icons, bool circle = true) => new()
    {
        Lanes = LaneDefinition.CreateDefault(),
        Notes = notes,
        NoteIcons = icons,
        IsCircleNoteShape = circle,
        MeasureCount = 2,
        RowHeight = 16,
        VerticalZoom = 8,
        HorizontalZoom = 1.5,
        LaneWidth = LaneWidth,
        BeatSplit = 16,
        GridMeasure = 4,
        Bpm = 120,
    };

    private static BmsNote Note(string lane, double at, string key)
    {
        var measure = (int)Math.Floor(at);
        return new BmsNote { LaneId = lane, Measure = measure, Position = at - measure, WavKey = key };
    }

    // 세로 보기에서 0마디가 아래. 마디 하나 높이는 64 × 8 = 512px 이고 컨트롤 높이는 1024px.
    private static double NoteY(double measureAt) => 1024 - (measureAt * 512);

    [Fact]
    public void 그림이_있는_노트는_그림으로_없는_노트는_단색으로_그려진다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var folder = MakeIconFolder(("011101", Color.FromRgb(255, 0, 0)));
        var icons = new NoteIconCatalog(folder).Build(new Dictionary<string, string> { ["01"] = "011101.wav" });

        var withIcon = Note("13", 0.5, "01");
        var withoutIcon = Note("13", 1.0, "02");
        var snapshot = GridSnapshot.Capture(Grid(new[] { withIcon, withoutIcon }, icons));
        snapshot.Save(Path.Combine(HoldTestSupport.ArtifactDirectory("muse-dash-icons"), "icon-vs-plain.png"));

        // 그림(빨강 40x30)은 가운데가 빨갛고, 지름 30px 로 그려져 가운데에서 12px 떨어진 곳도 칠한다.
        var icon = snapshot.At(Lane13Center, NoteY(0.5));
        Assert.True(icon.R > 200 && icon.G < 60 && icon.B < 60, $"그림 가운데 {icon}");
        Assert.True(snapshot.At(Lane13Center + 12, NoteY(0.5)).R > 150, "그림이 단색 동그라미(반지름 7.5)보다 넓게 그려져야 한다");

        // 그림이 없는 노트는 예전처럼 반지름 7.5 의 단색 동그라미. 12px 떨어진 곳은 비어 있다.
        Assert.False(snapshot.At(Lane13Center + 12, NoteY(1.0)).R > 150 && snapshot.At(Lane13Center + 12, NoteY(1.0)).G < 60);
        Assert.NotEqual(Colors.Black, snapshot.At(Lane13Center, NoteY(1.0)));
    });

    [Fact]
    public void 네모_모양에서는_그림을_쓰지_않는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var folder = MakeIconFolder(("011101", Color.FromRgb(255, 0, 0)));
        var icons = new NoteIconCatalog(folder).Build(new Dictionary<string, string> { ["01"] = "011101.wav" });

        var snapshot = GridSnapshot.Capture(Grid(new[] { Note("13", 0.5, "01") }, icons, circle: false));

        // 네모 노트는 56x6 이라 가로로는 넓지만 세로로 12px 떨어진 곳은 비어 있어야 한다.
        var away = snapshot.At(Lane13Center, NoteY(0.5) - 12);
        Assert.False(away.R > 200 && away.G < 60 && away.B < 60, $"네모 모양인데 그림이 그려졌다 {away}");
    });

    [Fact]
    public void 키음_번호가_대소문자만_달라도_그림을_찾는다() => HoldTestSupport.RunOnUiThread(() =>
    {
        var folder = MakeIconFolder(("011101", Colors.Red));
        var icons = new NoteIconCatalog(folder).Build(new Dictionary<string, string> { ["0A"] = "011101.wav" });

        Assert.True(icons.ContainsKey("0a"));
    });
}

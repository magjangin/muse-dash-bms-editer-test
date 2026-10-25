using System;
using System.IO;
using System.Linq;
using bms_editer.Services;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

// 렌더해 둔 그림 폴더 → note_icons/<UID>.png 변환 규칙을 합성 그림으로 못 박는다.
// 마지막 테스트는 이 PC 의 실제 폴더와 지금 쓰는 note_icons 를 견준다(폴더가 없으면 건너뛴다).
public sealed class NoteIconImporterTests : IDisposable
{
    private readonly string _root;
    private readonly ITestOutputHelper _output;

    public NoteIconImporterTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "bms-editer-tests", "icon-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // 가운데에 불투명 사각형이 있는 그림. 둘레는 투명 여백.
    private static void WritePng(string path, int bodyWidth, int bodyHeight, int margin = 4, SKColor? color = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(bodyWidth + (margin * 2), bodyHeight + (margin * 2), SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = color ?? SKColors.Red };
            canvas.DrawRect(margin, margin, bodyWidth, bodyHeight, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static (int Width, int Height) SizeOf(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return (bitmap.Width, bitmap.Height);
    }

    private (string Source, string Target) Folders(string name)
    {
        var source = Path.Combine(_root, name, "src");
        var target = Path.Combine(_root, name, "out");
        Directory.CreateDirectory(source);
        return (source, target);
    }

    [Fact]
    public void 길과_공중은_위아래_등장_번호까지_같은_그림으로_채운다()
    {
        var (source, target) = Folders("lanes");
        WritePng(Path.Combine(source, "장면01", "0105", "0105_road.png"), 10, 10);
        WritePng(Path.Combine(source, "장면01", "0105", "0105_air.png"), 20, 20);

        var result = NoteIconImporter.Import(source, target);

        foreach (var uid in new[] { "010501", "010507", "010513" })
            Assert.True(File.Exists(Path.Combine(target, uid + ".png")), uid);
        foreach (var uid in new[] { "010504", "010510", "010516" })
            Assert.True(File.Exists(Path.Combine(target, uid + ".png")), uid);

        Assert.Equal(18, SizeOf(Path.Combine(target, "010501.png")).Width);
        Assert.Equal(28, SizeOf(Path.Combine(target, "010516.png")).Width);
        Assert.Equal(6, result.Written);
    }

    [Fact]
    public void 변형이_여럿이면_origin_Miku_hexie_순으로_고른다()
    {
        var (source, target) = Folders("variants");
        WritePng(Path.Combine(source, "장면09", "0916", "0916_road__hexie.png"), 11, 11);
        WritePng(Path.Combine(source, "장면09", "0916", "0916_road__origin.png"), 12, 12);
        WritePng(Path.Combine(source, "장면10", "1005", "1005_road__RinLen.png"), 13, 13);
        WritePng(Path.Combine(source, "장면10", "1005", "1005_road__Miku.png"), 14, 14);

        NoteIconImporter.Import(source, target);

        Assert.Equal(20, SizeOf(Path.Combine(target, "091601.png")).Width);
        Assert.Equal(22, SizeOf(Path.Combine(target, "100501.png")).Width);
    }

    [Fact]
    public void 렌더_직후의_평평한_폴더도_이름만_보고_찾는다()
    {
        var (source, target) = Folders("flat");
        WritePng(Path.Combine(source, "0105_road.png"), 10, 10);

        Assert.True(NoteIconImporter.LooksLikeSource(source));
        NoteIconImporter.Import(source, target);

        Assert.True(File.Exists(Path.Combine(target, "010501.png")));
    }

    [Fact]
    public void 하트_음표_씬전환은_씬과_무관한_UID_로_간다()
    {
        var (source, target) = Folders("misc");
        WritePng(Path.Combine(source, "하트과 노트", "note_1.png"), 10, 10);
        WritePng(Path.Combine(source, "하트과 노트", "note_2.png"), 10, 10);
        WritePng(Path.Combine(source, "장면 바꾸기", "scene_3.png"), 10, 10);
        WritePng(Path.Combine(source, "장면 바꾸기", "scene_10.png"), 10, 10);
        Directory.CreateDirectory(Path.Combine(source, "장면01"));

        NoteIconImporter.Import(source, target);

        foreach (var uid in new[] { "000201", "000204", "000301", "000304", "000403", "000410" })
            Assert.True(File.Exists(Path.Combine(target, uid + ".png")), uid);
    }

    [Fact]
    public void 보스는_대기_프레임을_쓰고_없으면_다음_동작으로_물러난다()
    {
        var (source, target) = Folders("boss");
        var frames = Path.Combine(source, "장면05", "보스", "0501", "프레임");
        WritePng(Path.Combine(frames, "standby", "standby_00.png"), 30, 40);
        WritePng(Path.Combine(frames, "far_atk_1_standby", "far_atk_1_standby_00.png"), 31, 41);
        // far_atk_2_standby 와 far_atk_2_end 가 없으면 standby 로 물러난다.

        var result = NoteIconImporter.Import(source, target);

        Assert.Equal(30, SizeOf(Path.Combine(target, "050101.png")).Width);
        Assert.Equal(30, SizeOf(Path.Combine(target, "050102.png")).Width);
        Assert.Equal(31, SizeOf(Path.Combine(target, "050107.png")).Width);
        Assert.Equal(30, SizeOf(Path.Combine(target, "050109.png")).Width);
        Assert.Equal(30, SizeOf(Path.Combine(target, "050110.png")).Width);
        Assert.Empty(result.Warnings.Where(w => w.Contains("보스")));
    }

    [Fact]
    public void 보스_원본_검열이_나뉘면_원본을_쓴다()
    {
        var (source, target) = Folders("boss-split");
        var boss = Path.Combine(source, "장면03", "보스", "0301");
        WritePng(Path.Combine(boss, "검열(hexie)", "프레임", "standby", "standby_00.png"), 10, 10);
        WritePng(Path.Combine(boss, "원본(origin)", "프레임", "standby", "standby_00.png"), 20, 20);

        NoteIconImporter.Import(source, target);

        Assert.Equal(20, SizeOf(Path.Combine(target, "030101.png")).Width);
    }

    [Fact]
    public void 긴_차체는_앞_55퍼센트만_남긴다()
    {
        var (source, target) = Folders("wide");
        var frames = Path.Combine(source, "장면04", "보스", "0401", "프레임");
        WritePng(Path.Combine(frames, "standby", "standby_00.png"), 200, 40);

        NoteIconImporter.Import(source, target);

        var (width, height) = SizeOf(Path.Combine(target, "040101.png"));
        Assert.Equal(110, width);
        Assert.Equal(40, height);
    }

    [Fact]
    public void 씬8_발사체_01_04만_돌린다()
    {
        var (source, target) = Folders("tilt");
        WritePng(Path.Combine(source, "장면08", "0806", "0806_road.png"), 100, 20);
        WritePng(Path.Combine(source, "장면08", "0806", "0806_air.png"), 100, 20);

        NoteIconImporter.Import(source, target);

        var plain = SizeOf(Path.Combine(target, "080607.png"));
        var tilted = SizeOf(Path.Combine(target, "080601.png"));
        Assert.Equal(108, plain.Width);
        Assert.NotEqual(plain, tilted);
        Assert.True(tilted.Height > plain.Height, "시계 방향으로 돌리면 세로가 늘어난다");
        Assert.Equal(tilted, SizeOf(Path.Combine(target, "080604.png")));
    }

    [Fact]
    public void 실제_폴더로_가져온_결과가_지금_쓰는_그림과_같다()
    {
        const string RealSource = @"H:\유튜브 곡 모음\음악원본 저작권 O\뮤즈대쉬\bms 에디터 노트 이미지";
        const string Current = @"H:\source\repos\muse dash bms editer\muse dash bms editer\note_icons";
        if (!Directory.Exists(RealSource) || !Directory.Exists(Current))
        {
            _output.WriteLine("실제 폴더가 없어 건너뜀");
            return;
        }

        var (_, target) = Folders("real");
        var result = NoteIconImporter.Import(RealSource, target);
        foreach (var warning in result.Warnings)
            _output.WriteLine("경고: " + warning);

        var expected = Directory.GetFiles(Current, "*.png").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        var actual = Directory.GetFiles(target, "*.png").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(expected, actual);

        var drifted = 0;
        foreach (var name in expected)
        {
            var a = File.ReadAllBytes(Path.Combine(Current, name!));
            var b = File.ReadAllBytes(Path.Combine(target, name!));
            if (a.AsSpan().SequenceEqual(b))
                continue;

            // 보스·돌린 그림은 이전에 손으로 만든 것이라 바이트까지 같지는 않다. 크기가 비슷하면 같은 그림으로 본다.
            var (cw, ch) = SizeOf(Path.Combine(Current, name!));
            var (nw, nh) = SizeOf(Path.Combine(target, name!));
            var close = Math.Abs(cw - nw) <= Math.Max(3, cw * 0.06) && Math.Abs(ch - nh) <= Math.Max(3, ch * 0.06);
            _output.WriteLine($"{name}: 현재 {cw}x{ch} / 가져옴 {nw}x{nh} {(close ? "" : "← 다름")}");
            Assert.True(close, name);
            drifted++;
        }

        _output.WriteLine($"바이트까지 같지는 않은 그림: {drifted}장 / {expected.Length}장");
    }
}

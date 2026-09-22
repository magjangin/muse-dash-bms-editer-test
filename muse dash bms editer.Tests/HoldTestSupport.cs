using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using bms_editer.Views.Controls;

namespace bms_editer.Tests;

internal static class HoldTestSupport
{
    // Avalonia 는 자기 스레드에서만 컨트롤을 그린다. 세션 하나를 어셈블리 단위로 공유한다.
    public static void RunOnUiThread(Action body) =>
        HeadlessUnitTestSession
            .GetOrStartForAssembly(typeof(HoldTestSupport).Assembly)
            .Dispatch(body, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    // 사람이 눈으로 확인할 그림을 남기는 곳. 테스트가 끝나도 지우지 않는다.
    public static string ArtifactDirectory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // 몸통 색에 가까운지. 몸통은 반투명이라 검은 바탕과 섞여 조금 어두워진다.
    // 노트 색(빨강·파랑·흰색)과는 적어도 한 채널이 크게 달라서 헷갈리지 않는다.
    public static bool LooksLikeHoldBody(Color pixel, string kind)
    {
        var expected = NoteGridControl.GetHoldColor(kind);
        return Math.Abs(pixel.R - expected.R) <= 60
            && Math.Abs(pixel.G - expected.G) <= 60
            && Math.Abs(pixel.B - expected.B) <= 60;
    }
}

// 컨트롤 하나를 실제로 그려 픽셀을 읽는다.
//
// 격자는 수천 픽셀 높이라 창 화면을 뜨면 잘린다. 컨트롤을 원하는 크기로 배치하고
// 비트맵에 통째로 그린다.
internal sealed class GridSnapshot
{
    private readonly byte[] _pixels;
    private readonly int _stride;
    private readonly WriteableBitmap _bitmap;

    private GridSnapshot(WriteableBitmap bitmap, byte[] pixels, int stride, int width, int height)
    {
        _bitmap = bitmap;
        _pixels = pixels;
        _stride = stride;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public static GridSnapshot Capture(Control control)
    {
        control.Measure(Size.Infinity);
        var desired = control.DesiredSize;
        control.Arrange(new Rect(desired));

        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(desired.Width)),
            Math.Max(1, (int)Math.Ceiling(desired.Height)));

        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        target.Render(control);

        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        byte[] pixels;
        int stride;

        using (var frame = bitmap.Lock())
        {
            target.CopyPixels(frame);
            stride = frame.RowBytes;
            pixels = new byte[stride * size.Height];
            Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
        }

        return new GridSnapshot(bitmap, pixels, stride, size.Width, size.Height);
    }

    public Color At(double x, double y)
    {
        var column = Math.Clamp((int)Math.Floor(x), 0, Width - 1);
        var row = Math.Clamp((int)Math.Floor(y), 0, Height - 1);
        var index = (row * _stride) + (column * 4);
        return Color.FromArgb(_pixels[index + 3], _pixels[index + 2], _pixels[index + 1], _pixels[index]);
    }

    public void Save(string path) => _bitmap.Save(path);
}

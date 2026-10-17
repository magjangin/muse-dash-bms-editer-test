using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace bms_editer.Services;

// 격자에 그릴 노트 그림 한 장. 그림은 이미 그림자와 빈 여백을 걷어낸 상태다.
public sealed class NoteIcon
{
    public NoteIcon(Bitmap image, double scale)
    {
        Image = image;
        Scale = scale;
    }

    public Bitmap Image { get; }

    // 기본 크기에 곱하는 값. 가로로 넓은 그림처럼 같은 칸에 넣으면 작아 보이는 그림을 키울 때 쓴다.
    public double Scale { get; }
}

// UID(파일명 앞 6자리) 로 노트 그림을 찾는다.
//
// 그림은 exe 옆 note_icons 폴더에서 <UID>.png 로 찾는다. 게임에서 뽑은 그림이라 exe 에는 넣지 않는다.
// 폴더가 없거나 그림이 없는 노트는 호출하는 쪽이 단색 동그라미로 그린다.
public sealed class NoteIconCatalog
{
    // 같은 종류(UID 3~4번째 자리)는 그림 모양이 비슷해서 배율도 같게 둔다.
    // 지금은 눈으로 보고 정한 값이다: 0112 는 가로로 넓고 낮아서 30px 에서 작아 보였다.
    private static readonly Dictionary<string, double> ScaleByKind = new()
    {
        ["12"] = 38.0 / 30.0,
    };

    private readonly string _folder;
    private readonly Dictionary<string, NoteIcon?> _byUid = new(StringComparer.Ordinal);

    public NoteIconCatalog(string folder)
    {
        _folder = folder;
    }

    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "note_icons");

    // #WAV 에 적힌 글자(경로여도 된다)의 파일명이 여섯 자리 숫자로 시작하면 그 여섯 자리.
    public static string? UidOf(string wavText)
    {
        if (string.IsNullOrWhiteSpace(wavText))
            return null;

        var name = Path.GetFileName(wavText.Replace('\\', '/'));
        if (name.Length < 6)
            return null;

        for (var i = 0; i < 6; i++)
        {
            if (name[i] is < '0' or > '9')
                return null;
        }

        return name.Substring(0, 6);
    }

    public static double ScaleFor(string uid) =>
        uid.Length == 6 && ScaleByKind.TryGetValue(uid.Substring(2, 2), out var scale) ? scale : 1.0;

    // 키음 번호 → 그림. 그림이 있는 번호만 담는다.
    public IReadOnlyDictionary<string, NoteIcon> Build(IEnumerable<KeyValuePair<string, string>> wavTable)
    {
        var result = new Dictionary<string, NoteIcon>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_folder))
            return result;

        foreach (var (key, text) in wavTable)
        {
            var uid = UidOf(text);
            if (uid is null)
                continue;

            var icon = Find(uid);
            if (icon is not null)
                result[key] = icon;
        }

        return result;
    }

    private NoteIcon? Find(string uid)
    {
        if (_byUid.TryGetValue(uid, out var cached))
            return cached;

        NoteIcon? icon = null;
        var path = Path.Combine(_folder, uid + ".png");
        if (File.Exists(path))
        {
            try
            {
                using var source = new Bitmap(path);
                var prepared = NoteIconImage.Prepare(source);
                if (prepared is not null)
                    icon = new NoteIcon(prepared, ScaleFor(uid));
            }
            catch (Exception)
            {
                // 깨진 그림 한 장이 에디터를 멈추게 하지 않는다. 그 노트는 단색으로 그려진다.
                icon = null;
            }
        }

        _byUid[uid] = icon;
        return icon;
    }
}

// 게임에서 뽑은 그림을 그대로 쓰면 그림자와 긴 줄 때문에 본체가 작아진다. 불러올 때 한 번 다듬는다.
internal static class NoteIconImage
{
    public static Bitmap? Prepare(Bitmap source)
    {
        var size = source.PixelSize;
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        byte[] pixels;
        int stride;

        using (var frame = bitmap.Lock())
        {
            source.CopyPixels(new PixelRect(size), frame.Address, frame.RowBytes * size.Height, frame.RowBytes);
            stride = frame.RowBytes;
            pixels = new byte[stride * size.Height];
            Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
        }

        var cleaned = NoteIconPixels.Clean(pixels, size.Width, size.Height, stride);
        if (cleaned is null)
            return null;

        var (data, width, height) = cleaned.Value;
        var output = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = output.Lock())
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(data, y * width * 4, frame.Address + (y * frame.RowBytes), width * 4);
        }

        return output;
    }
}

// 픽셀만 다루는 부분. 화면 없이 단위 테스트할 수 있게 따로 뗐다.
public static class NoteIconPixels
{
    // 게임이 노트 밑에 깔아 둔 그림자의 색(반투명 남보라).
    private const int ShadowB = 62, ShadowG = 7, ShadowR = 29;
    private const int ShadowTolerance = 6;

    // 이 값보다 가로가 훨씬 짧고 세로로 긴 그림(줄에 매달린 해머)은 줄을 자르고 머리만 남긴다.
    private const double TallRatio = 2.0;

    // 입력은 premultiplied BGRA. 돌려주는 것은 위 여백·아래 여백을 자른 같은 형식의 픽셀이다.
    // 보이는 픽셀이 하나도 없으면 null.
    public static (byte[] Pixels, int Width, int Height)? Clean(byte[] bgra, int width, int height, int stride)
    {
        var alpha = new byte[width * height];
        var colors = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = (y * stride) + (x * 4);
                var a = bgra[source + 3];
                var index = (y * width) + x;
                alpha[index] = a;
                if (a == 0)
                    continue;

                // premultiplied → 원래 색. 그림자 색을 찾으려면 원래 색이 필요하다.
                colors[(index * 4) + 0] = Unpremultiply(bgra[source + 0], a);
                colors[(index * 4) + 1] = Unpremultiply(bgra[source + 1], a);
                colors[(index * 4) + 2] = Unpremultiply(bgra[source + 2], a);
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var a = alpha[index];
                if (a == 0)
                    continue;

                var b = colors[(index * 4) + 0];
                var g = colors[(index * 4) + 1];
                var r = colors[(index * 4) + 2];

                var isShadow = Math.Abs(b - ShadowB) <= ShadowTolerance
                    && Math.Abs(g - ShadowG) <= ShadowTolerance
                    && Math.Abs(r - ShadowR) <= ShadowTolerance;

                // 그림자 가장자리는 안티앨리어싱으로 반투명 어두운 점이 남는다. 아래쪽에서만 걷어낸다.
                var isShadowFringe = y >= height * 0.75 && a < 255 && g < 100 && r < 150 && b < 150;

                if (isShadow || isShadowFringe)
                    alpha[index] = 0;
            }
        }

        var box = BoundingBox(alpha, width, height, 0, height);
        if (box is null)
            return null;

        var (left, top, right, bottom) = box.Value;
        var boxWidth = right - left + 1;
        var boxHeight = bottom - top + 1;

        if (boxHeight > boxWidth * TallRatio)
        {
            // 위에서부터 가로가 절반 넘게 벌어지는 줄을 머리로 본다.
            var maxWidth = 0;
            var rowWidths = new int[height];
            for (var y = top; y <= bottom; y++)
            {
                rowWidths[y] = RowWidth(alpha, width, y);
                maxWidth = Math.Max(maxWidth, rowWidths[y]);
            }

            for (var y = top; y <= bottom; y++)
            {
                if (rowWidths[y] * 2 >= maxWidth)
                {
                    top = Math.Max(top, y - 4);
                    break;
                }
            }

            box = BoundingBox(alpha, width, height, top, bottom + 1);
            if (box is null)
                return null;

            (left, top, right, bottom) = box.Value;
            boxWidth = right - left + 1;
            boxHeight = bottom - top + 1;
        }

        var output = new byte[boxWidth * boxHeight * 4];
        for (var y = 0; y < boxHeight; y++)
        {
            for (var x = 0; x < boxWidth; x++)
            {
                var sourceX = left + x;
                var sourceY = top + y;
                if (alpha[(sourceY * width) + sourceX] == 0)
                    continue;

                var from = (sourceY * stride) + (sourceX * 4);
                var to = ((y * boxWidth) + x) * 4;
                output[to + 0] = bgra[from + 0];
                output[to + 1] = bgra[from + 1];
                output[to + 2] = bgra[from + 2];
                output[to + 3] = bgra[from + 3];
            }
        }

        return (output, boxWidth, boxHeight);
    }

    private static byte Unpremultiply(byte value, byte alpha) =>
        (byte)Math.Min(255, (value * 255 + (alpha / 2)) / alpha);

    private static int RowWidth(byte[] alpha, int width, int y)
    {
        int first = -1, last = -1;
        for (var x = 0; x < width; x++)
        {
            if (alpha[(y * width) + x] <= 40)
                continue;

            if (first < 0)
                first = x;
            last = x;
        }

        return first < 0 ? 0 : last - first + 1;
    }

    private static (int Left, int Top, int Right, int Bottom)? BoundingBox(
        byte[] alpha, int width, int height, int fromRow, int toRowExclusive)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = Math.Max(0, fromRow); y < Math.Min(height, toRowExclusive); y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[(y * width) + x] == 0)
                    continue;

                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < 0 ? null : (left, top, right, bottom);
    }
}

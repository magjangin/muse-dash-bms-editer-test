using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace bms_editer.Services;

// 몬스터가 등장하는 방향. UID 끝 두 자리(yy)로 알 수 있다: 07·10 위에서, 13·16 아래에서.
public enum NoteDirection
{
    None,
    Up,
    Down,
}

// 보스 연출 노트(xx=01)가 하는 일. 보스 그림 위에 글자로 구분한다.
public enum BossMarker
{
    None,
    Enter,
    Exit,
    Start,
    End,
}

// 격자에 그릴 노트 그림 한 장. 그림은 이미 그림자와 빈 여백을 걷어낸 상태다.
public sealed class NoteIcon
{
    public NoteIcon(Bitmap image, double scale, NoteDirection direction = NoteDirection.None, BossMarker marker = BossMarker.None)
    {
        Image = image;
        Scale = scale;
        Direction = direction;
        Marker = marker;
    }

    // 보스 등장·퇴장·액션 시작·끝. 같은 보스 그림이라 구석에 글자를 얹어서 구분한다.
    public BossMarker Marker { get; }

    // 위/아래 등장 노트는 같은 그림이라, 그림 구석에 작은 삼각형을 얹어서 구분한다.
    public NoteDirection Direction { get; }

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
    // 같은 종류(UID 앞 4자리 = 씬+종류)는 그림 모양이 비슷해서 배율도 같게 둔다.
    // 종류 번호만으로 찾으면 다른 씬의 같은 번호(0106 구슬 ↔ 0206 블록)에 배율이 번져서 씬까지 같이 본다.
    // 6자리 전체(yy 까지)로 적은 항목이 있으면 그것이 먼저다.
    // 지금은 눈으로 보고 정한 값이다.
    private static readonly Dictionary<string, double> ScaleByKind = new()
    {
        // 0112 는 가로로 넓고 낮아서 30px 에서 작아 보였다.
        ["0112"] = 38.0 / 30.0,

        // 0106 은 구슬 오른쪽에 길게 꼬리 선이 달려 있어서, 폭에 맞추면 구슬이 18px 로 줄어든다.
        ["0106"] = 1.7,

        // 0211 은 낮고 넓은 날개 네모라 21px 높이로 작아 보였다.
        ["0211"] = 1.2,
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

    // 해머(종류 15). 줄이 비스듬하거나 가로로 길게 달려서 줄까지 두면 머리가 작아진다.
    private static bool IsHammer(string uid) => uid.Length == 6 && uid.AsSpan(2, 2).SequenceEqual("15");

    // 씬 번호가 00 이 아니고 종류가 01 이면 보스 연출 노트다. 끝 두 자리 07·08·09·10 은 방향이 아니라 액션 번호다.
    private static bool IsBossAction(string uid) =>
        uid.Length == 6 && !uid.AsSpan(0, 2).SequenceEqual("00") && uid.AsSpan(2, 2).SequenceEqual("01");

    public static BossMarker BossMarkerOf(string uid)
    {
        if (!IsBossAction(uid))
            return BossMarker.None;

        return uid.AsSpan(4, 2) switch
        {
            "01" => BossMarker.Enter,
            "02" => BossMarker.Exit,
            "07" or "09" => BossMarker.Start,
            "08" or "10" => BossMarker.End,
            _ => BossMarker.None,
        };
    }

    public static NoteDirection DirectionOf(string uid)
    {
        if (uid.Length != 6 || IsBossAction(uid))
            return NoteDirection.None;

        return uid.AsSpan(4, 2) switch
        {
            "07" or "10" => NoteDirection.Up,
            "13" or "16" => NoteDirection.Down,
            _ => NoteDirection.None,
        };
    }

    private const double AirDoubleNoteScale = 1.3;

    // 보스는 몸집이 커서 노트 크기로는 작게 보인다. 보스 연출 노트(종류 01)는 키워서 그린다.
    private const double BossScale = 2.0;

    // 종류가 05 이고 공중(yy 04, 위 10, 아래 16)인 그림.
    private static bool IsAirDoubleNote(string uid) =>
        uid.AsSpan(2, 2).SequenceEqual("05") && uid.AsSpan(4, 2) is "04" or "10" or "16";

    public static double ScaleFor(string uid)
    {
        if (uid.Length != 6)
            return 1.0;

        if (ScaleByKind.TryGetValue(uid, out var exact))
            return exact;

        if (ScaleByKind.TryGetValue(uid.Substring(0, 4), out var scale))
            return scale;

        // 복선(05)의 공중은 어느 씬이든 위로 길쭉한 막대가 달려 몸통이 작아 보인다.
        if (IsBossAction(uid))
            return BossScale;

        return IsAirDoubleNote(uid) ? AirDoubleNoteScale : 1.0;
    }

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
                var prepared = NoteIconImage.Prepare(source, IsHammer(uid));
                if (prepared is not null)
                    icon = new NoteIcon(prepared, ScaleFor(uid), DirectionOf(uid), BossMarkerOf(uid));
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
    public static Bitmap? Prepare(Bitmap source, bool bodyOnly = false)
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

        var cleaned = NoteIconPixels.Clean(pixels, size.Width, size.Height, stride, bodyOnly);
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
    // 반투명(알파 이 값 이하)이면서 같은 색이 이만큼 많이 모여 있으면 그림자로 본다.
    // 게임은 노트 밑에 반투명 남보라 타원을 깔아 두고, 공중 노트는 그것이 멀리 떨어져 있거나
    // 몸 아래로 푸른 막대로 이어진다. 색은 그림마다 조금씩 달라서(알파 54~78) 고정값으로 비교하지 않고
    // 그림에서 가장 많이 나오는 반투명 색을 찾는다. 고스트처럼 몸 전체가 반투명이어도
    // 같은 값이 수백 개씩 겹치지는 않아서 걸리지 않는다.
    private const int ShadowMaxAlpha = 100;
    private const int ShadowMinPixels = 300;

    // 이 값보다 가로가 훨씬 짧고 세로로 긴 그림(줄에 매달린 해머)은 줄을 자르고 머리만 남긴다.
    private const double TallRatio = 2.0;

    // 입력은 premultiplied BGRA. 돌려주는 것은 위 여백·아래 여백을 자른 같은 형식의 픽셀이다.
    // 보이는 픽셀이 하나도 없으면 null.
    // bodyOnly 이면 줄처럼 가는 부분을 모두 지우고 굵은 몸통만 남긴다(해머).
    public static (byte[] Pixels, int Width, int Height)? Clean(byte[] bgra, int width, int height, int stride, bool bodyOnly = false)
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

        var shadow = FindShadowColor(alpha, colors);

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

                // 알파가 작을수록 원래 색을 되살리는 오차가 커서, 그만큼 넉넉하게 같은 색으로 본다.
                var isShadow = shadow is { } found
                    && a <= ShadowMaxAlpha
                    && Math.Max(Math.Abs(b - found.B), Math.Max(Math.Abs(g - found.G), Math.Abs(r - found.R)))
                        <= 12 + (600 / a);

                // 그림자 가장자리는 안티앨리어싱으로 반투명 어두운 점이 남는다. 아래쪽에서만 걷어낸다.
                var isShadowFringe = y >= height * 0.75 && a < 255 && g < 100 && r < 150 && b < 150;

                if (isShadow || isShadowFringe)
                    alpha[index] = 0;
            }
        }

        if (bodyOnly)
        {
            KeepBody(alpha, width, height);
            (bgra, alpha, width, height, stride) = StandUp(bgra, alpha, width, height, stride);
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

    // 해머는 줄 방향으로 기울어져 날아오는 모습이라 몸통도 비스듬하다. 격자에서는 똑바로 세워 둔다.
    // 몸통의 긴 쪽 축(2차 모멘트의 주축)이 가로가 되도록 돌린다. 줄에 매달린 통이 옆으로 누운 모습(예전 그림과 같다).
    // 90도를 넘게 돌지는 않아서 뒤집히지 않는다.
    private static (byte[] Bgra, byte[] Alpha, int Width, int Height, int Stride) StandUp(
        byte[] bgra, byte[] alpha, int width, int height, int stride)
    {
        double count = 0, sumX = 0, sumY = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[(y * width) + x] <= 40)
                    continue;

                count++;
                sumX += x;
                sumY += y;
            }
        }

        if (count == 0)
            return (bgra, alpha, width, height, stride);

        var cx = sumX / count;
        var cy = sumY / count;
        double mu20 = 0, mu02 = 0, mu11 = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[(y * width) + x] <= 40)
                    continue;

                var dx = x - cx;
                var dy = y - cy;
                mu20 += dx * dx;
                mu02 += dy * dy;
                mu11 += dx * dy;
            }
        }

        // 거의 둥근 몸통은 긴 쪽이 없어서 방향이 흔들린다. 그런 그림은 그대로 둔다.
        var mean = (mu20 + mu02) / 2;
        var spread = Math.Sqrt((((mu20 - mu02) / 2) * ((mu20 - mu02) / 2)) + (mu11 * mu11));
        if (mean - spread <= 0 || (mean + spread) / (mean - spread) < 1.15)
            return (bgra, alpha, width, height, stride);

        // 주축이 +x 에서 아래쪽으로 theta 만큼 기울어 있다. 가로(0도)로 만들려면 -theta 만큼 시계 방향으로 돈다.
        var theta = 0.5 * Math.Atan2(2 * mu11, mu20 - mu02);
        var phi = -theta;
        while (phi > Math.PI / 2)
            phi -= Math.PI;
        while (phi <= -Math.PI / 2)
            phi += Math.PI;

        if (Math.Abs(phi) < Math.PI / 180)
            return (bgra, alpha, width, height, stride);

        var cos = Math.Cos(phi);
        var sin = Math.Sin(phi);

        // 지운 자리는 완전히 비운 채로 돌려야 줄 조각이 번지지 않는다.
        var masked = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[(y * width) + x] == 0)
                    continue;

                Array.Copy(bgra, (y * stride) + (x * 4), masked, ((y * width) + x) * 4, 4);
            }
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (alpha[(y * width) + x] == 0)
                    continue;

                var rx = ((x - cx) * cos) - ((y - cy) * sin);
                var ry = ((x - cx) * sin) + ((y - cy) * cos);
                minX = Math.Min(minX, rx);
                maxX = Math.Max(maxX, rx);
                minY = Math.Min(minY, ry);
                maxY = Math.Max(maxY, ry);
            }
        }

        var outWidth = (int)Math.Ceiling(maxX - minX) + 3;
        var outHeight = (int)Math.Ceiling(maxY - minY) + 3;
        var rotated = new byte[outWidth * outHeight * 4];
        var rotatedAlpha = new byte[outWidth * outHeight];

        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                // 출력 칸에서 거꾸로 원래 그림의 자리를 찾는다.
                var rx = x + minX - 1;
                var ry = y + minY - 1;
                var sx = cx + (rx * cos) + (ry * sin);
                var sy = cy - (rx * sin) + (ry * cos);

                var x0 = (int)Math.Floor(sx);
                var y0 = (int)Math.Floor(sy);
                var fx = sx - x0;
                var fy = sy - y0;
                var to = ((y * outWidth) + x) * 4;

                for (var channel = 0; channel < 4; channel++)
                {
                    var value =
                        (Sample(masked, width, height, x0, y0, channel) * (1 - fx) * (1 - fy))
                        + (Sample(masked, width, height, x0 + 1, y0, channel) * fx * (1 - fy))
                        + (Sample(masked, width, height, x0, y0 + 1, channel) * (1 - fx) * fy)
                        + (Sample(masked, width, height, x0 + 1, y0 + 1, channel) * fx * fy);
                    rotated[to + channel] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                }

                // 가장자리에 흐릿하게 남은 것은 없는 것으로 본다. 그래야 여백이 잘린다.
                rotatedAlpha[(y * outWidth) + x] = rotated[to + 3] < 8 ? (byte)0 : rotated[to + 3];
            }
        }

        return (rotated, rotatedAlpha, outWidth, outHeight, outWidth * 4);
    }

    private static double Sample(byte[] data, int width, int height, int x, int y, int channel) =>
        x < 0 || y < 0 || x >= width || y >= height ? 0 : data[(((y * width) + x) * 4) + channel];

    // 줄의 굵기(몇 픽셀)보다 굵은 덩어리만 남긴다. 몸통을 깎았다가(침식) 다시 부풀려서(팽창)
    // 줄은 사라지고 몸통은 제자리로 돌아온다. 원래 그림에서 부푼 자리 안의 픽셀만 살려서 몸통 모양은 그대로다.
    private const int LineThickness = 12;
    private const int BodyMargin = 3;

    private static void KeepBody(byte[] alpha, int width, int height)
    {
        var solid = new bool[width * height];
        for (var i = 0; i < solid.Length; i++)
            solid[i] = alpha[i] > 40;

        var radius = LineThickness / 2;
        var core = Erode(solid, width, height, radius);
        var grown = Dilate(core, width, height, radius + BodyMargin);

        // 몸통이 하나도 남지 않으면(그림 전체가 가늘면) 손대지 않는다.
        if (Array.IndexOf(core, true) < 0)
            return;

        for (var i = 0; i < alpha.Length; i++)
        {
            if (!grown[i])
                alpha[i] = 0;
        }
    }

    // 네모 창(반지름 radius) 안이 전부 켜져 있는 곳만 남긴다. 합 표로 계산해서 그림 크기에 거의 비례한다.
    private static bool[] Erode(bool[] on, int width, int height, int radius)
    {
        var sum = SumTable(on, width, height);
        var result = new bool[on.Length];
        for (var y = radius; y < height - radius; y++)
        {
            for (var x = radius; x < width - radius; x++)
            {
                var side = (2 * radius) + 1;
                result[(y * width) + x] = WindowSum(sum, width, x - radius, y - radius, x + radius, y + radius) == side * side;
            }
        }

        return result;
    }

    // 네모 창 안에 켜진 것이 하나라도 있으면 켠다.
    private static bool[] Dilate(bool[] on, int width, int height, int radius)
    {
        var sum = SumTable(on, width, height);
        var result = new bool[on.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - radius);
                var top = Math.Max(0, y - radius);
                var right = Math.Min(width - 1, x + radius);
                var bottom = Math.Min(height - 1, y + radius);
                result[(y * width) + x] = WindowSum(sum, width, left, top, right, bottom) > 0;
            }
        }

        return result;
    }

    // (width+1) x (height+1) 합 표. sum[(y*(width+1))+x] = 왼쪽 위 x×y 칸 안에 켜진 것의 수.
    private static int[] SumTable(bool[] on, int width, int height)
    {
        var stride = width + 1;
        var sum = new int[stride * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            var rowSum = 0;
            for (var x = 0; x < width; x++)
            {
                rowSum += on[(y * width) + x] ? 1 : 0;
                sum[((y + 1) * stride) + x + 1] = sum[(y * stride) + x + 1] + rowSum;
            }
        }

        return sum;
    }

    private static int WindowSum(int[] sum, int width, int left, int top, int right, int bottom)
    {
        var stride = width + 1;
        return sum[((bottom + 1) * stride) + right + 1]
            - sum[(top * stride) + right + 1]
            - sum[((bottom + 1) * stride) + left]
            + sum[(top * stride) + left];
    }

    // 반투명 픽셀 중 가장 많이 나오는 색(원래 색). 충분히 많지 않으면 그림자가 없다고 본다.
    private static (int B, int G, int R)? FindShadowColor(byte[] alpha, byte[] colors)
    {
        var counts = new Dictionary<(int, int, int, int), int>();
        var best = ((0, 0, 0, 0), 0);

        for (var index = 0; index < alpha.Length; index++)
        {
            var a = alpha[index];
            if (a == 0 || a > ShadowMaxAlpha)
                continue;

            var key = (colors[(index * 4) + 0], colors[(index * 4) + 1], colors[(index * 4) + 2], a);
            counts.TryGetValue(key, out var count);
            counts[key] = ++count;
            if (count > best.Item2)
                best = (key, count);
        }

        if (best.Item2 < ShadowMinPixels)
            return null;

        var (blue, green, red, _) = best.Item1;
        return (blue, green, red);
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

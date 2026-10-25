using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace bms_editer.Services;

// 가져오기 결과. 만든 그림 수와, 만들지 못한 항목의 이유.
public sealed record NoteIconImportResult(int Written, IReadOnlyList<string> Warnings);

// 렌더해 둔 노트 그림 폴더를 읽어 note_icons/<UID 6자리>.png 로 바꿔 저장한다.
//
// 렌더 폴더는 Spine Support Tool 이 만든 것이다. 장면NN\XXXX\XXXX_road.png 모양이면 폴더 구조를 따지지 않고
// 파일 이름만 보고 찾는다(렌더 직후의 평평한 폴더도 된다). 게임에서 뽑은 그림이라 저장소에는 넣지 않고,
// 사용자가 자기 PC 에서 이 가져오기를 돌려 사용자 폴더에 만든다.
//
// 이름 규칙(사용자가 정함):
//  - UID = 씬(2) + 종류(2) + 끝 두 자리. _road → 01·07·13, _air → 04·10·16 (위/아래 등장은 같은 그림을 복사).
//  - 같은 노트에 변형이 여럿이면 변형 없음 > origin > Miku > 그 밖 > hexie(검열) 순으로 고른다.
//  - 보스 연출(종류 01)은 보스 프레임의 대기 자세 한 장을 쓴다.
//  - 씬 8 보스 발사체(0806)의 01·04 는 원본이 비스듬해서 시계 방향 33도 돌린다.
public static class NoteIconImporter
{
    // 사용자별 그림 폴더. exe 를 다시 만들어도 남는다.
    public static string UserFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "muse dash bms editer",
        "note_icons");

    // 마지막으로 가져온 폴더를 적어 두는 파일. 다음에 같은 폴더를 바로 제안하려고 쓴다.
    private static string LastSourceFile => Path.Combine(Path.GetDirectoryName(UserFolder)!, "note_icon_source.txt");

    private static readonly Regex SceneFolderPattern = new(@"^장면\d\d$", RegexOptions.Compiled);
    private static readonly Regex NotePattern = new(@"^(\d\d)(\d\d)_(road|air)(?:__(.+))?\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeartNotePattern = new(@"^note_(\d+)\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SceneChangePattern = new(@"^scene_(\d+)\.png$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] RoadSuffixes = { "01", "07", "13" };
    private static readonly string[] AirSuffixes = { "04", "10", "16" };

    // 보스 연출 노트 끝 두 자리 → 쓰는 프레임 동작(앞쪽이 먼저, 없으면 뒤쪽).
    private static readonly (string[] Suffixes, string[] Actions)[] BossFrames =
    {
        (new[] { "01", "02" }, new[] { "standby" }),
        (new[] { "07", "08" }, new[] { "far_atk_1_standby", "far_atk_1_end", "standby" }),
        (new[] { "09", "10" }, new[] { "far_atk_2_standby", "far_atk_2_end", "standby" }),
    };

    // 가로가 세로의 이 배 이상이면 긴 차체(리무진 등)로 보고 앞부분만 쓴다.
    private const double WideRatio = 2.0;
    private const double WideKeepFraction = 0.55;

    // 씬 8 보스 발사체. 이 두 장은 시계 방향으로 이만큼 돌린다.
    private const double TiltDegrees = 33.0;

    public static string? LastSourceFolder()
    {
        try
        {
            if (File.Exists(LastSourceFile))
            {
                var path = File.ReadAllText(LastSourceFile).Trim();
                if (LooksLikeSource(path))
                    return path;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    // 장면NN 폴더가 하나라도 있거나, 렌더 직후의 평평한 폴더(NNKK_road.png)면 가져올 수 있는 폴더로 본다.
    public static bool LooksLikeSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        try
        {
            if (Directory.EnumerateDirectories(path).Any(d => SceneFolderPattern.IsMatch(Path.GetFileName(d))))
                return true;

            return Directory.EnumerateFiles(path, "*.png").Any(f => NotePattern.IsMatch(Path.GetFileName(f)));
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 마지막으로 쓴 폴더를 먼저 보고, 없으면 흔한 자리를 얕게 훑는다. 못 찾으면 null.
    public static string? FindSourceFolder()
    {
        var last = LastSourceFolder();
        if (last is not null)
            return last;

        var roots = new List<string>();
        foreach (var special in new[]
        {
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyPictures,
        })
        {
            var folder = Environment.GetFolderPath(special);
            if (!string.IsNullOrEmpty(folder))
                roots.Add(folder);
        }

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        roots.Add(downloads);

        foreach (var root in roots)
        {
            var found = SearchShallow(root, depth: 3);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? SearchShallow(string folder, int depth)
    {
        if (depth < 0 || !Directory.Exists(folder))
            return null;

        try
        {
            if (LooksLikeSource(folder))
                return folder;

            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || name.StartsWith('$'))
                    continue;

                var found = SearchShallow(child, depth - 1);
                if (found is not null)
                    return found;
            }
        }
        catch (Exception)
        {
            // 권한이 없는 폴더는 건너뛴다.
        }

        return null;
    }

    public static NoteIconImportResult Import(string sourceFolder, string targetFolder, Action<string>? progress = null)
    {
        var warnings = new List<string>();
        var written = 0;
        Directory.CreateDirectory(targetFolder);

        var pngs = Directory.EnumerateFiles(sourceFolder, "*.png", SearchOption.AllDirectories).ToList();

        written += ImportNotes(pngs, targetFolder, warnings, progress);
        written += ImportSimple(pngs, targetFolder, HeartNotePattern, n => $"00{n + 1:00}", new[] { "01", "04" }, "하트·음표", warnings);
        written += ImportSimple(pngs, targetFolder, SceneChangePattern, n => "0004", Array.Empty<string>(), "씬 전환", warnings, sceneChange: true);
        written += ImportBosses(sourceFolder, targetFolder, warnings, progress);

        try
        {
            File.WriteAllText(LastSourceFile, sourceFolder);
        }
        catch (Exception)
        {
        }

        return new NoteIconImportResult(written, warnings);
    }

    // 장면NN\XXXX\XXXX_road|air[__변형].png → 일반 노트 그림.
    private static int ImportNotes(List<string> pngs, string target, List<string> warnings, Action<string>? progress)
    {
        var best = new Dictionary<(string Scene, string Kind, string Lane), (int Rank, string Path)>();

        foreach (var file in pngs)
        {
            var match = NotePattern.Match(Path.GetFileName(file));
            if (!match.Success)
                continue;

            var key = (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value.ToLowerInvariant());
            var rank = VariantRank(match.Groups[4].Success ? match.Groups[4].Value : null);
            if (!best.TryGetValue(key, out var current) || rank < current.Rank)
                best[key] = (rank, file);
        }

        var written = 0;
        foreach (var ((scene, kind, lane), (_, file)) in best.OrderBy(p => p.Key))
        {
            // 보스 연출(종류 01)은 보스 프레임에서 따로 만든다.
            if (kind == "01")
                continue;

            var suffixes = lane == "road" ? RoadSuffixes : AirSuffixes;
            foreach (var suffix in suffixes)
            {
                var uid = scene + kind + suffix;

                if (scene == "08" && kind == "06" && suffix is "01" or "04")
                {
                    if (TryWriteRotated(file, Path.Combine(target, uid + ".png"), TiltDegrees, warnings))
                        written++;
                    continue;
                }

                File.Copy(file, Path.Combine(target, uid + ".png"), overwrite: true);
                written++;
            }

            progress?.Invoke($"{scene}{kind} {lane}");
        }

        return written;
    }

    private static int VariantRank(string? variant)
    {
        if (string.IsNullOrEmpty(variant))
            return 0;
        if (variant.Equals("origin", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (variant.Equals("Miku", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (variant.Equals("hexie", StringComparison.OrdinalIgnoreCase))
            return 4;
        return 3;
    }

    // 하트·음표(note_N → 00(N+1)01·04)와 씬 전환(scene_N → 0004NN)처럼 씬과 무관한 그림.
    private static int ImportSimple(
        List<string> pngs,
        string target,
        Regex pattern,
        Func<int, string> prefix,
        string[] suffixes,
        string label,
        List<string> warnings,
        bool sceneChange = false)
    {
        var written = 0;
        foreach (var file in pngs)
        {
            var match = pattern.Match(Path.GetFileName(file));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number))
                continue;

            if (sceneChange)
            {
                File.Copy(file, Path.Combine(target, $"0004{number:00}.png"), overwrite: true);
                written++;
                continue;
            }

            foreach (var suffix in suffixes)
            {
                File.Copy(file, Path.Combine(target, prefix(number) + suffix + ".png"), overwrite: true);
                written++;
            }
        }

        if (written == 0)
            warnings.Add($"{label} 그림을 찾지 못했습니다.");

        return written;
    }

    // 장면NN\보스\NN01\프레임\<동작>\<동작>_00.png → NN01yy.png. 긴 차체는 앞쪽만 남긴다.
    private static int ImportBosses(string source, string target, List<string> warnings, Action<string>? progress)
    {
        var written = 0;
        foreach (var sceneFolder in Directory.EnumerateDirectories(source).Where(d => SceneFolderPattern.IsMatch(Path.GetFileName(d))))
        {
            var scene = Path.GetFileName(sceneFolder).Substring(2, 2);
            var bossRoot = Path.Combine(sceneFolder, "보스", scene + "01");
            var frameRoot = FindFrameRoot(bossRoot);
            if (frameRoot is null)
                continue;

            foreach (var (suffixes, actions) in BossFrames)
            {
                var frame = actions
                    .Select(a => Path.Combine(frameRoot, a, a + "_00.png"))
                    .FirstOrDefault(File.Exists);

                if (frame is null)
                {
                    warnings.Add($"씬 {scene} 보스 {string.Join("·", suffixes)}: 쓸 프레임이 없습니다.");
                    continue;
                }

                using var bitmap = LoadTrimmed(frame);
                if (bitmap is null)
                {
                    warnings.Add($"씬 {scene} 보스 {string.Join("·", suffixes)}: 프레임이 비어 있습니다.");
                    continue;
                }

                using var cropped = KeepFrontIfWide(bitmap);
                foreach (var suffix in suffixes)
                {
                    SavePng(cropped, Path.Combine(target, scene + "01" + suffix + ".png"));
                    written++;
                }
            }

            progress?.Invoke($"{scene}01 보스");
        }

        return written;
    }

    // 프레임 폴더가 바로 있으면 거기, 아니면 하위 폴더 중 원본(origin)을 먼저(씬 3 은 원본/검열로 나뉜다).
    private static string? FindFrameRoot(string bossRoot)
    {
        if (!Directory.Exists(bossRoot))
            return null;

        if (Directory.Exists(Path.Combine(bossRoot, "프레임")))
            return Path.Combine(bossRoot, "프레임");

        var sub = Directory.EnumerateDirectories(bossRoot)
            .Where(d => Directory.Exists(Path.Combine(d, "프레임")))
            .OrderBy(d => Path.GetFileName(d).StartsWith("원본", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(d => d, StringComparer.Ordinal)
            .FirstOrDefault();

        return sub is null ? null : Path.Combine(sub, "프레임");
    }

    // ----- 그림 처리 (SkiaSharp) -----

    private static SKBitmap? LoadTrimmed(string path)
    {
        using var source = SKBitmap.Decode(path);
        return source is null ? null : TrimTransparent(source);
    }

    // 알파가 0 이 아닌 곳만 남기고 자른다. 보이는 픽셀이 없으면 null.
    public static SKBitmap? TrimTransparent(SKBitmap source)
    {
        int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).Alpha == 0)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)
            return null;

        var result = new SKBitmap(maxX - minX + 1, maxY - minY + 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(minX, minY, maxX + 1, maxY + 1), new SKRect(0, 0, result.Width, result.Height));
        return result;
    }

    // 가로로 아주 긴 그림은 왼쪽(앞) 55%만 남긴다. 아니면 같은 그림을 복사해서 돌려준다.
    public static SKBitmap KeepFrontIfWide(SKBitmap bitmap)
    {
        if (bitmap.Width < bitmap.Height * WideRatio)
            return bitmap.Copy();

        var keep = (int)Math.Round(bitmap.Width * WideKeepFraction);
        using var part = new SKBitmap(keep, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(part))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, keep, bitmap.Height), new SKRect(0, 0, keep, bitmap.Height));
        }

        return TrimTransparent(part) ?? bitmap.Copy();
    }

    // 시계 방향으로 degrees 만큼 돌려 여백을 자른다.
    public static SKBitmap? Rotate(SKBitmap source, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var width = (int)Math.Ceiling((source.Width * cos) + (source.Height * sin)) + 2;
        var height = (int)Math.Ceiling((source.Width * sin) + (source.Height * cos)) + 2;

        using var canvasBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(canvasBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateDegrees((float)degrees);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            using var paint = new SKPaint { IsAntialias = true };
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, 0, 0, sampling, paint);
        }

        return TrimTransparent(canvasBitmap);
    }

    private static bool TryWriteRotated(string sourcePath, string targetPath, double degrees, List<string> warnings)
    {
        using var source = SKBitmap.Decode(sourcePath);
        using var rotated = source is null ? null : Rotate(source, degrees);
        if (rotated is null)
        {
            warnings.Add($"{Path.GetFileName(sourcePath)} 를 돌리지 못했습니다.");
            return false;
        }

        SavePng(rotated, targetPath);
        return true;
    }

    private static void SavePng(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}

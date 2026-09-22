using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace bms_editer.Services;

// 곡 폴더 하나에서 차트·음원·영상을 고르는 규칙.
//
// 폴더 열기와 건반 BMS 가져오기가 같이 쓴다. 한때 메인 창 코드 안에만 있어서 테스트가 없었고,
// 가져오기가 같은 일을 하려면 규칙을 한 벌 더 적어야 했다.
public static class FolderMedia
{
    public static readonly IReadOnlyCollection<string> ChartExtensions = new[] { ".bms", ".bme", ".bml" };

    // 재생·파형은 NVorbis 로 풀기 때문에 OGG 만 받는다. 같은 폴더의 MP3 는 고르지 않는다.
    public static readonly IReadOnlyCollection<string> AudioExtensions = new[] { ".ogg" };

    public static readonly IReadOnlyCollection<string> VideoExtensions =
        new[] { ".mp4", ".webm", ".mov", ".avi", ".mkv", ".ogv" };

    // 폴더 이름과 같은 파일이 있으면 그것, 없으면 이름순 첫 번째. 하나도 없으면 null.
    //
    // 폴더 이름을 먼저 보는 이유: 곡 폴더에는 같은 확장자가 여럿일 수 있다(예: 원곡과 짧은 미리듣기).
    // 폴더와 이름이 같은 쪽이 그 곡의 본 파일일 가능성이 가장 높다.
    public static string? FindBestFile(string folderPath, IReadOnlyCollection<string> extensions)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var folderName = new DirectoryInfo(folderPath).Name;
        var candidates = Directory.EnumerateFiles(folderPath)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        return candidates.FirstOrDefault(path =>
                   string.Equals(Path.GetFileNameWithoutExtension(path), folderName, StringComparison.OrdinalIgnoreCase))
               ?? candidates[0];
    }
}

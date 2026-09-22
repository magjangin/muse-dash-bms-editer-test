using System;
using System.IO;
using System.Text;

namespace bms_editer.Services;

// 텍스트 파일을 "쓰다 말아서 원본이 잘리는 일"이 없도록 저장한다.
//
// File.WriteAllText 는 원본을 먼저 비우고 그 위에 쓴다. 쓰는 도중에 디스크가 차거나
// 앱이 죽으면 원본 차트가 잘린 채 남고, 이 에디터에는 Undo 도 사본도 없어서
// 되돌릴 방법이 아예 없다.
//
// 그래서 항상 같은 폴더의 임시 파일에 끝까지 다 쓴 뒤에만 원본 자리로 바꿔치기한다.
// 실패는 임시 파일 단계에서만 나므로 원본은 늘 온전하다. 덤으로 직전 내용이 .bak 로 남는다.
public static class SafeFileWriter
{
    public const string BackupExtension = ".bak";

    public static void WriteAllText(string filePath, string content, Encoding encoding)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            throw new IOException($"저장 위치를 알 수 없습니다: {filePath}");

        Directory.CreateDirectory(directory);

        // 임시 파일은 반드시 같은 폴더에 둔다. 다른 볼륨이면 바꿔치기가 원자적이지 않다.
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            WriteTempFile(tempPath, content, encoding);
            ReplaceOriginal(tempPath, fullPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void WriteTempFile(string tempPath, string content, Encoding encoding)
    {
        using var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, encoding);

        writer.Write(content);
        writer.Flush();

        // 디스크까지 내려보내고 나서야 바꿔치기한다. 여기까지 왔으면 내용은 확실히 남아 있다.
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceOriginal(string tempPath, string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            File.Move(tempPath, fullPath);
            return;
        }

        try
        {
            // 직전 내용을 .bak 로 남긴다. 저장한 뒤에야 잘못된 걸 알아채는 경우가 많다.
            File.Replace(tempPath, fullPath, fullPath + BackupExtension, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 네트워크 드라이브처럼 Replace 를 지원하지 않는 곳도 있다.
            // Move(overwrite) 도 같은 볼륨 안에서는 원자적이라 원본이 잘리지는 않는다.
            // 다만 .bak 은 남지 않는다.
            File.Move(tempPath, fullPath, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 임시 파일 정리 실패로 저장 실패 사유를 덮어쓰면 안 된다.
        }
    }
}

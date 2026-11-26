using System;
using System.IO;
using System.Text;

namespace bms_editer.Services;

// 텍스트 파일을 "쓰다 말아서 원본이 잘리는 일"이 없도록 저장한다.
//
// File.WriteAllText 는 원본을 먼저 비우고 그 위에 쓴다. 쓰는 도중에 디스크가 차거나
// 앱이 죽으면 원본 차트가 잘린 채 남고, 앱이 이미 닫힌 뒤에는 실행 취소(Undo) 기록도
// 남지 않는다.
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

        // 저장이 끝난 뒤에만 알린다. 탐색기가 옛 목록을 들고 있지 않도록.
        if (OperatingSystem.IsWindows())
            ShellNotify.FileChanged(fullPath);

        CleanStaleTempFiles(fullPath);
    }

    // 임시 파일을 다 쓰고 바꿔치기하기 전에 전원이 나가거나 프로세스가 죽으면
    // 곡 폴더에 "<차트>.<32자리>.tmp" 가 남아 저장할 때마다 쌓였다(v0.1.8 에서 정리).
    // 이 형식과 정확히 맞고, 한 시간 넘게 지난 것만 지운다. 다른 창이 지금 쓰는 임시 파일은 건드리지 않는다.
    public static int CleanStaleTempFiles(string chartPath, TimeSpan? olderThan = null)
    {
        var removed = 0;
        try
        {
            var fullPath = Path.GetFullPath(chartPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return 0;

            var prefix = Path.GetFileName(fullPath) + ".";
            var cutoff = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(1));
            foreach (var temp in Directory.EnumerateFiles(directory, prefix + "*.tmp"))
            {
                // Windows 와일드카드는 8.3 이름까지 맞춰 엉뚱한 파일이 올 수 있어 형식을 다시 본다.
                var name = Path.GetFileName(temp);
                if (name.Length != prefix.Length + 32 + ".tmp".Length
                    || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || !IsHex(name.Substring(prefix.Length, 32)))
                    continue;

                try
                {
                    if (File.GetLastWriteTimeUtc(temp) < cutoff)
                    {
                        File.Delete(temp);
                        removed++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 다음 파일로
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // 정리는 덤이다. 저장 결과에 섞지 않는다.
            System.Diagnostics.Debug.WriteLine($"임시 파일 정리 실패: {ex.Message}");
        }

        return removed;
    }

    private static bool IsHex(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        return true;
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
        var backupPath = fullPath + BackupExtension;
        EnsureNotReadOnly(backupPath);
        EnsureNotReadOnly(fullPath);

        if (!File.Exists(fullPath))
        {
            MoveWithRetry(tempPath, fullPath, overwrite: false);
            return;
        }

        // 백신 실시간 검사나 클라우드 동기화(OneDrive 등)로 인해 일시적인 파일 잠금이 발생할 수 있다.
        // 최대 3회 짧게 재시도하여 저장 실패를 방지한다.
        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // 직전 내용을 .bak 로 남긴다. 저장한 뒤에야 잘못된 걸 알아채는 경우가 많다.
                File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                lastException = ex;
                EnsureNotReadOnly(backupPath);
                EnsureNotReadOnly(fullPath);

                if (attempt < 2)
                    System.Threading.Thread.Sleep((attempt + 1) * 30);
            }
        }

        // 네트워크 드라이브처럼 Replace 를 지원하지 않거나 끝내 Replace 가 실패한 경우
        // Move(overwrite) 도 같은 볼륨 안에서는 원자적이라 원본이 잘리지는 않는다.
        try
        {
            MoveWithRetry(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            if (lastException is not null)
                throw lastException;
            throw;
        }
    }

    private static void MoveWithRetry(string source, string destination, bool overwrite)
    {
        EnsureNotReadOnly(destination);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite);
                return;
            }
            catch (Exception ex) when (attempt < 2 && (ex is IOException or UnauthorizedAccessException))
            {
                EnsureNotReadOnly(destination);
                System.Threading.Thread.Sleep((attempt + 1) * 30);
            }
        }
    }

    private static void EnsureNotReadOnly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
            // 속성 변경 실패 무시
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

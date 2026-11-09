using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace bms_editer.Services;

// 자동 저장 한 벌에 같이 적어 두는 것.
//
// BasePath 가 핵심이다. BmsWriter 는 넘겨받은 저장 경로를 기준으로 키음 상대 경로를 만드는데,
// 자동 저장 폴더를 기준으로 쓰면 복구했을 때 키음이 하나도 안 붙는다. 그래서 내용은 원본 차트 경로를
// 기준으로 만들고, 복구할 때도 같은 기준으로 읽는다.
public sealed record AutoSaveMeta(
    string? OriginalPath,
    string BasePath,
    DateTime SavedAtUtc,
    int NoteCount,
    int CodePage,
    bool HasBom,
    string Title);

public sealed record AutoSaveEntry(string Id, string Folder, string ChartFile, AutoSaveMeta Meta);

// 자동 저장과 저장 전 백업을 사용자 폴더에 쌓는 곳.
//
// 정상적으로 저장하거나 "저장 안 함"으로 닫으면 그 문서의 자동 저장을 지운다.
// 그래서 **다음 실행 때 남아 있는 것은 곧 비정상 종료의 증거**다.
//
// 다른 창에서 같은 앱을 띄워 둔 경우가 있어서, 작업 중인 폴더는 잠금 파일을 쥐고 있다.
// 프로세스가 죽으면 운영체제가 잠금을 풀어 주므로 "쥐고 있지 않은 폴더"만 복구 대상이다.
public sealed class AutoSaveStore : IDisposable
{
    public const int MaxGenerations = 5;
    public const int MaxBackups = 10;

    private const string MetaFileName = "meta.json";
    private const string LockFileName = "session.lock";
    private const string OriginalBackupName = "original.bms";

    private readonly object _gate = new();
    private string _sessionId = NewSessionId();
    private FileStream? _sessionLock;
    private bool _disposed;

    public AutoSaveStore(string? dataRoot = null)
    {
        DataRoot = dataRoot ?? DefaultDataRoot;
    }

    public static string DefaultDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "muse dash bms editer");

    public string DataRoot { get; }

    public string AutoSaveRoot => Path.Combine(DataRoot, "autosave");

    public string BackupRoot => Path.Combine(DataRoot, "backups");

    public string CurrentSessionId
    {
        get
        {
            lock (_gate)
                return _sessionId;
        }
    }

    // 키음 상대 경로의 기준이 될 경로. 아직 저장한 적 없는 문서는 자동 저장 폴더를 기준으로 삼는다.
    public string BasePathFor(string? originalPath)
    {
        lock (_gate)
            return originalPath ?? Path.Combine(AutoSaveRoot, _sessionId, "untitled.bms");
    }

    // sessionId 는 내용을 만들 때 읽어 둔 값이다. 뒤에서 쓰는 사이에 문서가 바뀌었으면 쓰지 않는다.
    public void Write(string sessionId, string content, AutoSaveMeta meta)
    {
        lock (_gate)
        {
            if (_disposed || sessionId != _sessionId)
                return;

            var folder = Path.Combine(AutoSaveRoot, sessionId);
            Directory.CreateDirectory(folder);
            EnsureSessionLock(folder);

            var stamp = meta.SavedAtUtc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            WriteAtomic(Path.Combine(folder, stamp + ".bms"), content);
            WriteAtomic(Path.Combine(folder, MetaFileName), JsonSerializer.Serialize(meta));
            PruneOldest(ChartFiles(folder), MaxGenerations);
        }
    }

    // 지금 문서의 자동 저장을 지우고 다음 문서를 새로 시작한다.
    public void DiscardCurrent()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            ReleaseSessionLock();
            TryDeleteDirectory(Path.Combine(AutoSaveRoot, _sessionId));
            _sessionId = NewSessionId();
        }
    }

    // 복구한 문서는 그 자동 저장을 이어서 쓴다. 복구 직후에 또 죽어도 복구 지점이 남아 있어야 한다.
    public void Adopt(AutoSaveEntry entry)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            ReleaseSessionLock();
            TryDeleteDirectory(Path.Combine(AutoSaveRoot, _sessionId));
            _sessionId = entry.Id;
            EnsureSessionLock(entry.Folder);
        }
    }

    // 복구할 수 있는 것, 최근 것부터. 다른 창이 쓰고 있는 폴더와 읽을 수 없는 폴더는 뺀다.
    public IReadOnlyList<AutoSaveEntry> FindRecoverable()
    {
        lock (_gate)
        {
            if (!Directory.Exists(AutoSaveRoot))
                return Array.Empty<AutoSaveEntry>();

            var entries = new List<AutoSaveEntry>();
            foreach (var folder in Directory.EnumerateDirectories(AutoSaveRoot))
            {
                var id = Path.GetFileName(folder);
                if (id == _sessionId || IsLocked(folder))
                    continue;

                if (TryReadEntry(id, folder) is { } entry)
                    entries.Add(entry);
            }

            return entries.OrderByDescending(e => e.Meta.SavedAtUtc).ToArray();
        }
    }

    public void Delete(AutoSaveEntry entry)
    {
        lock (_gate)
            TryDeleteDirectory(entry.Folder);
    }

    // 저장으로 덮어쓰기 직전의 파일을 세대별로 남긴다. 곡 폴더는 .bak 하나로 깨끗하게 둔다.
    //
    // 열었을 때의 원본은 따로 하나 둔다. 세대가 밀려 사라져도 가장 되돌리고 싶어지는 것이 그것이다.
    // 백업에 실패해도 저장은 계속한다. 사용자가 누른 것은 저장이다.
    public void BackupBeforeSave(string filePath, bool isFirstSave)
    {
        try
        {
            if (!File.Exists(filePath))
                return;

            var folder = BackupFolderFor(filePath);
            Directory.CreateDirectory(folder);

            if (isFirstSave)
                File.Copy(filePath, Path.Combine(folder, OriginalBackupName), overwrite: true);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            File.Copy(filePath, Path.Combine(folder, stamp + ".bms"), overwrite: true);

            PruneOldest(
                Directory.EnumerateFiles(folder, "*.bms")
                    .Where(f => !string.Equals(Path.GetFileName(f), OriginalBackupName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray(),
                MaxBackups);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"백업 실패: {ex.Message}");
        }
    }

    public string BackupFolderFor(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        // 이름이 같은 차트가 곡 폴더마다 있으므로 전체 경로의 해시를 붙여 나눈다.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
        return Path.Combine(BackupRoot, $"{Path.GetFileNameWithoutExtension(fullPath)}-{Convert.ToHexString(hash, 0, 4)}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            ReleaseSessionLock();
        }
    }

    private void EnsureSessionLock(string folder)
    {
        _sessionLock ??= new FileStream(
            Path.Combine(folder, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private void ReleaseSessionLock()
    {
        _sessionLock?.Dispose();
        _sessionLock = null;
    }

    private static bool IsLocked(string folder)
    {
        var path = Path.Combine(folder, LockFileName);
        if (!File.Exists(path))
            return false;

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static AutoSaveEntry? TryReadEntry(string id, string folder)
    {
        try
        {
            var chart = ChartFiles(folder).LastOrDefault();
            var metaPath = Path.Combine(folder, MetaFileName);
            if (chart is null || !File.Exists(metaPath))
                return null;

            var meta = JsonSerializer.Deserialize<AutoSaveMeta>(File.ReadAllText(metaPath));
            return meta is null ? null : new AutoSaveEntry(id, folder, chart, meta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // 파일 이름이 시각이라 이름순이 곧 시간순이다.
    private static string[] ChartFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.bms").OrderBy(f => f, StringComparer.Ordinal).ToArray();

    private static void PruneOldest(string[] oldestFirst, int keep)
    {
        for (var i = 0; i < oldestFirst.Length - keep; i++)
            File.Delete(oldestFirst[i]);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"자동 저장 정리 실패: {ex.Message}");
        }
    }

    private static string NewSessionId() => Guid.NewGuid().ToString("N");
}

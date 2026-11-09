using System;
using System.Globalization;
using System.IO;

namespace bms_editer.Services;

// 예기치 못한 오류를 파일로 남긴다.
//
// 예전에는 exe 옆 crash.log 에 썼다. Program Files 처럼 쓰기 금지된 곳이면 그 쓰기가
// catch 안에서 다시 예외를 던져 원래 오류까지 가렸고, 매번 덮어써서 한 번치만 남았다.
// 기록을 남기다 실패해도 원래 오류를 가리면 안 되므로 여기서 나는 예외는 전부 삼킨다.
public static class CrashLog
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "muse dash bms editer",
        "logs");

    // 쓴 파일의 경로. 쓰지 못했으면 null.
    public static string? Write(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(Folder, $"crash-{stamp}.log");
            var version = typeof(CrashLog).Assembly.GetName().Version;

            File.AppendAllText(path, $"[{source}] v{version}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

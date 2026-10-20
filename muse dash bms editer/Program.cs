using System;
using System.IO;
using System.Linq;
using Avalonia;
using bms_editer.Services;

namespace bms_editer;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            App.StartupFilePath = args.FirstOrDefault(IsChartFile);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, ex.ToString());
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    // 탐색기 연결로 넘어온 인자 중 실제 차트 파일만 골라낸다.
    private static bool IsChartFile(string path)
        => File.Exists(path) && FileAssociation.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

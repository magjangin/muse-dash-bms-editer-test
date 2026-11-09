using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception fatal)
                CrashLog.Write(fatal, "AppDomain");

            // 이 뒤로 프로세스가 끝난다. 살릴 수는 없어도 작업은 남긴다.
            App.EmergencySave?.Invoke();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "Task");
            e.SetObserved();
        };

        try
        {
            App.StartupFilePath = args.FirstOrDefault(IsChartFile);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Main");
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

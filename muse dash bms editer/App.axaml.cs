using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace bms_editer;

public partial class App : Application
{
    // 탐색기에서 BMS 파일을 더블클릭해 실행됐을 때 그 파일 경로. Program.Main 이 채운다.
    public static string? StartupFilePath { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            if (StartupFilePath is { } startupPath)
                mainWindow.Opened += async (_, _) => await mainWindow.OpenExternalFileAsync(startupPath);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 오른쪽 패널의 "파형으로 / 소리로" 안내가 창에서 실제로 보이고 색이 바뀌는지.
// XAML 의 Classes 바인딩이나 스타일이 틀려도 빌드는 통과하므로 창을 띄워 본다.
public sealed class GridFitAdviceUiTests
{
    private static Border FindAdviceBorder(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("gridfit"));

    [Fact]
    public void 안내가_판정에_따라_보이고_색이_바뀐다() => HoldTestSupport.RunOnUiThread(() =>
    {
        using var vm = new MainWindowViewModel();
        var window = new bms_editer.MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var border = FindAdviceBorder(window);
        Assert.False(border.IsVisible);

        vm.GridFitAdvice = GridFitAdvice.Listen;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(border.IsVisible);
        Assert.Equal(Color.Parse("#4A3510"), ((ISolidColorBrush)border.Background!).Color);
        Assert.Equal(vm.GridFitAdviceText, ((TextBlock)border.Child!).Text);

        vm.GridFitAdvice = GridFitAdvice.Waveform;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(border.IsVisible);
        Assert.Equal(Color.Parse("#1E3A2A"), ((ISolidColorBrush)border.Background!).Color);
        Assert.Equal(vm.GridFitAdviceText, ((TextBlock)border.Child!).Text);

        window.Close();
    });
}

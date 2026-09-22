using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using bms_editer.ViewModels;

namespace bms_editer.Views;

// 키음을 넓은 타일 판에서 고르는 창.
// 고른 키음이 그대로 노트를 찍는 붓이 되므로 편집 중 계속 띄워두도록 모드리스로 연다.
public partial class WavPaletteWindow : Window
{
    public WavPaletteWindow()
    {
        InitializeComponent();
    }

    public WavPaletteWindow(WavPaletteViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    // 메인 창과 같은 방식으로 고르되, 팔레트에서는 여러 개를 한 번에 담을 수 있게 한다.
    private async void OnAddWavClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WavPaletteViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "WAV 파일 선택",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("WAV 오디오") { Patterns = new[] { "*.wav" } },
            },
        });

        foreach (var path in files.Select(f => f.TryGetLocalPath()).Where(p => p is not null))
        {
            // 하나라도 실패하면 멈추고 알린다. 조용히 넘어가면 몇 개가 담겼는지 알 수 없다.
            if (vm.AddWav(path!))
                continue;

            await ConfirmWindow.ShowMessageAsync(
                this, $"키음을 추가하지 못했습니다.\n\n{vm.Owner.LastErrorMessage}", "키음 추가 실패");
            return;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using bms_editer.ViewModels;

namespace bms_editer.Views;

// 조건에 맞는 노트를 찾아 선택/삭제하거나 키음 번호를 바꾸는 창.
// 결과를 격자에서 바로 확인할 수 있도록 모달이 아닌 모드리스로 띄운다.
public partial class NoteSearchWindow : Window
{
    public NoteSearchWindow()
    {
        InitializeComponent();
    }

    public NoteSearchWindow(NoteSearchViewModel viewModel) : this()
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

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

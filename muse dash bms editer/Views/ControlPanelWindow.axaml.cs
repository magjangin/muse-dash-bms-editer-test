using Avalonia.Controls;
using Avalonia.Input;
using bms_editer.ViewModels;

namespace bms_editer.Views;

// 집계를 보면서 그 줄로 노트를 다루는 공구함 창.
//
// 누르는 것은 전부 목록 밖의 버튼이 맡는다. 여기 코드비하인드는 "줄을 두 번 누르면
// 그 줄의 노트를 선택"하는 지름길만 이어 준다. 목록 항목 안에서 명령을 조상 바인딩으로
// 끌어오다 컴파일된 바인딩에서 조용히 끊어진 적이 있어, 그 길은 다시 쓰지 않는다.
public partial class ControlPanelWindow : Window
{
    public ControlPanelWindow()
    {
        InitializeComponent();
    }

    public ControlPanelWindow(ControlPanelViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnLaneRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ControlPanelViewModel vm && vm.SelectLaneNotesCommand.CanExecute(null))
            vm.SelectLaneNotesCommand.Execute(null);
    }

    private void OnWavRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ControlPanelViewModel vm && vm.SelectWavNotesCommand.CanExecute(null))
            vm.SelectWavNotesCommand.Execute(null);
    }
}

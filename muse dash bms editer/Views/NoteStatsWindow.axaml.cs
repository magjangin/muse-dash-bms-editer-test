using Avalonia.Controls;
using bms_editer.ViewModels;

namespace bms_editer.Views;

// 집계를 보여주기만 하는 창이다. 누르는 것은 아무것도 없다.
// 골라서 다루는 쪽은 컨트롤 패널(ControlPanelWindow)이 맡는다.
public partial class NoteStatsWindow : Window
{
    public NoteStatsWindow()
    {
        InitializeComponent();
    }

    public NoteStatsWindow(NoteStatsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace bms_editer.Views;

// 3지선다 대화상자가 돌려주는 답.
//
// 창 닫기처럼 "저장하고 닫기 / 그냥 닫기 / 닫지 않기"를 물어야 하는 자리가 있다.
// 확인/취소 두 개뿐이면 사용자는 저장할 기회를 잃거나 아예 못 닫는다.
public enum ConfirmChoice
{
    // 아무것도 하지 않고 돌아가기.
    //
    // ⚠️ 반드시 0 이어야 한다. ShowDialog<T> 는 창이 결과 없이 닫히면(X 버튼, Alt+F4)
    // default(T) 를 돌려준다. 되돌릴 수 없는 답이 기본값 자리에 있으면,
    // 창을 그냥 닫았을 뿐인데 작업을 버리는 쪽으로 진행된다.
    Cancel = 0,

    // 확인/저장. 기본 버튼.
    Confirm = 1,

    // 저장하지 않고 진행.
    Alternate = 2,
}

// 공용 확인 대화상자. 확인/취소 2지선다, 알림 전용, 3지선다 세 가지로 쓴다.
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    private ConfirmWindow(
        string message,
        string title,
        string confirmText,
        string? alternateText,
        bool showCancel) : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (alternateText is not null)
        {
            AlternateButton.Content = alternateText;
            AlternateButton.IsVisible = true;
        }

        CancelButton.IsVisible = showCancel;
    }

    // 확인을 누르면 true, 취소하거나 창을 닫으면 false를 돌려준다.
    public static async Task<bool> ShowAsync(Window owner, string message, string title = "확인")
    {
        var dialog = new ConfirmWindow(message, title, "확인", alternateText: null, showCancel: true);
        return await dialog.ShowDialog<ConfirmChoice>(owner) == ConfirmChoice.Confirm;
    }

    // 확인 버튼만 있는 알림창. 저장/열기 실패처럼 알리기만 할 때 쓴다.
    public static async Task ShowMessageAsync(Window owner, string message, string title = "알림")
    {
        var dialog = new ConfirmWindow(message, title, "확인", alternateText: null, showCancel: false);
        await dialog.ShowDialog<ConfirmChoice>(owner);
    }

    // 저장 / 저장 안 함 / 취소 3지선다. 창 닫기처럼 되돌릴 수 없는 갈림길에서 쓴다.
    public static async Task<ConfirmChoice> ShowThreeWayAsync(
        Window owner,
        string message,
        string confirmText,
        string alternateText,
        string title = "확인")
    {
        var dialog = new ConfirmWindow(message, title, confirmText, alternateText, showCancel: true);
        return await dialog.ShowDialog<ConfirmChoice>(owner);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(ConfirmChoice.Confirm);

    private void OnAlternateClick(object? sender, RoutedEventArgs e) => Close(ConfirmChoice.Alternate);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(ConfirmChoice.Cancel);
}

using Avalonia;
using Avalonia.Headless;
using bms_editer;

[assembly: AvaloniaTestApplication(typeof(bms_editer.Tests.HeadlessAppFixture))]

namespace bms_editer.Tests;

// 화면 없이 창을 실제로 띄워 보는 테스트용 앱.
//
// 왜 필요한가:
// XAML 배선이 깨져도 빌드는 통과하고 예외도 안 난다. 실제로 두 번 당했다.
//   * 통계 창의 명령을 조상 바인딩으로 끌어왔더니 컴파일된 바인딩에서 조용히 실패해서,
//     줄을 눌러도 아무 일이 없었다. Command 가 null 인 버튼이었을 뿐이다.
//   * 그 전에는 버튼 스타일이 테마 템플릿에 가려 버튼처럼 보이지도 않았다.
// 단위 테스트는 뷰모델만 보므로 이런 걸 못 잡는다. 창을 실제로 만들어 봐야 한다.
//
// UseHeadlessDrawing=false + Skia 로 두면 실제로 픽셀까지 그린다.
// CaptureRenderedFrame() 으로 그림을 떠서 눈으로 확인할 수 있다.
public sealed class HeadlessAppFixture
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

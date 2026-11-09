using System;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace bms_editer.Views.Controls;

// 숫자 입력 칸. NumericUpDown 과 같고 접근성 연결만 다르다.
//
// Avalonia 12.0.5 의 NumericUpDownAutomationPeer 는 값이 바뀌면 decimal 을 UIA 로 넘기는데,
// Win32 마샬러가 decimal 을 지원하지 않아 ArgumentException 을 던진다. UI 자동화 클라이언트
// (화면 낭독기·접근성 도구 등)가 붙어 있을 때만 터져서 평소에는 안 보이고, 터지면 차트를 여는
// 도중에 바인딩이 끊겨 문서가 반쯤만 들어온다. 안쪽 TextBox 는 그대로 읽히므로 잃는 것이 거의 없다.
public sealed class NumberBox : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);
}

using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace bms_editer.Services;

// 탐색기에서 .bms/.bme/.bml 을 더블클릭하면 이 에디터로 열리게 등록한다.
//
// HKCU 아래에만 쓴다. 관리자 권한이 필요 없고 다른 사용자 계정에는 영향이 없다.
// Windows 10/11 은 사용자가 직접 고른 기본 프로그램 기록(UserChoice)을 따로 보관하므로,
// 등록 직후에는 "연결 프로그램" 에서 한 번 골라 줘야 기본값이 바뀔 수 있다.
[SupportedOSPlatform("windows")]
public static class FileAssociation
{
    public const string ProgId = "MuseDashBms.Chart";

    public static readonly string[] Extensions = { ".bms", ".bme", ".bml" };

    public static void Register(string exePath)
    {
        using (var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId)!)
        {
            progId.SetValue("", "Muse Dash BMS 차트");

            using (var icon = progId.CreateSubKey("DefaultIcon")!)
                icon.SetValue("", $"\"{exePath}\",0");

            using (var command = progId.CreateSubKey(@"shell\open\command")!)
                command.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        foreach (var extension in Extensions)
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + extension)!;
            key.SetValue("", ProgId);
        }
    }
}

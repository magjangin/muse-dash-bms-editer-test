using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace bms_editer.Services;

// 저장 직후 탐색기에 "이 파일이 바뀌었다"고 알린다.
//
// 저장은 임시 파일을 만든 뒤 원본과 바꿔치기한다(SafeFileWriter). 이 방식은 원본을 지켜 주지만
// 탐색기가 변경 알림을 놓쳐서, 새로 고침(F5) 전까지 옛 목록을 보여 주는 일이 있다.
// 알림은 화면을 다시 그리라는 힌트일 뿐이라, 실패해도 저장 결과에는 영향이 없다.
[SupportedOSPlatform("windows")]
internal static class ShellNotify
{
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, string? item2);

    public static void FileChanged(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, fullPath, null);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, directory, null);
        }
        catch (Exception)
        {
            // 알림을 못 보내도 저장은 이미 끝났다. 화면이 조금 늦게 바뀔 뿐이다.
        }
    }
}

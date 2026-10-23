using System;
using System.Collections.Generic;
using System.Globalization;

namespace bms_editer.Services;

// 뮤즈 대시 UID(zzxxyy) 끝 두 자리(yy)에 든 노트 속도.
//
// yy 는 레인·모션·속도를 한꺼번에 담는다(muse dash test docs/experiments/NOTE_SPEED_DT.md).
//   01~03 지상 기본, 04~06 공중 기본, 07~09 지상 Up, 10~12 공중 Up, 13~15 지상 Down, 16~18 공중 Down
//   각 묶음의 세 번호가 속도 1·2·3 이다. 속도 = (yy-1)%3+1, 묶음 = (yy-1)/3.
// 씬 전환(0004yy)의 yy 는 씬 번호이고, 보스 연출(xx=01)의 yy 는 동작 번호라 속도가 아니다.
public static class NoteSpeed
{
    public static readonly IReadOnlyList<int> Choices = new[] { 1, 2, 3 };

    // 속도가 든 UID 인지. 6자리 숫자여야 한다.
    public static bool HasSpeed(string? uid)
    {
        if (uid is null || uid.Length != 6)
            return false;

        if (uid.StartsWith("0004", StringComparison.Ordinal) || uid.AsSpan(2, 2).SequenceEqual("01"))
            return false;

        return TryYy(uid, out _);
    }

    // UID 의 속도(1~3). 속도가 없는 UID 는 null.
    public static int? Of(string? uid) =>
        HasSpeed(uid) && TryYy(uid!, out var yy) ? ((yy - 1) % 3) + 1 : null;

    // 같은 레인·모션에서 속도만 바꾼 UID. 속도가 없는 UID 는 그대로 돌려준다.
    public static string WithSpeed(string uid, int speed)
    {
        if (!HasSpeed(uid) || speed < 1 || speed > 3 || !TryYy(uid, out var yy))
            return uid;

        var changed = ((yy - 1) / 3 * 3) + speed;
        return string.Concat(uid.AsSpan(0, 4), changed.ToString("00", CultureInfo.InvariantCulture));
    }

    private static bool TryYy(string uid, out int yy) =>
        int.TryParse(uid.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out yy) && yy >= 1 && yy <= 18;
}

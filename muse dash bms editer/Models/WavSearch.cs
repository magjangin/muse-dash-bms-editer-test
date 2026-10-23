using System;
using System.IO;
using System.Text.RegularExpressions;

namespace bms_editer.Models;

// 키음 팔레트의 검색과 표시용 이름.
//
// 키음 파일명은 "000201_하트 지상 스피드1_dt1.48.wav" 처럼 앞의 코드와 뒤의
// 박자 보정(_dt)이 붙어 있어 눈으로 읽기에 길다. 그래서 표시할 때는 가운데
// 이름만 보이고, 검색은 파일명 전체를 대상으로 한다.
public static partial class WavSearch
{
    private static readonly char[] Initials =
    [
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
        'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
    ];

    [GeneratedRegex(@"^\d+_")]
    private static partial Regex LeadingCode();

    [GeneratedRegex(@"_dt[\d.]+$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingDelta();

    // 확장자, 앞의 코드, 뒤의 _dt 를 떼고 남은 이름. 규칙에 안 맞는 파일명은
    // 확장자만 떼서 그대로 돌려준다(떼고 나면 빈 문자열이 되는 경우도 마찬가지).
    public static string CleanName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var cleaned = TrailingDelta().Replace(LeadingCode().Replace(name, string.Empty), string.Empty).Trim();
        return cleaned.Length == 0 ? name : cleaned;
    }

    // 띄어쓰기로 나눈 낱말이 전부 들어 있어야 맞는다(AND).
    // 낱말은 번호, 파일명, 초성으로 맞춰 본다. 예) "ㅎㅌ 지상" → "하트 지상".
    public static bool Matches(BmsWavItem item, string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (!item.Key.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !ContainsWithInitials(item.FileName, token))
                return false;
        }

        return true;
    }

    // text 안에서 token 이 이어서 나오는지. token 의 초성 자음(ㅎ)은
    // 같은 자리의 완성형 글자(하)의 첫 자음과 맞으면 통과한다.
    private static bool ContainsWithInitials(string text, string token)
    {
        for (var start = 0; start + token.Length <= text.Length; start++)
        {
            var i = 0;
            while (i < token.Length && CharMatches(text[start + i], token[i]))
                i++;

            if (i == token.Length)
                return true;
        }

        return false;
    }

    private static bool CharMatches(char textChar, char tokenChar)
    {
        if (char.ToLowerInvariant(textChar) == char.ToLowerInvariant(tokenChar))
            return true;

        // 완성형 한글 가~힣 이고 token 쪽이 호환 자모 자음이면 초성만 본다.
        if (textChar is >= '가' and <= '힣' && tokenChar is >= 'ㄱ' and <= 'ㅎ')
            return Initials[(textChar - '가') / 588] == tokenChar;

        return false;
    }
}

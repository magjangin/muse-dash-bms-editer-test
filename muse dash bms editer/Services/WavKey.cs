using System;
using bms_editer.Models;

namespace bms_editer.Services;

// 키음 번호(base-36 "01" ~ "ZZ" / "001" ~ "ZZZ")를 다루는 규칙 한 곳.
//
// 같은 규칙이 뷰모델마다 한 벌씩 복사돼 있었다. 자릿수를 세는 법과 base-36 변환이
// 세 곳에 흩어지면 한 곳만 고치게 되는데, 이 값이 어긋나면 저장할 때
// BmsWriter 가 폭을 맞추면서 엉뚱한 번호의 키음을 조용히 덮어쓴다.
public static class WavKey
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public const int MinWidth = 2;
    public const int MaxWidth = 3;

    // "01", "A3", "0ZZ" 같은 키를 정수로 바꾼다.
    // BMS는 2자리와 3자리 키음 배치를 모두 쓰므로 세 자리까지 받아들인다.
    public static bool TryParse(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Length is 0 or > MaxWidth)
            return false;

        foreach (var c in trimmed)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'Z' => c - 'A' + 10,
                >= 'a' and <= 'z' => c - 'a' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                value = 0;
                return false;
            }

            value = (value * 36) + digit;
        }

        return true;
    }

    // 정해진 자릿수에 맞춰 키 문자열을 만든다. ("01", "001")
    public static string Format(int value, int width)
    {
        var chars = new char[width];
        for (var i = width - 1; i >= 0; i--)
        {
            chars[i] = Digits[value % 36];
            value /= 36;
        }

        return new string(chars);
    }

    // 그 자릿수로 나타낼 수 있는 가장 큰 값. ("ZZ" = 1295)
    public static int MaxValue(int width)
    {
        var limit = 1;
        for (var i = 0; i < width; i++)
            limit *= 36;

        return limit - 1;
    }

    // 이 차트가 쓰는 키 자릿수. 키음 테이블과 노트가 가리키는 번호를 함께 본다.
    // BmsWriter.ComputeKeyWidth 와 같은 규칙이어야 한다.
    public static int WidthOf(BmsChart chart)
    {
        var width = MinWidth;

        foreach (var key in chart.WavTable.Keys)
            width = Math.Max(width, key.Length);

        foreach (var note in chart.Notes)
            width = Math.Max(width, note.WavKey.Length);

        return Math.Clamp(width, MinWidth, MaxWidth);
    }
}

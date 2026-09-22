using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services.Holds;

// 노트 하나가 이 규칙에서 시작인지, 끝인지, 짝 수열에 드는지를 정한다.
internal static class HoldRoleClassifier
{
    public static (HoldRole Role, HoldRole Label) Classify(
        GameProfile profile,
        HoldRule rule,
        BmsNote note,
        IReadOnlyDictionary<string, string> wavTexts)
    {
        var role = rule.Role;

        switch (role.Type)
        {
            case HoldRoleSource.KeyValue:
            {
                var key = NormalizeKey(note.WavKey);
                if (role.HeadKeys.Any(k => NormalizeKey(k) == key))
                    return (HoldRole.Head, HoldRole.None);
                if (role.TailKeys.Any(k => NormalizeKey(k) == key))
                    return (HoldRole.Tail, HoldRole.None);
                return (HoldRole.None, HoldRole.None);
            }

            case HoldRoleSource.FileNameKeyword:
            {
                if (!wavTexts.TryGetValue(note.WavKey, out var text) || string.IsNullOrEmpty(text))
                    return (HoldRole.None, HoldRole.None);

                if (role.RequiredKeyword.Length > 0 && !ContainsIgnoreCase(text, role.RequiredKeyword))
                    return (HoldRole.None, HoldRole.None);

                var isHead = role.HeadKeywords.Any(k => ContainsIgnoreCase(text, k));
                var isTail = role.TailKeywords.Any(k => ContainsIgnoreCase(text, k));

                if (role.TailFirst)
                {
                    if (isTail) return (HoldRole.Tail, HoldRole.None);
                    if (isHead) return (HoldRole.Head, HoldRole.None);
                }
                else
                {
                    if (isHead) return (HoldRole.Head, HoldRole.None);
                    if (isTail) return (HoldRole.Tail, HoldRole.None);
                }

                return (HoldRole.None, HoldRole.None);
            }

            case HoldRoleSource.FileNameUid:
            {
                if (profile.UidTable is null
                    || !wavTexts.TryGetValue(note.WavKey, out var text)
                    || string.IsNullOrEmpty(text))
                {
                    return (HoldRole.None, HoldRole.None);
                }

                var type = UidTypeResolver.Resolve(profile.UidTable, text);
                if (!string.Equals(type, rule.Kind, StringComparison.OrdinalIgnoreCase))
                    return (HoldRole.None, HoldRole.None);

                return (HoldRole.Member, LabelOf(role, FileNameWithoutExtension(text)));
            }

            case HoldRoleSource.FileNameBase:
            {
                if (!wavTexts.TryGetValue(note.WavKey, out var text) || string.IsNullOrEmpty(text))
                    return (HoldRole.None, HoldRole.None);

                var name = FileNameWithoutExtension(text).Trim();
                var label = HoldRole.None;

                foreach (var suffix in role.StripSuffixes)
                {
                    if (suffix.Length == 0 || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    name = name[..^suffix.Length].Trim();
                    label = HoldRole.Tail;
                    break;
                }

                if (!role.BaseNames.Any(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase)))
                    return (HoldRole.None, HoldRole.None);

                if (label == HoldRole.None)
                    label = LabelOf(role, name);

                return (HoldRole.Member, label);
            }

            default:
                return (HoldRole.None, HoldRole.None);
        }
    }

    // 키 값 비교용. "02" 와 3자리 모드의 "002" 를 같은 값으로 본다.
    // 대문자로 올리고 앞의 0을 떼면 자릿수와 무관하게 같은 문자열이 된다.
    internal static string NormalizeKey(string key)
    {
        var trimmed = key.Trim().TrimStart('0').ToUpperInvariant();
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    // #WAV 에 적힌 글자에서 파일명만. 폴더가 역슬래시로 적혀 있어도 운영체제와 무관하게 떼어낸다.
    internal static string FileNameWithoutExtension(string wavText) =>
        Path.GetFileNameWithoutExtension(wavText.Replace('\\', '/'));

    private static HoldRole LabelOf(HoldRoleRule role, string name)
    {
        var head = role.HeadLabels.Any(l => ContainsIgnoreCase(name, l));
        var tail = role.TailLabels.Any(l => ContainsIgnoreCase(name, l));

        return head == tail ? HoldRole.None : head ? HoldRole.Head : HoldRole.Tail;
    }

    private static bool ContainsIgnoreCase(string text, string keyword) =>
        keyword.Length > 0 && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
}

// 파일명 앞자리 UID 로 노트 종류를 정한다. 게임 파서의 분기 순서를 그대로 따른다.
//
// 순서가 곧 규칙이다. 예를 들어 씬 전환(0004xx)은 타입 자리가 "04"라 샌드백과 겹치는데,
// 게임은 앞자리를 먼저 봐서 씬 전환으로 정한다. 순서를 바꾸면 씬 전환이 샌드백 짝 수열에 끼어들어
// 그 뒤의 샌드백이 전부 뒤집힌다.
internal static class UidTypeResolver
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new();

    public static string? Resolve(UidTable table, string wavText)
    {
        var name = HoldRoleClassifier.FileNameWithoutExtension(wavText);
        var lower = name.ToLowerInvariant();

        var regex = Patterns.GetOrAdd(table.Pattern, p => new Regex(p, RegexOptions.CultureInvariant));
        var match = regex.Match(name);
        var uid = match.Success ? match.Groups[match.Groups.Count > 1 ? 1 : 0].Value : null;

        string? type = null;
        var resolved = false;

        if (uid is not null)
        {
            var prefix = Slice(uid, 0, table.PrefixLength);
            var typeCode = Slice(uid, table.TypeOffset, table.TypeLength);
            var wide = Slice(uid, table.WideOffset, table.WideLength);

            if (prefix is not null && table.PrefixTypes.TryGetValue(prefix, out var prefixType))
            {
                type = prefixType;
                resolved = true;
            }
            else if (typeCode is not null && table.TypeCodes.TryGetValue(typeCode, out var codeType))
            {
                type = codeType;
                resolved = true;
            }

            if (wide is not null && table.ResolvedWideCodes.Contains(wide))
                resolved = true;

            if (typeCode is not null && table.ResolvedTypeCodes.Contains(typeCode))
                resolved = true;
        }

        if (uid is not null)
        {
            foreach (var (finalPrefix, finalType) in table.FinalPrefixes)
            {
                if (uid.StartsWith(finalPrefix, StringComparison.Ordinal))
                    return finalType;
            }
        }

        foreach (var keyword in table.BlockingKeywords)
        {
            if (keyword.Length > 0 && lower.Contains(keyword, StringComparison.Ordinal))
                return table.BlockingType;
        }

        if (resolved)
            return type;

        foreach (var fallback in table.NameFallbacks)
        {
            var hit = fallback.Keywords.Any(k => k.Length > 0 && lower.Contains(k, StringComparison.Ordinal))
                || (fallback.UidPrefix.Length > 0 && uid is not null
                    && uid.StartsWith(fallback.UidPrefix, StringComparison.Ordinal))
                || (fallback.UidTypeCode.Length > 0 && uid is not null
                    && Slice(uid, table.TypeOffset, table.TypeLength) == fallback.UidTypeCode);

            if (hit)
                return fallback.Type;
        }

        return type;
    }

    private static string? Slice(string text, int offset, int length) =>
        offset >= 0 && length > 0 && offset + length <= text.Length ? text.Substring(offset, length) : null;
}

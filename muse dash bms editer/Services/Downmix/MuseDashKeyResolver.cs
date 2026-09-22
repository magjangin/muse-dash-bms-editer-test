using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using bms_editer.Models;
using bms_editer.Services.Holds;

namespace bms_editer.Services.Downmix;

// 다운믹서가 쓸 키음 번호 묶음. 씬 하나에서 고른다.
public sealed record DownmixKeys(
    string Scene,
    string GroundNormal,
    string AirNormal,
    string GroundHoldStart,
    string GroundHoldEnd,
    string AirHoldStart,
    string AirHoldEnd);

// 지금 문서의 `#WAV` 표에서 "지상 일반 노트는 몇 번인가"를 찾아낸다.
//
// 변환기가 노트를 만들려면 뮤즈 대시 키음이 이미 문서에 등록돼 있어야 한다.
// 건반 BMS 에는 그 정보가 없다 — 그쪽 키음은 그 게임의 소리일 뿐이다.
// 그래서 **뮤즈 대시 차트(또는 키음만 채운 템플릿)를 열어 둔 상태에서** 가져오기를 한다.
public static class MuseDashKeyResolver
{
    private static readonly Regex UidPattern = new(@"^(\d{6})", RegexOptions.Compiled);

    // UID 가운데 두 자리(종류). 02=홀드, 10=일반 노트1.
    private const string HoldType = "02";
    private const string NormalType = "10";

    // UID 끝 두 자리(변형). 01=지상, 04=공중.
    private const string GroundVariant = "01";
    private const string AirVariant = "04";

    public static DownmixKeys? Resolve(
        IReadOnlyDictionary<string, string> wavTable,
        IReadOnlyList<BmsNote> existingNotes,
        out string failureReason)
    {
        // 키 -> (씬, 종류, 변형, 파일명)
        var parsed = new Dictionary<string, (string Scene, string Type, string Variant, string Name)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (key, text) in wavTable)
        {
            var name = HoldRoleClassifier.FileNameWithoutExtension(text);
            var match = UidPattern.Match(name);
            if (!match.Success)
                continue;

            var uid = match.Groups[1].Value;
            parsed[key] = (uid[..2], uid.Substring(2, 2), uid.Substring(4, 2), name);
        }

        if (parsed.Count == 0)
        {
            failureReason =
                "지금 문서에 뮤즈 대시 키음이 없습니다.\n\n" +
                "변환된 노트에 붙일 키음 번호를 고를 수 없습니다. " +
                "뮤즈 대시 차트(또는 키음만 채운 템플릿)를 먼저 연 뒤에 가져오십시오.";
            return null;
        }

        // 어느 씬을 쓸까. 지금 노트가 쓰고 있는 씬을 따른다 — 이미 그 씬의 그림으로 만든 차트다.
        // 노트가 없으면(빈 템플릿) 가장 앞 씬을 쓴다.
        var sceneRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in existingNotes)
        {
            if (parsed.TryGetValue(note.WavKey, out var info) && info.Scene != "00")
                sceneRank[info.Scene] = sceneRank.GetValueOrDefault(info.Scene) + 1;
        }

        var scenes = parsed.Values
            .Select(v => v.Scene)
            .Where(s => s != "00")
            .Distinct()
            .OrderByDescending(s => sceneRank.GetValueOrDefault(s))
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToArray();

        foreach (var scene in scenes)
        {
            var keys = TryBuildForScene(parsed, scene);
            if (keys is not null)
            {
                failureReason = "";
                return keys;
            }
        }

        failureReason =
            "지금 문서의 키음 표에서 한 씬의 일반 노트·홀드 번호를 모두 찾지 못했습니다.\n\n" +
            "지상/공중 일반 노트와 홀드 시작·끝이 모두 등록된 씬이 하나는 있어야 합니다.";
        return null;
    }

    // 1번 씬의 노트 정의 여섯 개를 **없으면 새로 적어** 넣는다.
    //
    // 건반 BMS 에는 뮤즈 대시 키음이 있을 리 없다. 그래서 가져오기는 남의 차트에서 표를 빌리는 대신
    // 빈 번호에 정의를 직접 적는다. 파일 자체는 만들지 않는다 — 소리는 그 씬의 WAV 폴더에서 온다.
    public static DownmixKeys? CreateDefinitions(
        IDictionary<string, string> wavTable, int keyWidth, out string failureReason)
    {
        var added = new List<string>();

        string? Add(string name)
        {
            for (var value = 1; value <= WavKey.MaxValue(keyWidth); value++)
            {
                var key = WavKey.Format(value, keyWidth);
                if (!wavTable.TryAdd(key, DefaultSceneFolder + name + DefaultSuffix))
                    continue;

                added.Add(key);
                return key;
            }

            return null;
        }

        var keys = new[]
        {
            Add("011001_일반 노트1 지상 노멀"),
            Add("011004_일반 노트1 공중 노멀"),
            Add("010201_홀드 지상 시작 노트"),
            Add("010201_홀드 지상 끝 노트"),
            Add("010204_홀드 공중 시작 노트"),
            Add("010204_홀드 공중 끝 노트"),
        };

        if (keys.Any(k => k is null))
        {
            // 하나라도 못 넣었으면 넣던 것을 도로 뺀다. 반쯤 만든 표를 남기지 않는다.
            foreach (var key in added)
                wavTable.Remove(key);

            failureReason = "키음 번호가 가득 차서 노트 정의를 만들 자리가 없습니다.";
            return null;
        }

        failureReason = "";
        return new DownmixKeys("01", keys[0]!, keys[1]!, keys[2]!, keys[3]!, keys[4]!, keys[5]!);
    }

    private const string DefaultSceneFolder = "1번 씬 wav폴더\\";
    private const string DefaultSuffix = "_dt1.48.wav";

    private static DownmixKeys? TryBuildForScene(
        IReadOnlyDictionary<string, (string Scene, string Type, string Variant, string Name)> parsed,
        string scene)
    {
        string? Find(string type, string variant, string? label)
        {
            foreach (var (key, info) in parsed.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (info.Scene != scene || info.Type != type || info.Variant != variant)
                    continue;

                // 홀드는 시작과 끝이 **같은 UID** 를 쓰고 파일명으로만 갈린다.
                // (예: 010201_홀드 지상 시작 노트 / 010201_홀드 지상 끝 노트)
                if (label is not null && !info.Name.Contains(label, StringComparison.OrdinalIgnoreCase))
                    continue;

                return key;
            }

            return null;
        }

        var groundNormal = Find(NormalType, GroundVariant, null);
        var airNormal = Find(NormalType, AirVariant, null);
        var groundHoldStart = Find(HoldType, GroundVariant, "시작");
        var groundHoldEnd = Find(HoldType, GroundVariant, "끝");
        var airHoldStart = Find(HoldType, AirVariant, "시작");
        var airHoldEnd = Find(HoldType, AirVariant, "끝");

        if (groundNormal is null || airNormal is null
            || groundHoldStart is null || groundHoldEnd is null
            || airHoldStart is null || airHoldEnd is null)
        {
            return null;
        }

        return new DownmixKeys(
            scene, groundNormal, airNormal,
            groundHoldStart, groundHoldEnd,
            airHoldStart, airHoldEnd);
    }
}

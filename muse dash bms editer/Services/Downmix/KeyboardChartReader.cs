using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using bms_editer.Models;
using bms_editer.Services.Holds;

namespace bms_editer.Services.Downmix;

// 건반형 BMS(4K/5K/7K)를 읽어 노트만 뽑아낸다.
//
// 롱노트는 **시작 노트와 끝 노트**로만 읽는다. 원본 게임(스타트레일 · 스타게이저 · 건볼트 등)은
// 롱노트를 건반 채널에 찍힌 시작·끝 노트(키음 값이나 `#WAV` 파일명)로 적고, 그 판정은 게임 프로파일이 한다.
// 채널 51~59 와 `#LNOBJ` 는 그 게임들이 읽지 않는다. 여기서 읽으면 게임에 없던 홀드가 생긴다.
public static class KeyboardChartReader
{
    // 1P 건반 채널을 **화면에 놓인 왼쪽→오른쪽 순서**로. 16(스크래치)이 가장 왼쪽이다.
    // 다운믹서가 좌우를 가르는 기준이라 이 순서가 규칙 그 자체다.
    public static readonly IReadOnlyList<string> KeyChannelsLeftToRight =
        new[] { "16", "11", "12", "13", "14", "15", "18", "19" };

    private static readonly Regex DataLine =
        new(@"^#(\d{3})([0-9A-Za-z]{2}):\s*([0-9A-Za-z]+)\s*$", RegexOptions.Compiled);

    private static readonly Regex HeaderLine =
        new(@"^#([A-Za-z][A-Za-z0-9]*)(?:\s+(.*))?$", RegexOptions.Compiled);

    public static KeyboardChart Read(string filePath)
    {
        var lines = BmsParser.ReadLinesForAnalysis(filePath);

        // 어느 게임 차트인지 먼저 안다. 그 게임이 노트로 읽지 않는 채널을 거르는 데 필요하다.
        var profile = GameProfileCatalog.Default.DetectFromPath(filePath);

        var header = ScanHeaders(lines);
        var warnings = new List<string>();
        var notes = ReadNotes(lines, header, profile, warnings, out var channels, out var measureCount);

        // 롱노트는 채널이 아니라 **시작·끝 노트의 키음 값**에 적혀 있다
        // (스타트레일 02/03 · 건볼트 02/19). 그 규칙은 이미 게임 프로파일에 있고,
        // 짝 맞추는 엔진도 이미 있다. 여기서 다시 적지 않고 그대로 돌린다.
        if (profile is not null)
            ApplyProfileHolds(notes, header.WavTexts, profile, warnings);

        return new KeyboardChart(
            notes, channels, header.Title, header.Bpm, measureCount, header.WavTexts, profile, warnings);
    }

    // 제목·BPM·`#WAV` 표. 데이터 줄을 몇 글자씩 끊을지가 `#WAV` 키 자릿수에서 나온다.
    private sealed record ChartHeader(
        string Title, double Bpm, int WavKeyLength, Dictionary<string, string> WavTexts);

    private static ChartHeader ScanHeaders(string[] lines)
    {
        var title = "";
        var bpm = 120.0;
        var wavKeyLength = 2;
        var wavTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '#')
                continue;

            var header = HeaderLine.Match(line);
            if (!header.Success)
                continue;

            var name = header.Groups[1].Value.ToUpperInvariant();
            var value = header.Groups[2].Success ? header.Groups[2].Value.Trim() : "";

            switch (name)
            {
                case "TITLE":
                    title = value;
                    break;
                case "BPM":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                        bpm = parsed;
                    break;
            }

            if (name.StartsWith("WAV", StringComparison.Ordinal) && name.Length > 3)
            {
                wavKeyLength = Math.Max(wavKeyLength, name.Length - 3);
                wavTexts[name[3..]] = value;
            }
        }

        return new ChartHeader(title, bpm, wavKeyLength, wavTexts);
    }

    // 마디·채널 순으로 훑는다. 같은 마디·채널이 두 줄이면 파일에 적힌 순서를 따른다.
    // 게임 프로파일의 짝 규칙이 이 순서로 시작·끝을 맞춘다.
    private static IEnumerable<(int Measure, string Channel, string Data)> DataLinesInOrder(string[] lines)
    {
        var found = new List<(int Measure, string Channel, string Data, int Order)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var match = DataLine.Match(lines[i].Trim());
            if (!match.Success)
                continue;

            found.Add((
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                match.Groups[2].Value.ToUpperInvariant(),
                match.Groups[3].Value.ToUpperInvariant(),
                i));
        }

        return found
            .OrderBy(d => d.Measure)
            .ThenBy(d => d.Channel, StringComparer.Ordinal)
            .ThenBy(d => d.Order)
            .Select(d => (d.Measure, d.Channel, d.Data));
    }

    private static List<KeyboardNote> ReadNotes(
        string[] lines,
        ChartHeader header,
        GameProfile? profile,
        List<string> warnings,
        out string[] channels,
        out int measureCount)
    {
        var layout = profile?.Downmix is { IsEmpty: false } known ? known : null;

        var notes = new List<KeyboardNote>();
        var usedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxMeasure = 0;
        var sawSecondPlayer = false;

        // 51~59 에서 건너뛴 노트 수. 원본 게임이 읽지 않는 채널이다.
        var skippedLongChannel = 0;

        // 그 게임이 안 읽는 채널에서 버린 노트 수. 왜 노트 수가 줄었는지 알려주는 데 쓴다.
        var ignoredByGame = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (measure, channel, data) in DataLinesInOrder(lines))
        {
            var slots = data.Length / header.WavKeyLength;
            if (slots == 0)
                continue;

            string Code(int slot) => data.Substring(slot * header.WavKeyLength, header.WavKeyLength);

            if (!KeyChannelsLeftToRight.Contains(channel))
            {
                // 2P 쪽(21~29 · 61~69)은 뮤즈 대시에 자리가 없다. 한 번만 알린다.
                if (!sawSecondPlayer && (channel.StartsWith("2", StringComparison.Ordinal)
                                         || channel.StartsWith("6", StringComparison.Ordinal)))
                {
                    sawSecondPlayer = true;
                    warnings.Add("2P 채널이 들어 있습니다. 뮤즈 대시는 1P만 쓰므로 건너뜁니다.");
                }

                if (channel.StartsWith("5", StringComparison.Ordinal))
                    skippedLongChannel += Enumerable.Range(0, slots).Count(slot => !IsEmptySlot(Code(slot)));

                continue;
            }

            // 그 게임이 노트로 읽지 않는 자리. 게임이 무시하는 줄을 노트로 만들면 **없던 노트가 생긴다.**
            // (건볼트는 13 을, 스타게이저는 14·15·18 을 읽지 않는다)
            if (layout is not null && !layout.Reads(channel))
            {
                var ignored = Enumerable.Range(0, slots).Count(slot => !IsEmptySlot(Code(slot)));
                if (ignored > 0)
                    ignoredByGame[channel] = ignoredByGame.GetValueOrDefault(channel) + ignored;

                continue;
            }

            maxMeasure = Math.Max(maxMeasure, measure);

            for (var slot = 0; slot < slots; slot++)
            {
                var code = Code(slot);
                if (IsEmptySlot(code))
                    continue;

                usedChannels.Add(channel);

                // 롱노트 시작·끝은 여기서 가리지 않는다. 게임 프로파일이 키음 값으로 표시한다.
                notes.Add(new KeyboardNote(
                    channel, measure, (double)slot / slots, KeyboardNoteKind.Normal, code));
            }
        }

        if (skippedLongChannel > 0)
            warnings.Add($"롱노트 채널(51~59)의 노트 {skippedLongChannel}개를 건너뛰었습니다. "
                         + "롱노트는 시작·끝 노트로만 읽습니다.");

        foreach (var (channel, count) in ignoredByGame.OrderBy(p => p.Key, StringComparer.Ordinal))
            warnings.Add($"{profile!.DisplayName} 의 노트 채널이 아닌 {channel} 번에서 노트 {count}개를 건너뛰었습니다.");

        channels = KeyChannelsLeftToRight.Where(usedChannels.Contains).ToArray();
        measureCount = maxMeasure + 1;
        return notes;
    }

    private static bool IsEmptySlot(string code)
    {
        foreach (var c in code)
        {
            if (c != '0')
                return false;
        }

        return true;
    }

    // 게임이 플레이어가 **붙잡고 있는** 노트로 치지 않는 짝. 뮤즈 대시 홀드로 옮기면 안 된다.
    // 게이트는 스타트레일의 무대 장치(열림/닫힘)라 누르는 노트가 아니다.
    private static readonly HashSet<string> NotPlayerHeld =
        new(StringComparer.OrdinalIgnoreCase) { "Gate" };

    // 게임 프로파일의 짝 규칙으로 롱노트를 찾아 표시한다.
    //
    // 규칙도 엔진도 이미 있다(Profiles/*.json · HoldPairingEngine). 다운믹서가 게임마다
    // "롱노트를 어디에 적는가"를 다시 적으면 규칙이 두 곳에 생긴다. 그래서 그대로 빌려 쓴다.
    private static void ApplyProfileHolds(
        List<KeyboardNote> notes,
        IReadOnlyDictionary<string, string> wavTexts,
        GameProfile profile,
        List<string> warnings)
    {
        // 엔진은 BmsNote 로 말한다. 자리 번호를 그대로 보존해 결과를 되짚는다.
        var asBmsNotes = notes
            .Select(n => new BmsNote
            {
                LaneId = n.Channel,
                Measure = n.Measure,
                Position = n.Position,
                WavKey = n.WavKey,
            })
            .ToList();

        var indexOf = new Dictionary<BmsNote, int>(asBmsNotes.Count);
        for (var i = 0; i < asBmsNotes.Count; i++)
            indexOf[asBmsNotes[i]] = i;

        var result = HoldPairingEngine.Pair(asBmsNotes, wavTexts, profile);
        if (result.Links.Count == 0)
            return;

        var skippedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var marked = 0;

        foreach (var link in result.Links)
        {
            if (NotPlayerHeld.Contains(link.Kind))
            {
                skippedKinds.Add(link.Kind);
                continue;
            }

            // 채널을 건너뛰는 짝은 두 레인에 걸쳐 있어 뮤즈 대시 홀드로 옮길 수 없다.
            if (!string.Equals(link.Head.LaneId, link.Tail.LaneId, StringComparison.OrdinalIgnoreCase))
            {
                skippedKinds.Add(link.Kind + "(채널 건너뜀)");
                continue;
            }

            if (!indexOf.TryGetValue(link.Head, out var head) || !indexOf.TryGetValue(link.Tail, out var tail))
                continue;

            notes[head] = notes[head] with { Kind = KeyboardNoteKind.LongStart };
            notes[tail] = notes[tail] with { Kind = KeyboardNoteKind.LongEnd };
            marked++;
        }

        if (marked > 0)
            warnings.Add($"{profile.DisplayName} 규칙으로 롱노트 {marked}개를 찾았습니다.");

        foreach (var kind in skippedKinds)
            warnings.Add($"{kind} 짝은 뮤즈 대시 홀드로 옮기지 않았습니다.");
    }
}

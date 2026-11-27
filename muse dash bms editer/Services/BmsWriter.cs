using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;

namespace bms_editer.Services;

public static class BmsWriter
{
    private const int MaxResolutionDenominator = 1920;

    public static string Write(
        BmsChart chart,
        string title,
        string artist,
        string genre,
        double bpm,
        int player,
        int rank,
        string level,
        IReadOnlyList<BmsWavItem> wavItems,
        string outputFilePath)
    {
        var sb = new StringBuilder();
        var nl = string.IsNullOrEmpty(chart.NewLine) ? "\r\n" : chart.NewLine;

        // 값이 빈 헤더는 아예 쓰지 않는다.
        // 예전에는 무조건 다 써서, 세 줄짜리 차트를 열었다 저장하면 원본에 없던
        // `#TITLE `·`#GENRE `·`#PLAYLEVEL ` 같은 빈 줄이 붙어 열한 줄이 됐다.
        AppendIfPresent(sb, "#TITLE", title, nl);
        AppendIfPresent(sb, "#ARTIST", artist, nl);
        AppendIfPresent(sb, "#GENRE", genre, nl);

        // BPM·PLAYER·RANK 는 재생에 반드시 필요한 값이라 비어 있을 수 없다. 늘 쓴다.
        sb.Append("#BPM ").Append(bpm.ToString("0.######", CultureInfo.InvariantCulture)).Append(nl);
        sb.Append("#PLAYER ").Append((player + 1).ToString(CultureInfo.InvariantCulture)).Append(nl);
        sb.Append("#RANK ").Append(rank.ToString(CultureInfo.InvariantCulture)).Append(nl);

        AppendIfPresent(sb, "#PLAYLEVEL", level, nl);

        if (double.IsFinite(chart.Header.AudioOffsetMs) && Math.Abs(chart.Header.AudioOffsetMs) > 1e-4)
            sb.Append("#BMSEDITER_OFFSET ").Append(chart.Header.AudioOffsetMs.ToString("0.####", CultureInfo.InvariantCulture)).Append(nl);

        // 에디터가 다루지 않는 헤더(#TOTAL, #STAGEFILE, #BPMxx, #STOPxx, #BMPxx 등)를
        // 읽어들인 원문 그대로 되돌려 놓는다. 없으면 저장할 때마다 사라진다.
        foreach (var raw in chart.PreservedLines)
        {
            if (!raw.IsData)
                sb.Append(raw.Text).Append(nl);
        }

        sb.Append(nl);

        var keyWidth = ComputeKeyWidth(chart, wavItems);
        var emptySlot = new string('0', keyWidth);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath)) ?? "";

        // 같은 번호가 두 번 정의돼 있으면 마지막 것만 쓴다.
        //
        // WavTable(재생에 쓰는 표)은 원래부터 마지막 것만 남기는데 WavItems 에는 둘 다
        // 들어 있어서, 저장하면 #WAV01 이 두 줄로 늘어났다. 저장할 때마다 늘어나고,
        // 재생과 파일이 서로 다른 파일을 가리키게 된다.
        var uniqueWavItems = wavItems
            .GroupBy(w => w.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var wav in uniqueWavItems)
        {
            var key = wav.Key.PadLeft(keyWidth, '0');
            sb.Append("#WAV").Append(key).Append(' ').Append(ResolveOutputPath(wav, outputDirectory)).Append(nl);
        }
        sb.Append(nl);

        var laneOrder = BuildLaneOrder(chart.Lanes);

        // 데이터 줄은 두 갈래로 모은다.
        //   * 조건 밖의 줄: 마디 순서로 정렬한다. 같은 마디 안에서는 원문 줄(BGM·마디 길이·BPM 변화 등)이
        //     먼저 오고, 편집한 건반 줄이 레인 순서로 뒤따른다.
        //   * 조건 블록의 줄(제어 줄과 갈래 안의 줄): 원문 순서 그대로 덩어리로 묶는다(GroupConditionalBlocks).
        var plainLines = new List<(int Measure, int Order, string Text)>();
        var conditionalLines = new List<(double SourceOrder, int Measure, string Text)>();

        // 조건 밖 줄이 원문 몇 번째 줄이었는지. 조건 블록을 어디서 끊을지 가르는 데 쓴다.
        var plainSourceOrders = new List<int>();

        foreach (var raw in chart.PreservedLines)
        {
            if (!raw.IsData)
                continue;

            if (raw.IsControlFlow || raw.BranchId > 0)
            {
                conditionalLines.Add((raw.Order, raw.Measure, raw.Text));
            }
            else
            {
                plainLines.Add((raw.Measure, 0, raw.Text));
                plainSourceOrders.Add(raw.Order);
            }
        }

        var groups = chart.Notes
            .GroupBy(n => (n.Measure, n.BranchId, n.LaneId))
            .OrderBy(g => g.Key.Measure)
            .ThenBy(g => g.Key.BranchId)
            .ThenBy(g => laneOrder.TryGetValue(g.Key.LaneId, out var order) ? order : int.MaxValue);

        foreach (var group in groups)
        {
            var notes = group.OrderBy(n => n.Position).ToList();
            var layers = new List<string[]>();

            // 한 줄에 담을 분할 수가 상한(1920)을 넘으면 줄을 나눈다. (SplitByResolution 주석)
            foreach (var (resolution, bucket) in SplitByResolution(notes))
                FillLayers(layers, bucket, resolution, keyWidth, emptySlot);

            var measureTag = group.Key.Measure.ToString("000", CultureInfo.InvariantCulture);
            var branchOrder = group.Key.BranchId > 0
                ? BranchNoteOrder(notes, group.Key.BranchId, chart.PreservedLines)
                : 0;
            var lIndex = laneOrder.TryGetValue(group.Key.LaneId, out var o) ? o + 1 : 100;

            foreach (var slots in layers)
            {
                var text = $"#{measureTag}{group.Key.LaneId}:{string.Concat(slots)}";
                if (group.Key.BranchId > 0)
                    conditionalLines.Add((branchOrder, group.Key.Measure, text));
                else
                    plainLines.Add((group.Key.Measure, lIndex, text));
            }

            if (group.Key.BranchId == 0)
            {
                foreach (var note in notes)
                {
                    if (note.SourceLineOrder > 0)
                        plainSourceOrders.Add(note.SourceLineOrder);
                }
            }
        }

        var dataLines = plainLines
            .Select(line => (line.Measure, Order: (double)line.Order, Lines: (IReadOnlyList<string>)new[] { line.Text }))
            .Concat(GroupConditionalBlocks(conditionalLines, plainSourceOrders));

        // OrderBy 는 안정 정렬이라 순서 값이 같은 원문 줄끼리는 담은 순서가 유지된다.
        foreach (var entry in dataLines.OrderBy(d => d.Measure).ThenBy(d => d.Order))
        {
            foreach (var line in entry.Lines)
                sb.Append(line).Append(nl);
        }

        return sb.ToString();
    }

    // 조건 블록 덩어리는 같은 마디의 조건 밖 줄(순서 값 0~100) 뒤에 나간다.
    private const double ConditionalOrderBase = 10000;

    // 조건 블록의 줄을 원문 순서대로 덩어리로 묶는다. 덩어리는 쪼개지지 않고 한꺼번에 나간다.
    //
    // 예전에는 조건 줄도 한 줄씩 (마디, 순서)로 정렬했다. 그런데 #IF 줄의 마디는 "그 앞에 나온
    // 데이터 줄의 마디"라서, 블록이 여러 마디에 걸치면 뒷마디의 **조건 밖** 줄(BGM·노트)이
    // #IF 와 #ENDIF 사이로 끼어들었다. 저장하면 늘 나오던 줄이 한 갈래에서만 나오게 됐다.
    //
    // 원문에서 조건 밖 줄이 하나도 끼지 않은 연속 구간을 한 덩어리로 본다. #IF~#ENDIF 사이는
    // 전부 갈래 안의 줄이라 두 덩어리로 갈라질 수 없다. 덩어리는 첫 줄의 마디 자리에 나간다.
    private static IEnumerable<(int Measure, double Order, IReadOnlyList<string> Lines)> GroupConditionalBlocks(
        List<(double SourceOrder, int Measure, string Text)> lines,
        List<int> plainSourceOrders)
    {
        if (lines.Count == 0)
            yield break;

        plainSourceOrders.Sort();
        var ordered = lines.OrderBy(line => line.SourceOrder).ToList();

        var first = ordered[0];
        var previousOrder = first.SourceOrder;
        var block = new List<string>();

        foreach (var line in ordered)
        {
            if (block.Count > 0 && HasPlainLineBetween(plainSourceOrders, previousOrder, line.SourceOrder))
            {
                yield return (first.Measure, ConditionalOrderBase + first.SourceOrder, block);
                first = line;
                block = new List<string>();
            }

            block.Add(line.Text);
            previousOrder = line.SourceOrder;
        }

        yield return (first.Measure, ConditionalOrderBase + first.SourceOrder, block);
    }

    // 원문에서 from 과 to 사이(양끝 제외)에 조건 밖 줄이 있었는지. sortedOrders 는 정렬돼 있어야 한다.
    private static bool HasPlainLineBetween(List<int> sortedOrders, double from, double to)
    {
        var index = sortedOrders.BinarySearch((int)Math.Floor(from) + 1);
        if (index < 0)
            index = ~index;

        return index < sortedOrders.Count && sortedOrders[index] < to;
    }

    // 갈래 안 노트 묶음이 원문 몇 번째 줄 자리에 들어가는지.
    // 파일에서 읽은 노트는 원래 줄 번호를 들고 있다. 없으면 그 갈래를 연 제어 줄 바로 뒤에 둔다.
    private static double BranchNoteOrder(IReadOnlyList<BmsNote> notes, int branchId, IReadOnlyList<BmsRawLine> preservedLines)
    {
        var sourceOrder = notes.Where(n => n.SourceLineOrder > 0).Select(n => n.SourceLineOrder).DefaultIfEmpty(0).Min();
        if (sourceOrder > 0)
            return sourceOrder;

        var opener = preservedLines
            .Where(raw => raw.IsControlFlow && raw.BranchId == branchId)
            .Select(raw => raw.Order)
            .DefaultIfEmpty(-1)
            .Min();

        return opener >= 0 ? opener + 0.5 : int.MaxValue;
    }

    private static void AppendIfPresent(StringBuilder sb, string tag, string value, string nl)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        sb.Append(tag).Append(' ').Append(value).Append(nl);
    }

    // 키 자릿수는 #WAV 테이블과 노트 키 **양쪽**의 최대 길이로 잡는다.
    //
    // 테이블만 보면, 테이블에 정의되지 않은 3자리 키를 가리키는 노트가 있을 때 keyWidth 가 2 로
    // 잡히고, 아래 슬롯 채우기의 Substring(0, keyWidth) 이 "0ZZ" 를 "0Z" 로 잘라 버린다.
    // 노트가 전혀 다른 소리를 가리키게 되는데 아무 경고도 없다.
    private static int ComputeKeyWidth(BmsChart chart, IReadOnlyList<BmsWavItem> wavItems)
    {
        var width = 2;

        foreach (var wav in wavItems)
            width = Math.Max(width, wav.Key.Length);

        foreach (var note in chart.Notes)
            width = Math.Max(width, note.WavKey.Length);

        // BMS 규격상 키는 2자리 아니면 3자리다.
        return Math.Clamp(width, 2, 3);
    }

    // 적힌 자리에 파일이 없어 하위 폴더에서 같은 이름을 찾아 붙인 경우에는
    // 그 **추측 결과를 파일에 박지 않는다.** 재생에는 쓰되 저장은 원문을 지킨다.
    // 오래된 백업 폴더가 남아 있으면 차트가 조용히 그쪽을 가리키게 되기 때문이다.
    private static string ResolveOutputPath(BmsWavItem wav, string outputDirectory) =>
        wav.IsPathGuessed && !string.IsNullOrEmpty(wav.SourceText)
            ? wav.SourceText
            : MakeRelativePath(outputDirectory, wav.FilePath);

    private static Dictionary<string, int> BuildLaneOrder(IReadOnlyList<LaneDefinition> lanes)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lanes.Count; i++)
            map[lanes[i].Id] = i;
        return map;
    }

    // 노트들을 "한 줄로 정확히 쓸 수 있는 묶음"으로 나눈다. 각 묶음은 (분할 수, 노트들).
    //
    // 예전에는 분모의 최소공배수가 1920 을 넘으면 1920 으로 잘라 버렸다. 1920 의 약수가 아닌
    // 분모(7분할 줄이 있는 차트에 1/384 노트를 찍는 경우 등: LCM 2688)는 가장 가까운 칸으로
    // 반올림돼, 저장할 때마다 노트가 **경고 없이** 조금씩 밀렸다.
    // 이제는 최소공배수가 상한 안에 드는 노트끼리만 한 줄로 묶고, 넘치는 노트는 같은 마디·채널의
    // 다음 줄로 보낸다. BMS 는 같은 마디·채널 줄을 여러 개 두면 합쳐 읽으므로 위치가 그대로 남는다.
    // 대부분의 차트는 묶음이 하나라 출력이 예전과 같다.
    private static List<(int Resolution, List<BmsNote> Notes)> SplitByResolution(IReadOnlyList<BmsNote> notes)
    {
        var buckets = new List<(int Resolution, List<BmsNote> Notes)>();

        foreach (var note in notes)
        {
            var denominator = ToDenominator(note.Position, MaxResolutionDenominator);
            var placed = false;

            for (var i = 0; i < buckets.Count; i++)
            {
                var merged = Lcm(buckets[i].Resolution, denominator);
                if (merged > MaxResolutionDenominator)
                    continue;

                buckets[i].Notes.Add(note);
                buckets[i] = ((int)merged, buckets[i].Notes);
                placed = true;
                break;
            }

            if (!placed)
                buckets.Add((denominator, new List<BmsNote> { note }));
        }

        return buckets;
    }

    // 같은 칸에 노트가 둘 이상이면 덮어쓰지 않고 새 줄(겹)을 만든다.
    // 겹은 분할 수가 같을 때만 함께 쓴다. 분할 수가 다른 줄과 섞으면 칸 번호가 어긋난다.
    private static void FillLayers(List<string[]> layers, List<BmsNote> notes, int resolution, int keyWidth, string emptySlot)
    {
        var firstLayer = layers.Count;

        foreach (var note in notes)
        {
            var index = Math.Clamp((int)Math.Round(note.Position * resolution), 0, resolution - 1);
            var code = note.WavKey;
            var formatted = code.Length >= keyWidth ? code.Substring(0, keyWidth) : code.PadLeft(keyWidth, '0');

            string[]? targetSlots = null;
            for (var i = firstLayer; i < layers.Count; i++)
            {
                if (layers[i][index] == emptySlot)
                {
                    targetSlots = layers[i];
                    break;
                }
            }

            if (targetSlots is null)
            {
                targetSlots = new string[resolution];
                Array.Fill(targetSlots, emptySlot);
                layers.Add(targetSlots);
            }

            targetSlots[index] = formatted;
        }
    }

    private static int ToDenominator(double value, int maxDenominator)
    {
        if (!double.IsFinite(value) || value <= 0)
            return 1;

        value -= Math.Floor(value);
        if (value <= 1e-9)
            return 1;

        for (var denominator = 1; denominator <= maxDenominator; denominator++)
        {
            var numerator = value * denominator;
            if (Math.Abs(numerator - Math.Round(numerator)) < 1e-6)
                return denominator;
        }

        return maxDenominator;
    }

    private static long Lcm(long a, long b)
    {
        if (a <= 0 || b <= 0)
            return Math.Max(1, Math.Max(a, b));

        var gcd = Gcd(a, b);
        return gcd == 0 ? 1 : Math.Abs(a / gcd * b);
    }

    private static long Gcd(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a == 0 ? 1 : a;
    }

    private static string MakeRelativePath(string baseDirectory, string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath))
            return targetPath;

        try
        {
            return Path.GetRelativePath(baseDirectory, targetPath);
        }
        catch
        {
            return targetPath;
        }
    }
}

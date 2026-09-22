using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services;

// 파일 하나를 읽어낸 결과. BPM 과 마디 수는 Chart 안에 들어 있다.
// 키음 목록만 따로 나오는데, 정의된 순서를 그대로 지켜야 해서
// 순서가 없는 Chart.WavTable 로는 대신할 수 없기 때문이다.
//
// Encoding 은 이 파일을 어떤 인코딩으로 읽어냈는지다. 저장할 때 같은 인코딩으로
// 되돌려 써야 원본이 갈아치워지지 않는다.
public sealed record BmsParseResult(BmsChart Chart, IReadOnlyList<BmsWavItem> WavItems, Encoding Encoding)
{
    public double Bpm => Chart.Header.Bpm;
    public int MeasureCount => Chart.MeasureCount;
}

public static partial class BmsParser
{
    [GeneratedRegex(@"^#WAV([0-9a-zA-Z]{2,3})\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex WavRegex();

    [GeneratedRegex(@"^#TITLE\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^#ARTIST\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistRegex();

    [GeneratedRegex(@"^#BPM\s+([0-9\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BpmRegex();

    // #BPMxx 확장 BPM 표. "#BPM " 과 달리 번호가 붙으므로 위 정규식과 겹치지 않는다.
    // 이 줄은 원문 보존 대상으로도 남는다(IsConsumedHeader 에 넣지 않는다).
    // 읽어두는 이유는 저장이 아니라 채널 08 의 BPM 변화를 풀어내기 위해서다.
    [GeneratedRegex(@"^#BPM([0-9a-zA-Z]{2})\s+([0-9\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExtendedBpmRegex();

    [GeneratedRegex(@"^#GENRE\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex GenreRegex();

    // #PLAYLEVEL 과 겹치지 않는다. "#PLAYER" 뒤에는 반드시 공백이 와야 한다.
    [GeneratedRegex(@"^#PLAYER\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerRegex();

    [GeneratedRegex(@"^#RANK\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RankRegex();

    [GeneratedRegex(@"^#PLAYLEVEL\s+(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex PlayLevelRegex();

    [GeneratedRegex(@"^#BMSEDITER_OFFSET\s+([-+0-9\.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AudioOffsetRegex();

    // 마디는 보통 세 자리지만, 규격을 넘겨 네 자리를 쓰는 차트가 실제로 있다.
    // 세 자리로만 받으면 해석에 실패해서 "에디터가 모르는 줄"이 되고,
    // 저장할 때 데이터 줄이 아니라 파일 맨 위 헤더 블록으로 끌려 올라갔다.
    [GeneratedRegex(@"^#([0-9]{3,})([0-9a-zA-Z]{2}):(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex DataRegex();

    // 갈래를 나누는 제어 줄. 이 에디터는 아직 해석하지 못한다. (BmsChart.HasConditionalBlocks 참고)
    [GeneratedRegex(
        @"^#(?:RANDOM|SETRANDOM|ENDRANDOM|RONDAM|IF|ELSEIF|ELSE|ENDIF|SWITCH|SETSWITCH|CASE|SKIP|DEF|ENDSW)(?:\s|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ControlFlowRegex();

    private static readonly HashSet<string> SupportedChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "16", "11", "12", "13", "14", "15", "18"
    };

    // 예전에는 반환값 하나에 out 3개였다. 그중 BPM 과 마디 수는 차트 안에도 같은 값이
    // 들어 있어서, 호출한 쪽이 어느 쪽을 믿어야 하는지 매번 헷갈렸다.
    // 이제 차트를 다 채워서 하나로 돌려준다.
    public static BmsParseResult Parse(string filePath)
    {
        var chart = new BmsChart();
        var parsedBpm = 120.0;
        var measureCount = 32;
        var wavItems = new List<BmsWavItem>();

        if (!File.Exists(filePath))
        {
            chart.Header.Bpm = parsedBpm;
            chart.MeasureCount = measureCount;
            return new BmsParseResult(chart, wavItems, DefaultEncoding);
        }

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var mediaPathIndex = BuildFileNameIndex(directory);

        // 파일을 바이트로 한 번만 읽고 인코딩을 가려낸다. 어느 인코딩으로 읽었는지는
        // 결과에 실어 보내, 저장할 때 그대로 되돌려 쓴다.
        var (rawLines, encoding) = ReadAllLines(filePath, directory, mediaPathIndex);

        var maxMeasure = 0;
        var has3DigitWav = false;

        // 1단계: WAV 정의 및 기본 메타데이터 수집
        foreach (var rawLine in rawLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("#")) continue;

            var titleMatch = TitleRegex().Match(line);
            if (titleMatch.Success)
            {
                chart.Header.Title = titleMatch.Groups[1].Value.Trim();
                continue;
            }

            var artistMatch = ArtistRegex().Match(line);
            if (artistMatch.Success)
            {
                chart.Header.Artist = artistMatch.Groups[1].Value.Trim();
                continue;
            }

            var bpmMatch = BpmRegex().Match(line);
            if (bpmMatch.Success)
            {
                if (double.TryParse(bpmMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempBpm))
                {
                    parsedBpm = tempBpm;
                }
                continue;
            }

            var extendedBpmMatch = ExtendedBpmRegex().Match(line);
            if (extendedBpmMatch.Success)
            {
                if (double.TryParse(extendedBpmMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tableBpm)
                    && tableBpm > 0)
                {
                    chart.BpmTable[extendedBpmMatch.Groups[1].Value.ToUpper()] = tableBpm;
                }
                continue;
            }

            var genreMatch = GenreRegex().Match(line);
            if (genreMatch.Success)
            {
                chart.Header.Genre = genreMatch.Groups[1].Value.Trim();
                continue;
            }

            // #PLAYER 는 1(Single)/2(Couple)/3(Double). 파일 값을 그대로 담아둔다.
            var playerMatch = PlayerRegex().Match(line);
            if (playerMatch.Success)
            {
                if (int.TryParse(playerMatch.Groups[1].Value, out var tempPlayer))
                    chart.Header.Player = tempPlayer;
                continue;
            }

            var rankMatch = RankRegex().Match(line);
            if (rankMatch.Success)
            {
                if (int.TryParse(rankMatch.Groups[1].Value, out var tempRank))
                    chart.Header.Rank = tempRank;
                continue;
            }

            var playLevelMatch = PlayLevelRegex().Match(line);
            if (playLevelMatch.Success)
            {
                chart.Header.Level = playLevelMatch.Groups[1].Value.Trim();
                continue;
            }

            var audioOffsetMatch = AudioOffsetRegex().Match(line);
            if (audioOffsetMatch.Success)
            {
                if (double.TryParse(audioOffsetMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempOffset))
                    chart.Header.AudioOffsetMs = tempOffset;
                continue;
            }

            var wavMatch = WavRegex().Match(line);
            if (wavMatch.Success)
            {
                var key = wavMatch.Groups[1].Value.ToUpper();
                var wavFile = wavMatch.Groups[2].Value.Trim();

                if (key.Length == 3)
                {
                    has3DigitWav = true;
                }

                var absoluteWavPath = ResolveMediaPath(directory, wavFile, mediaPathIndex, out var pathGuessed);
                chart.WavTable[key] = absoluteWavPath;

                wavItems.Add(new BmsWavItem
                {
                    Key = key,
                    FilePath = absoluteWavPath,
                    SourceText = wavFile,
                    IsPathGuessed = pathGuessed,
                });
            }
        }

        // 2단계: 채보 데이터(노트) 및 제어문 파싱
        var currentMeasure = 0;
        var currentBranchId = 0;
        var nextBranchId = 1;
        var branchStack = new Stack<int>();

        for (var lineIndex = 0; lineIndex < rawLines.Length; lineIndex++)
        {
            var rawLine = rawLines[lineIndex];
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            // '#' 로 시작하지 않는 줄은 주석이다(`*----` 같은 구분선이 흔하다).
            // 예전에는 원문 보존 대상에서 빠져 저장할 때 통째로 사라졌다.
            if (!line.StartsWith("#"))
            {
                chart.PreservedLines.Add(new BmsRawLine
                {
                    Text = line,
                    Measure = -1,
                    Order = lineIndex,
                    BranchId = currentBranchId,
                });
                continue;
            }

            var dataMatch = DataRegex().Match(line);
            if (!dataMatch.Success)
            {
                // 갈래를 나누는 제어 줄
                if (ControlFlowRegex().IsMatch(line))
                {
                    chart.HasConditionalBlocks = true;

                    if (Regex.IsMatch(line, @"^#(?:IF|CASE|DEF)\b", RegexOptions.IgnoreCase))
                    {
                        currentBranchId = nextBranchId++;
                        branchStack.Push(currentBranchId);
                    }
                    else if (Regex.IsMatch(line, @"^#(?:ELSEIF|ELSE)\b", RegexOptions.IgnoreCase))
                    {
                        if (branchStack.Count > 0) branchStack.Pop();
                        currentBranchId = nextBranchId++;
                        branchStack.Push(currentBranchId);
                    }
                    else if (Regex.IsMatch(line, @"^#(?:ENDIF|ENDSW)\b", RegexOptions.IgnoreCase))
                    {
                        if (branchStack.Count > 0) branchStack.Pop();
                        currentBranchId = branchStack.Count > 0 ? branchStack.Peek() : 0;
                    }

                    chart.PreservedLines.Add(new BmsRawLine
                    {
                        Text = line,
                        Measure = currentMeasure,
                        Order = lineIndex,
                        BranchId = currentBranchId,
                        IsControlFlow = true,
                    });
                    continue;
                }

                // 데이터 줄도 아니고 1단계에서 읽어간 헤더도 아니면 에디터가 모르는 줄이다.
                // 저장할 때 그대로 되돌려 놓으려고 원문을 보관한다.
                if (!IsConsumedHeader(line))
                {
                    chart.PreservedLines.Add(new BmsRawLine
                    {
                        Text = line,
                        Measure = -1,
                        Order = lineIndex,
                        BranchId = 0,
                    });
                }
                continue;
            }

            var measureNum = int.Parse(dataMatch.Groups[1].Value);
            var channel = dataMatch.Groups[2].Value; // 예: "11", "12" ...
            var dataStr = dataMatch.Groups[3].Value.Trim();
            currentMeasure = measureNum;

            // 편집 대상은 1P 건반 채널(16-11-12-13-14-15-18)뿐이다.
            // 나머지(BGM·마디 길이·BPM 변화·STOP·BGA·롱노트·2P 등)는 원문 그대로 보관한다.
            if (!SupportedChannels.Contains(channel))
            {
                chart.PreservedLines.Add(new BmsRawLine
                {
                    Text = line,
                    Measure = measureNum,
                    Order = lineIndex,
                    BranchId = currentBranchId,
                });

                // 건반이 없는 뒷마디까지 그리드가 이어지도록 마디 수에도 반영한다.
                maxMeasure = Math.Max(maxMeasure, measureNum);

                // 편집 대상은 아니지만 격자·재생 시각을 맞추려면 읽어는 둬야 하는 채널들.
                // 원문 보존은 위에서 이미 했으므로 저장에는 영향이 없다.
                ReadTimingChannel(chart, measureNum, channel, dataStr);
                continue;
            }

            maxMeasure = Math.Max(maxMeasure, measureNum);

            // 데이터 자르기 단위 크기 결정 (2자리 또는 3자리)
            var chunkSize = DetermineChunkSize(dataStr, has3DigitWav, chart.WavTable);

            var chunks = new List<string>();
            for (var i = 0; i < dataStr.Length; i += chunkSize)
            {
                if (i + chunkSize <= dataStr.Length)
                {
                    chunks.Add(dataStr.Substring(i, chunkSize).ToUpper());
                }
            }

            var totalChunks = chunks.Count;
            for (var index = 0; index < totalChunks; index++)
            {
                var code = chunks[index];
                
                // 빈 데이터("00", "000") 건너뛰기
                if (code == "00" || code == "000") continue;

                var position = (double)index / totalChunks;
                chart.Notes.Add(new BmsNote
                {
                    Measure = measureNum,
                    LaneId = channel,
                    Position = position,
                    WavKey = code,
                    Type = NoteType.Normal,
                    BranchId = currentBranchId,
                    SourceLineOrder = lineIndex,
                });
            }
        }

        measureCount = Math.Max(32, maxMeasure + 1);
        chart.Header.Bpm = parsedBpm;
        chart.MeasureCount = measureCount;
        return new BmsParseResult(chart, wavItems, encoding);
    }

    // 1단계에서 이미 읽어간(= 저장할 때 에디터가 새로 써주는) 헤더인지 판별한다.
    // 여기서 false 인 줄만 원문 보관 대상이 된다.
    private static bool IsConsumedHeader(string line) =>
        TitleRegex().IsMatch(line)
        || ArtistRegex().IsMatch(line)
        || BpmRegex().IsMatch(line)
        || GenreRegex().IsMatch(line)
        || PlayerRegex().IsMatch(line)
        || RankRegex().IsMatch(line)
        || PlayLevelRegex().IsMatch(line)
        || AudioOffsetRegex().IsMatch(line)
        || WavRegex().IsMatch(line);

    // 시간축을 바꾸는 채널을 읽어 차트에 담는다.
    //
    // 이 줄들은 예전에도 원문 그대로 보존돼서 저장하면 되돌아왔다. 문제는 **읽는 쪽**이었다.
    // 아무도 해석하지 않아서 격자·노트 위치·키음 타이밍이 전부 단일 BPM 과 4/4 를 가정했고,
    // 박자나 BPM 이 바뀌는 차트는 그 지점 이후로 화면과 소리가 어긋났다.
    private static void ReadTimingChannel(BmsChart chart, int measure, string channel, string data)
    {
        // 02: 마디 길이 배율. 슬롯이 아니라 실수 하나가 통째로 온다. (#00002:0.75)
        if (string.Equals(channel, "02", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && length > 0)
                chart.MeasureLengths[measure] = length;
            return;
        }

        // 03: 16진수 두 자리를 그대로 BPM 으로 쓴다. 정수만 되고 255가 한계다.
        if (string.Equals(channel, "03", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (position, code) in EnumerateSlots(data))
            {
                if (int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bpm) && bpm > 0)
                    chart.BpmChanges.Add(new BpmChange(measure, position, bpm));
            }
            return;
        }

        // 08: base-36 번호로 #BPMxx 표를 가리킨다. 소수점도 되고 255도 넘는다.
        if (string.Equals(channel, "08", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (position, code) in EnumerateSlots(data))
            {
                if (chart.BpmTable.TryGetValue(code, out var bpm) && bpm > 0)
                    chart.BpmChanges.Add(new BpmChange(measure, position, bpm));
            }
        }
    }

    // 데이터 줄을 두 자리씩 끊어 "마디 안 위치 + 코드"로 내놓는다. 빈 칸("00")은 건너뛴다.
    private static IEnumerable<(double Position, string Code)> EnumerateSlots(string data)
    {
        var count = data.Length / 2;
        if (count == 0)
            yield break;

        for (var i = 0; i < count; i++)
        {
            var code = data.Substring(i * 2, 2).ToUpperInvariant();
            if (code == "00")
                continue;

            yield return ((double)i / count, code);
        }
    }

    private static int DetermineChunkSize(string dataStr, bool has3DigitWav, Dictionary<string, string> wavTable)
    {
        if (!has3DigitWav)
            return 2;

        if (dataStr.Length % 3 == 0 && dataStr.Length % 2 != 0)
            return 3;

        if (dataStr.Length % 6 == 0)
        {
            var matchCount2 = 0;
            var matchCount3 = 0;

            for (var i = 0; i < dataStr.Length; i += 2)
            {
                var k = dataStr.Substring(i, 2).ToUpper();
                if (k != "00" && wavTable.ContainsKey(k))
                    matchCount2++;
            }

            for (var i = 0; i < dataStr.Length; i += 3)
            {
                var k = dataStr.Substring(i, 3).ToUpper();
                if (k != "000" && wavTable.ContainsKey(k))
                    matchCount3++;
            }

            if (matchCount3 > matchCount2)
            {
                return 3;
            }
        }

        return 2;
    }

    private static Dictionary<string, string> BuildFileNameIndex(string directory)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return index;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(path);
            if (!index.ContainsKey(fileName))
                index[fileName] = path;
        }

        return index;
    }

    // guessed: 적힌 자리에 파일이 없어서 같은 이름을 하위 폴더에서 찾아 붙였는지.
    // 부르는 쪽이 이 결과를 저장 파일에 박지 않도록 구분해서 알려준다.
    private static string ResolveMediaPath(
        string baseDirectory, string mediaPath, Dictionary<string, string> fileNameIndex, out bool guessed)
    {
        guessed = false;

        if (Path.IsPathRooted(mediaPath))
            return mediaPath;

        var directPath = Path.GetFullPath(Path.Combine(baseDirectory, mediaPath));
        if (File.Exists(directPath))
            return directPath;

        var fileName = Path.GetFileName(mediaPath);
        if (fileNameIndex.TryGetValue(fileName, out var indexedPath))
        {
            guessed = true;
            return indexedPath;
        }

        return directPath;
    }

    // ── 인코딩 감지 ────────────────────────────────────────────────────────────
    //
    // 예전에는 "UTF-8 로 읽어보고 앞 100줄에 U+FFFD 가 있으면 CP949 로 다시 읽기" 였다.
    // 구멍이 셋이었다.
    //   * Shift_JIS(CP932) 갈래가 없어서 야생 차트 대부분이 엉뚱한 한글로 읽혔다.
    //     제목뿐 아니라 #WAV 파일명까지 깨져서 키음이 하나도 안 붙었다.
    //   * CP949 는 Shift_JIS 바이트를 오류 없이 삼켜서 U+FFFD 검사에 걸리지도 않았다.
    //   * 앞 100줄만 봐서, 비ASCII 글자가 그 뒤에 처음 나오면 재시도가 아예 안 돌았다.
    //     (#WAV 가 수백 줄이고 헤더는 영문인 차트가 여기에 딱 걸린다)
    //
    // 이제 바이트로 한 번만 읽고 BOM -> UTF-8(엄격) -> CP932/CP949 순으로 가른다.
    // 줄 수 제한이 사라졌고, 고른 인코딩을 그대로 들고 나가 저장에 쓴다.

    private static readonly Encoding DefaultEncoding = new UTF8Encoding(false);

    // 한 바이트라도 어긋나면 예외를 던지는 UTF-8. 조용히 U+FFFD 로 때우면 감지가 안 된다.
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // 인코딩을 가릴 때만 쓰는, #WAV/#BMP 줄의 파일명 추출용.
    [GeneratedRegex(@"^#(?:WAV|BMP)[0-9a-zA-Z]{2,3}\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MediaLineRegex();

    // 노트만 훑어보려는 쪽(건반 BMS 다운믹서)이 쓰는 입구.
    //
    // 같은 인코딩 판별을 두 벌 적지 않으려고 연다. 야생 BMS 는 Shift_JIS 가 흔한데,
    // 그 판별이 여기 한 곳에만 있어야 한쪽만 고치는 일이 안 생긴다.
    public static string[] ReadLinesForAnalysis(string filePath)
    {
        if (!File.Exists(filePath))
            return Array.Empty<string>();

        var directory = Path.GetDirectoryName(filePath) ?? "";
        var (lines, _) = ReadAllLines(filePath, directory, BuildFileNameIndex(directory));
        return lines;
    }

    private static (string[] Lines, Encoding Encoding) ReadAllLines(
        string filePath, string directory, Dictionary<string, string> fileNameIndex)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var bytes = File.ReadAllBytes(filePath);

        if (TryReadByBom(bytes, out var bomText, out var bomEncoding))
            return (SplitLines(bomText), bomEncoding);

        // UTF-8 로 한 글자도 어긋나지 않고 읽히면 UTF-8 이다.
        // CP932/CP949 로 쓴 한글·일본어가 우연히 올바른 UTF-8 이 되는 일은 사실상 없다.
        if (TryDecodeStrict(bytes, StrictUtf8, out var utf8Text))
            return (SplitLines(utf8Text), DefaultEncoding);

        return DecodeLegacy(bytes, directory, fileNameIndex);
    }

    private static bool TryReadByBom(byte[] bytes, out string text, out Encoding encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            encoding = new UTF8Encoding(true);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            encoding = Encoding.Unicode;
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            encoding = Encoding.BigEndianUnicode;
            return true;
        }

        text = string.Empty;
        encoding = DefaultEncoding;
        return false;
    }

    private static bool TryDecodeStrict(byte[] bytes, Encoding encoding, out string text)
    {
        try
        {
            text = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    // CP932(일본어)와 CP949(한국어)는 서로의 바이트열을 대부분 오류 없이 삼킨다.
    // 그래서 "디코딩에 실패했는가"로는 못 가른다. 대신 증거 두 가지를 쓴다.
    //
    //   1순위: #WAV/#BMP 파일명이 실제로 폴더에 있는가.
    //          인코딩이 틀리면 파일명이 깨져서 하나도 안 맞는다. 이게 가장 확실한 증거다.
    //   2순위: 읽어낸 글자가 말이 되는가. 한국어 바이트를 CP932 로 읽으면 반각 가타카나가
    //          잔뜩 나오는데, 실제 일본어 제목·파일명에는 거의 안 쓰인다.
    //
    // 둘 다 못 가르면 CP932 로 둔다. 야생의 BMS 차트는 Shift_JIS 가 가장 많다.
    private static (string[] Lines, Encoding Encoding) DecodeLegacy(
        byte[] bytes, string directory, Dictionary<string, string> fileNameIndex)
    {
        Encoding? bestEncoding = null;
        string? bestText = null;
        var bestResolved = -1;
        var bestScore = int.MinValue;

        foreach (var codePage in new[] { 932, 949 })
        {
            Encoding candidate;
            try
            {
                candidate = Encoding.GetEncoding(codePage);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            var text = candidate.GetString(bytes);
            var resolved = CountResolvableMedia(text, directory, fileNameIndex);
            var score = ScoreLegacyText(text);

            if (resolved > bestResolved || (resolved == bestResolved && score > bestScore))
            {
                bestEncoding = candidate;
                bestText = text;
                bestResolved = resolved;
                bestScore = score;
            }
        }

        if (bestEncoding is null || bestText is null)
        {
            // CodePages 공급자가 없는 환경. 손실을 감수하고라도 읽기는 해야 한다.
            return (SplitLines(Encoding.UTF8.GetString(bytes)), DefaultEncoding);
        }

        return (SplitLines(bestText), bestEncoding);
    }

    private static int CountResolvableMedia(string text, string directory, Dictionary<string, string> fileNameIndex)
    {
        var found = 0;

        foreach (var line in SplitLines(text))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '#')
                continue;

            var match = MediaLineRegex().Match(trimmed);
            if (!match.Success)
                continue;

            var mediaPath = match.Groups[1].Value.Trim();
            if (mediaPath.Length == 0)
                continue;

            try
            {
                if (fileNameIndex.ContainsKey(Path.GetFileName(mediaPath)))
                {
                    found++;
                    continue;
                }

                if (directory.Length > 0 && File.Exists(Path.Combine(directory, mediaPath)))
                    found++;
            }
            catch (ArgumentException)
            {
                // 깨진 파일명에 경로로 못 쓰는 글자가 섞인 경우. 못 찾은 것으로 친다.
            }
        }

        return found;
    }

    private static int ScoreLegacyText(string text)
    {
        var score = 0;

        foreach (var c in text)
        {
            // 디코딩 실패 자리.
            if (c == '�')
                score -= 6;
            // 반각 가타카나. 한국어 바이트를 CP932 로 읽으면 여기가 잔뜩 나온다.
            else if (c is >= '｡' and <= 'ﾟ')
                score -= 3;
            // 텍스트 한가운데의 제어 문자는 어느 쪽이든 잘못 읽은 신호다.
            else if (char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
                score -= 4;
        }

        return score;
    }

    private static string[] SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 저장이 원본을 상하게 하지 않는다는 것을 못 박아 두는 테스트.
// (알려진 문제 1·11·16·18·20번 — 문서의 1순위 "파일 왕복 무결성" 묶음)
//
// 되돌리기(Ctrl+Z)는 에디터 안의 편집만 되돌리고, .bak 은 직전 한 벌뿐이다. 저장 한 번이 곧 원본이라,
// 여기서 새는 것은 사용자가 알아채기 전에 이미 되돌릴 수 없다.
public sealed class SaveIntegrityTests : IDisposable
{
    private readonly string _directory;

    public SaveIntegrityTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bms-editer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private string WriteChart(string content, string fileName = "chart.bms")
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string TouchFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not really audio");
        return path;
    }

    // ── 20. 저장이 원자적이지 않아, 실패하면 원본이 잘린 채 남는다 ──────────────

    [Fact]
    public void 저장에_실패해도_원본은_그대로_남는다()
    {
        var original = "#TITLE 소중한 원본\r\n#BPM 120\r\n#WAV01 a.wav\r\n";
        var path = WriteChart(original);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        vm.Title = "덮어쓰려던 제목";

        // 다른 프로세스가 파일을 쥐고 있는 상황.
        // 예전 방식(File.WriteAllText)은 원본을 먼저 비우고 쓰기 때문에 여기서 잘려 나갔다.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(vm.SaveBms(path));
            Assert.False(string.IsNullOrEmpty(vm.LastErrorMessage));
        }

        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void 저장하면_직전_내용이_bak_으로_남는다()
    {
        var path = WriteChart("#TITLE 예전 제목\r\n#BPM 120\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        vm.Title = "새 제목";

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        Assert.Contains("새 제목", File.ReadAllText(path));
        Assert.Contains("예전 제목", File.ReadAllText(path + SafeFileWriter.BackupExtension));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void 없던_파일에_저장하면_그냥_만들어진다()
    {
        var vm = new MainWindowViewModel();
        vm.Title = "새 차트";

        var path = Path.Combine(_directory, "새로 만든 차트.bms");
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + SafeFileWriter.BackupExtension));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    // ── 18. #RANDOM/#IF 차트가 저장하면 통째로 무너진다 ────────────────────────

    [Fact]
    public void RANDOM_블록과_IF_분기가_손상_없이_원문_그대로_보존되어_저장된다()
    {
        var original =
            "#TITLE t\r\n#BPM 120\r\n#PLAYER 1\r\n#RANK 3\r\n#WAV01 a.wav\r\n\r\n" +
            "#00101:01\r\n#RANDOM 2\r\n#IF 1\r\n#00111:01000000\r\n#ENDIF\r\n#IF 2\r\n#00111:00001000\r\n#ENDIF\r\n";
        var path = WriteChart(original);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.True(vm.Chart.HasConditionalBlocks);

        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        var savedText = File.ReadAllText(path);
        // 갈래별 노드가 하나로 합쳐지지 않고 각 IF 블록 안에 안전하게 보존되었는지 검증
        Assert.Contains("#RANDOM 2", savedText);
        Assert.Contains("#IF 1\r\n#00111:01\r\n#ENDIF", savedText);
        Assert.Contains("#IF 2\r\n#00111:0010\r\n#ENDIF", savedText);
    }

    [Fact]
    public void SWITCH_CASE_조건_블록도_손상_없이_저장된다()
    {
        var original = "#TITLE t\r\n#BPM 120\r\n#PLAYER 1\r\n#RANK 3\r\n#WAV01 a.wav\r\n\r\n#SWITCH 3\r\n#CASE 1\r\n#00111:01\r\n#ENDSW\r\n";
        var path = WriteChart(original);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.True(vm.Chart.HasConditionalBlocks);
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        var savedText = File.ReadAllText(path);
        Assert.Contains("#SWITCH 3", savedText);
        Assert.Contains("#CASE 1\r\n#00111:01\r\n#ENDSW", savedText);
    }

    // #IF 줄은 "그 앞 데이터 줄의 마디"를 달고 있다. 블록이 여러 마디에 걸치면 예전 라이터가
    // 뒷마디의 조건 밖 줄(BGM·노트)을 #IF 와 #ENDIF 사이에 끼워 넣어, 한 갈래에서만 나오게 만들었다.
    [Fact]
    public void 여러_마디에_걸친_RANDOM_블록_뒤의_줄이_블록_안으로_들어가지_않는다()
    {
        const string data =
            "#RANDOM 2\r\n#IF 1\r\n#00111:01\r\n#ENDIF\r\n#IF 2\r\n#00111:02\r\n#ENDIF\r\n#ENDRANDOM\r\n" +
            "#00101:01\r\n#00113:01\r\n";
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#PLAYER 1\r\n#RANK 3\r\n#WAV01 a.wav\r\n#WAV02 b.wav\r\n\r\n" + data);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        var savedText = File.ReadAllText(path);
        Assert.Equal(0, ConditionalDepthOf(savedText, "#00101:01"));
        Assert.Equal(0, ConditionalDepthOf(savedText, "#00113:01"));

        // 원문 순서도 그대로다.
        Assert.Contains(data, savedText);
    }

    [Fact]
    public void ENDRANDOM_이_없어도_갈래_안팎이_섞이지_않는다()
    {
        var path = WriteChart(
            "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n\r\n" +
            "#00101:01\r\n#RANDOM 2\r\n" +
            "#IF 1\r\n#00111:01\r\n#00211:01\r\n#ENDIF\r\n" +
            "#IF 2\r\n#00112:01\r\n#ENDIF\r\n" +
            "#00213:01\r\n#00301:01\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        var savedText = File.ReadAllText(path);
        Assert.Equal(1, ConditionalDepthOf(savedText, "#00111:01"));
        Assert.Equal(1, ConditionalDepthOf(savedText, "#00211:01"));
        Assert.Equal(1, ConditionalDepthOf(savedText, "#00112:01"));
        Assert.Equal(0, ConditionalDepthOf(savedText, "#00101:01"));
        Assert.Equal(0, ConditionalDepthOf(savedText, "#00213:01"));
        Assert.Equal(0, ConditionalDepthOf(savedText, "#00301:01"));
    }

    // CASE 마다 갈래를 쌓기만 해서, CASE 가 둘 이상이면 #ENDSW 뒤의 노트가 첫 CASE 갈래로 읽혔다.
    // 홀드 짝은 갈래별로 따로 세므로 SWITCH 를 사이에 둔 짝이 끊겼다.
    [Fact]
    public void CASE_가_여럿인_SWITCH_뒤의_노트는_조건_밖으로_읽힌다()
    {
        var original =
            "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n\r\n" +
            "#00113:01\r\n" +
            "#SWITCH 2\r\n#CASE 1\r\n#00111:01\r\n#SKIP\r\n#CASE 2\r\n#00112:01\r\n#SKIP\r\n#ENDSW\r\n" +
            "#00213:01\r\n";
        var path = WriteChart(original);

        var notes = BmsParser.Parse(path).Chart.Notes;
        var before = notes.Single(n => n.Measure == 1 && n.LaneId == "13");
        var case1 = notes.Single(n => n.LaneId == "11");
        var case2 = notes.Single(n => n.LaneId == "12");
        var after = notes.Single(n => n.Measure == 2);

        Assert.Equal(0, before.BranchId);
        Assert.Equal(0, after.BranchId);
        Assert.NotEqual(0, case1.BranchId);
        Assert.NotEqual(0, case2.BranchId);
        Assert.NotEqual(case1.BranchId, case2.BranchId);

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);

        var savedText = File.ReadAllText(path);
        Assert.Equal(1, ConditionalDepthOf(savedText, "#00111:01"));
        Assert.Equal(1, ConditionalDepthOf(savedText, "#00112:01"));
        Assert.Equal(0, ConditionalDepthOf(savedText, "#00213:01"));
    }

    [Fact]
    public void SWITCH_가_IF_안에_있어도_ENDSW_뒤에는_바깥_IF_갈래로_돌아온다()
    {
        var path = WriteChart(
            "#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n\r\n" +
            "#RANDOM 2\r\n#IF 1\r\n" +
            "#SWITCH 2\r\n#CASE 1\r\n#00111:01\r\n#CASE 2\r\n#00112:01\r\n#ENDSW\r\n" +
            "#00113:01\r\n#ENDIF\r\n#00214:01\r\n");

        var notes = BmsParser.Parse(path).Chart.Notes;
        var insideIf = notes.Single(n => n.LaneId == "13");

        Assert.NotEqual(0, insideIf.BranchId);
        Assert.NotEqual(insideIf.BranchId, notes.Single(n => n.LaneId == "11").BranchId);
        Assert.Equal(0, notes.Single(n => n.LaneId == "14").BranchId);
    }

    // 저장된 파일에서 그 줄이 조건 블록 몇 겹 안에 있는지. 0 이면 조건 밖이다.
    private static int ConditionalDepthOf(string savedText, string target)
    {
        var depth = 0;
        foreach (var line in savedText.Split("\r\n"))
        {
            if (line == target)
                return depth;

            var keyword = line.StartsWith('#') ? line[1..].Split(' ')[0].ToUpperInvariant() : "";
            if (keyword is "IF" or "SWITCH")
                depth++;
            else if (keyword is "ENDIF" or "ENDSW")
                depth--;
        }

        throw new Xunit.Sdk.XunitException($"'{target}' 줄이 저장 결과에 없습니다.");
    }

    [Fact]
    public void 조건_줄이_없는_보통_차트는_그대로_저장된다()
    {
        // #RANDOM 을 막는 검사가 멀쩡한 차트까지 막으면 안 된다.
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00111:01\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));

        Assert.False(vm.Chart.HasConditionalBlocks);
        Assert.True(vm.SaveBms(path), vm.LastErrorMessage);
    }

    [Fact]
    public void 새로_만들기를_하면_조건_블록_표시가_풀린다()
    {
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#RANDOM 2\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));
        Assert.True(vm.Chart.HasConditionalBlocks);

        vm.Chart.Clear();

        Assert.False(vm.Chart.HasConditionalBlocks);
    }

    // ── 11. 3자리 노트 키가 잘려서 저장된다 ──────────────────────────────────

    [Fact]
    public void 테이블에_없는_세자리_노트_키도_잘리지_않는다()
    {
        // #WAV 테이블에는 2자리 키 하나뿐인데 노트는 3자리를 가리키는 상황.
        // 예전에는 테이블만 보고 keyWidth=2 로 잡아 "0ZZ" 가 "0Z" 로 잘렸다.
        var chart = new BmsChart();
        chart.Notes.Add(new BmsNote { Measure = 1, LaneId = "11", Position = 0.0, WavKey = "0ZZ" });
        chart.Notes.Add(new BmsNote { Measure = 1, LaneId = "11", Position = 0.5, WavKey = "1AB" });

        var wavItems = new[] { new BmsWavItem { Key = "01", FilePath = TouchFile("a.wav") } };

        var text = BmsWriter.Write(
            chart, "t", "", "", 120, 0, 2, "", wavItems, Path.Combine(_directory, "out.bms"));

        Assert.Contains("#00111:0ZZ1AB", text);
        Assert.DoesNotContain("#00111:0Z1A", text);

        // 테이블 키도 같은 폭으로 맞춰 나가야 노트와 짝이 맞는다.
        Assert.Contains("#WAV001 a.wav", text);
    }

    // ── 1. 3자리 키음 차트에 키음을 추가하면 기존 키음이 덮어써진다 ────────────

    [Fact]
    public void 세자리_차트에_키음을_추가해도_기존_키음을_덮어쓰지_않는다()
    {
        TouchFile("snd001.wav");
        TouchFile("snd002.wav");
        var newWav = TouchFile("새키음.wav");

        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#WAV001 snd001.wav\r\n#WAV002 snd002.wav\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));

        vm.AddWav(newWav);

        // 예전에는 항상 2자리("01")를 만들었고, 저장할 때 "001" 이 되어 기존 001을 덮어썼다.
        var added = vm.WavList.Last();
        Assert.Equal("003", added.Key);

        var savedPath = Path.Combine(_directory, "out.bms");
        Assert.True(vm.SaveBms(savedPath), vm.LastErrorMessage);

        var saved = File.ReadAllText(savedPath);
        Assert.Contains("#WAV001 snd001.wav", saved);
        Assert.Contains("#WAV002 snd002.wav", saved);
        Assert.Contains("#WAV003 새키음.wav", saved);

        // 001 이 여전히 원래 파일을 가리키는지가 이 항목의 핵심이다.
        var reparsed = BmsParser.Parse(savedPath);
        Assert.Equal("snd001.wav", Path.GetFileName(reparsed.Chart.WavTable["001"]));
    }

    [Fact]
    public void 두자리_차트에_키음을_추가하면_두자리를_유지한다()
    {
        var newWav = TouchFile("new.wav");
        TouchFile("a.wav");

        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#WAV01 a.wav\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path));

        vm.AddWav(newWav);

        Assert.Equal("02", vm.WavList.Last().Key);
    }

    // ── 16. 저장할 때 #WAV 경로가 추측 결과로 덮어써진다 ──────────────────────

    [Fact]
    public void 못_찾아_추측한_키음_경로는_원문_그대로_저장된다()
    {
        // 적힌 자리(kick.wav)에는 없고 오래된 백업 폴더에만 있는 상황.
        TouchFile(Path.Combine("old_backup", "kick.wav"));
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#WAV01 kick.wav\r\n");

        var parsed = BmsParser.Parse(path);
        var wav = Assert.Single(parsed.WavItems);

        // 재생용 경로는 찾아낸 자리를 가리켜야 한다. (이 보강 기능 자체는 유지)
        Assert.True(wav.IsPathGuessed);
        Assert.Contains("old_backup", wav.FilePath);

        var text = BmsWriter.Write(parsed.Chart, "t", "", "", 120, 0, 2, "", parsed.WavItems, path);

        // 저장은 원문을 지킨다. 예전에는 "#WAV01 old_backup\kick.wav" 가 박혔다.
        Assert.Contains("#WAV01 kick.wav", text);
        Assert.DoesNotContain("old_backup", text);
    }

    [Fact]
    public void 제자리에_있는_키음은_예전처럼_상대경로로_저장된다()
    {
        // 16번 수정이 정상적인 경우까지 바꿔놓으면 안 된다.
        TouchFile(Path.Combine("sounds", "kick.wav"));
        var path = WriteChart("#TITLE t\r\n#BPM 120\r\n#WAV01 sounds/kick.wav\r\n");

        var parsed = BmsParser.Parse(path);
        Assert.False(Assert.Single(parsed.WavItems).IsPathGuessed);

        var text = BmsWriter.Write(parsed.Chart, "t", "", "", 120, 0, 2, "", parsed.WavItems, path);

        Assert.Contains(@"#WAV01 sounds\kick.wav", text);
    }
}

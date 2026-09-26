using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 구간 씬 바꾸기.
//
// 못 박아 두는 것:
//   * 노트는 **같은 종류의** 다른 씬 키음으로 간다. 홀드 시작·끝은 UID 가 같아 이름으로만 갈리는데,
//     UID 만 보고 고르면 시작이 끝으로 바뀌어 짝이 통째로 뒤집힌다.
//   * 하트·음표(00xxxx)는 씬과 무관해서 그대로 둔다.
//   * 씬 전환 노트는 구간 시작과 끝난 뒤에 들어가고, 되돌리기 한 번에 전부 돌아온다.
public sealed class SceneConvertTests : IDisposable
{
    private readonly string _directory;

    public SceneConvertTests()
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

    // 뮤즈 대시 템플릿과 같은 모양의 표. 5번 씬 홀드는 끝을 먼저 적어서 번호 순서로 고르면 틀리게 했다.
    private const string TemplateChart =
        "#TITLE scene\r\n" +
        "#BPM 120\r\n" +
        "#WAV01 1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav\r\n" +
        "#WAV02 1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.47.wav\r\n" +
        "#WAV03 1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.47.wav\r\n" +
        "#WAV04 10번 씬 wav폴더\\000201_하트 지상_dt1.48.wav\r\n" +
        "#WAV05 1번 씬 wav폴더\\010101_1번 씬 보스 등장_dt0.wav\r\n" +
        "#WAV11 5번 씬 wav폴더\\051001_일반 노트1 지상 노멀_dt1.48.wav\r\n" +
        "#WAV12 5번 씬 wav폴더\\050201_홀드 지상 끝 노트_dt1.47.wav\r\n" +
        "#WAV13 5번 씬 wav폴더\\050201_홀드 지상 시작 노트_dt1.47.wav\r\n" +
        "#WAV15 5번 씬 wav폴더\\050101_5번 씬 보스 등장_dt0.wav\r\n" +
        "#WAV21 씬 전환 wav 폴더\\000401_1번 씬 전환_dt0.wav\r\n" +
        "#WAV25 씬 전환 wav 폴더\\000405_5번 씬 전환_dt0.wav\r\n" +
        "#00113:01\r\n" +
        "#00213:00010203\r\n" +
        "#00214:04\r\n" +
        "#00313:01\r\n" +
        "#00315:05\r\n" +
        "#00413:01\r\n";

    private MainWindowViewModel Load(string content)
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    private static BmsNote At(MainWindowViewModel vm, string lane, int measure, double position) =>
        vm.Chart.Notes.Single(n => n.LaneId == lane && n.Measure == measure && Math.Abs(n.Position - position) < 0.0001);

    private static SceneConvertResult Convert(MainWindowViewModel vm, int from, int to, int scene) =>
        vm.ConvertSectionToScene(
            vm.Chart.Notes.Where(n => n.Measure >= from && n.Measure <= to).ToArray(),
            from, to, scene, addStartToggle: true, addReturnToggle: true);

    [Fact]
    public void 구간_노트는_같은_종류의_다른_씬_키음으로_바뀐다()
    {
        var vm = Load(TemplateChart);

        var result = Convert(vm, 2, 3, 5);

        Assert.Equal(5, result.Converted);
        Assert.Equal("11", At(vm, "13", 2, 0.25).WavKey);
        Assert.Equal("13", At(vm, "13", 2, 0.5).WavKey);   // 홀드 시작 → 시작
        Assert.Equal("12", At(vm, "13", 2, 0.75).WavKey);  // 홀드 끝 → 끝
        Assert.Equal("11", At(vm, "13", 3, 0).WavKey);
        Assert.Equal("15", At(vm, "15", 3, 0).WavKey);     // 보스 등장도 그 씬의 보스로

        // 구간 밖과 씬 무관 노트는 그대로다.
        Assert.Equal("01", At(vm, "13", 1, 0).WavKey);
        Assert.Equal("01", At(vm, "13", 4, 0).WavKey);
        Assert.Equal("04", At(vm, "14", 2, 0).WavKey);
        Assert.Equal(1, result.NotSceneBound);
        Assert.Equal(0, result.UnresolvedNotes);
        Assert.Equal(0, result.CreatedWavs);
    }

    [Fact]
    public void 구간_시작과_끝난_뒤에_씬_전환_노트가_들어간다()
    {
        var vm = Load(TemplateChart);

        var result = Convert(vm, 2, 3, 5);

        // 2마디 첫 박 13 레인은 비어 있다.
        Assert.Equal("25", At(vm, "13", 2, 0).WavKey);
        Assert.NotNull(result.StartToggle);

        // 4마디 첫 박 13 레인은 차 있어서 14 레인에 둔다. 구간 뒤 첫 노트가 1번 씬이라 1번으로 돌아간다.
        Assert.Equal("21", At(vm, "14", 4, 0).WavKey);
        Assert.NotNull(result.ReturnToggle);
    }

    [Fact]
    public void 되돌리기_한_번에_전부_돌아온다()
    {
        var vm = Load(TemplateChart);
        var before = vm.Chart.Notes.Select(n => (n.LaneId, n.Measure, n.Position, n.WavKey)).OrderBy(x => x).ToArray();

        Convert(vm, 2, 3, 5);
        Assert.Equal("되돌리기 (씬 바꾸기)", vm.UndoMenuHeader);

        vm.UndoCommand.Execute(null);

        var after = vm.Chart.Notes.Select(n => (n.LaneId, n.Measure, n.Position, n.WavKey)).OrderBy(x => x).ToArray();
        Assert.Equal(before, after);
    }

    [Fact]
    public void 다시_실행해도_씬_전환이_겹치지_않는다()
    {
        var vm = Load(TemplateChart);

        Convert(vm, 2, 3, 5);
        var count = vm.Chart.Notes.Count;
        var second = Convert(vm, 2, 3, 5);

        Assert.Equal(0, second.Converted);
        Assert.Equal(count, vm.Chart.Notes.Count);
    }

    [Fact]
    public void 같은_자리의_다른_씬_전환은_갈아_끼운다()
    {
        var vm = Load(TemplateChart);

        Convert(vm, 2, 3, 5);
        Convert(vm, 2, 3, 1);

        var toggles = vm.Chart.Notes.Where(n => n.Measure == 2 && n.Position == 0 && n.WavKey is "21" or "25").ToArray();
        Assert.Single(toggles);
        Assert.Equal("21", toggles[0].WavKey);
        Assert.Equal("01", At(vm, "13", 2, 0.25).WavKey);
        Assert.Equal("02", At(vm, "13", 2, 0.5).WavKey);
    }

    [Fact]
    public void 표에_없는_씬이면_같은_규칙으로_정의를_새로_적고_저장된다()
    {
        var vm = Load(
            "#TITLE scene\r\n" +
            "#BPM 120\r\n" +
            "#WAV01 1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav\r\n" +
            "#00013:01\r\n");

        var result = Convert(vm, 0, 0, 3);

        Assert.Equal(1, result.Converted);
        Assert.Equal(2, result.CreatedWavs);  // 3번 씬 노트 + 3번 씬 전환
        Assert.Equal(2, result.MissingFiles); // 파일은 만들지 않는다

        var saved = Path.Combine(_directory, "saved.bms");
        Assert.True(vm.SaveBms(saved), vm.LastErrorMessage);
        var text = File.ReadAllText(saved);

        Assert.Contains("3번 씬 wav폴더\\031001_일반 노트1 지상 노멀_dt1.48.wav", text);
        Assert.Contains("씬 전환 wav 폴더\\000403_3번 씬 전환_dt0.wav", text);
    }

    [Fact]
    public void 씬_번호는_자릿수를_구분해_바꾼다()
    {
        var wavs = new[]
        {
            new BmsWavItem { Key = "01", SourceText = "10번 씬 wav폴더\\100101_10번 씬 보스 등장_dt0.wav", FilePath = "x" },
            new BmsWavItem { Key = "02", SourceText = "1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav", FilePath = "y" },
        };
        var resolver = new SceneWavResolver(wavs, Array.Empty<string>(), 2, null, _ => true);

        Assert.Equal(SceneKeyOutcome.Converted, resolver.ResolveNote("01", "05", out _));
        Assert.Equal(SceneKeyOutcome.Converted, resolver.ResolveNote("02", "12", out _));

        Assert.Equal("5번 씬 wav폴더\\050101_5번 씬 보스 등장_dt0.wav", resolver.Created[0].SourceText);
        Assert.Equal("12번 씬 wav폴더\\121001_일반 노트1 지상 노멀_dt1.48.wav", resolver.Created[1].SourceText);
    }

    [Fact]
    public void 같은_UID가_여럿인데_이름으로_못_가리면_짐작하지_않는다()
    {
        var wavs = new[]
        {
            new BmsWavItem { Key = "01", SourceText = "a\\010201_hold start.wav", FilePath = "a" },
            new BmsWavItem { Key = "02", SourceText = "b\\050201_foo.wav", FilePath = "b" },
            new BmsWavItem { Key = "03", SourceText = "b\\050201_bar.wav", FilePath = "c" },
        };
        var resolver = new SceneWavResolver(wavs, Array.Empty<string>(), 2, null, _ => true);

        Assert.Equal(SceneKeyOutcome.Unresolved, resolver.ResolveNote("01", "05", out var key));
        Assert.Null(key);
        Assert.Empty(resolver.Created);
    }
}

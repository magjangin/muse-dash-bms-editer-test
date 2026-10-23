using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.Services.Downmix;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 노트 속도(UID 끝 두 자리)와 속도 바꾸기, 건반 가져오기의 속도.
public sealed class NoteSpeedTests : IDisposable
{
    private readonly string _directory;

    public NoteSpeedTests()
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
        }
    }

    [Theory]
    [InlineData("011001", 1)]
    [InlineData("011002", 2)]
    [InlineData("011003", 3)]
    [InlineData("011005", 2)]
    [InlineData("011012", 3)]
    [InlineData("011118", 3)]
    [InlineData("000412", null)]   // 씬 전환
    [InlineData("010107", null)]   // 보스 연출
    [InlineData("011019", null)]
    [InlineData("0110", null)]
    public void 속도는_끝_두_자리에서_읽는다(string uid, int? expected) =>
        Assert.Equal(expected, NoteSpeed.Of(uid));

    [Theory]
    [InlineData("011001", 3, "011003")]
    [InlineData("011004", 2, "011005")]
    [InlineData("011006", 1, "011004")]
    [InlineData("011117", 3, "011118")]
    [InlineData("000401", 2, "000401")]
    [InlineData("010101", 2, "010101")]
    public void 속도만_바꾼_UID(string uid, int speed, string expected) =>
        Assert.Equal(expected, NoteSpeed.WithSpeed(uid, speed));

    [Theory]
    [InlineData(1, "011001", "011004", "010201", "010204")]
    [InlineData(2, "011002", "011005", "010202", "010205")]
    [InlineData(3, "011003", "011006", "010203", "010206")]
    public void 건반_가져오기_정의는_고른_속도의_UID를_쓴다(
        int speed, string ground, string air, string holdGround, string holdAir)
    {
        var table = new System.Collections.Generic.Dictionary<string, string>();

        var keys = MuseDashKeyResolver.CreateDefinitions(table, 2, out _, speed);

        Assert.NotNull(keys);
        Assert.Contains("\\" + ground, table[keys!.GroundNormal]);
        Assert.Contains("\\" + air, table[keys.AirNormal]);
        Assert.Contains("\\" + holdGround, table[keys.GroundHoldStart]);
        Assert.Contains("\\" + holdGround, table[keys.GroundHoldEnd]);
        Assert.Contains("\\" + holdAir, table[keys.AirHoldStart]);
        Assert.Contains("\\" + holdAir, table[keys.AirHoldEnd]);
    }

    private const string Chart =
        "#TITLE speed\r\n" +
        "#BPM 120\r\n" +
        "#WAV01 1번 씬 wav폴더\\011001_일반 노트1 지상 노멀_dt1.48.wav\r\n" +
        "#WAV02 1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.47.wav\r\n" +
        "#WAV03 1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.47.wav\r\n" +
        "#WAV05 1번 씬 wav폴더\\010101_1번 씬 보스 등장_dt0.wav\r\n" +
        "#WAV21 씬 전환 wav 폴더\\000401_1번 씬 전환_dt0.wav\r\n" +
        "#WAV12 1번 씬 wav폴더\\010202_홀드 지상 끝 노트_dt1.47.wav\r\n" +
        "#00113:01\r\n" +
        "#00213:0203\r\n" +
        "#00214:03\r\n" +
        "#00215:05\r\n" +
        "#00216:21\r\n";

    private MainWindowViewModel Load()
    {
        var path = Path.Combine(_directory, "chart.bms");
        File.WriteAllText(path, Chart, new UTF8Encoding(false));
        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    [Fact]
    public void 속도_바꾸기는_키음을_속도_변형으로_바꾸고_없으면_정의를_새로_적는다()
    {
        var vm = Load();

        var result = vm.ConvertNotesToSpeed(vm.Chart.Notes.ToArray(), 3);

        // 일반 노트 · 홀드 시작 · 홀드 끝 둘. 보스 연출과 씬 전환은 속도가 없다.
        Assert.Equal(2, result.NoSpeed);
        Assert.Equal(4, result.Converted);
        Assert.Equal(3, result.CreatedWavs);

        string TextOf(BmsNote n) => vm.Chart.WavTable[n.WavKey];
        Assert.Contains("011003_", TextOf(vm.Chart.Notes.Single(n => n.LaneId == "13" && n.Measure == 1)));
        Assert.Contains("010203_홀드 지상 시작", TextOf(vm.Chart.Notes.Single(n => n.LaneId == "13" && n.Measure == 2 && n.Position < 0.1)));
        Assert.Contains("010203_홀드 지상 끝", TextOf(vm.Chart.Notes.Single(n => n.LaneId == "13" && n.Measure == 2 && n.Position > 0.1)));
        Assert.Contains("010101_", TextOf(vm.Chart.Notes.Single(n => n.LaneId == "15")));
        Assert.Contains("000401_", TextOf(vm.Chart.Notes.Single(n => n.LaneId == "16")));
    }

    [Fact]
    public void 속도_바꾸기는_이미_있는_변형을_이름표로_골라_쓴다()
    {
        var vm = Load();
        var end = vm.Chart.Notes.Single(n => n.LaneId == "13" && n.Measure == 2 && n.Position > 0.1);

        var result = vm.ConvertNotesToSpeed(new[] { end }, 2);

        Assert.Equal(1, result.Converted);
        Assert.Equal(0, result.CreatedWavs);
        Assert.Equal("12", end.WavKey);
    }

    [Fact]
    public void 이미_그_속도면_바꾸지_않고_되돌리기_한_번으로_돌아온다()
    {
        var vm = Load();
        var note = vm.Chart.Notes.Single(n => n.LaneId == "13" && n.Measure == 1);

        Assert.Equal(1, vm.ConvertNotesToSpeed(new[] { note }, 1).AlreadyTarget);
        Assert.Equal("01", note.WavKey);

        vm.ConvertNotesToSpeed(new[] { note }, 2);
        Assert.NotEqual("01", note.WavKey);

        vm.UndoCommand.Execute(null);
        Assert.Equal("01", vm.Chart.Notes.Single(n => n.LaneId == "13" && n.Measure == 1).WavKey);
    }
}

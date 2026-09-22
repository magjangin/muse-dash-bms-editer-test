using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using bms_editer.Models;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 컨트롤 패널이 집계를 넘어 "그 줄의 노트를 실제로 다루는지" 못 박아 두는 테스트.
//
// 같은 기능을 세 번 만들다 접은 자리다. 세 번 다 뷰모델은 멀쩡했고, 화면에서 명령까지
// 닿는 길이나 선택이 유지되는지에서 어긋났다. 그래서 여기서는 "명령을 부르면 선택 집합과
// 차트가 실제로 바뀌는지"를 보고, 창이 열리고 목록이 붙는지는 WindowSmokeTests 가 본다.
public sealed class ControlPanelTests
{
    private static MainWindowViewModel WithNotes(params (string Lane, int Measure, string WavKey)[] notes)
    {
        var vm = new MainWindowViewModel();
        foreach (var (lane, measure, wavKey) in notes)
        {
            vm.Chart.Notes.Add(new BmsNote
            {
                LaneId = lane,
                Measure = measure,
                Position = 0,
                WavKey = wavKey,
            });
        }

        return vm;
    }

    // 미리듣기는 실제 오디오 장치를 건드린다. 동작을 보는 테스트에서는 꺼 둔다.
    private static ControlPanelViewModel PanelFor(MainWindowViewModel owner) =>
        new(owner) { PreviewOnSelect = false };

    [Fact]
    public void 레인_집계는_채널_번호와_표시_이름을_따로_들고_있다()
    {
        // LaneId 자리에 Header 를 넣어 두 값이 어긋나 있었다. 지금은 둘이 같아 드러나지
        // 않지만, 이 목록으로 노트를 골라내려면 채널 번호가 진짜여야 한다.
        var owner = WithNotes(("11", 0, "01"));
        var panel = PanelFor(owner);

        var stat = Assert.Single(panel.Stats);
        Assert.Equal("11", stat.LaneId);
        Assert.Equal(owner.Chart.Lanes.First(l => l.Id == "11").Header, stat.Header);
        Assert.Equal(1, stat.Count);
    }

    [Fact]
    public void 레인_줄을_고르면_그_레인의_노트를_모두_선택한다()
    {
        var owner = WithNotes(("11", 0, "01"), ("11", 1, "02"), ("12", 0, "01"));
        var panel = PanelFor(owner);

        panel.SelectedLaneStat = panel.Stats.First(s => s.LaneId == "11");
        panel.SelectLaneNotesCommand.Execute(null);

        Assert.Equal(2, owner.SelectedNotes.Count);
        Assert.All(owner.SelectedNotes, note => Assert.Equal("11", note.LaneId));
    }

    [Fact]
    public void 키음_줄을_고르면_그_키음의_노트를_선택하고_그_자리로_스크롤한다()
    {
        // 고른 노트가 화면 밖이면 아무 일도 안 일어난 것처럼 보인다. 예전에 이 기능을
        // 접은 이유 중 하나라, 선택과 함께 스크롤 요청이 나가는지도 같이 본다.
        var owner = WithNotes(("11", 0, "01"), ("12", 8, "02"), ("13", 9, "02"));
        var panel = PanelFor(owner);

        var ratios = new List<double>();
        owner.ScrollToRatioRequested += ratio => ratios.Add(ratio);

        panel.SelectedWavStat = panel.WavStats.First(s => s.Key == "02");
        panel.SelectWavNotesCommand.Execute(null);

        Assert.Equal(2, owner.SelectedNotes.Count);
        Assert.All(owner.SelectedNotes, note => Assert.Equal("02", note.WavKey));
        Assert.Single(ratios);
        Assert.InRange(ratios[0], 0, 1);
    }

    [Fact]
    public void 번호를_일괄_교체하면_바뀐_번호_줄을_고른_채로_둔다()
    {
        var owner = WithNotes(("11", 0, "01"), ("12", 1, "01"), ("13", 2, "02"));
        var panel = PanelFor(owner);

        panel.SelectedWavStat = panel.WavStats.First(s => s.Key == "01");
        panel.ReplacementWavKey = "0C";
        panel.ReplaceWavNotesCommand.Execute(null);

        Assert.Equal(2, owner.Chart.Notes.Count(n => n.WavKey == "0C"));
        Assert.DoesNotContain(owner.Chart.Notes, n => n.WavKey == "01");

        // 바뀐 줄을 그대로 고르고 있어야 이어서 미리듣기·선택으로 넘어갈 수 있다.
        Assert.Equal("0C", panel.SelectedWavStat!.Key);
        Assert.Equal(2, panel.SelectedWavStat.Count);
    }

    [Fact]
    public void 잘못된_번호로는_교체하지_않는다()
    {
        var owner = WithNotes(("11", 0, "01"));
        var panel = PanelFor(owner);

        panel.SelectedWavStat = panel.WavStats.First();
        panel.ReplacementWavKey = "00";
        panel.ReplaceWavNotesCommand.Execute(null);

        Assert.Equal("01", owner.Chart.Notes[0].WavKey);
    }

    [Fact]
    public void 편집으로_다시_집계해도_고르고_있던_줄은_풀리지_않는다()
    {
        // 목록이 통째로 갈리면서 ListBox 가 선택을 null 로 밀어 넣던 자리다.
        // 노트 하나 찍을 때마다 고른 줄이 풀리면 버튼이 계속 잠긴다.
        var owner = WithNotes(("11", 0, "01"), ("12", 1, "02"));
        var panel = PanelFor(owner);

        panel.SelectedLaneStat = panel.Stats.First(s => s.LaneId == "11");
        panel.SelectedWavStat = panel.WavStats.First(s => s.Key == "01");

        owner.DeleteNotes(owner.Chart.Notes.Where(n => n.WavKey == "02").ToList());

        Assert.Equal("11", panel.SelectedLaneStat!.LaneId);
        Assert.Equal("01", panel.SelectedWavStat!.Key);
    }

    [Fact]
    public async Task 확인을_거절하면_노트를_지우지_않는다()
    {
        var owner = WithNotes(("11", 0, "01"), ("12", 1, "01"));
        owner.ConfirmAsync = _ => Task.FromResult(false);
        var panel = PanelFor(owner);

        panel.SelectedWavStat = panel.WavStats.First();
        await panel.DeleteWavNotesCommand.ExecuteAsync(null);

        Assert.Equal(2, owner.Chart.Notes.Count);
    }

    [Fact]
    public async Task 확인_창을_띄울_수_없으면_지우지_않는다()
    {
        // 되돌리기가 없다. 물어볼 길이 없으면 지우는 쪽으로 흘러가서는 안 된다.
        var owner = WithNotes(("11", 0, "01"));
        var panel = PanelFor(owner);

        panel.SelectedWavStat = panel.WavStats.First();
        await panel.DeleteWavNotesCommand.ExecuteAsync(null);

        Assert.Single(owner.Chart.Notes);
    }

    [Fact]
    public async Task 확인하면_그_키음을_쓰는_노트를_모두_지운다()
    {
        var owner = WithNotes(("11", 0, "01"), ("12", 1, "01"), ("13", 2, "02"));
        owner.ConfirmAsync = _ => Task.FromResult(true);
        var panel = PanelFor(owner);

        panel.SelectedWavStat = panel.WavStats.First(s => s.Key == "01");
        await panel.DeleteWavNotesCommand.ExecuteAsync(null);

        Assert.Single(owner.Chart.Notes);
        Assert.Equal("02", owner.Chart.Notes[0].WavKey);

        // 개수가 0이 된 줄은 목록에서 사라지므로 선택도 함께 풀린다.
        Assert.Null(panel.SelectedWavStat);
    }

    [Fact]
    public void 고른_줄이_없으면_작업_버튼이_잠긴다()
    {
        // 눌러도 아무 일이 없는 버튼을 열어 두면 "고장났나" 하고 헤매게 된다.
        var owner = WithNotes(("11", 0, "01"));
        var panel = PanelFor(owner);

        Assert.False(panel.SelectLaneNotesCommand.CanExecute(null));
        Assert.False(panel.SelectWavNotesCommand.CanExecute(null));
        Assert.False(panel.PreviewWavCommand.CanExecute(null));
        Assert.False(panel.ReplaceWavNotesCommand.CanExecute(null));
        Assert.False(panel.DeleteWavNotesCommand.CanExecute(null));

        panel.SelectedLaneStat = panel.Stats.First();
        panel.SelectedWavStat = panel.WavStats.First();

        Assert.True(panel.SelectLaneNotesCommand.CanExecute(null));
        Assert.True(panel.SelectWavNotesCommand.CanExecute(null));
        Assert.True(panel.PreviewWavCommand.CanExecute(null));
        Assert.True(panel.ReplaceWavNotesCommand.CanExecute(null));
        Assert.True(panel.DeleteWavNotesCommand.CanExecute(null));
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.ViewModels;
using Xunit;

namespace bms_editer.Tests;

// 노트를 옮기고 지우는 편집이 조용히 어긋나지 않는지 못 박아 두는 테스트.
// (알려진 문제 4·5·6·12·7-2번)
//
// 이 부류는 사용자가 직접 한 행동의 결과라 추적은 되지만, 결과가 "조금 다른 패턴"이라
// 저장하고 한참 뒤에야 알아채게 된다.
public sealed class NoteEditingTests : IDisposable
{
    private readonly string _directory;

    public NoteEditingTests()
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

    private static MainWindowViewModel WithNotes(params (string Lane, int Measure, double Position)[] notes)
    {
        var vm = new MainWindowViewModel();
        foreach (var (lane, measure, position) in notes)
        {
            vm.Chart.Notes.Add(new BmsNote
            {
                LaneId = lane,
                Measure = measure,
                Position = position,
                WavKey = "01",
            });
        }
        return vm;
    }

    private static BmsNote Find(MainWindowViewModel vm, string lane, int measure) =>
        vm.Chart.Notes.First(n => n.LaneId == lane && n.Measure == measure);

    // ── 4. 방향키로 옮기면 현재 격자로 강제 스냅된다 ──────────────────────────

    [Fact]
    public void 잇단음을_다른_격자에서_옮겨도_잇단음이_유지된다()
    {
        // 12분할로 찍은 3잇단음(1/3 지점)을 16분할 상태에서 한 칸 옮긴다.
        var vm = WithNotes(("11", 1, 1.0 / 3.0));
        vm.BeatSplit = 16;
        var note = vm.Chart.Notes[0];
        vm.SetNoteSelection(new[] { note });

        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeForward);

        // 1/3 + 1/16 = 0.39583... 잇단음의 어긋난 정도가 그대로 실려 있어야 한다.
        // 예전에는 Math.Round 로 다시 계산해서 16분음표 격자로 뭉개졌다(0.375).
        Assert.Equal((1.0 / 3.0) + (1.0 / 16.0), note.Position, 9);
        Assert.Equal(1, note.Measure);
    }

    [Fact]
    public void 시간축_이동은_마디를_넘어간다()
    {
        var vm = WithNotes(("11", 1, 15.0 / 16.0));
        vm.BeatSplit = 16;
        vm.SetNoteSelection(new[] { vm.Chart.Notes[0] });

        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeForward);

        Assert.Equal(2, vm.Chart.Notes[0].Measure);
        Assert.Equal(0.0, vm.Chart.Notes[0].Position, 9);
    }

    // ── 5·6. 그룹 이동이 일부만 움직이거나 겹친다 ────────────────────────────

    [Fact]
    public void 앞이_막히면_선택_전체가_제자리에_남는다()
    {
        // 11번 레인 1·2마디를 선택하고, 3마디에 선택 밖의 노트가 있다.
        var vm = WithNotes(("11", 1, 0.0), ("11", 2, 0.0), ("11", 3, 0.0));
        vm.BeatSplit = 1;

        var moving = new[] { Find(vm, "11", 1), Find(vm, "11", 2) };
        vm.SetNoteSelection(moving);

        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeForward);

        // 예전에는 2마디 노트만 막히고 1마디 노트는 2마디로 올라가서 간격이 무너졌다.
        Assert.Equal(1, moving[0].Measure);
        Assert.Equal(2, moving[1].Measure);
    }

    [Fact]
    public void 막히지_않으면_선택_전체가_같이_움직인다()
    {
        var vm = WithNotes(("11", 1, 0.0), ("11", 2, 0.0));
        vm.BeatSplit = 1;

        var moving = vm.Chart.Notes.ToArray();
        vm.SetNoteSelection(moving);

        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeForward);

        Assert.Equal(new[] { 2, 3 }, moving.Select(n => n.Measure));
    }

    [Fact]
    public void 선택한_노트끼리_겹치도록_옮길_수_없다()
    {
        // 같은 레인 1·2마디를 둘 다 선택한 뒤 레인을 옮기면 겹치지 않지만,
        // 서로 다른 레인의 같은 자리를 한쪽으로 몰면 겹친다.
        var vm = WithNotes(("11", 1, 0.0), ("12", 1, 0.0));
        var moving = new[] { Find(vm, "12", 1) };
        vm.SetNoteSelection(moving);

        // 12 -> 11 로 옮기면 이미 있는 11번 노트와 같은 자리가 된다.
        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.LanePrevious);

        // 겹치면 저장할 때 한쪽이 조용히 사라진다. 아예 옮기지 않는다.
        Assert.Equal("12", moving[0].LaneId);
        Assert.Equal(2, vm.Chart.Notes.Count);
    }

    [Fact]
    public void 마디_범위를_벗어나면_아무것도_옮기지_않는다()
    {
        var vm = WithNotes(("11", 0, 0.0), ("11", 1, 0.0));
        vm.BeatSplit = 1;

        var moving = vm.Chart.Notes.ToArray();
        vm.SetNoteSelection(moving);

        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeBackward);

        Assert.Equal(new[] { 0, 1 }, moving.Select(n => n.Measure));
    }

    // ── 12. OGG를 나중에 열면 뒤쪽 마디를 편집할 수 없게 된다 ─────────────────

    [Fact]
    public void 마디_수는_차트가_요구하는_아래로_내려가지_않는다()
    {
        var vm = WithNotes(("11", 199, 0.0));

        // 사용자가 직접 줄이려 해도 이미 노트가 있는 마디까지 잠기면 안 된다.
        vm.MeasureCount = 10;

        Assert.True(vm.MeasureCount >= 200, $"마디 수가 {vm.MeasureCount} 로 줄었다");
        Assert.Equal(vm.MeasureCount, vm.Chart.MeasureCount);
    }

    [Fact]
    public void 마디_수를_늘리는_것은_그대로_받는다()
    {
        var vm = new MainWindowViewModel();
        vm.MeasureCount = 400;

        Assert.Equal(400, vm.MeasureCount);
        Assert.Equal(400, vm.Chart.MeasureCount);
    }

    [Fact]
    public void 최소_마디_수는_노트와_보존줄을_모두_본다()
    {
        var vm = WithNotes(("11", 50, 0.0));
        vm.Chart.PreservedLines.Add(new BmsRawLine { Text = "#30001:01", Measure = 300 });

        Assert.Equal(301, vm.MinimumMeasureCount);
    }

    // ── 23. 키음을 지워도 그 번호를 쓰던 노트가 그대로 남는다 ─────────────────

    [Fact]
    public void 쓰이는_키음을_지우려_하면_확인을_받는다()
    {
        var vm = WithNotes(("11", 1, 0.0));
        var wavPath = Path.Combine(_directory, "a.wav");
        File.WriteAllText(wavPath, "x");
        Assert.True(vm.AddWav(wavPath));

        // 방금 추가한 키음(01)을 노트가 쓰고 있다.
        Assert.Equal("01", vm.WavList[0].Key);
        Assert.Equal(1, vm.CountNotesUsingWavKey("01"));

        var asked = false;
        vm.ConfirmAsync = _ => { asked = true; return System.Threading.Tasks.Task.FromResult(false); };

        vm.RemoveWavCommand.Execute(null);

        Assert.True(asked, "확인을 받지 않고 지웠다");
        Assert.Single(vm.WavList);
        Assert.True(vm.Chart.WavTable.ContainsKey("01"));
    }

    [Fact]
    public void 쓰이지_않는_키음은_묻지_않고_지운다()
    {
        var vm = new MainWindowViewModel();
        var wavPath = Path.Combine(_directory, "a.wav");
        File.WriteAllText(wavPath, "x");
        Assert.True(vm.AddWav(wavPath));

        var asked = false;
        vm.ConfirmAsync = _ => { asked = true; return System.Threading.Tasks.Task.FromResult(true); };

        vm.RemoveWavCommand.Execute(null);

        Assert.False(asked);
        Assert.Empty(vm.WavList);
    }

    // ── 29. 노트 배치·삭제 시 Notes 컬렉션 참조가 갱신되어 UI가 다시 그려진다 ───

    [Fact]
    public void 노트_배치시_Notes_참조가_바뀌어_Avalonia_바인딩이_갱신된다()
    {
        var vm = new MainWindowViewModel();
        var wavPath = Path.Combine(_directory, "hit.wav");
        File.WriteAllText(wavPath, "dummy");
        vm.AddWav(wavPath);
        Assert.NotNull(vm.SelectedWavItem);

        var initialNotes = vm.Notes;
        var propertyChangedFired = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Notes))
                propertyChangedFired = true;
        };

        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("11", 1, 0.0));

        Assert.True(propertyChangedFired, "Notes PropertyChanged 알림이 발생하지 않았습니다");
        Assert.Single(vm.Notes);
        Assert.NotSame(initialNotes, vm.Notes);
    }

    [Fact]
    public void 노트_삭제시_Notes_참조가_바뀌어_Avalonia_바인딩이_갱신된다()
    {
        var vm = WithNotes(("11", 1, 0.0));
        var initialNotes = vm.Notes;
        var propertyChangedFired = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Notes))
                propertyChangedFired = true;
        };

        vm.RemoveNoteCommand.Execute(new NotePlacementArgs("11", 1, 0.0));

        Assert.True(propertyChangedFired, "Notes PropertyChanged 알림이 발생하지 않았습니다");
        Assert.Empty(vm.Notes);
        Assert.NotSame(initialNotes, vm.Notes);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using bms_editer.Models;
using bms_editer.Services;
using bms_editer.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace bms_editer.Tests;

// 되돌리기 / 다시 하기. (known_issues.md C 항목)
//
// 특히 못 박아 두는 것은 **제자리에서 고치는 편집**이다. 노트 이동과 키음 교체는 새 노트를
// 만드는 대신 기존 BmsNote 의 속성을 바꾼다. 기록을 참조로만 담으면 되돌릴 것까지 같이
// 바뀌어서, 되돌리기를 눌러도 아무 일이 안 일어난다. 그래서 스냅샷은 복제본이어야 한다.
public sealed class UndoRedoTests : IDisposable
{
    private readonly string _directory;
    private readonly ITestOutputHelper _output;

    public UndoRedoTests(ITestOutputHelper output)
    {
        _output = output;
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

    // 편집 경로를 실제로 지나가게 하려고 배치 명령으로 노트를 만든다.
    // Chart.Notes 에 직접 넣으면 기록이 남지 않아 되돌리기 테스트가 의미를 잃는다.
    private static MainWindowViewModel WithPlacedNotes(params (string Lane, int Measure)[] notes)
    {
        var vm = new MainWindowViewModel();

        // 키음 표에도 같이 넣는다. 목록에만 넣으면 다음 번호를 고르는 쪽이 빈 번호로 보고
        // 같은 "01" 을 다시 내주어서, 키음 추가 테스트가 엉뚱한 것을 비교하게 된다.
        vm.Chart.WavTable["01"] = "a.wav";
        vm.WavList.Add(new BmsWavItem { Key = "01", FilePath = "a.wav" });
        vm.SelectedWavItem = vm.WavList[0];
        vm.IsEditMode = true;

        foreach (var (lane, measure) in notes)
            vm.PlaceNoteCommand.Execute(new NotePlacementArgs(lane, measure, 0));

        return vm;
    }

    [Fact]
    public void 새_문서의_첫_편집도_되돌릴_수_있다()
    {
        // 바닥 상태를 깔아 두지 않으면 첫 편집만 되돌릴 수 없다.
        // 두 번째부터는 멀쩡해서 놓치기 쉬운 구멍이다.
        var vm = new MainWindowViewModel();
        Assert.False(vm.CanUndo);

        vm.WavList.Add(new BmsWavItem { Key = "01", FilePath = "a.wav" });
        vm.SelectedWavItem = vm.WavList[0];
        vm.IsEditMode = true;
        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("13", 1, 0));

        Assert.Single(vm.Chart.Notes);
        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
    }

    [Fact]
    public void 노트를_지우고_되돌리면_되살아난다()
    {
        var vm = WithPlacedNotes(("13", 1), ("13", 2), ("14", 3));
        Assert.Equal(3, vm.Chart.Notes.Count);

        vm.SetNoteSelection(vm.Chart.Notes.ToArray());
        vm.DeleteSelectedNotesCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);

        vm.UndoCommand.Execute(null);

        Assert.Equal(3, vm.Chart.Notes.Count);
        Assert.Equal(new[] { 1, 2, 3 }, vm.Chart.Notes.Select(n => n.Measure).OrderBy(m => m));
        Assert.Equal(new[] { "13", "13", "14" }, vm.Chart.Notes.Select(n => n.LaneId).OrderBy(l => l));
    }

    [Fact]
    public void 되돌린_뒤_다시_하면_다시_지워진다()
    {
        var vm = WithPlacedNotes(("13", 1), ("13", 2));
        vm.SetNoteSelection(vm.Chart.Notes.ToArray());
        vm.DeleteSelectedNotesCommand.Execute(null);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Chart.Notes.Count);
        Assert.True(vm.CanRedo);

        vm.RedoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
    }

    // 이동은 BmsNote 의 Measure 를 제자리에서 고친다. 참조만 담았으면 여기서 깨진다.
    [Fact]
    public void 노트를_옮기고_되돌리면_자리가_돌아온다()
    {
        var vm = WithPlacedNotes(("13", 1));
        vm.BeatSplit = 16;
        var before = (vm.Chart.Notes[0].Measure, vm.Chart.Notes[0].Position);

        vm.SetNoteSelection(vm.Chart.Notes.ToArray());
        vm.MoveSelectedNotesCommand.Execute(NoteMoveDirection.TimeForward);
        Assert.NotEqual(before, (vm.Chart.Notes[0].Measure, vm.Chart.Notes[0].Position));

        vm.UndoCommand.Execute(null);

        Assert.Equal(before, (vm.Chart.Notes[0].Measure, vm.Chart.Notes[0].Position));
    }

    // 키음 교체도 제자리에서 고친다.
    [Fact]
    public void 키음_교체를_되돌리면_번호가_돌아온다()
    {
        var vm = WithPlacedNotes(("13", 1), ("13", 2));
        vm.WavList.Add(new BmsWavItem { Key = "02", FilePath = "b.wav" });

        Assert.Equal(2, vm.ReplaceWavKey(vm.Chart.Notes.ToArray(), "02"));
        Assert.All(vm.Chart.Notes, n => Assert.Equal("02", n.WavKey));

        vm.UndoCommand.Execute(null);

        Assert.All(vm.Chart.Notes, n => Assert.Equal("01", n.WavKey));
    }

    [Fact]
    public void 되돌리기는_스스로_기록을_쌓지_않는다()
    {
        // 되돌리기가 한 칸을 새로 쌓으면, 두 번 눌러야 한 칸 움직이고 다시 하기는 영영 비활성이 된다.
        var vm = WithPlacedNotes(("13", 1), ("13", 2), ("13", 3));
        Assert.Equal(3, vm.Chart.Notes.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Chart.Notes.Count);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Chart.Notes);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
    }

    [Fact]
    public void 되돌린_뒤_새로_편집하면_다시_하기가_사라진다()
    {
        var vm = WithPlacedNotes(("13", 1), ("13", 2));
        vm.UndoCommand.Execute(null);
        Assert.True(vm.CanRedo);

        vm.PlaceNoteCommand.Execute(new NotePlacementArgs("14", 5, 0));

        Assert.False(vm.CanRedo);
        Assert.Contains(vm.Chart.Notes, n => n.LaneId == "14" && n.Measure == 5);
    }

    [Fact]
    public void 선택도_함께_되돌아간다()
    {
        var vm = WithPlacedNotes(("13", 1), ("13", 2));
        vm.SetNoteSelection(new[] { vm.Chart.Notes[0] });
        vm.DeleteSelectedNotesCommand.Execute(null);
        Assert.Empty(vm.SelectedNotes);

        vm.UndoCommand.Execute(null);

        // 지우기 직전에 골라 두었던 노트가 다시 선택돼 있어야 어디를 되돌린 건지 보인다.
        var restored = Assert.Single(vm.SelectedNotes);
        Assert.Equal(1, restored.Measure);

        // 되살아난 노트는 복제본이므로, 선택은 지금 문서 안의 노트를 가리켜야 한다.
        Assert.Contains(restored, vm.Chart.Notes);
    }

    [Fact]
    public void 키음_추가를_되돌리면_키음_표도_돌아간다()
    {
        // 노트만 되돌리고 키음을 두면, 지운 번호를 가리키는 유령 노트가 생긴다.
        var vm = WithPlacedNotes(("13", 1));
        var wavPath = Path.Combine(_directory, "added.wav");
        File.WriteAllBytes(wavPath, Array.Empty<byte>());

        Assert.True(vm.AddWav(wavPath));
        var addedKey = vm.WavList[^1].Key;
        Assert.True(vm.Chart.WavTable.ContainsKey(addedKey));

        vm.UndoCommand.Execute(null);

        Assert.DoesNotContain(vm.WavList, w => w.Key == addedKey);
        Assert.False(vm.Chart.WavTable.ContainsKey(addedKey));
    }

    [Fact]
    public void 홀드_짝도_되돌린_상태로_다시_읽는다()
    {
        var vm = LoadChartWithOneHold();
        Assert.Single(vm.HoldLinks);
        Assert.False(vm.HasHoldDiagnostics);

        // 끝 노트를 지우면 짝이 깨진다.
        var tail = vm.Chart.Notes.OrderBy(n => n.Measure).Last();
        vm.DeleteNotes(new[] { tail });
        Assert.Empty(vm.HoldLinks);
        Assert.True(vm.HasHoldDiagnostics);

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.HoldLinks);
        Assert.False(vm.HasHoldDiagnostics);
    }

    [Fact]
    public void 파일을_새로_열면_이전_문서로_거슬러_올라갈_수_없다()
    {
        var vm = WithPlacedNotes(("13", 1), ("13", 2));
        Assert.True(vm.CanUndo);

        var path = WriteChart("#TITLE 새문서\r\n#BPM 120\r\n#WAV01 a.wav\r\n#00113:0100\r\n");
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.Single(vm.Chart.Notes);
    }

    [Fact]
    public void 되돌릴_것이_없으면_명령이_비활성이다()
    {
        var vm = new MainWindowViewModel();

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));

        // 눌러도 아무 일이 없어야 한다(예외 없이).
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.Empty(vm.Chart.Notes);
    }

    [Fact]
    public void 메뉴에_무엇이_되돌아가는지_보인다()
    {
        var vm = WithPlacedNotes(("13", 1));
        Assert.Equal("되돌리기 (노트 배치)", vm.UndoMenuHeader);

        vm.SetNoteSelection(vm.Chart.Notes.ToArray());
        vm.DeleteSelectedNotesCommand.Execute(null);
        Assert.Equal("되돌리기 (노트 삭제)", vm.UndoMenuHeader);

        vm.UndoCommand.Execute(null);
        Assert.Equal("다시 하기 (노트 삭제)", vm.RedoMenuHeader);
    }

    [Fact]
    public void 칸이_상한을_넘으면_가장_오래된_것부터_버린다()
    {
        var vm = new MainWindowViewModel();
        vm.WavList.Add(new BmsWavItem { Key = "01", FilePath = "a.wav" });
        vm.SelectedWavItem = vm.WavList[0];
        vm.IsEditMode = true;

        var steps = EditHistory.MaxDepth + 20;
        for (var i = 0; i < steps; i++)
            vm.PlaceNoteCommand.Execute(new NotePlacementArgs("13", i, 0));

        Assert.Equal(steps, vm.Chart.Notes.Count);

        // 상한만큼만 되돌아가고, 그 뒤로는 더 내려가지 않는다.
        for (var i = 0; i < steps; i++)
            vm.UndoCommand.Execute(null);

        Assert.False(vm.CanUndo);
        Assert.Equal(steps - EditHistory.MaxDepth, vm.Chart.Notes.Count);
    }

    // 되돌리기가 편집을 느리게 만들지 않는지.
    //
    // 편집 한 번마다 노트 전체를 복제하는 설계라, 큰 차트에서 값이 어떻게 되는지 남겨 둔다.
    // 상한은 아주 느슨하게 둔다 — 여기서 걸린다는 것은 어딘가 제곱으로 돌고 있다는 뜻이다.
    // (이 저장소의 성능 테스트는 벽시계보다 횟수를 세는 쪽을 선호하지만,
    //  복제 비용은 횟수로 드러나지 않아 시간으로 잰다)
    [Fact]
    public void 큰_차트에서도_편집마다_기록하는_비용이_감당된다()
    {
        const int measures = 500;
        const int noteCount = measures * 2;
        const int edits = 100;

        // 파일로 읽어 들인다. Chart.Notes 에 직접 넣으면 편집 경로를 지나지 않아
        // 되돌리기의 바닥이 "빈 문서"로 남고, 되돌리다가 노트가 통째로 사라진다.
        var sb = new StringBuilder();
        sb.Append("#TITLE 큰차트\r\n#BPM 150\r\n#WAV01 a.wav\r\n");
        for (var m = 1; m <= measures; m++)
            sb.Append($"#{m:000}13:0101\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(WriteChart(sb.ToString())), vm.LastErrorMessage);
        Assert.Equal(noteCount, vm.Chart.Notes.Count);

        vm.SelectedWavItem = vm.WavList[0];
        vm.IsEditMode = true;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < edits; i++)
            vm.PlaceNoteCommand.Execute(new NotePlacementArgs("14", i + 1, 0));

        Assert.Equal(noteCount + edits, vm.Chart.Notes.Count);

        for (var i = 0; i < edits; i++)
            vm.UndoCommand.Execute(null);
        watch.Stop();

        _output.WriteLine($"노트 {noteCount}개 · 편집 {edits}회 + 되돌리기 {edits}회 = {watch.ElapsedMilliseconds}ms");
        Assert.Equal(noteCount, vm.Chart.Notes.Count);
        Assert.True(
            watch.ElapsedMilliseconds < 5000,
            $"노트 {noteCount}개 차트에서 편집 {edits}회 + 되돌리기 {edits}회에 {watch.ElapsedMilliseconds}ms 걸렸습니다.");
    }

    // 홀드 시작·끝 한 쌍이 든 차트.
    private MainWindowViewModel LoadChartWithOneHold()
    {
        var path = WriteChart(
            "#TITLE 홀드\r\n#BPM 120\r\n" +
            "#WAV01 1번 씬 wav폴더\\010201_홀드 지상 시작 노트_dt1.48.wav\r\n" +
            "#WAV02 1번 씬 wav폴더\\010201_홀드 지상 끝 노트_dt1.48.wav\r\n" +
            "#00113:0100\r\n#00213:0200\r\n");

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadBms(path), vm.LastErrorMessage);
        return vm;
    }

    private string WriteChart(string content)
    {
        var path = Path.Combine(_directory, Path.GetRandomFileName() + ".bms");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using bms_editer.Models;
using bms_editer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

// 컨트롤 패널(🎛️) 창의 상태 모델.
//
// 집계를 보여주는 데서 그치지 않고, 그 줄을 골라 노트를 다루는 공구함이다.
//   * 레인/키음 한 줄을 고르면 그 노트를 격자에서 한꺼번에 선택하고 그 자리로 스크롤한다
//   * 고른 키음을 바로 들어 본다
//   * 고른 키음을 쓰는 노트의 번호를 한꺼번에 바꾸거나 지운다
//
// 개수가 0인 항목은 목록에서 빼고 실제로 쓰인 레인/키음만 보여준다.
// 키음은 등록된 #WAV 목록이 아니라 노트가 실제로 가리키는 번호를 기준으로 센다.
// (수백 개짜리 키음 테이블에서 쓰지 않는 번호가 목록을 가득 채우는 걸 막는다)
//
// **화면에서 명령까지 닿는 길을 짧게 둔다.** 예전에 같은 기능을 세 번 만들다 접었고,
// 그중 두 번은 목록 항목 안에서 조상 바인딩으로 명령을 끌어오다 컴파일된 바인딩에서
// 조용히 끊어진 것이 원인이었다. 그래서 목록 항목에는 아무 명령도 걸지 않는다.
// 고른 줄은 SelectedLaneStat / SelectedWavStat 로 받고, 누르는 것은 전부 목록 **밖**의
// 버튼이다. 창의 DataContext 가 곧 이 뷰모델이라 바인딩이 한 칸도 건너뛰지 않는다.
public sealed partial class ControlPanelViewModel : NoteStatsViewModel
{
    // 다시 집계하는 동안에는 목록이 통째로 갈린다. 그때 ListBox 가 선택을 null 로
    // 밀어 넣는데, 그 null 을 그대로 받으면 편집 한 번에 고른 줄이 풀린다.
    // 되찾아 넣는 동안 미리듣기가 제멋대로 울리는 것도 같은 자리에서 막는다.
    private bool _isRefreshing;
    private string? _selectedLaneId;
    private string? _selectedWavKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectLaneNotesCommand))]
    private LaneNoteStat? _selectedLaneStat;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectWavNotesCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewWavCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWavNotesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteWavNotesCommand))]
    private WavNoteStat? _selectedWavStat;

    // 키음 줄을 고르면 바로 소리가 나야 "어떤 소리였더라"를 따로 눌러 보지 않아도 안다.
    // 키음 팔레트의 미리듣기와 같은 성격이라 기본값도 같이 켜 둔다.
    [ObservableProperty] private bool _previewOnSelect = true;

    [ObservableProperty] private string _replacementWavKey = string.Empty;

    [ObservableProperty] private string _statusMessage = "레인이나 키음 줄을 고른 뒤 아래 버튼을 누르세요.";

    public ControlPanelViewModel(MainWindowViewModel owner) : base(owner)
    {
        ReplacementWavKey = WavKey.Format(1, Owner.WavKeyWidth);
    }

    // 고른 키음을 바로 들려준다. 다시 집계하느라 목록이 갈리면서 같은 줄을
    // 되찾아 넣는 중에는 울리지 않는다. 편집할 때마다 소리가 나면 안 된다.
    partial void OnSelectedWavStatChanged(WavNoteStat? value)
    {
        if (_isRefreshing || value is null || !PreviewOnSelect)
            return;

        Owner.PlayWavSound(value.Key);
    }

    // 고른 레인의 노트를 격자에서 한꺼번에 선택한다.
    [RelayCommand(CanExecute = nameof(HasLaneSelection))]
    private void SelectLaneNotes()
    {
        if (SelectedLaneStat is not { } lane)
            return;

        var matches = Owner.Chart.Notes
            .Where(note => string.Equals(note.LaneId, lane.LaneId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SelectAndFocus(matches, $"{lane.Header} 레인의 노트 {matches.Count}개를 선택했습니다.");
    }

    // 고른 키음을 쓰는 노트를 격자에서 한꺼번에 선택한다.
    [RelayCommand(CanExecute = nameof(HasWavSelection))]
    private void SelectWavNotes()
    {
        if (SelectedWavStat is not { } wav)
            return;

        var matches = FindNotesUsing(wav.Key);
        SelectAndFocus(matches, $"#{wav.Key} 를 쓰는 노트 {matches.Count}개를 선택했습니다.");
    }

    [RelayCommand(CanExecute = nameof(HasWavSelection))]
    private void PreviewWav()
    {
        if (SelectedWavStat is not { } wav)
            return;

        if (!Owner.Chart.WavTable.ContainsKey(wav.Key))
        {
            StatusMessage = $"#{wav.Key} 는 등록되지 않은 번호라 들려줄 소리가 없습니다.";
            return;
        }

        Owner.PlayWavSound(wav.Key);
        StatusMessage = $"#{wav.Key} 를 재생했습니다.";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        Owner.ClearNoteSelection();
        StatusMessage = "선택을 해제했습니다.";
    }

    // 고른 키음을 쓰는 노트의 번호를 한꺼번에 바꾼다.
    [RelayCommand(CanExecute = nameof(HasWavSelection))]
    private void ReplaceWavNotes()
    {
        if (SelectedWavStat is not { } wav)
            return;

        var width = Owner.WavKeyWidth;
        if (!WavKey.TryParse(ReplacementWavKey, out var parsed) || parsed == 0 || parsed > WavKey.MaxValue(width))
        {
            StatusMessage = $"바꿀 번호는 {WavKey.Format(1, width)} ~ {new string('Z', width)} 사이여야 합니다.";
            return;
        }

        var target = WavKey.Format(parsed, width);
        ReplacementWavKey = target;

        if (string.Equals(target, wav.Key, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "바꿀 번호가 지금 번호와 같습니다.";
            return;
        }

        var matches = FindNotesUsing(wav.Key);
        var changed = Owner.ReplaceWavKey(matches, target);

        // 바뀐 노트가 곧 새 번호 줄이 된다. 다시 집계된 목록에서 그 줄을 고른 채로 둔다.
        SelectWavStatByKey(target);

        var unregistered = Owner.Chart.WavTable.ContainsKey(target)
            ? string.Empty
            : $" (경고: #WAV{target} 가 등록돼 있지 않습니다)";

        StatusMessage = $"{changed}개를 #{target} (으)로 바꿨습니다.{unregistered}";
    }

    // 고른 키음을 쓰는 노트를 전부 지운다.
    //
    // **되돌릴 수 없다.** 되돌리기가 아직 없어서, 한 번에 수백 개가 사라질 수 있는
    // 이 버튼만은 먼저 물어본다. 확인 창을 띄울 길이 없으면 아무것도 하지 않는다.
    [RelayCommand(CanExecute = nameof(HasWavSelection))]
    private async Task DeleteWavNotesAsync()
    {
        if (SelectedWavStat is not { } wav)
            return;

        var matches = FindNotesUsing(wav.Key);
        if (matches.Count == 0)
        {
            StatusMessage = $"#{wav.Key} 를 쓰는 노트가 없습니다.";
            return;
        }

        if (Owner.ConfirmAsync is not { } confirm)
        {
            StatusMessage = "확인 창을 띄울 수 없어 지우지 않았습니다.";
            return;
        }

        var proceed = await confirm(
            $"#{wav.Key} 를 쓰는 노트 {matches.Count}개를 지웁니다.\n\n" +
            "되돌리기가 없어서 지운 노트는 되살릴 수 없습니다.\n\n" +
            "그래도 지울까요?");

        if (!proceed)
        {
            StatusMessage = "지우지 않았습니다.";
            return;
        }

        var removed = Owner.DeleteNotes(matches);
        StatusMessage = $"#{wav.Key} 를 쓰던 노트 {removed}개를 지웠습니다.";
    }

    private bool HasLaneSelection => SelectedLaneStat is not null;

    private bool HasWavSelection => SelectedWavStat is not null;

    private IReadOnlyList<BmsNote> FindNotesUsing(string key) =>
        Owner.Chart.Notes
            .Where(note => string.Equals(note.WavKey, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

    // 격자 밖에서 만든 선택이므로 Search 로 알린다. 그래야 고른 노트가 화면 밖이어도
    // 격자가 그 자리로 스크롤해서, 눌렀는지조차 알 수 없던 예전 문제가 되풀이되지 않는다.
    private void SelectAndFocus(IReadOnlyList<BmsNote> notes, string message)
    {
        Owner.SetNoteSelection(notes, NoteSelectionSource.Search);
        StatusMessage = notes.Count == 0 ? "해당하는 노트가 없습니다." : message;
    }

    private void SelectWavStatByKey(string key) =>
        SelectedWavStat = WavStats.FirstOrDefault(
            stat => string.Equals(stat.Key, key, StringComparison.OrdinalIgnoreCase));

    // 목록이 통째로 갈리기 직전에 고르고 있던 줄을 번호로 적어 둔다.
    protected override void OnBeforeRefresh()
    {
        _selectedLaneId = SelectedLaneStat?.LaneId;
        _selectedWavKey = SelectedWavStat?.Key;
        _isRefreshing = true;
    }

    // 새 인스턴스로 같은 줄을 다시 잡아 준다. 개수가 0이 되어 사라진 줄은 선택이 풀린다.
    protected override void OnAfterRefresh()
    {
        SelectedLaneStat = _selectedLaneId is null
            ? null
            : Stats.FirstOrDefault(stat =>
                string.Equals(stat.LaneId, _selectedLaneId, StringComparison.OrdinalIgnoreCase));

        SelectedWavStat = _selectedWavKey is null
            ? null
            : WavStats.FirstOrDefault(stat =>
                string.Equals(stat.Key, _selectedWavKey, StringComparison.OrdinalIgnoreCase));

        _isRefreshing = false;
    }
}

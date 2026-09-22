using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace bms_editer.ViewModels;

// 레인 한 줄. LaneId 는 실제 채널 번호(11~18), Header 는 화면에 보이는 이름이다.
//
// 예전에는 LaneId 자리에 Header 를 넣어 두 값이 어긋나 있었다. 지금은 둘이 같아서
// 드러나지 않았지만, 이 목록으로 노트를 골라내려면(컨트롤 패널) 채널 번호가 진짜여야 한다.
public sealed record LaneNoteStat(string LaneId, string Header, int Count);

public sealed record WavNoteStat(string Key, string FileName, int Count);

// 노트 통계 창(📊)의 상태 모델. **집계해서 보여주기만 한다.**
//
// 편집 결과가 곧바로 보이도록 메인 뷰모델의 노트 변경 알림을 구독해 다시 집계한다.
// 개수가 0인 항목은 목록에서 빼고 실제로 쓰인 레인/키음만 보여준다.
// 키음은 등록된 #WAV 목록이 아니라 노트가 실제로 가리키는 번호를 기준으로 센다.
// (수백 개짜리 키음 테이블에서 쓰지 않는 번호가 목록을 가득 채우는 걸 막는다)
//
// 이 집계를 그대로 물려받아 "고른 줄의 노트를 다루는" 공구함이 컨트롤 패널(🎛️)이다.
// 세는 규칙이 두 벌로 갈라지면 두 창이 서로 다른 숫자를 보여주게 되므로,
// 집계는 여기 한 곳에만 두고 컨트롤 패널은 앞뒤로 끼어들 자리만 빌려 간다.
public partial class NoteStatsViewModel : OwnerObservingViewModel
{
    [ObservableProperty] private IReadOnlyList<LaneNoteStat> _stats = Array.Empty<LaneNoteStat>();
    [ObservableProperty] private IReadOnlyList<WavNoteStat> _wavStats = Array.Empty<WavNoteStat>();
    [ObservableProperty] private int _totalCount;

    public NoteStatsViewModel(MainWindowViewModel owner) : base(owner)
    {
        Refresh();
    }

    public bool HasLaneStats => Stats.Count > 0;
    public bool HasWavStats => WavStats.Count > 0;

    partial void OnStatsChanged(IReadOnlyList<LaneNoteStat> value) => OnPropertyChanged(nameof(HasLaneStats));

    partial void OnWavStatsChanged(IReadOnlyList<WavNoteStat> value) => OnPropertyChanged(nameof(HasWavStats));

    protected override void OnOwnerPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(MainWindowViewModel.Notes))
            Refresh();
    }

    // 키음을 추가·삭제하면 번호에 딸린 파일명 표시가 달라진다.
    protected override void OnWavListChanged() => Refresh();

    // 목록을 통째로 갈아끼우기 직전과 직후. 고른 줄을 지켜야 하는 창이 여기에 끼어든다.
    protected virtual void OnBeforeRefresh() { }

    protected virtual void OnAfterRefresh() { }

    protected void Refresh()
    {
        var chart = Owner.Chart;

        // 키음 번호 -> 파일명. 같은 번호가 두 번 정의돼 있으면 마지막 것을 쓴다.
        var fileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Owner.WavList)
        {
            fileNames[item.Key] = Path.GetFileName(item.FilePath);
        }

        var laneCounts = new Dictionary<string, int>();
        var wavCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in chart.Notes)
        {
            laneCounts[note.LaneId] = laneCounts.GetValueOrDefault(note.LaneId) + 1;
            wavCounts[note.WavKey] = wavCounts.GetValueOrDefault(note.WavKey) + 1;
        }

        OnBeforeRefresh();

        Stats = chart.Lanes
            .Select(lane => new LaneNoteStat(lane.Id, lane.Header, laneCounts.GetValueOrDefault(lane.Id)))
            .Where(stat => stat.Count > 0)
            .ToList();

        WavStats = wavCounts
            .Select(kv => new WavNoteStat(
                kv.Key,
                fileNames.TryGetValue(kv.Key, out var fileName) ? fileName : "(등록되지 않은 번호)",
                kv.Value))
            .OrderBy(stat => stat.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TotalCount = chart.Notes.Count;

        OnAfterRefresh();
    }
}

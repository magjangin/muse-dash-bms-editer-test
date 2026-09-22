using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using bms_editer.Models;
using bms_editer.Services.Holds;

namespace bms_editer.ViewModels;

// 홀드·샌드백 짝 읽기.
//
// 이 에디터는 뮤즈 대시 전용이라 게임을 고르는 자리를 두지 않는다. 프로파일은 exe 안의
// muse_dash.json 하나로 고정한다. 여러 게임을 다루는 쪽(bms editer)은 콤보박스와
// #BMSEDITER_PROFILE 헤더로 프로파일을 고르지만, 여기서 그 헤더를 쓰면 열었다 저장만 해도
// 원본에 없던 줄이 붙는다. 저장 결과가 바이트까지 같아야 한다는 약속을 깨지 않으려고 뺐다.
//
// 결과(HoldResult)는 Chart.Notes 를 건드리지 않는 파생 정보다. 편집마다 통째로 다시 만든다.
public sealed partial class MainWindowViewModel
{
    // 프로파일을 못 읽으면 홀드가 하나도 안 잡힌다. 그 상태를 화면에 말해 주려고 이유를 들고 있는다.
    private static readonly GameProfile HoldProfile =
        GameProfileCatalog.Default.Find("muse_dash") ?? GameProfile.None;

    public HoldPairingResult HoldResult { get; private set; } = HoldPairingResult.Empty;

    public IReadOnlyList<HoldLink> HoldLinks => HoldResult.Links;

    public IReadOnlyList<HoldDiagnostic> HoldDiagnostics => HoldResult.Diagnostics;

    public bool HasHoldDiagnostics => HoldResult.Diagnostics.Count > 0;

    // 격자에 경고 테두리를 칠 노트. 오류 수준만 칠한다. 주의까지 칠하면 경고가 풍경이 된다.
    public IReadOnlyList<BmsNote> HoldProblemNotes { get; private set; } = Array.Empty<BmsNote>();

    public string HoldSummaryText { get; private set; } = "";

    // 짝이 맞은 노트. 검색·통계에서 "롱"을 노트 종류가 아니라 짝으로 판정할 때 쓴다.
    private HashSet<BmsNote> _holdNotes = new();

    public bool IsHoldNote(BmsNote note) => _holdNotes.Contains(note);

    // 검사기 목록에서 고른 항목. 고르면 그 노트를 선택해 격자를 그 자리로 옮긴다.
    [ObservableProperty] private HoldDiagnostic? _selectedHoldDiagnostic;

    partial void OnSelectedHoldDiagnosticChanged(HoldDiagnostic? value)
    {
        if (value is not null && Chart.Notes.Contains(value.Note))
            SetNoteSelection(new[] { value.Note }, NoteSelectionSource.Search);
    }

    // 모든 편집 경로가 NotifyNotesChanged 를 지나므로 짝도 거기서 한 번에 다시 읽는다.
    // 부분 갱신은 캐시가 어긋나는 버그를 부르고, 실측 규모(수백 노트)에서는 전체 재계산이
    // 눈에 띄지 않는다.
    private void RecomputeHolds()
    {
        var result = HoldPairingEngine.Pair(Chart.Notes, BuildWavTexts(), HoldProfile);

        HoldResult = result;
        _holdNotes = new HashSet<BmsNote>(result.Links.SelectMany(link => new[] { link.Head, link.Tail }));
        HoldProblemNotes = result.Diagnostics
            .Where(d => d.Severity == HoldDiagnosticSeverity.Error)
            .Select(d => d.Note)
            .Distinct()
            .ToArray();
        HoldSummaryText = BuildHoldSummary(result);

        OnPropertyChanged(nameof(HoldResult));
        OnPropertyChanged(nameof(HoldLinks));
        OnPropertyChanged(nameof(HoldDiagnostics));
        OnPropertyChanged(nameof(HasHoldDiagnostics));
        OnPropertyChanged(nameof(HoldProblemNotes));
        OnPropertyChanged(nameof(HoldSummaryText));
    }

    private static string BuildHoldSummary(HoldPairingResult result)
    {
        if (HoldProfile.IsNone)
            return "⚠️ 홀드 규칙(muse_dash 프로파일)을 읽지 못했습니다. 짝을 읽지 않습니다.";

        var errors = result.Diagnostics.Count(d => d.Severity == HoldDiagnosticSeverity.Error);
        var warnings = result.Diagnostics.Count - errors;

        var text = $"홀드 짝 {result.Links.Count}개";
        text += errors > 0 ? $" · 🔴 문제 {errors}" : " · 문제 없음";
        if (warnings > 0)
            text += $" · 🟡 주의 {warnings}";

        return text;
    }

    // 키 -> #WAV 에 적힌 글자 그대로. 뮤즈 대시는 파일명 앞 6자리 UID 로 종류를 가리는데,
    // 폴더가 붙은 채로 적혀 있어서(`10번 씬 wav폴더\000201_하트 지상.wav`) 원문을 넘기고
    // 폴더를 떼는 일은 규칙 쪽(HoldRoleClassifier)에 맡긴다.
    private IReadOnlyDictionary<string, string> BuildWavTexts()
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in WavList)
            texts[item.Key] = string.IsNullOrEmpty(item.SourceText) ? item.FileName : item.SourceText;

        return texts;
    }
}

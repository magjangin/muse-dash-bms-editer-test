using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

// 구간 씬 바꾸기의 결과. 검색 창이 사람이 읽을 문장으로 옮긴다.
public sealed record SceneConvertResult(
    int Converted,
    int AlreadyTarget,
    int NotSceneBound,
    int UnresolvedNotes,
    IReadOnlyList<string> UnresolvedNames,
    int CreatedWavs,
    int MissingFiles,
    string? StartToggle,
    string? ReturnToggle,
    int TogglesInside,
    IReadOnlyList<string> Warnings)
{
    public bool Changed => Converted > 0 || StartToggle is not null || ReturnToggle is not null;
}

// 구간 씬 바꾸기.
//
// 뮤즈 대시에서 구간의 씬을 바꾸려면 두 가지를 같이 해야 한다.
//   1. 배경: 구간 시작에 씬 전환 노트(0004yy)를 둔다.
//   2. 몬스터: 구간 안 노트의 키음을 그 씬의 같은 키음(zz 만 다른 것)으로 바꾼다.
//      게임은 노트마다 UID 앞 두 자리 씬의 그림을 쓴다. 배경만 바꾸면 1번 씬 몬스터가 5번 씬 배경에 나온다.
// 손으로 하면 구간의 노트를 종류별로 골라 번호를 하나씩 바꿔야 해서, 한 번에 한다.
public sealed partial class MainWindowViewModel
{
    // 씬 전환 노트를 둘 레인. 앞 레인이 차 있으면 다음 레인에 둔다.
    // 게임은 노트 채널(13·14)에 있는 씬 전환 노트를 읽는다.
    private static readonly string[] SceneToggleLanes = { "13", "14" };

    public SceneConvertResult ConvertSectionToScene(
        IReadOnlyList<BmsNote> notes,
        int measureFrom,
        int measureTo,
        int scene,
        bool addStartToggle,
        bool addReturnToggle)
    {
        var target = SceneUid.Format(scene);
        var low = Math.Max(0, Math.Min(measureFrom, measureTo));
        var high = Math.Min(Math.Max(measureFrom, measureTo), MeasureCount - 1);
        var warnings = new List<string>();

        if (low > high)
        {
            warnings.Add($"마디 범위가 차트 밖입니다. 이 차트는 0~{MeasureCount - 1}마디입니다.");
            return Empty(warnings);
        }

        var resolver = new SceneWavResolver(
            WavList, Chart.WavTable.Keys, WavKeyWidth, FindChartFolder(), File.Exists);

        bool IsToggle(BmsNote note) => SceneUid.ToggleTargetOf(resolver.TextOfKey(note.WavKey)) is not null;

        // 1. 키음 바꾸기
        var rekeys = new List<(BmsNote Note, string Key)>();
        var already = 0;
        var notBound = 0;
        var unresolvedNotes = 0;
        var unresolvedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in notes)
        {
            if (IsToggle(note))
                continue;

            switch (resolver.ResolveNote(note.WavKey, target, out var key))
            {
                case SceneKeyOutcome.Converted when key is not null:
                    if (!string.Equals(note.WavKey, key, StringComparison.OrdinalIgnoreCase))
                        rekeys.Add((note, key));
                    break;
                case SceneKeyOutcome.AlreadyTarget:
                    already++;
                    break;
                case SceneKeyOutcome.NotSceneBound:
                    notBound++;
                    break;
                default:
                    unresolvedNotes++;
                    unresolvedNames.Add(resolver.TextOfKey(note.WavKey) is { } text
                        ? SceneUid.FileNameOf(text)
                        : $"#WAV{note.WavKey} (정의 없음)");
                    break;
            }
        }

        // 2. 씬 전환 노트
        var removed = new List<BmsNote>();
        var added = new List<BmsNote>();
        string? startToggle = null;
        string? returnToggle = null;

        if (addStartToggle)
        {
            // 같은 자리에 있던 씬 전환은 갈아 끼운다. 같은 시각에 두 씬으로 가라고 하면 어느 쪽이 될지 모른다.
            var atStart = Chart.Notes.Where(n => IsToggle(n) && IsAt(n, low, 0.0)).ToList();
            var keep = atStart.FirstOrDefault(n => SceneUid.ToggleTargetOf(resolver.TextOfKey(n.WavKey)) == target);

            var replaced = atStart.Where(n => n != keep).ToList();
            removed.AddRange(replaced);

            if (keep is not null)
            {
                startToggle = $"{low}마디 시작 (이미 있음)";
            }
            else if (PlaceToggle(resolver, target, low, removed, added, warnings) is { } lane)
            {
                startToggle = $"{low}마디 시작 · {lane} 레인";
            }
            else
            {
                // 새 것을 못 넣었으면 있던 것도 그대로 둔다.
                removed.RemoveAll(replaced.Contains);
            }
        }

        if (addReturnToggle && high + 1 < MeasureCount)
        {
            var boundary = high + 1;

            // 구간 뒤에서 처음 나오는 씬 노트의 씬으로 돌려 놓는다. 뒤쪽은 이번에 바꾸지 않았으니 원래 씬이다.
            var next = Chart.Notes
                .Where(n => n.Measure >= boundary && SceneUid.SceneOf(resolver.TextOfKey(n.WavKey)) is not null)
                .MinBy(n => n.Measure + n.Position);

            if (next is not null)
            {
                var nextScene = SceneUid.SceneOf(resolver.TextOfKey(next.WavKey))!;
                var nextTime = next.Measure + next.Position;

                // 그 사이에 씬 전환이 이미 있으면 사용자가 정해 둔 것이다. 덧대지 않는다.
                var decided = Chart.Notes.Any(n =>
                    IsToggle(n) && n.Measure + n.Position >= boundary - PositionEpsilon
                    && n.Measure + n.Position <= nextTime + PositionEpsilon);

                if (nextScene != target && !decided
                    && PlaceToggle(resolver, nextScene, boundary, removed, added, warnings) is { } lane)
                {
                    returnToggle = $"{boundary}마디 시작 · {lane} 레인 → {int.Parse(nextScene)}번 씬";
                }
            }
        }

        // 구간 안의 다른 씬 전환. 그 뒤로는 그 씬 배경이 되므로 알려 준다.
        var togglesInside = Chart.Notes.Count(n =>
            IsToggle(n) && !removed.Contains(n)
            && n.Measure + n.Position > low + PositionEpsilon
            && n.Measure + n.Position < high + 1 - PositionEpsilon);

        var result = new SceneConvertResult(
            rekeys.Count, already, notBound, unresolvedNotes, unresolvedNames.ToArray(),
            resolver.Created.Count, resolver.MissingFiles,
            startToggle, returnToggle, togglesInside, warnings);

        if (rekeys.Count == 0 && added.Count == 0 && removed.Count == 0)
            return result;

        // 3. 한꺼번에 적용한다. 되돌리기 한 칸에 전부 담긴다.
        foreach (var wav in resolver.Created)
        {
            Chart.WavTable[wav.Key] = wav.FilePath;
            WavList.Add(wav);
        }
        _keySoundPlayer.PreloadAsync(resolver.Created.Select(w => w.FilePath).Where(File.Exists).ToArray());

        foreach (var (note, key) in rekeys)
            note.WavKey = key;

        foreach (var note in removed)
            Chart.Notes.Remove(note);
        Chart.Notes.AddRange(added);

        // 바꾼 노트와 새 전환 노트를 골라 두어 결과를 바로 눈으로 확인하게 한다.
        SetNoteSelection(rekeys.Select(r => r.Note).Concat(added), NoteSelectionSource.Search);
        NotifyNotesChanged("씬 바꾸기");

        return result;
    }

    private static SceneConvertResult Empty(IReadOnlyList<string> warnings) =>
        new(0, 0, 0, 0, Array.Empty<string>(), 0, 0, null, null, 0, warnings);

    private static bool IsAt(BmsNote note, int measure, double position) =>
        note.Measure == measure && Math.Abs(note.Position - position) < PositionEpsilon;

    // 마디 첫 박에 씬 전환 노트를 둔다. 둔 레인을 돌려주고, 못 두면 사유를 warnings 에 적는다.
    private string? PlaceToggle(
        SceneWavResolver resolver,
        string scene,
        int measure,
        IReadOnlyCollection<BmsNote> removed,
        List<BmsNote> added,
        List<string> warnings)
    {
        var key = resolver.ResolveToggle(scene, out var failure);
        if (key is null)
        {
            warnings.Add($"{measure}마디에 씬 전환 노트를 넣지 못했습니다. {failure}");
            return null;
        }

        foreach (var lane in SceneToggleLanes)
        {
            if (!Chart.Lanes.Any(l => l.Id == lane))
                continue;

            var taken = Chart.Notes.Any(n => n.LaneId == lane && IsAt(n, measure, 0.0) && !removed.Contains(n))
                        || added.Any(n => n.LaneId == lane && IsAt(n, measure, 0.0));
            if (taken)
                continue;

            added.Add(new BmsNote
            {
                Measure = measure,
                LaneId = lane,
                Position = 0.0,
                WavKey = key,
                Type = NoteType.Normal,
            });
            return lane;
        }

        warnings.Add($"{measure}마디 첫 박의 {string.Join("·", SceneToggleLanes)} 레인이 모두 차 있어 "
                     + $"{int.Parse(scene)}번 씬 전환 노트를 넣지 못했습니다. 손으로 넣어 주세요.");
        return null;
    }

    // 새 #WAV 정의의 경로를 정할 기준 폴더. 저장된 차트면 그 폴더,
    // 아니면 적힌 경로와 실제 경로가 맞물리는 키음에서 거꾸로 구한다(건반 BMS 가져오기 직후 등).
    private string? FindChartFolder()
    {
        if (CurrentFilePath is { } path && Path.GetDirectoryName(path) is { Length: > 0 } folder)
            return folder;

        foreach (var wav in WavList)
        {
            if (wav.IsPathGuessed || string.IsNullOrEmpty(wav.SourceText))
                continue;

            var written = wav.SourceText.Replace('/', '\\');
            var actual = wav.FilePath.Replace('/', '\\');
            if (actual.Length > written.Length
                && actual.EndsWith("\\" + written, StringComparison.OrdinalIgnoreCase))
            {
                return actual[..(actual.Length - written.Length - 1)];
            }
        }

        return null;
    }
}

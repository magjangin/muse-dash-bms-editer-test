using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bms_editer.Models;
using bms_editer.Services;

namespace bms_editer.ViewModels;

// 노트 속도 바꾸기의 결과. 검색 창이 사람이 읽을 문장으로 옮긴다.
public sealed record SpeedConvertResult(
    int Converted,
    int AlreadyTarget,
    int NoSpeed,
    int UnresolvedNotes,
    IReadOnlyList<string> UnresolvedNames,
    int CreatedWavs,
    int MissingFiles);

// 노트 속도 바꾸기.
//
// 속도는 UID 끝 두 자리에 든다(Services/NoteSpeed). 노트의 키음을 같은 레인·모션의
// 속도 2·3 키음으로 바꾸면 게임이 그 속도로 노트를 내보낸다. 키음 표에 없으면 정의를 새로 적는다.
public sealed partial class MainWindowViewModel
{
    public SpeedConvertResult ConvertNotesToSpeed(IReadOnlyList<BmsNote> notes, int speed)
    {
        var resolver = new SceneWavResolver(
            WavList, Chart.WavTable.Keys, WavKeyWidth, FindChartFolder(), File.Exists);

        var rekeys = new List<(BmsNote Note, string Key)>();
        var already = 0;
        var noSpeed = 0;
        var unresolved = 0;
        var unresolvedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in notes)
        {
            switch (resolver.ResolveSpeed(note.WavKey, speed, out var key))
            {
                case SceneKeyOutcome.Converted when key is not null:
                    if (!string.Equals(note.WavKey, key, StringComparison.OrdinalIgnoreCase))
                        rekeys.Add((note, key));
                    break;
                case SceneKeyOutcome.AlreadyTarget:
                    already++;
                    break;
                case SceneKeyOutcome.NotSceneBound:
                    noSpeed++;
                    break;
                default:
                    unresolved++;
                    unresolvedNames.Add(resolver.TextOfKey(note.WavKey) is { } text
                        ? SceneUid.FileNameOf(text)
                        : $"#WAV{note.WavKey} (정의 없음)");
                    break;
            }
        }

        var result = new SpeedConvertResult(
            rekeys.Count, already, noSpeed, unresolved, unresolvedNames.ToArray(),
            resolver.Created.Count, resolver.MissingFiles);

        if (rekeys.Count == 0)
            return result;

        foreach (var wav in resolver.Created)
        {
            Chart.WavTable[wav.Key] = wav.FilePath;
            WavList.Add(wav);
        }
        _keySoundPlayer.PreloadAsync(resolver.Created.Select(w => w.FilePath).Where(File.Exists).ToArray());

        foreach (var (note, key) in rekeys)
            note.WavKey = key;

        SetNoteSelection(rekeys.Select(r => r.Note), NoteSelectionSource.Search);
        NotifyNotesChanged("속도 바꾸기");

        return result;
    }
}

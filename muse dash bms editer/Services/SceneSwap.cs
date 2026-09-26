using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services;

// 뮤즈 대시 #WAV 파일명의 6자리 UID(zzxxyy)에서 씬 번호를 읽는다.
//
//   zz = 노트가 그려질 씬. 게임은 노트마다 이 번호의 몬스터 그림을 쓴다.
//   0004yy = 씬 전환 노트. 배경을 yy번 씬으로 바꾼다.
//   zz 가 00 이면(하트·음표·씬 전환) 어느 씬에도 묶이지 않는다.
public static class SceneUid
{
    // 게임에 있는 씬 번호. 11번은 씬 전환 표(모드의 SceneToggleIbmsIdByScene)에 없어 뺀다.
    public static readonly IReadOnlyList<int> KnownScenes = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12 };

    public static string Format(int scene) => scene.ToString("00", CultureInfo.InvariantCulture);

    // 폴더를 뗀 파일명 앞 6자리. 없으면 null.
    public static string? Of(string? wavText)
    {
        if (string.IsNullOrEmpty(wavText))
            return null;

        var name = FileNameOf(wavText);
        if (name.Length < 6)
            return null;

        for (var i = 0; i < 6; i++)
        {
            if (name[i] is < '0' or > '9')
                return null;
        }

        return name[..6];
    }

    // 노트가 그려질 씬(zz). 씬에 묶이지 않는 노트(zz=00)와 UID 없는 키음은 null.
    public static string? SceneOf(string? wavText) =>
        Of(wavText) is { } uid && uid[..2] != "00" ? uid[..2] : null;

    // 씬 전환 노트면 바꿀 씬 번호(yy), 아니면 null.
    public static string? ToggleTargetOf(string? wavText) =>
        Of(wavText) is { } uid && uid.StartsWith("0004", StringComparison.Ordinal) ? uid[4..] : null;

    public static string FileNameOf(string wavText)
    {
        var cut = wavText.LastIndexOfAny(new[] { '\\', '/' });
        return cut < 0 ? wavText : wavText[(cut + 1)..];
    }
}

// 키음 하나를 다른 씬의 같은 키음으로 옮긴 결과.
public enum SceneKeyOutcome
{
    Converted,
    AlreadyTarget,
    NotSceneBound,
    Unresolved,
}

// "이 키음의 5번 씬 짝은 몇 번인가"를 찾는다.
//
// 뮤즈 대시 템플릿은 씬마다 같은 이름의 키음을 둔다.
//     1번 씬 wav폴더\011001_일반 노트1 지상 노멀_dt1.48.wav
//     5번 씬 wav폴더\051001_일반 노트1 지상 노멀_dt1.48.wav
// 그래서 폴더·UID·이름 속 "N번 씬" 만 바꾼 글자를 표에서 찾으면 대부분 바로 나온다.
// 이름이 템플릿과 다르면 같은 UID 중에서 이름표(씬 번호·_dt 를 뗀 이름)가 같은 것을 쓴다.
// 홀드 시작·끝처럼 UID 가 같고 이름으로만 갈리는 키음이 있어서 UID 만으로 고르면 안 된다.
//
// 표에 없으면 같은 규칙으로 만든 경로로 **정의를 새로 적는다**(Created). 파일은 만들지 않는다.
// 파일이 없으면 MissingFiles 에 센다 — 게임은 파일명의 UID 만 읽지만 에디터 미리듣기는 소리가 안 난다.
public sealed class SceneWavResolver
{
    private readonly List<BmsWavItem> _wavs;
    private readonly Dictionary<string, BmsWavItem> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keyByText = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Key, string Scene), (SceneKeyOutcome, string?)> _cache = new();
    private readonly HashSet<string> _usedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _keyWidth;
    private readonly string? _chartFolder;
    private readonly Func<string, bool> _fileExists;

    public SceneWavResolver(
        IEnumerable<BmsWavItem> wavs,
        IEnumerable<string> usedKeys,
        int keyWidth,
        string? chartFolder,
        Func<string, bool> fileExists)
    {
        // 키 순서로 둔다. 같은 이름이 여러 번 적혀 있으면 앞 번호를 고르게 된다.
        _wavs = wavs.OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var wav in _wavs)
        {
            // 같은 번호가 두 번 정의돼 있으면 저장할 때처럼 마지막 것을 쓴다.
            _byKey[wav.Key] = wav;
            _keyByText.TryAdd(Normalize(TextOf(wav)), wav.Key);
        }

        _usedKeys.UnionWith(usedKeys);
        _usedKeys.UnionWith(_byKey.Keys);
        _keyWidth = keyWidth;
        _chartFolder = chartFolder;
        _fileExists = fileExists;
    }

    // 이번에 새로 적은 정의. 부르는 쪽이 키음 표에 넣는다.
    public List<BmsWavItem> Created { get; } = new();

    // 새로 적은 정의 중 가리키는 파일이 없는 것.
    public int MissingFiles { get; private set; }

    // 키음 표에 적힌 글자. 노트 종류를 가를 때 쓰는 것과 같다(MainWindowViewModel.BuildWavTexts).
    public static string TextOf(BmsWavItem wav) =>
        string.IsNullOrEmpty(wav.SourceText) ? wav.FileName : wav.SourceText;

    public string? TextOfKey(string key) =>
        _byKey.TryGetValue(key, out var wav) ? TextOf(wav) : null;

    public SceneKeyOutcome ResolveNote(string sourceKey, string targetScene, out string? key)
    {
        if (!_cache.TryGetValue((sourceKey, targetScene), out var cached))
        {
            cached = ResolveNoteUncached(sourceKey, targetScene);
            _cache[(sourceKey, targetScene)] = cached;
        }

        key = cached.Item2;
        return cached.Item1;
    }

    private (SceneKeyOutcome, string?) ResolveNoteUncached(string sourceKey, string targetScene)
    {
        if (!_byKey.TryGetValue(sourceKey, out var source))
            return (SceneKeyOutcome.Unresolved, null);

        var text = TextOf(source);
        if (SceneUid.Of(text) is not { } uid || uid[..2] == "00")
            return (SceneKeyOutcome.NotSceneBound, null);

        var fromScene = uid[..2];
        if (fromScene == targetScene)
            return (SceneKeyOutcome.AlreadyTarget, sourceKey);

        var targetUid = targetScene + uid[2..];

        // 1. 템플릿대로 바꾼 글자가 표에 그대로 있으면 그것.
        var targetText = Rewrite(text, targetUid, fromScene, targetScene, rewriteFolder: true);
        if (_keyByText.TryGetValue(Normalize(targetText), out var exact))
            return (SceneKeyOutcome.Converted, exact);

        // 2. 같은 UID 중 이름표가 같은 것.
        var sameUid = _wavs.Where(w => SceneUid.Of(TextOf(w)) == targetUid).ToList();
        if (sameUid.Count > 0)
        {
            var label = Label(text);
            var match = sameUid.FirstOrDefault(w => Label(TextOf(w)) == label);
            if (match is not null)
                return (SceneKeyOutcome.Converted, match.Key);

            // 이름이 하나뿐이면 홀드 시작·끝처럼 헷갈릴 일이 없다.
            if (sameUid.Count == 1)
                return (SceneKeyOutcome.Converted, sameUid[0].Key);

            // 여러 개인데 어느 것인지 모르면 짐작하지 않는다. 홀드 시작을 끝으로 바꾸면 짝이 뒤집힌다.
            return (SceneKeyOutcome.Unresolved, null);
        }

        // 3. 표에 없으면 같은 규칙으로 정의를 새로 적는다.
        var created = CreateFrom(source, targetUid, fromScene, targetScene, rewriteFolder: true);
        return created is null ? (SceneKeyOutcome.Unresolved, null) : (SceneKeyOutcome.Converted, created);
    }

    // targetScene 으로 바꾸는 씬 전환 노트의 키음 번호. 못 만들면 null 과 사유.
    public string? ResolveToggle(string targetScene, out string failure)
    {
        failure = "";
        var targetUid = "0004" + targetScene;

        var existing = _wavs.FirstOrDefault(w => SceneUid.Of(TextOf(w)) == targetUid);
        if (existing is not null)
            return existing.Key;

        if (Created.FirstOrDefault(w => SceneUid.Of(TextOf(w)) == targetUid) is { } made)
            return made.Key;

        // 다른 씬으로 가는 전환 노트가 있으면 그 이름을 본뜬다. 폴더(씬 전환 wav 폴더)는 그대로다.
        var template = _wavs.FirstOrDefault(w => SceneUid.ToggleTargetOf(TextOf(w)) is not null);
        if (template is not null)
        {
            var fromScene = SceneUid.ToggleTargetOf(TextOf(template))!;
            var key = CreateFrom(template, targetUid, fromScene, targetScene, rewriteFolder: false);
            if (key is null)
                failure = "키음 번호가 가득 차서 씬 전환 노트를 정의할 자리가 없습니다.";
            return key;
        }

        // 본뜰 것도 없으면 템플릿 폴더 이름으로 새로 적는다. 경로를 만들려면 차트 폴더를 알아야 한다.
        if (_chartFolder is null)
        {
            failure = "키음 표에 씬 전환 노트가 하나도 없고, 차트가 아직 저장되지 않아 새로 적을 경로를 정할 수 없습니다.";
            return null;
        }

        var number = int.Parse(targetScene, CultureInfo.InvariantCulture);
        var text = $"씬 전환 wav 폴더\\{targetUid}_{number}번 씬 전환_dt0.wav";
        var newKey = AddDefinition(text, Path.Combine(_chartFolder, text), isPathGuessed: false);
        if (newKey is null)
            failure = "키음 번호가 가득 차서 씬 전환 노트를 정의할 자리가 없습니다.";
        return newKey;
    }

    private string? CreateFrom(BmsWavItem source, string targetUid, string fromScene, string toScene, bool rewriteFolder)
    {
        var text = Rewrite(TextOf(source), targetUid, fromScene, toScene, rewriteFolder);

        // 같은 글자로 이미 만들었으면 그것을 쓴다. 표에 같은 이름이 두 번 적혀 있던 경우다.
        if (_keyByText.TryGetValue(Normalize(text), out var known))
            return known;

        var sourceText = string.IsNullOrEmpty(source.SourceText)
            ? ""
            : Rewrite(source.SourceText, targetUid, fromScene, toScene, rewriteFolder);
        var filePath = Rewrite(source.FilePath, targetUid, fromScene, toScene, rewriteFolder);

        return AddDefinition(sourceText, filePath, source.IsPathGuessed);
    }

    private string? AddDefinition(string sourceText, string filePath, bool isPathGuessed)
    {
        var key = NextFreeKey();
        if (key is null)
            return null;

        var wav = new BmsWavItem
        {
            Key = key,
            SourceText = sourceText,
            FilePath = filePath,
            IsPathGuessed = isPathGuessed,
        };

        Created.Add(wav);
        _byKey[key] = wav;
        _keyByText.TryAdd(Normalize(TextOf(wav)), key);

        if (!_fileExists(filePath))
            MissingFiles++;

        return key;
    }

    private string? NextFreeKey()
    {
        // 00 / 000 은 "빈 자리"라 1부터 센다.
        for (var value = 1; value <= WavKey.MaxValue(_keyWidth); value++)
        {
            var key = WavKey.Format(value, _keyWidth);
            if (_usedKeys.Add(key))
                return key;
        }

        return null;
    }

    // 파일명의 UID 를 갈고, 파일명과 바로 위 폴더 이름의 "N번 씬" 을 새 번호로 바꾼다.
    // 그보다 위 폴더는 건드리지 않는다. 곡 폴더 이름에 우연히 "1번 씬" 이 들어 있을 수 있다.
    internal static string Rewrite(string path, string newUid, string fromScene, string toScene, bool rewriteFolder)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        var cut = path.LastIndexOfAny(new[] { '\\', '/' });
        var folder = cut < 0 ? "" : path[..(cut + 1)];
        var name = cut < 0 ? path : path[(cut + 1)..];

        if (SceneUid.Of(name) is not null)
            name = newUid + name[6..];
        name = ReplaceSceneNumber(name, fromScene, toScene);

        if (rewriteFolder && folder.Length > 1)
        {
            var parentCut = folder.LastIndexOfAny(new[] { '\\', '/' }, folder.Length - 2);
            var parent = parentCut < 0 ? "" : folder[..(parentCut + 1)];
            var last = parentCut < 0 ? folder : folder[(parentCut + 1)..];
            folder = parent + ReplaceSceneNumber(last, fromScene, toScene);
        }

        return folder + name;
    }

    // "1번 씬" 의 1 만 바꾼다. 10번·11번 씬의 1 은 건드리지 않는다.
    private static string ReplaceSceneNumber(string text, string fromScene, string toScene)
    {
        var from = int.Parse(fromScene, CultureInfo.InvariantCulture);
        var to = int.Parse(toScene, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        return Regex.Replace(text, $@"(?<!\d)0*{from}(?=\s*번\s*씬)", to);
    }

    // 씬마다 달라지는 부분(UID·"N번 씬"·_dt 값·확장자)을 뗀 이름. 같은 키음인지 볼 때 쓴다.
    internal static string Label(string wavText)
    {
        var name = Path.GetFileNameWithoutExtension(SceneUid.FileNameOf(wavText));
        if (SceneUid.Of(name) is not null)
            name = name[6..];

        name = Regex.Replace(name, @"(?<!\d)\d+\s*번\s*씬", "");
        name = Regex.Replace(name, @"_dt-?[0-9.]+", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"[\s_]+", " ");
        return name.Trim().ToLowerInvariant();
    }

    private static string Normalize(string text) => text.Replace('/', '\\').Trim();
}

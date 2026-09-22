using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using bms_editer.Models;

namespace bms_editer.Services.Holds;

// 게임 프로파일 목록.
//
// 기본 프로파일은 exe 안에 들어 있다(EmbeddedResource). README 가 "exe 하나만 복사하면 실행"을
// 약속하므로, 옆 폴더에만 두면 exe 만 복사해 간 사용자는 홀드가 전혀 안 잡히는데 이유가 화면에 없다.
// exe 옆에 profiles 폴더가 있으면 같은 id 를 덮어쓴다. 리빌드 없이 게임을 추가하거나 고칠 수 있다.
public sealed class GameProfileCatalog
{
    private const string ResourcePrefix = "profiles/";

    private static readonly Lazy<GameProfileCatalog> DefaultCatalog =
        new(() => Load(Path.Combine(AppContext.BaseDirectory, "profiles")));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GameProfileCatalog Default => DefaultCatalog.Value;

    public IReadOnlyList<GameProfile> Profiles { get; }

    // 읽지 못한 프로파일과 그 이유. 조용히 빠지면 "왜 이 게임이 목록에 없지"를 알 길이 없다.
    public IReadOnlyList<string> LoadErrors { get; }

    private GameProfileCatalog(IReadOnlyList<GameProfile> profiles, IReadOnlyList<string> loadErrors)
    {
        Profiles = profiles;
        LoadErrors = loadErrors;
    }

    public static GameProfileCatalog Load(string? overrideDirectory)
    {
        var byId = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        void TryAdd(string name, string json)
        {
            try
            {
                var profile = Parse(json);
                byId[profile.Id] = profile;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }

        foreach (var (name, json) in ReadEmbedded())
            TryAdd(name, json);

        if (!string.IsNullOrEmpty(overrideDirectory) && Directory.Exists(overrideDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(overrideDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    TryAdd(file, File.ReadAllText(file, Encoding.UTF8));
                }
                catch (IOException ex)
                {
                    errors.Add($"{file}: {ex.Message}");
                }
            }
        }

        var profiles = byId.Values
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCulture)
            .ToArray();

        return new GameProfileCatalog(profiles, errors);
    }

    public static IEnumerable<(string Name, string Json)> ReadEmbedded()
    {
        var assembly = typeof(GameProfileCatalog).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            yield return (name, reader.ReadToEnd());
        }
    }

    public static GameProfile Parse(string json)
    {
        var profile = JsonSerializer.Deserialize<GameProfile>(json, JsonOptions)
            ?? throw new FormatException("프로파일이 비어 있습니다.");

        Validate(profile);
        return profile;
    }

    // 규칙이 빠진 프로파일은 "홀드가 하나도 안 잡히는" 조용한 실패로 이어진다. 읽을 때 막는다.
    private static void Validate(GameProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new FormatException("id 가 없습니다.");

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            profile.DisplayName = profile.Id;

        if (profile.HoldRules.Count == 0)
            throw new FormatException($"{profile.Id}: holdRules 가 비어 있습니다.");

        foreach (var rule in profile.HoldRules)
        {
            var role = rule.Role;
            var where = $"{profile.Id}/{rule.Kind}";

            switch (role.Type)
            {
                case HoldRoleSource.KeyValue when role.HeadKeys.Count == 0 || role.TailKeys.Count == 0:
                    throw new FormatException($"{where}: KeyValue 는 headKeys 와 tailKeys 가 모두 있어야 합니다.");
                case HoldRoleSource.FileNameKeyword when role.HeadKeywords.Count == 0 || role.TailKeywords.Count == 0:
                    throw new FormatException($"{where}: FileNameKeyword 는 headKeywords 와 tailKeywords 가 모두 있어야 합니다.");
                case HoldRoleSource.FileNameUid when profile.UidTable is null:
                    throw new FormatException($"{where}: FileNameUid 는 프로파일에 uidTable 이 있어야 합니다.");
                case HoldRoleSource.FileNameBase when role.BaseNames.Count == 0:
                    throw new FormatException($"{where}: FileNameBase 는 baseNames 가 있어야 합니다.");
            }

            // 순서로 짝짓는 규칙은 시작/끝을 따로 고르지 않으므로 Alternate 와만 맞는다. 반대도 마찬가지다.
            var ordersByOccurrence = role.Type is HoldRoleSource.FileNameUid or HoldRoleSource.FileNameBase;
            if (ordersByOccurrence != (rule.Policy == HoldPairingPolicy.Alternate))
                throw new FormatException($"{where}: {role.Type} 와 {rule.Policy} 는 함께 쓸 수 없습니다.");
        }
    }

    public GameProfile? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    // 차트 경로로 게임을 추정한다. 파일에 가장 가까운 폴더부터 본다.
    //
    // 실제 게임 차트는 게임 폴더 아래 hwa\<곡>\hwa2.bms 에 있어서, 곡 폴더가 아니라
    // 그 위의 게임 폴더 이름이 단서가 된다. 여러 힌트가 걸리면 긴 힌트(더 구체적인 이름)를 따른다.
    public GameProfile? DetectFromPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var segments = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = segments.Length - 2; i >= 0; i--)
        {
            GameProfile? best = null;
            var bestLength = 0;

            foreach (var profile in Profiles)
            {
                foreach (var hint in profile.PathHints)
                {
                    if (hint.Length > bestLength && segments[i].Contains(hint, StringComparison.OrdinalIgnoreCase))
                    {
                        best = profile;
                        bestLength = hint.Length;
                    }
                }
            }

            if (best is not null)
                return best;
        }

        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace bms_editer.ViewModels;

// 키음 목록을 씬·종류·위치·스피드로 거르는 드롭다운 네 개의 상태.
//
// 키음 팔레트와 컨트롤 패널이 같은 규칙을 쓰므로 한 곳에 둔다. 값은 파일명에서
// 읽고(WavSearch.Traits), 지금 불러온 키음에 실제로 있는 것만 드롭다운에 나온다.
// 고르면 changed 로 알려서 부르는 쪽이 목록을 다시 거른다.
public sealed class WavTraitFilter : ObservableObject
{
    // 드롭다운의 "걸러내지 않음" 칸.
    public const string All = "전체";

    private readonly Action _changed;

    private string _sceneFilter = All;
    private string _kindFilter = All;
    private string _positionFilter = All;
    private string _speedFilter = All;

    private IReadOnlyList<string> _sceneOptions = [All];
    private IReadOnlyList<string> _kindOptions = [All];
    private IReadOnlyList<string> _positionOptions = [All];
    private IReadOnlyList<string> _speedOptions = [All];

    public WavTraitFilter(Action changed)
    {
        _changed = changed;
    }

    // ComboBox 는 목록이 바뀌면 null 을 밀어 넣으므로 null 은 무시한다.
    public IReadOnlyList<string> SceneOptions => _sceneOptions;
    public IReadOnlyList<string> KindOptions => _kindOptions;
    public IReadOnlyList<string> PositionOptions => _positionOptions;
    public IReadOnlyList<string> SpeedOptions => _speedOptions;

    public string SceneFilter
    {
        get => _sceneFilter;
        set { if (value is not null && SetProperty(ref _sceneFilter, value)) _changed(); }
    }

    public string KindFilter
    {
        get => _kindFilter;
        set { if (value is not null && SetProperty(ref _kindFilter, value)) _changed(); }
    }

    public string PositionFilter
    {
        get => _positionFilter;
        set { if (value is not null && SetProperty(ref _positionFilter, value)) _changed(); }
    }

    public string SpeedFilter
    {
        get => _speedFilter;
        set { if (value is not null && SetProperty(ref _speedFilter, value)) _changed(); }
    }

    // 값이 하나도 없는 분류는 드롭다운을 숨긴다.
    public bool HasSceneOptions => _sceneOptions.Count > 1;
    public bool HasKindOptions => _kindOptions.Count > 1;
    public bool HasPositionOptions => _positionOptions.Count > 1;
    public bool HasSpeedOptions => _speedOptions.Count > 1;
    public bool HasAnyOptions => HasSceneOptions || HasKindOptions || HasPositionOptions || HasSpeedOptions;

    public bool IsActive =>
        _sceneFilter != All || _kindFilter != All || _positionFilter != All || _speedFilter != All;

    // 네 드롭다운을 모두 풀되 changed 는 부르지 않는다. 부르는 쪽이 한 번에 다시 거른다.
    public void Reset()
    {
        _sceneFilter = _kindFilter = _positionFilter = _speedFilter = All;
        RaiseFilters();
    }

    // 지금 있는 키음 파일명에서 드롭다운 값을 다시 뽑는다. 새 목록에 없는 값을
    // 고르고 있었다면 풀어 준다(이때도 changed 는 부르지 않는다).
    public void Rebuild(IEnumerable<string> fileNames)
    {
        var traits = fileNames.Select(WavSearch.Traits).ToList();

        _sceneOptions = [All, .. traits.Where(t => t.Scene.HasValue).Select(t => t.Scene!.Value)
            .Distinct().Order().Select(WavSearch.SceneLabel)];
        _kindOptions = [All, .. traits.Where(t => t.Kind is not null).Select(t => t.Kind!).Distinct()];
        _positionOptions = [All, .. new[] { "지상", "공중" }.Where(p => traits.Any(t => t.Position == p))];
        _speedOptions = [All, .. traits.Where(t => t.Speed.HasValue).Select(t => t.Speed!.Value)
            .Distinct().Order().Select(speed => $"스피드{speed}")];

        if (!_sceneOptions.Contains(_sceneFilter)) _sceneFilter = All;
        if (!_kindOptions.Contains(_kindFilter)) _kindFilter = All;
        if (!_positionOptions.Contains(_positionFilter)) _positionFilter = All;
        if (!_speedOptions.Contains(_speedFilter)) _speedFilter = All;

        OnPropertyChanged(nameof(SceneOptions));
        OnPropertyChanged(nameof(KindOptions));
        OnPropertyChanged(nameof(PositionOptions));
        OnPropertyChanged(nameof(SpeedOptions));
        OnPropertyChanged(nameof(HasSceneOptions));
        OnPropertyChanged(nameof(HasKindOptions));
        OnPropertyChanged(nameof(HasPositionOptions));
        OnPropertyChanged(nameof(HasSpeedOptions));
        OnPropertyChanged(nameof(HasAnyOptions));
        RaiseFilters();
    }

    public bool Passes(string fileName)
    {
        if (!IsActive)
            return true;

        var traits = WavSearch.Traits(fileName);
        return (_sceneFilter == All || (traits.Scene is { } scene && WavSearch.SceneLabel(scene) == _sceneFilter)) &&
               (_kindFilter == All || traits.Kind == _kindFilter) &&
               (_positionFilter == All || traits.Position == _positionFilter) &&
               (_speedFilter == All || (traits.Speed is { } speed && $"스피드{speed}" == _speedFilter));
    }

    private void RaiseFilters()
    {
        OnPropertyChanged(nameof(SceneFilter));
        OnPropertyChanged(nameof(KindFilter));
        OnPropertyChanged(nameof(PositionFilter));
        OnPropertyChanged(nameof(SpeedFilter));
        OnPropertyChanged(nameof(IsActive));
    }
}

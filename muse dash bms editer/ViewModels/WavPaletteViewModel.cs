using System;
using System.Collections.Generic;
using System.Linq;
using bms_editer.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bms_editer.ViewModels;

// 키음 팔레트 창의 상태 모델.
//
// 사이드바의 좁은 목록 대신 넓은 타일 판에서 키음을 고르기 위한 창이다.
// 고른 키음이 곧 노트를 찍을 때 쓰이는 붓이므로 선택 상태를 따로 두지 않고
// 메인 뷰모델의 SelectedWavItem 을 그대로 읽고 쓴다. 그래서 사이드바 목록과
// 팔레트가 언제나 같은 항목을 가리킨다.
public sealed partial class WavPaletteViewModel : OwnerObservingViewModel
{
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private IReadOnlyList<BmsWavItem> _items = Array.Empty<BmsWavItem>();

    // 팔레트에서 색을 고르면 바로 소리가 나야 붓을 고르는 느낌이 난다.
    [ObservableProperty] private bool _previewOnSelect = true;

    // 분류 드롭다운의 "걸러내지 않음" 칸.
    public const string All = "전체";

    private string _sceneFilter = All;
    private string _kindFilter = All;
    private string _positionFilter = All;
    private string _speedFilter = All;

    private IReadOnlyList<string> _sceneOptions = [All];
    private IReadOnlyList<string> _kindOptions = [All];
    private IReadOnlyList<string> _positionOptions = [All];
    private IReadOnlyList<string> _speedOptions = [All];

    public WavPaletteViewModel(MainWindowViewModel owner) : base(owner)
    {
        RebuildOptions();
        Refresh();
    }

    // 분류 드롭다운. 지금 불러온 키음에서 실제로 있는 값만 나온다.
    // ComboBox 는 목록이 바뀌면 null 을 밀어 넣으므로 null 은 무시한다.
    public IReadOnlyList<string> SceneOptions => _sceneOptions;
    public IReadOnlyList<string> KindOptions => _kindOptions;
    public IReadOnlyList<string> PositionOptions => _positionOptions;
    public IReadOnlyList<string> SpeedOptions => _speedOptions;

    public string SceneFilter
    {
        get => _sceneFilter;
        set { if (value is not null && SetProperty(ref _sceneFilter, value)) Refresh(); }
    }

    public string KindFilter
    {
        get => _kindFilter;
        set { if (value is not null && SetProperty(ref _kindFilter, value)) Refresh(); }
    }

    public string PositionFilter
    {
        get => _positionFilter;
        set { if (value is not null && SetProperty(ref _positionFilter, value)) Refresh(); }
    }

    public string SpeedFilter
    {
        get => _speedFilter;
        set { if (value is not null && SetProperty(ref _speedFilter, value)) Refresh(); }
    }

    // 값이 하나도 없는 분류는 드롭다운을 숨긴다.
    public bool HasSceneOptions => _sceneOptions.Count > 1;
    public bool HasKindOptions => _kindOptions.Count > 1;
    public bool HasPositionOptions => _positionOptions.Count > 1;
    public bool HasSpeedOptions => _speedOptions.Count > 1;
    public bool HasAnyOptions => HasSceneOptions || HasKindOptions || HasPositionOptions || HasSpeedOptions;

    public BmsWavItem? SelectedWavItem
    {
        get => Owner.SelectedWavItem;
        set
        {
            // 검색어를 좁혀 선택 항목이 목록에서 빠지면 ListBox 가 null 을 밀어 넣는다.
            // 그때 편집용 붓까지 풀려버리지 않도록 무시한다.
            if (value is null || ReferenceEquals(Owner.SelectedWavItem, value))
                return;

            Owner.SelectedWavItem = value;
            Owner.IsEditMode = true;

            if (PreviewOnSelect)
                Owner.PlayWavSound(value.Key);
        }
    }

    // 파일명이 길어서 아이콘 보기에서는 대부분 잘린다. 그래서 탐색기처럼
    // 목록(한 줄에 하나, 파일명 그대로) ~ 아주 큰 아이콘까지 고를 수 있게 한다.
    // 값은 창을 닫아도 남도록 메인 뷰모델에 둔다.
    public int ViewModeIndex
    {
        get => Owner.WavPaletteViewModeIndex;
        set
        {
            if (Owner.WavPaletteViewModeIndex == value)
                return;

            Owner.WavPaletteViewModeIndex = value;
            OnPropertyChanged();
            RaiseViewModeFlags();
        }
    }

    public bool IsListMode => ViewModeIndex == 0;
    public bool IsMediumMode => ViewModeIndex == 1;
    public bool IsLargeMode => ViewModeIndex == 2;
    public bool IsExtraLargeMode => ViewModeIndex == 3;

    public int TotalCount => Owner.WavList.Count;
    public bool HasItems => Items.Count > 0;
    public bool IsEmptyTable => Owner.WavList.Count == 0;

    // 목록 보기와 아이콘 보기는 판이 따로라 어느 쪽을 띄울지 여기서 정한다.
    public bool ShowListView => HasItems && IsListMode;
    public bool ShowIconView => HasItems && !IsListMode;
    public bool ShowNoMatch => !HasItems && !IsEmptyTable;

    public string StatusText => IsEmptyTable
        ? "등록된 키음이 없습니다. [키음 추가]로 WAV를 불러오세요."
        : Items.Count == TotalCount
            ? $"키음 {TotalCount}개"
            : $"{Items.Count} / {TotalCount}개 표시 중";

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnItemsChanged(IReadOnlyList<BmsWavItem> value)
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowListView));
        OnPropertyChanged(nameof(ShowIconView));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseViewModeFlags()
    {
        OnPropertyChanged(nameof(IsListMode));
        OnPropertyChanged(nameof(IsMediumMode));
        OnPropertyChanged(nameof(IsLargeMode));
        OnPropertyChanged(nameof(IsExtraLargeMode));
        OnPropertyChanged(nameof(ShowListView));
        OnPropertyChanged(nameof(ShowIconView));
    }

    protected override void OnOwnerPropertyChanged(string? propertyName)
    {
        // 사이드바에서 고른 항목이 팔레트에도 그대로 반영돼야 한다.
        if (propertyName == nameof(MainWindowViewModel.SelectedWavItem))
            OnPropertyChanged(nameof(SelectedWavItem));
        else if (propertyName == nameof(MainWindowViewModel.WavPaletteViewModeIndex))
            RaiseViewModeFlags();
    }

    protected override void OnWavListChanged()
    {
        RebuildOptions();
        Refresh();
    }

    // 새로 넣은 키음이 검색어에 걸려 안 보이면 추가한 걸 못 찾으므로 검색어를 지운다.
    // 실패하면 false. 여러 개를 담을 때 어디서 멈췄는지 부르는 쪽이 알아야 한다.
    public bool AddWav(string filePath)
    {
        ResetFilters();
        return Owner.AddWav(filePath);
    }

    [RelayCommand]
    private void ClearFilter() => ResetFilters();

    // 검색어와 분류를 모두 풀고 한 번만 다시 거른다.
    private void ResetFilters()
    {
        _sceneFilter = _kindFilter = _positionFilter = _speedFilter = All;
        RaiseFilterProperties();

        if (Filter.Length == 0)
            Refresh();
        else
            Filter = string.Empty; // OnFilterChanged 가 Refresh 한다.
    }

    private void RaiseFilterProperties()
    {
        OnPropertyChanged(nameof(SceneFilter));
        OnPropertyChanged(nameof(KindFilter));
        OnPropertyChanged(nameof(PositionFilter));
        OnPropertyChanged(nameof(SpeedFilter));
    }

    private void RebuildOptions()
    {
        var traits = Owner.WavList.Select(item => WavSearch.Traits(item.FileName)).ToList();

        _sceneOptions = [All, .. traits.Where(t => t.Scene.HasValue).Select(t => t.Scene!.Value)
            .Distinct().Order().Select(WavSearch.SceneLabel)];
        _kindOptions = [All, .. traits.Where(t => t.Kind is not null).Select(t => t.Kind!).Distinct()];
        _positionOptions = [All, .. new[] { "지상", "공중" }.Where(p => traits.Any(t => t.Position == p))];
        _speedOptions = [All, .. traits.Where(t => t.Speed.HasValue).Select(t => t.Speed!.Value)
            .Distinct().Order().Select(speed => $"스피드{speed}")];

        // 새 목록에 없는 값을 고르고 있었다면 풀어 준다.
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
        RaiseFilterProperties();
    }

    private bool PassesFilters(BmsWavItem item)
    {
        if (_sceneFilter == All && _kindFilter == All && _positionFilter == All && _speedFilter == All)
            return true;

        var traits = WavSearch.Traits(item.FileName);
        return (_sceneFilter == All || (traits.Scene is { } scene && WavSearch.SceneLabel(scene) == _sceneFilter)) &&
               (_kindFilter == All || traits.Kind == _kindFilter) &&
               (_positionFilter == All || traits.Position == _positionFilter) &&
               (_speedFilter == All || (traits.Speed is { } speed && $"스피드{speed}" == _speedFilter));
    }

    private void Refresh()
    {
        var filter = Filter.Trim();

        Items = Owner.WavList
            .Where(item => PassesFilters(item) && (filter.Length == 0 || WavSearch.Matches(item, filter)))
            .ToList();

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsEmptyTable));
        OnPropertyChanged(nameof(StatusText));
    }
}

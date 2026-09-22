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

    public WavPaletteViewModel(MainWindowViewModel owner) : base(owner)
    {
        Refresh();
    }

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

    protected override void OnWavListChanged() => Refresh();

    // 새로 넣은 키음이 검색어에 걸려 안 보이면 추가한 걸 못 찾으므로 검색어를 지운다.
    // 실패하면 false. 여러 개를 담을 때 어디서 멈췄는지 부르는 쪽이 알아야 한다.
    public bool AddWav(string filePath)
    {
        Filter = string.Empty;
        return Owner.AddWav(filePath);
    }

    [RelayCommand]
    private void ClearFilter() => Filter = string.Empty;

    private void Refresh()
    {
        var filter = Filter.Trim();

        Items = filter.Length == 0
            ? Owner.WavList.ToList()
            : Owner.WavList
                .Where(item =>
                    item.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    item.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsEmptyTable));
        OnPropertyChanged(nameof(StatusText));
    }
}

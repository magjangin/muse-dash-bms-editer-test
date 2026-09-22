using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace bms_editer.ViewModels;

// 여러 항목을 한꺼번에 갈아끼우고 알림은 **한 번만** 내는 컬렉션.
//
// 왜 필요한가:
// 차트를 열 때 키음 목록을 Clear + Add x N 으로 채웠다. ObservableCollection 은
// 항목마다 CollectionChanged 를 쏘므로 N+1 번의 알림이 나갔고, 통계·팔레트 창이
// 그때마다 전체 재집계를 돌았다. 키음 300개 / 노트 1600개 차트에서
// 재집계 301회, 약 337만 회 비교. 실전 규모(키음 1000 / 노트 5000)면 3,500만 회다.
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

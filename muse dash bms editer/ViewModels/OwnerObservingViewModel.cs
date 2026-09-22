using System;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace bms_editer.ViewModels;

// 메인 뷰모델을 곁눈질하며 따라 움직이는 보조 창들의 공통 바탕.
//
// 통계 창과 키음 팔레트가 똑같은 절차를 각자 복사해 갖고 있었다. 구독 두 개를
// 걸고 Dispose 에서 같은 두 개를 떼는 짝인데, 한쪽만 빠뜨려도 닫은 창의 뷰모델이
// 계속 살아남아 편집할 때마다 헛돈다. 짝이 어긋날 수 없도록 여기 한 곳에 모은다.
public abstract partial class OwnerObservingViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    protected OwnerObservingViewModel(MainWindowViewModel owner)
    {
        Owner = owner;
        Owner.PropertyChanged += HandleOwnerPropertyChanged;
        Owner.WavList.CollectionChanged += HandleWavListChanged;
    }

    // 삭제·재생 테스트처럼 메인 뷰모델의 명령을 그대로 쓰는 곳이 있어 공개한다.
    public MainWindowViewModel Owner { get; }

    // 메인 뷰모델의 프로퍼티가 바뀌었을 때. 어떤 프로퍼티인지는 파생 클래스가 가린다.
    protected abstract void OnOwnerPropertyChanged(string? propertyName);

    // 키음 목록이 늘거나 줄었을 때. 신경 쓰지 않는 창은 그냥 두면 된다.
    protected virtual void OnWavListChanged() { }

    private void HandleOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnOwnerPropertyChanged(e.PropertyName);

    private void HandleWavListChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnWavListChanged();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Owner.PropertyChanged -= HandleOwnerPropertyChanged;
        Owner.WavList.CollectionChanged -= HandleWavListChanged;
        OnDispose();
    }

    protected virtual void OnDispose() { }
}

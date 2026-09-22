using Xunit;

// Avalonia 헤드리스 테스트는 디스패처 스레드 하나를 어셈블리 단위로 공유한다.
// xunit 이 컬렉션을 병렬로 돌리면 그 스레드를 두고 다투다가 테스트 호스트가 죽는다.
// (증상: "총 테스트 수: 알 수 없음" 과 함께 UI 테스트가 통째로 사라진다)
[assembly: CollectionBehavior(DisableTestParallelization = true)]

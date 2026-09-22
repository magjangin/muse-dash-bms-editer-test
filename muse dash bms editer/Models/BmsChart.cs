using System.Collections.Generic;

namespace bms_editer.Models;

public sealed class BmsChart
{
    public BmsHeader Header { get; } = new();

    public IReadOnlyList<LaneDefinition> Lanes { get; set; } = LaneDefinition.CreateDefault();

    // 마디 인덱스 -> 박자 배율 (#xxx02, 기본값 1.0 = 4/4)
    public Dictionary<int, double> MeasureLengths { get; } = new();

    // #BPMxx 확장 BPM 표. 채널 08 이 이 번호를 가리킨다.
    public Dictionary<string, double> BpmTable { get; } = new();

    // 곡 도중의 BPM 변화 (#xxx03 · #xxx08). 파일에 있던 순서와 무관하게 시각 순으로 쓴다.
    public List<BpmChange> BpmChanges { get; } = new();

    public List<BmsNote> Notes { get; } = new();

    // 편집 대상이 아닌 원본 줄(BGM·BPM 변화·BGA·2P 채널·확장 헤더 등).
    // 저장할 때 그대로 다시 내보내야 하므로 파일에 있던 순서대로 담는다.
    public List<BmsRawLine> PreservedLines { get; } = new();

    public Dictionary<string, string> WavTable { get; } = new();

    public int MeasureCount { get; set; } = 32;

    // #RANDOM·#IF·#SWITCH 처럼 갈래를 나누는 줄이 파일에 있었는지.
    //
    // 이 에디터는 조건 블록을 해석하지 못한다. 블록 안의 건반 줄을 평범한 노트로 읽고,
    // 저장할 때 같은 마디·레인이면 한 줄로 합쳐 버린다. 조건 줄 자체도 데이터 줄이 아니라
    // 파일 맨 위 헤더 블록으로 끌려 올라간다. 그래서 열어서 저장만 해도 차트가 무너진다.
    //
    // 제대로 다룰 수 있을 때까지는 이 표시를 보고 저장을 막는다.
    public bool HasConditionalBlocks { get; set; }

    public double GetMeasureLength(int measure) =>
        MeasureLengths.TryGetValue(measure, out var length) ? length : 1.0;

    // 읽어들인 차트의 내용을 이 인스턴스로 옮긴다.
    //
    // Chart 는 뷰에 바인딩돼 있어 통째로 갈아끼울 수 없고, 그렇다고 부르는 쪽에서
    // 컬렉션을 하나씩 옮기면 새 컬렉션이 생길 때마다 빠뜨리기 쉽다. 실제로
    // MeasureLengths 가 그렇게 빠져 있었다. 옮기는 자리를 여기 하나로 모은다.
    public void ReplaceContentWith(BmsChart source)
    {
        Header.CopyFrom(source.Header);
        Lanes = source.Lanes;
        MeasureCount = source.MeasureCount;
        HasConditionalBlocks = source.HasConditionalBlocks;

        Notes.Clear();
        Notes.AddRange(source.Notes);

        PreservedLines.Clear();
        PreservedLines.AddRange(source.PreservedLines);

        BpmChanges.Clear();
        BpmChanges.AddRange(source.BpmChanges);

        CopyInto(source.MeasureLengths, MeasureLengths);
        CopyInto(source.WavTable, WavTable);
        CopyInto(source.BpmTable, BpmTable);
    }

    // 새로 만들기처럼 문서를 비울 때. ReplaceContentWith 와 같은 자리에 두어
    // 컬렉션이 늘어나면 양쪽을 같이 고치게 한다.
    public void Clear() => ReplaceContentWith(new BmsChart());

    private static void CopyInto<TKey, TValue>(Dictionary<TKey, TValue> source, Dictionary<TKey, TValue> target)
        where TKey : notnull
    {
        target.Clear();
        foreach (var (key, value) in source)
            target[key] = value;
    }
}

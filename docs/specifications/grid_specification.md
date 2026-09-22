# BMS Editor - 기본 그리드 분할 명세서 (Grid Split Specification)

이 문서는 BMS 에디터의 마디(Measure) 내부 그리드 분할 규칙에 대한 핵심 동작 사양과 관련 핵심 코드 구현 스니펫을 정의합니다.

---

## 📌 기본 요구 사항: 마디당 16개 그리드 선 배치
BMS 에디터가 처음 구동되거나 차트가 초기화될 때, **마디와 마디 사이(1마디 공간)에는 무조건 16개의 그리드 선(16분할선)**이 생성되어야 합니다.

* **정의**: 4/4 박자를 기준으로 한 마디를 16등분하는 그리드(16분음표 단위 격자)가 초기 기준이 됩니다.
* **목적**: 채보 제작자가 노트를 배치할 때 가장 대중적인 단위인 16비트(16분음표) 격자에 정확히 맞물려(Snap) 배치되도록 하기 위함입니다.

---

## 💻 핵심 코드 구현 스니펫 (Core Code Snippets)

### 1. 기본값 정의 (TimelineControlBase.cs)
격자 분할 수를 담당하는 `BeatSplit` 속성의 기본값은 의존성 프로퍼티(StyledProperty) 정의 단계에서 **`16`**으로 지정되어 있습니다.

```csharp
// muse dash bms editer/Views/Controls/TimelineControlBase.cs

public abstract class TimelineControlBase : Control
{
    // 그리드 분할 수 Property 정의 (기본값: 16)
    public static readonly StyledProperty<int> BeatSplitProperty =
        AvaloniaProperty.Register<TimelineControlBase, int>(nameof(BeatSplit), 16);

    public int BeatSplit
    {
        get => GetValue(BeatSplitProperty);
        set => SetValue(BeatSplitProperty, value);
    }

    // 특정 인덱스의 선이 주요 박자 선(Beat Line)인지 판별하는 헬퍼 함수
    protected static bool IsMeasureBeatLine(int index, int split, int gridMeasure)
    {
        if (gridMeasure <= 0 || split < gridMeasure || split % gridMeasure != 0)
            return false;

        return Mod(index, split / gridMeasure) == 0;
    }

    protected static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
}
```

---

### 2. 그리드 격자선 렌더링 루프 (NoteGridControl.cs)
`NoteGridControl`의 `Render` 메커니즘에서는 `BeatSplit` 설정값만큼 마디 내부를 세분화하여 각 그리드 분할선을 화면에 그립니다.

```csharp
// muse dash bms editer/Views/Controls/NoteGridControl.cs

public override void Render(DrawingContext context)
{
    // ... 배경색 및 기본 레이아웃 계산 생략 ...

    var split = Math.Max(1, BeatSplit); // 기본값: 16

    if (DurationSeconds > 0 && Bpm > 0)
    {
        // 배경 오디오 파일이 연동되어 전체 재생 초(Duration) 기반일 때 분할선 렌더링
        var secondsPerStep = 240.0 / (Bpm * split);
        for (var index = 0; ; index++)
        {
            var seconds = index * secondsPerStep;
            if (seconds > DurationSeconds)
                goto FinishedBeatLines;

            var ratio = seconds / DurationSeconds;
            var tPos = IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
            
            // 박자 우선순위에 따른 브러시 펜 종류 결정
            var pen = Mod(index, split) == 0
                ? measurePen                              // 마디 시작 선 (흰색 선)
                : IsMeasureBeatLine(index, split, GridMeasure)
                    ? beatPen                             // 주요 박자 선 (밝은 회색 선)
                    : subBeatPen;                         // 16분할 보조선 (어두운 회색 선)

            // 가로/세로 뷰에 맞춰 선 그리기
            if (IsHorizontalView)
                context.DrawLine(pen, new Point(tPos, 0), new Point(tPos, totalHeight));
            else
                context.DrawLine(pen, new Point(0, tPos), new Point(totalWidth, tPos));
        }
    }
    else
    {
        // 배경 음악이 없을 때 마디 갯수(MeasureCount) 기반의 분할선 렌더링
        var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();
        var tPos = IsHorizontalView ? 0.0 : timelineLength;
        for (var measure = 0; measure < MeasureCount; measure++)
        {
            for (var beat = 0; beat < split; beat++)
            {
                var beatTPos = IsHorizontalView 
                    ? (tPos + (rowHeight * beat / split))
                    : (tPos - (rowHeight * beat / split));

                var pen = beat == 0
                    ? measurePen
                    : IsMeasureBeatLine(beat, split, GridMeasure)
                        ? beatPen
                        : subBeatPen;

                if (IsHorizontalView)
                    context.DrawLine(pen, new Point(beatTPos, 0), new Point(beatTPos, totalHeight));
                else
                    context.DrawLine(pen, new Point(0, beatTPos), new Point(totalWidth, beatTPos));
            }

            if (IsHorizontalView) tPos += rowHeight;
            else tPos -= rowHeight;
        }
    }

FinishedBeatLines:
    // ... 후속 노트 및 텍스트 렌더링 생략 ...
}
```

### 3. 마우스 클릭 배치 시의 16분할 스냅 처리 (NoteGridControl.cs)
에디터 위에서 마우스 좌클릭/우클릭으로 노트를 배치하거나 지울 때도 `BeatSplit` 기반의 16분할 격자에 스냅 보정되어 위치가 결정됩니다.

```csharp
// muse dash bms editer/Views/Controls/NoteGridControl.cs

private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
{
    // ... 레인 인덱스 계산 생략 ...

    var tPos = IsHorizontalView ? point.Position.X : point.Position.Y;
    var split = Math.Max(1, BeatSplit); // 기본값: 16

    int measure = 0;
    double position = 0.0;

    if (DurationSeconds > 0)
    {
        var ratio = IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength));
        ratio = Math.Clamp(ratio, 0.0, 1.0);

        var seconds = ratio * DurationSeconds;
        var secondsPerMeasure = 240.0 / Bpm;
        var secondsPerStep = secondsPerMeasure / split;

        // 가장 가까운 16분 격자 포인트 인덱스로 반올림하여 스냅(Snap) 수행
        var totalStepIndex = (int)Math.Round(seconds / secondsPerStep);
        measure = totalStepIndex / split;
        position = (double)(totalStepIndex % split) / split;
    }
    else
    {
        // 오디오 미로드 시의 스냅 처리
        var ratio = IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength));
        ratio = Math.Clamp(ratio, 0.0, 1.0);

        var totalSteps = MeasureCount * split;
        var totalStepIndex = (int)Math.Round(ratio * totalSteps);

        measure = totalStepIndex / split;
        position = (double)(totalStepIndex % split) / split;
    }

    // ... 스냅된 measure 및 position 정보로 노트 배치/삭제 명령 수행 ...
}
```

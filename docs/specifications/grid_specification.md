# 격자 분할 명세서 (Grid Split Specification)

이 문서는 마디(Measure) 안을 몇 칸으로 나누고, 그 칸을 어떻게 그리고, 클릭한 자리를 어느 칸에 붙이는지를 정의합니다.
코드 조각은 실제 코드에서 옮겨 왔습니다. 코드가 바뀌면 이 조각도 같이 고쳐 주십시오.

---

## 📌 기본 요구 사항: 마디당 16칸

에디터를 처음 켜거나 **새로 만들기**를 하면 한 마디는 **16칸(16분음표 단위)** 으로 나뉩니다.

* **정의**: 4/4 박자 한 마디를 16등분합니다. 오른쪽 패널에는 `비트 16 / 마디 4` 로 보입니다.
* **목적**: 가장 흔한 16비트 격자에 노트가 정확히 붙도록 합니다.
* 잇단음은 `12/4` · `24/4` 처럼 비트 수를 바꾸거나, **격자에 맞추기**를 끄고 찍습니다.

| 설정 | 뷰모델 속성 | 기본값 | 범위 |
|:---|:---|:---:|:---:|
| 비트 (마디 한 칸 수) | `BeatSplit` | 16 | 1 ~ 192 |
| 마디 (박자 수) | `GridMeasure` | 4 | 1 ~ 48 |
| 세로 줌 | `VerticalZoom` | 8 | 4 ~ 8 |
| 그리드 가로 줌 (레인 폭) | `HorizontalZoom` | 1.5 | 0.25 ~ 4 |
| 격자에 맞추기 | `SnapToGrid` | 켜짐 | — |

---

## 💻 핵심 코드 (Core Code Snippets)

### 1. 기본값 정의

컨트롤 쪽 기본값은 `TimelineControlBase` 의 스타일 속성에, 문서 쪽 기본값은 뷰모델에 있습니다.
새로 만들기는 두 값을 명시적으로 되돌립니다.

```csharp
// muse dash bms editer/Views/Controls/TimelineControlBase.cs
public static readonly StyledProperty<int> BeatSplitProperty =
    AvaloniaProperty.Register<TimelineControlBase, int>(nameof(BeatSplit), 16);

public static readonly StyledProperty<int> GridMeasureProperty =
    AvaloniaProperty.Register<TimelineControlBase, int>(nameof(GridMeasure), 4);

// muse dash bms editer/ViewModels/MainWindowViewModel.FileIO.cs — NewFileAsync
// 새 차트는 명세서 기준값인 16분할 그리드로 시작한다.
BeatSplit = 16;
GridMeasure = 4;
```

### 2. 선의 세 종류

| 종류 | 조건 | 그리는 펜 |
|:---|:---|:---|
| 마디선 | 칸 번호가 `BeatSplit` 의 배수 | 흰색 굵은 선 |
| 박자선 | `BeatSplit` 이 `GridMeasure` 로 나눠떨어지고, 칸 번호가 `BeatSplit / GridMeasure` 의 배수 | 밝은 회색 |
| 보조선 | 그 밖 | 어두운 회색 |

```csharp
// muse dash bms editer/Views/Controls/TimelineControlBase.cs
private GridLineKind ClassifyGridLine(int index, int split) =>
    Mod(index, split) == 0
        ? GridLineKind.Measure
        : IsMeasureBeatLine(index, split, GridMeasure)
            ? GridLineKind.Beat
            : GridLineKind.SubBeat;

protected static bool IsMeasureBeatLine(int index, int split, int gridMeasure)
{
    if (gridMeasure <= 0 || split < gridMeasure || split % gridMeasure != 0)
        return false;

    return Mod(index, split / gridMeasure) == 0;
}
```

예: `16/4` 는 4칸마다 박자선, `12/4` 는 3칸마다 박자선, `10/4` 는 나눠떨어지지 않아 박자선 없이 마디선과 보조선만 그립니다.

### 3. 격자선 열거 — 한 곳에서만

격자선 위치를 구하는 곳은 `TimelineControlBase.EnumerateGridLines` 하나입니다. 격자(`NoteGridControl`)와 파형(`OggWaveformControl`)이 같이 씁니다.

```csharp
// muse dash bms editer/Views/Controls/TimelineControlBase.cs
protected IEnumerable<GridLine> EnumerateGridLines(double timelineLength)
{
    var split = Math.Max(1, BeatSplit);

    // 배경 음원이 있으면 화면 전체가 곡 길이를 뜻하므로 초 단위로 훑는다.
    if (DurationSeconds > 0 && Bpm > 0)
    {
        var timeline = EffectiveTimeline;

        for (var index = 0; ; index++)
        {
            var seconds = timeline.SecondsAt((double)index / split);
            if (seconds > DurationSeconds)
                yield break;

            var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);
            if (position < -0.5 || position > timelineLength + 0.5)
                continue;

            yield return new GridLine(position, ClassifyGridLine(index, split), index / split, seconds);
        }
    }
    else
    {
        // 음원이 없으면 마디 높이가 곧 화면 높이다.
        var rowHeight = RowHeight * VerticalZoom * GetGridSpacingScale();

        for (var measure = 0; measure <= MeasureCount; measure++)
        {
            for (var beat = 0; beat < split; beat++)
            {
                var measurePosition = measure + (beat / (double)split);
                var offset = measurePosition * rowHeight;
                var position = IsHorizontalView ? offset : timelineLength - offset;
                // ... 범위 검사 후 GridLine 으로 내놓는다
            }
        }
    }
}
```

* **음원이 있을 때**: 칸의 시각을 `ChartTimeline.SecondsAt` 에 묻습니다. BPM 변화 · 박자 변경이 있어도 격자가 소리와 맞습니다. → [bpm_sync_principles.md](../architecture/bpm_sync_principles.md)
* **음원이 없을 때**: 모든 마디를 같은 높이로 그립니다. 시간이 아니라 마디 수가 기준입니다.

그리는 쪽은 받은 선을 종류별 펜으로 긋기만 합니다.

```csharp
// muse dash bms editer/Views/Controls/NoteGridControl.cs — Render
foreach (var line in EnumerateGridLines(timelineLength))
{
    var pen = line.Kind switch
    {
        GridLineKind.Measure => MeasurePen,
        GridLineKind.Beat => BeatPen,
        _ => SubBeatPen,
    };

    if (IsHorizontalView)
        context.DrawLine(pen, new Point(line.Position, 0), new Point(line.Position, totalHeight));
    else
        context.DrawLine(pen, new Point(0, line.Position), new Point(totalWidth, line.Position));
}
```

### 4. 클릭한 자리를 칸에 붙이기 (스냅)

클릭 위치를 먼저 **마디 위치(tick)** 로 되돌리고, 격자에 맞추기가 켜져 있으면 가장 가까운 칸으로 반올림합니다.
그리는 쪽과 같은 시간축(`MeasurePositionAt`)을 쓰므로 BPM 변화 구간에서도 보이는 칸에 붙습니다.

```csharp
// muse dash bms editer/Views/Controls/NoteGridControl.cs — OnPointerPressed
var ratio = Math.Clamp(IsHorizontalView ? (tPos / timelineLength) : (1.0 - (tPos / timelineLength)), 0.0, 1.0);

var clickedMeasurePosition = DurationSeconds > 0
    ? EffectiveTimeline.MeasurePositionAt(ratio * DurationSeconds)
    : ratio * MeasureCount;

if (SnapToGrid)
{
    var totalStepIndex = (int)Math.Round(clickedMeasurePosition * split);

    // 맨 끝을 클릭해도 마지막 칸에 찍히도록 당긴다.
    totalStepIndex = Math.Clamp(totalStepIndex, 0, (MeasureCount * split) - 1);

    measure = totalStepIndex / split;
    position = (double)(totalStepIndex % split) / split;
}
else
{
    // 격자에 맞추기를 끄면 클릭한 자리 그대로. 잇단음처럼 격자로 표현 못 하는 자리를 잡을 때 쓴다.
    var clamped = Math.Clamp(clickedMeasurePosition, 0, MeasureCount - (1.0 / split));
    measure = (int)Math.Floor(clamped);
    position = clamped - measure;
}
```

* **좌클릭**: 그 자리에 고른 키음으로 노트를 찍습니다(같은 자리에 있으면 키음만 바꿈).
* **우클릭**: 그 레인에서 **격자 반 칸 안의 가장 가까운 노트 하나**를 지웁니다(`MainWindowViewModel.RemoveNote`).
* **방향키로 옮기기**: 시간축으로 **한 칸만큼 옮길 뿐 격자에 다시 붙이지 않습니다.** 12분할로 찍은 잇단음을 16분할에서 옮겨도 잇단음이 유지됩니다.

---

## 🔍 확대 / 축소 비율

타임라인 길이는 `TimelineControlBase.GetTimelineHeight` 한 식으로 정합니다.

```csharp
// muse dash bms editer/Views/Controls/TimelineControlBase.cs
protected double GetTimelineHeight()
{
    var spacingScale = GetGridSpacingScale();
    if (DurationSeconds > 0)
        return Math.Max(1.0, DurationSeconds * RowHeight * VerticalZoom * spacingScale / 2.0);

    return MeasureCount * RowHeight * VerticalZoom * spacingScale;
}

protected double GetGridSpacingScale() => Math.Max(1.0, BeatSplit / (double)Math.Max(1, GridMeasure));
```

`BeatSplit / GridMeasure` 가 곱해지므로, **비트 수를 늘리면 타임라인도 같이 길어져 칸 간격이 유지됩니다.**
기본값(`RowHeight 16`, 세로 줌 8, `16/4`)에서 칸 하나의 간격은 다음과 같습니다.

| 상황 | 칸 간격 | 기본값에서 |
|:---|:---|:---|
| 음원이 있을 때 | `480 × 세로줌 ÷ BPM` px (분할 수와 무관) | BPM 120 → **32px**, BPM 200 → 19.2px |
| 음원이 없을 때 | `RowHeight × 세로줌 ÷ GridMeasure` px | **32px** (BPM 무관) |

* **세로 줌**은 시간축 길이만 바꿉니다. 파형의 진폭은 **파형 가로 줌**이 따로 맡습니다.
* **그리드 가로 줌**은 레인 폭(`40 × 가로 줌` px)만 바꿉니다.
* 타임라인 길이를 계산하는 식은 `MainWindow.GetTimelineLength`(스크롤 위치 계산용)에도 같은 모양으로 한 번 더 적혀 있습니다. 한쪽을 고치면 다른 쪽도 고쳐야 합니다.

관련 문서: [bpm_sync_principles.md](../architecture/bpm_sync_principles.md) (마디 ↔ 초 변환) · [note_circle_icon.md](../plans/note_circle_icon.md) (칸 간격과 노트 크기)

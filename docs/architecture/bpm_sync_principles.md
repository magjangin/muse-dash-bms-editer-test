# BPM 설정에 따른 격자 · 파형 동기화 원리 (BPM & Grid Sync Principles)

이 문서는 BPM 을 바꾸거나 곡 도중에 BPM · 박자가 바뀔 때, 오디오 파형과 마디 격자선 · 노트가 화면에서 어떻게 맞물리는지 설명합니다.
코드 조각은 실제 코드에서 옮겨 왔습니다. 코드가 바뀌면 이 조각도 같이 고쳐 주십시오.

---

## 📌 핵심 요약 (Core Concept)

1. **오디오 파형은 움직이지 않습니다.** 화면 길이는 곡 길이(초)로만 정해지고, 파형은 그 위에 초 단위로 고정됩니다.
2. **격자선과 노트는 BPM 에 따라 늘어나고 줄어듭니다.** 마디 위치를 초로 바꾸는 규칙은 `ChartTimeline` 한 곳에만 있고, 격자 · 노트 · 클릭 · 재생 · 키음이 모두 거기에 묻습니다.
3. 기본 식은 **`시각(초) = tick × 240 / bpm`** 입니다(tick = 마디 + 마디 안 위치, 4/4 한 마디 = 4박 = 240/bpm 초).
   곡 도중의 BPM 변화(`#xxx03` · `#xxx08`)와 박자 변경(`#xxx02`)은 이 식을 **구간별로 이어 붙여** 계산합니다.

---

## 💻 상세 연동 메커니즘

### 1. 화면 길이는 곡 길이로만 정해진다

음원이 있으면 타임라인 길이는 곡 길이(`DurationSeconds`)와 줌 설정으로만 계산합니다. **BPM 이 식에 없습니다.**
그래서 BPM 을 바꿔도 파형이 늘어나거나 줄지 않습니다.

```csharp
// TimelineControlBase.GetTimelineHeight
protected double GetTimelineHeight()
{
    var spacingScale = GetGridSpacingScale();   // max(1, BeatSplit / GridMeasure)
    if (DurationSeconds > 0)
        return Math.Max(1.0, DurationSeconds * RowHeight * VerticalZoom * spacingScale / 2.0);

    return MeasureCount * RowHeight * VerticalZoom * spacingScale;   // 음원이 없을 때는 마디 수 기준
}
```

화면 위치는 늘 **"곡 전체에서 몇 %인가"** 로 정합니다. 세로 보기는 아래가 0초라 비율을 뒤집습니다.

```csharp
// TimelineControlBase.ToTimelinePosition
protected double ToTimelinePosition(double ratio, double timelineLength) =>
    IsHorizontalView ? ratio * timelineLength : (1.0 - ratio) * timelineLength;
```

### 2. 마디 위치 → 초: `ChartTimeline`

한 구간 안에서는 BPM 이 일정하므로 `박 수 × 60 / bpm` 으로 셉니다. 마디 길이 배율(`#xxx02`)이 박 수를 늘리고 줄입니다.

```csharp
// ChartTimeline.SpanSeconds — 마디 안에서 measureFraction 만큼이 차지하는 시간
var beats = 4.0 * GetMeasureLength(measure) * measureFraction;
return beats * 60.0 / bpm;
```

`SecondsAt(tick)` 은 마디 경계마다 누적 시각과 그 순간의 BPM 을 기억해 두고(`EnsureBoundary`),
마디 안에 BPM 변화가 있으면 변화 지점까지 끊어서 더합니다.

```csharp
// ChartTimeline.SecondsAt (요약)
var seconds = _measureStartSeconds[measure];     // 이 마디가 시작하는 시각 (앞 마디들의 합)
var bpm = _bpmAtMeasureStart[measure];           // 이 마디가 시작할 때의 BPM
var cursor = 0.0;

foreach (var change in changesInThisMeasure)     // 마디 안의 BPM 변화를 위치 순으로
{
    if (change.Position >= position) break;
    seconds += SpanSeconds(measure, change.Position - cursor, bpm);
    cursor = change.Position;
    bpm = change.Bpm;
}

return seconds + SpanSeconds(measure, position - cursor, bpm);
```

BPM 변화도 마디 길이 변화도 없는 차트에서는 이 계산이 `tick × 240 / bpm` 과 **완전히 같은 값**을 냅니다(`ChartTimelineTests`).
반대 방향(`MeasurePositionAt`, 초 → 마디 위치)은 클릭한 자리를 마디로 옮길 때 씁니다.

#### 예시 — 마디 2 한가운데에서 BPM 120 → 240

| tick | 계산 | 시각 |
|:---:|:---|---:|
| 2.0 | 2 × 240 / 120 | 4.0 s |
| 2.5 | 4.0 + 0.5 × 240 / 120 | 5.0 s |
| 3.0 | 5.0 + 0.5 × 240 / 240 | 5.5 s |
| 4.0 | 5.5 + 1 × 240 / 240 | 6.5 s |

### 3. 격자선

격자 칸 하나는 `1 / BeatSplit` 마디입니다. 칸마다 `SecondsAt` 으로 시각을 구해 화면 위치로 바꿉니다.
BPM 이 높으면 같은 칸이 더 이른 시각이 되어 **격자가 촘촘해지고**, 낮으면 **성겨집니다.**

```csharp
// TimelineControlBase.EnumerateGridLines — 음원이 있을 때
var timeline = EffectiveTimeline;

for (var index = 0; ; index++)
{
    var seconds = timeline.SecondsAt((double)index / split);
    if (seconds > DurationSeconds)
        yield break;

    var position = ToTimelinePosition(seconds / DurationSeconds, timelineLength);
    // ... 마디선 / 박자선 / 보조선을 가려 GridLine 으로 내놓는다
}
```

`NoteGridControl` 과 `OggWaveformControl` 이 **같은 열거 함수**를 써서 두 컨트롤의 격자가 어긋나지 않습니다.
파형 쪽 마디선 옆의 `#003 (5.7600s)` 라벨의 초도 여기서 나온 값입니다.

### 4. 노트

노트도 격자와 **같은 함수**로 시각을 구합니다. 그래서 BPM 을 바꾸면 노트가 자기 격자선에 붙은 채 함께 움직입니다.

```csharp
// NoteGridControl.ComputeNoteTPos
if (DurationSeconds > 0 && Bpm > 0)
{
    var seconds = EffectiveTimeline.SecondsAt(note.Measure + note.Position);
    return ToTimelinePosition(seconds / DurationSeconds, timelineLength);
}
```

홀드 몸통도 머리와 꼬리의 위치를 **각각** 이 함수로 구합니다. 마디 단위로 선형 보간하면 BPM 변화 구간을 지나는 몸통 끝이 꼬리 노트와 어긋납니다.
재생 중 키음도 `Timeline.SecondsAt` 으로 "이번 33ms 사이에 울릴 노트"를 찾습니다(`MainWindowViewModel.PlayNotesInTimeRange`).

### 5. 파형

파형은 화면 칸(2px)마다 **그 칸이 덮는 시간 구간**을 곧바로 계산해, 그 구간에 걸린 피크 버킷 중 가장 큰 값을 그립니다.
BPM 이 끼어들 자리가 없으므로 파형은 제자리에 있습니다. 버킷과 시각의 대응 규칙은 `OggPeakLoader.GetBucketRange` 한 곳에 있습니다.

```csharp
// OggWaveformControl.DrawBlockWaveform (요약)
var startRatio = tPos / timelineLength;
var endRatio = (tPos + blockLength) / timelineLength;
if (!isHorizontal)
    (startRatio, endRatio) = (1.0 - endRatio, 1.0 - startRatio);

startRatio -= offsetRatio;   // 음원 오프셋 (아래 6)
endRatio -= offsetRatio;

var half = GetDisplayPeak(peaks, startRatio, endRatio) * maxAmplitude;
```

### 6. 음원 오프셋

오프셋은 **음원에서 나온 것만**(파형 · 온셋 마커 · 재생 커서) 타임라인 위에서 민 값입니다. 격자와 노트, `ChartTimeline` 은 건드리지 않습니다.

```csharp
// TimelineControlBase
public double AudioRatio(double audioSeconds) =>
    DurationSeconds <= 0 ? 0.0 : (audioSeconds + AudioOffsetSeconds) / DurationSeconds;
```

재생 중 키음은 `음원 시각 + 오프셋` 구간의 노트를 울리므로, 밀린 파형과 같이 움직입니다.

> [!WARNING]
> 오프셋은 `#BMSEDITER_OFFSET` 이라는 에디터 전용 줄로 저장되고 **게임은 읽지 않습니다.**
> 오프셋을 걸고 맞춘 노트는 게임에서 그만큼 어긋납니다. 쓰는 법은 [beat_sync_workflow.md](../guides/beat_sync_workflow.md#1-처음부터-끝까지-일정한-양만큼-밀려-있다) 를 보십시오.

---

## 🔁 BPM 을 바꿨을 때 일어나는 일

`MainWindowViewModel.OnBpmChanged` 가 순서대로 처리합니다.

1. `Chart.Header.Bpm` 에 적고 문서를 고친 것으로 표시합니다.
2. 시간축을 버립니다(`InvalidateTimeline`). 다음에 누가 `Timeline` 을 물으면 새 BPM 으로 다시 만듭니다.
3. 음원 길이가 새 BPM 에서 요구하는 마디 수만큼은 확보합니다(줄이지는 않습니다).
4. 격자가 초록색으로 한 번 번쩍입니다(`FlashGridSync`). 다시 그려졌다는 표시입니다.

관련 문서: [grid_specification.md](../specifications/grid_specification.md) (격자 분할 규칙) · [beat_sync_workflow.md](../guides/beat_sync_workflow.md) (박자 맞추는 작업 순서)

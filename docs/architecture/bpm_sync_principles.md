# BMS Editor - BPM 설정에 따른 그리드 및 파형 동기화 원리 (BPM & Grid Sync Principles)

이 문서는 에디터 내에서 BPM(Beats Per Minute) 값을 변경하거나 설정할 때, 오디오 파형과 마디 격자선(Grid Lines)이 화면에 어떻게 상호작용하며 실시간 반영되는지 설명합니다.

---

## 📌 핵심 요약 (Core Concept)
1. **오디오 파형의 물리적 형태는 고정**됩니다. (OGG 곡의 총 길이와 시간대별 파형 진폭은 불변)
2. **그리드 격자선(마디/박자 선)은 BPM에 따라 유동적으로 신축**됩니다. (BPM이 높을수록 간격이 좁아지고, 낮을수록 넓어짐)
3. 에디터는 오디오 시간 축(Seconds)을 매개체로 파형과 격자선을 동기화하여, 사용자가 시각적·청각적 타격점(Onset)에 격자선을 정확하게 수동 튜닝할 수 있도록 돕습니다.

---

## 💻 상세 연동 메커니즘

### 1. 마디 격자선의 동적 압축 및 팽창
에디터 화면의 총 길이(Timeline Length)는 로드된 배경 오디오의 전체 시간(`DurationSeconds`)을 기준으로 결정됩니다.
이 고정된 화면 길이 내에서 그리드 선이 그려지는 간격(`secondsPerStep`)은 아래와 같은 수식으로 계산되어 BPM에 반비례하여 변경됩니다:

$$\text{secondsPerStep} = \frac{240.0}{\text{BPM} \times \text{BeatSplit}}$$

* **BPM이 올라갈 때 (BPM ↑)**
  - `secondsPerStep`이 작아집니다.
  - 마디선 간의 간격이 **시각적으로 좁아집니다 (그리드 압축)**.
  - 고정된 오디오 길이 안에 더 많은 마디선이 렌더링됩니다.
* **BPM이 내려갈 때 (BPM ↓)**
  - `secondsPerStep`이 커집니다.
  - 마디선 간의 간격이 **시각적으로 넓어집니다 (그리드 팽창)**.
  - 고정된 오디오 길이 안에 더 적은 마디선이 렌더링됩니다.

#### [렌더링 구현 코드 스니펫]
```csharp
// NoteGridControl.cs 및 OggWaveformControl.cs 공통
var secondsPerStep = 240.0 / (Bpm * split);
for (var index = 0; ; index++)
{
    var seconds = index * secondsPerStep;
    if (seconds > DurationSeconds) return;

    var ratio = seconds / DurationSeconds;
    // 고정된 timelineLength 상에서 ratio 비율에 맞춰 선의 위치(tPos)를 도출
    var tPos = IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
    
    // ... tPos 위치에 마디/박자 선 그리기 수행 ...
}
```

---

### 2. 파형(Waveform)의 고정 맵핑
- 배경 음악 파형(`OggWaveformControl`)은 오디오 버퍼의 실제 인덱스를 화면 픽셀 비율에 1:1 대응하여 그립니다.
- 따라서 **BPM이 아무리 바뀌어도 오디오 파형 자체는 늘어나거나 줄어들지 않고 제자리에 유지**됩니다.
- 이 구조 덕분에 사용자는 고정된 실제 오디오 파형 진폭(볼륨이 피크를 치는 지점)과 움직이는 마디 격자선을 비교하며, 마디 시작선이 드럼 비트나 온셋(Onset) 마커와 정확히 겹치도록 **BPM을 조율하는 작업**이 가능해집니다.

---

### 3. 배치된 노트(Notes)의 위치 동기화
이미 그리드 상에 배치된 노트들 또한 BPM이 변경되면 새로운 격자 위치에 맞춰 화면상의 좌표(`tPos`)가 동적으로 변경되어 함께 이동합니다.
노트의 초 단위 절대 위치 계산 공식은 다음과 같습니다:

$$\text{seconds} = (\text{Measure} + \text{Position}) \times \frac{240.0}{\text{BPM}}$$

#### [노트 위치 계산 코드 스니펫]
```csharp
// NoteGridControl.cs
private double ComputeNoteTPos(BmsNote note, double timelineLength)
{
    if (DurationSeconds > 0 && Bpm > 0)
    {
        var secondsPerMeasure = 240.0 / Bpm;
        var seconds = (note.Measure + note.Position) * secondsPerMeasure;
        var ratio = seconds / DurationSeconds;
        
        // 최종적으로 파형과 동일하게 오디오 비율(ratio) 기준으로 화면 상 위치 연산
        return IsHorizontalView ? (ratio * timelineLength) : ((1.0 - ratio) * timelineLength);
    }
    // ...
}
```

- **상호 작용**: BPM이 커지면 `seconds`가 짧아져 노트가 화면 왼쪽(세로뷰의 경우 아래쪽)으로 수축 이동하고, 격자선도 동일한 비율로 수축하므로 **노트가 자신이 속한 격자선 위에 완벽히 밀착해 함께 움직이는 시각적 효과**를 내게 됩니다.

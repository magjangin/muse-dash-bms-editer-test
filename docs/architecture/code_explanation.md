# 코드 설명서 (Code Explanation)

이 문서는 에디터를 이루는 소스 파일 하나하나의 역할과, 파일들 사이로 데이터가 어떻게 흐르는지를 설명합니다.
파일을 새로 만들거나 없애면 이 표도 같이 고쳐 주십시오.

---

## 🧭 0. 한눈에 보는 흐름

```
열기      BmsParser ──> BmsChart (노트 · 보존줄 · 키음 표 · BPM 변화 · 마디 길이)
                          │
                          ▼
편집      격자 클릭 · 검색 창 · 컨트롤 패널 ──> MainWindowViewModel 의 편집 메서드
                          │
                          ▼  모든 편집이 NotifyNotesChanged 한 곳을 지난다
          ├─ EditHistory        되돌리기 칸 기록 (문서 통째 스냅샷)
          ├─ HoldPairingEngine  홀드 · 샌드백 짝 다시 읽기 (muse_dash 프로파일)
          └─ 화면 갱신            격자 · 통계 · 컨트롤 패널

그리기    ChartTimeline (마디 위치 <-> 초) ──> NoteGridControl · OggWaveformControl

저장      BmsWriter ──> SafeFileWriter (원본 인코딩 유지 · 임시 파일 후 교체 · .bak)

가져오기  KeyboardChartReader ──> LanePlanner ──> DownmixEngine ──> 문서 통째 교체 (되돌리기 한 칸)
```

---

## 📂 1. Models (데이터 모델)

| 파일명 | 역할 및 핵심 구조 |
| :--- | :--- |
| **[BmsChart.cs](../../muse%20dash%20bms%20editer/Models/BmsChart.cs)** | 문서 전체의 컨테이너입니다. 헤더(`Header`), 노트(`Notes`), 키음 표(`WavTable`), 확장 BPM 표(`BpmTable`), 마디 길이 배율(`MeasureLengths`), BPM 변화(`BpmChanges`), 편집하지 않는 원문 줄(`PreservedLines`), 조건 블록이 있었는지(`HasConditionalBlocks`)를 들고 있습니다. 컬렉션을 옮기는 자리는 `ReplaceContentWith` / `Clear` 한 곳뿐입니다. |
| **[BmsHeader.cs](../../muse%20dash%20bms%20editer/Models/BmsHeader.cs)** | 제목 · 아티스트 · 장르 · 기본 BPM · `#PLAYER`(파일 값 1/2/3) · `#RANK` · 레벨 · 음원 오프셋(`AudioOffsetMs`, 에디터 전용 `#BMSEDITER_OFFSET`)입니다. 확장 헤더는 여기 담지 않고 보존줄로 둡니다. |
| **[BmsNote.cs](../../muse%20dash%20bms%20editer/Models/BmsNote.cs)** | 노트 한 개입니다. 마디(`Measure`), 레인(`LaneId`), 마디 안 위치(`Position`, 0.0~1.0), 키음 번호(`WavKey`), 조건 갈래(`BranchId`, 0이면 조건 밖), 원문 줄 번호(`SourceLineOrder`)를 가집니다. 파서는 `Type` 을 언제나 `Normal` 로 둡니다 — 홀드는 노트를 고쳐 만드는 것이 아니라 짝 검사가 얹는 파생 정보입니다. 격자·검색이 주고받는 인자(`NotePlacementArgs`, `NoteSelectionArgs`, `NoteCopyResult`, `NoteMoveDirection`, `NoteSelectionSource`)도 여기 있습니다. |
| **[BpmChange.cs](../../muse%20dash%20bms%20editer/Models/BpmChange.cs)** | 곡 도중 BPM 변화 한 건(마디, 마디 안 위치, 새 BPM)입니다. `#xxx03` 과 `#xxx08` 어느 쪽에서 왔든 같은 모양으로 다룹니다. |
| **[BmsRawLine.cs](../../muse%20dash%20bms%20editer/Models/BmsRawLine.cs)** | 에디터가 해석하지 않고 그대로 되돌려 쓰는 원문 줄(BGM, BPM 변화, 2P 채널, 확장 헤더, 주석, 조건 제어 줄 등)입니다. 원래 줄 순서(`Order`)와 조건 갈래(`BranchId`), 제어 줄 여부(`IsControlFlow`)를 추적합니다. |
| **[BmsWavItem.cs](../../muse%20dash%20bms%20editer/Models/BmsWavItem.cs)** | 키음 목록 한 줄입니다. 재생용 실제 경로(`FilePath`), 파일에 적혀 있던 글자(`SourceText`), 하위 폴더에서 같은 이름을 찾아 붙였는지(`IsPathGuessed`)를 나눠 들고 있어, 추측한 경로를 저장 파일에 박지 않습니다. |
| **[LaneDefinition.cs](../../muse%20dash%20bms%20editer/Models/LaneDefinition.cs)** | 격자에 띄울 레인 목록입니다. 기본값은 `16 · 11 · 12 · 13 · 14 · 15 · 18` 입니다. 뮤즈 대시가 읽는 채널은 `13 · 14 · 15 · 18` 뿐이고, 나머지는 건반 에디터 시절 레인이 남아 있는 것입니다. |
| **[GameProfile.cs](../../muse%20dash%20bms%20editer/Models/GameProfile.cs)** | 게임 하나의 규칙입니다. 홀드 짝 규칙(`HoldRule` — 종류, 채널, 시작/끝을 가리는 방법 `HoldRoleRule`, 짝 방식 `HoldPairingPolicy`, 범위 `HoldScope`), UID 로 종류를 정하는 표(`UidTable`), 다운믹스용 화면 배치(`DownmixLayout`), 규칙의 확인 수준(`ProfileVerification`)을 담습니다. 값은 `Profiles/*.json` 에서 읽습니다. |
| **[HoldModels.cs](../../muse%20dash%20bms%20editer/Models/HoldModels.cs)** | 짝 검사 결과입니다. 짝 하나(`HoldLink` — 머리·꼬리 노트를 가리키기만 함), 진단 한 줄(`HoldDiagnostic` — 고아 시작/끝, 순서 뒤집힘 등), 둘을 묶은 `HoldPairingResult` 입니다. |

---

## ⚙️ 2. Services (엔진)

### 2-1. 파일 읽기 · 쓰기

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[BmsParser.cs](../../muse%20dash%20bms%20editer/Services/BmsParser.cs)** | BMS 파일을 `BmsChart` 로 읽습니다. 바이트로 한 번만 읽고 BOM → UTF-8(엄격) → CP932/CP949 순으로 인코딩을 가립니다(`#WAV` 파일명이 폴더에 실제로 있는지가 1순위 증거). 2자리/3자리 키음 배치를 가리고, 편집 레인이 아닌 줄은 원문 보존줄로 남기면서 `#xxx02`·`#xxx03`·`#xxx08` 은 시간축용으로 따로 읽습니다. 조건 제어 줄은 `BranchTracker` 가 따라가며 줄마다 갈래 번호를 붙입니다(`#IF`~`#ENDIF`, `#SWITCH` 안에서는 `#CASE` 마다 앞 갈래를 닫고 새 갈래). 다운믹서가 같은 인코딩 판별을 쓰도록 `ReadLinesForAnalysis` 를 엽니다. |
| **[BmsWriter.cs](../../muse%20dash%20bms%20editer/Services/BmsWriter.cs)** | 문서를 BMS 텍스트로 씁니다. 값이 빈 헤더는 쓰지 않고, 보존한 헤더와 `#WAV`(중복 번호는 마지막 것 하나)를 쓴 뒤 데이터 줄을 씁니다. 조건 밖 줄은 마디 → (원문 줄, 레인 순서)로 정렬하고, 조건 블록은 원문 순서 그대로 **덩어리째** 내보내 조건 밖 줄이 `#IF`~`#ENDIF` 사이로 끼어들지 않게 합니다(`GroupConditionalBlocks`). 마디 안 위치는 분모의 최소공배수(최대 1920)로 분할합니다. ⚠️ 같은 마디·레인·자리에 노트가 둘이면 하나만 남습니다(알려진 한계). |
| **[SafeFileWriter.cs](../../muse%20dash%20bms%20editer/Services/SafeFileWriter.cs)** | 같은 폴더의 임시 파일에 끝까지 쓴 뒤에만 원본과 바꿔치기합니다(`File.Replace`). 직전 내용은 `.bak` 으로 남고, `Replace` 를 못 쓰는 드라이브에서는 `Move(overwrite)` 로 물러납니다. |
| **[FolderMedia.cs](../../muse%20dash%20bms%20editer/Services/FolderMedia.cs)** | 곡 폴더에서 차트 · 음원(OGG) · 영상을 고르는 규칙입니다. 폴더 이름과 같은 파일이 있으면 그것, 없으면 이름순 첫 번째. 폴더 열기와 건반 BMS 가져오기가 같이 씁니다. |
| **[WavKey.cs](../../muse%20dash%20bms%20editer/Services/WavKey.cs)** | base-36 키음 번호(`01`~`ZZ`, `001`~`ZZZ`)의 파싱 · 서식 · 자릿수 규칙을 한 곳에 둡니다. |

### 2-2. 시간축 · 되돌리기

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[ChartTimeline.cs](../../muse%20dash%20bms%20editer/Services/ChartTimeline.cs)** | **"마디 위치 ↔ 초" 변환을 맡는 유일한 곳**입니다. 기본 식은 `tick × 240 / bpm` 이고, 마디 길이 배율(`#xxx02`)과 BPM 변화(`#xxx03`/`#xxx08`)를 마디 경계마다 누적해 둡니다. 격자선 · 노트 · 클릭 · 재생 · 키음이 모두 여기에 묻습니다. → [bpm_sync_principles.md](bpm_sync_principles.md) |
| **[EditHistory.cs](../../muse%20dash%20bms%20editer/Services/EditHistory.cs)** | 되돌리기 / 다시 하기 칸입니다. 편집 직후의 문서를 통째 복제한 스냅샷(`EditSnapshot`)을 최대 100칸 쌓습니다. 명령마다 역연산을 적지 않으므로 빠뜨릴 편집 경로가 없습니다. |

### 2-3. 오디오

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[OggDecoder.cs](../../muse%20dash%20bms%20editer/Services/OggDecoder.cs)** | 배경 음악(OGG)을 NVorbis 로 한 번만 PCM16 으로 풉니다. 재생과 파형이 같은 결과를 나눠 씁니다. 디코더가 예상보다 많이/적게 내보내도 배열을 맞추고 길이를 실제 표본 수로 다시 잡습니다. |
| **[OggAudioPlayer.cs](../../muse%20dash%20bms%20editer/Services/OggAudioPlayer.cs)** | `winmm.dll` 의 `waveOut` 을 `[LibraryImport]` 로 직접 불러 재생합니다. 재생 배속은 장치 샘플레이트를 바꿔 구현하므로 음정이 같이 내려갑니다. 재생 위치는 벽시계가 아니라 장치에 묻습니다(`GetPlayedSeconds`). |
| **[OggPeakLoader.cs](../../muse%20dash%20bms%20editer/Services/OggPeakLoader.cs)** | 파형 막대(초당 80칸, 32~20000칸)와 온셋 마커를 만듭니다. "버킷 i = i × 길이 ÷ 개수 시점"이라는 규칙(`GetBucketRatio` / `GetBucketRange`)을 여기 한 곳에만 둡니다. |
| **[KeySoundPlayer.cs](../../muse%20dash%20bms%20editer/Services/KeySoundPlayer.cs)** | 키음 믹서입니다. 미리 풀어 둔 PCM 을 자기 `waveOut` 스트림(44.1kHz 스테레오, 40ms 버퍼 3개)에 더해 내보내므로 화음과 긴 키음이 끊기지 않습니다. 디코딩에 실패한 파일만 Win32 `PlaySound` 로 물러납니다. |
| **[WavDecoder.cs](../../muse%20dash%20bms%20editer/Services/WavDecoder.cs)** | 키음 WAV(8/16/24/32bit PCM, IEEE Float, `WAVE_FORMAT_EXTENSIBLE`)와 OGG 를 44.1kHz 16bit 스테레오로 풀고 선형 보간으로 리샘플링합니다. |

### 2-4. 홀드 짝 (`Services/Holds/`)

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[GameProfileCatalog.cs](../../muse%20dash%20bms%20editer/Services/Holds/GameProfileCatalog.cs)** | exe 안에 넣은 `Profiles/*.json` 을 읽고, exe 옆 `profiles` 폴더가 있으면 같은 id 를 덮어씁니다. 규칙이 빠진 프로파일은 읽을 때 거절합니다. 차트 경로의 폴더 이름으로 게임을 추정합니다(`DetectFromPath`, 다운믹서가 씀). |
| **[HoldPairingEngine.cs](../../muse%20dash%20bms%20editer/Services/Holds/HoldPairingEngine.cs)** | 규칙마다 해당 노트를 골라 `(조건 갈래, 레인)` 으로 묶고 시각 순으로 줄 세운 뒤 짝 방식을 적용합니다. `Chart.Notes` 는 건드리지 않고 편집마다 통째로 다시 계산합니다. |
| **[HoldRoleClassifier.cs](../../muse%20dash%20bms%20editer/Services/Holds/HoldRoleClassifier.cs)** | 노트 하나가 시작 · 끝 · 짝 수열 구성원 중 무엇인지 가립니다(키음 값 / 파일명 키워드 / 파일명 UID / 파일명 기본 이름). 뮤즈 대시 UID 는 `UidTypeResolver` 가 **앞 4자리 → 3~4번째 자리** 순서로 읽습니다(하트 `0002xx` 가 홀드로 잡히지 않게). |
| **[HoldPolicies.cs](../../muse%20dash%20bms%20editer/Services/Holds/HoldPolicies.cs)** | 게임 모드에서 옮겨 온 짝 방식 다섯 가지(`NearestUnconsumedTail`, `NearestTailShared`, `FifoQueue`, `ReplacePending`, `Alternate`)와 진단 문구입니다. 뮤즈 대시는 `Alternate`(같은 채널에서 홀수=시작, 짝수=끝)입니다. |

### 2-5. 건반 BMS 다운믹스 (`Services/Downmix/`)

→ 사용법과 규칙은 [keyboard_bms_downmix.md](../guides/keyboard_bms_downmix.md)

| 파일명 | 역할 및 핵심 기술 |
| :--- | :--- |
| **[KeyboardChart.cs](../../muse%20dash%20bms%20editer/Services/Downmix/KeyboardChart.cs)** | 건반 차트에서 읽어 낸 노트(`KeyboardNote` — 채널, 위치, 일반/롱 시작/롱 끝)와 차트 요약(`KeyboardChart`)입니다. |
| **[KeyboardChartReader.cs](../../muse%20dash%20bms%20editer/Services/Downmix/KeyboardChartReader.cs)** | 1P 건반 채널(`16 11 12 13 14 15 18 19`)을 읽습니다. 2P · `51~59` · `#LNOBJ` 는 읽지 않고, 게임 프로파일이 있으면 그 게임이 노트로 읽지 않는 채널도 버립니다. 롱노트는 프로파일의 짝 규칙(`HoldPairingEngine`)을 빌려 시작 · 끝 노트로 표시합니다. |
| **[LanePlanner.cs](../../muse%20dash%20bms%20editer/Services/Downmix/LanePlanner.cs)** | 채널마다 지상 · 공중 · 번갈아를 정합니다. 채널 번호로 짐작 → 게임 화면 배치(`DownmixLayout`)로 덮기 → 지상 비율이 45~55% 밖이면 무거운 레인을 번갈아로 돌리기, 세 단계입니다. |
| **[DownmixEngine.cs](../../muse%20dash%20bms%20editer/Services/Downmix/DownmixEngine.cs)** | 노트를 지상(`13`) · 공중(`14`)에 놓습니다. 자리가 차면 반대 레인으로 옮기고, 둘 다 차거나 홀드가 지나는 중이면 버립니다. 홀드의 시작 · 끝은 같은 레인에 붙여 짝 순서가 엇갈리지 않게 합니다. 결과 창에 쓸 숫자(`DownmixReport`)도 만듭니다. |
| **[MuseDashKeyResolver.cs](../../muse%20dash%20bms%20editer/Services/Downmix/MuseDashKeyResolver.cs)** | 변환된 노트에 붙일 키음 번호를 마련합니다. 가져오기는 `CreateDefinitions` 로 빈 번호에 1번 씬 노트 정의 6개를 새로 적습니다. `Resolve`(이미 있는 키음 표에서 찾기)는 지금 `HasMuseDashKeysounds` 와 테스트에서만 씁니다. |

---

## 🎮 3. Profiles (게임 규칙 JSON, exe 안에 포함)

| 파일명 | 쓰는 곳 |
| :--- | :--- |
| **[muse_dash.json](../../muse%20dash%20bms%20editer/Profiles/muse_dash.json)** | **에디터의 홀드 · 샌드백 짝 검사가 쓰는 유일한 프로파일**입니다(`MainWindowViewModel.Holds`). UID 표와 `Alternate` 규칙이 들어 있습니다. |
| [startrail.json](../../muse%20dash%20bms%20editer/Profiles/startrail.json) · [gunvolt.json](../../muse%20dash%20bms%20editer/Profiles/gunvolt.json) · [stargazer.json](../../muse%20dash%20bms%20editer/Profiles/stargazer.json) · [deflate.json](../../muse%20dash%20bms%20editer/Profiles/deflate.json) · [unbeatable.json](../../muse%20dash%20bms%20editer/Profiles/unbeatable.json) | **건반 BMS 가져오기가 원본 차트를 읽을 때만** 씁니다. 롱노트 규칙과 화면 배치(`downmix`)가 들어 있고, 근거 코드가 `source` 에 적혀 있습니다. |

---

## 🖥️ 4. ViewModels (MVVM 뷰모델)

`MainWindowViewModel` 은 partial 파일 7개로 나뉘어 있습니다.

| 파일명 | 역할 및 특징 |
| :--- | :--- |
| **[MainWindowViewModel.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.cs)** | 중심입니다. 헤더 · 격자 설정 바인딩, 시간축(`Timeline`), 마디 수(음원과 차트 중 큰 쪽을 바닥으로), 음원 오프셋과 자동 맞춤(`TryDetectAudioOffsetMs`), 변경 표시(`IsDirty`)와 제목 표시줄, `Dispose` 를 맡습니다. |
| **[MainWindowViewModel.Editing.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.Editing.cs)** | 노트 배치 · 삭제 · 이동(하나라도 못 가면 전부 제자리) · 마디 복제 · 키음 일괄 교체 · 선택, 키음 추가/삭제입니다. **모든 편집이 `NotifyNotesChanged` 를 지나며**, 여기서 되돌리기 기록과 홀드 짝 재계산이 같이 돕니다. |
| **[MainWindowViewModel.History.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.History.cs)** | 되돌리기 / 다시 하기 명령과 스냅샷 뜨기 · 되살리기입니다. 노트 · 키음 표 · 선택을 담고, 건반 BMS 가져오기를 되돌릴 때만 헤더 · 보존줄까지 문서 전체를 되살립니다. |
| **[MainWindowViewModel.Holds.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.Holds.cs)** | `muse_dash` 프로파일로 짝을 다시 읽고(`RecomputeHolds`), 격자에 줄 몸통(`HoldLinks`) · 경고 노트(`HoldProblemNotes`) · 상태줄 요약 · 검사기 목록을 내놓습니다. |
| **[MainWindowViewModel.Playback.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.Playback.cs)** | 재생 / 정지, 스크럽(드래그 중엔 커서만, 뗄 때 한 번 재생), 33ms 타이머로 장치 재생 위치를 읽어 커서를 옮기고, 그 구간의 노트 키음을 이진 탐색으로 찾아 울립니다. |
| **[MainWindowViewModel.FileIO.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.FileIO.cs)** | BMS 열기 / 저장(원본 인코딩으로 못 담는 글자가 생기면 UTF-8 로 물러나고 알림), OGG 비동기 로드(실패해도 기존 음원 유지), 영상 연결, 새로 만들기입니다. 헤더를 화면으로 옮기는 `PullHeaderFromChart` 와 그 반대인 `CopyHeaderTo` 가 여기 있습니다. |
| **[MainWindowViewModel.Downmix.cs](../../muse%20dash%20bms%20editer/ViewModels/MainWindowViewModel.Downmix.cs)** | 건반 BMS 가져오기입니다. 조건 블록 차트는 거절하고, 노트 정의를 만들고, 덮어쓰기 확인을 받은 뒤 문서를 통째로 갈아 끼우며(되돌리기 한 칸), 원본 폴더의 음원 · 영상을 같이 엽니다. 결과 창 문구(`DescribeDownmixReport`)도 만듭니다. |
| **[ControlPanelViewModel.cs](../../muse%20dash%20bms%20editer/ViewModels/ControlPanelViewModel.cs)** | 🎛️ 컨트롤 패널입니다. `NoteStatsViewModel` 의 집계를 물려받아, 고른 레인 · 키음 줄의 노트를 격자에서 선택하고 그 자리로 스크롤하며, 미리듣기 · 번호 일괄 교체 · 확인 후 일괄 삭제를 합니다. |
| **[NoteStatsViewModel.cs](../../muse%20dash%20bms%20editer/ViewModels/NoteStatsViewModel.cs)** | 📊 통계 창(보기 전용)입니다. 실제로 쓰인 레인 · 키음별 노트 수를 한 번의 루프로 셉니다. 집계 규칙은 여기 한 곳뿐입니다. |
| **[NoteSearchViewModel.cs](../../muse%20dash%20bms%20editer/ViewModels/NoteSearchViewModel.cs)** | 🧰 검색 / 삭제 / 교체 창입니다. 마디 범위 · 키음 번호 범위 · 레인 · 선택 상태로 노트를 찾아 선택 · 삭제 · 번호 변경 · 마디 옮겨 복사를 합니다. |
| **[WavPaletteViewModel.cs](../../muse%20dash%20bms%20editer/ViewModels/WavPaletteViewModel.cs)** | 🎨 키음 팔레트 창입니다. 메인 뷰모델의 `SelectedWavItem` 을 그대로 읽고 써서 사이드바 목록과 늘 같은 붓을 가리킵니다. 검색어 필터와 보기 크기 4단계가 있습니다. |
| **[OwnerObservingViewModel.cs](../../muse%20dash%20bms%20editer/ViewModels/OwnerObservingViewModel.cs)** | 보조 창 뷰모델의 바탕입니다. 메인 뷰모델 구독과 창을 닫을 때의 구독 해제를 짝으로 묶어 둡니다. |
| **[BulkObservableCollection.cs](../../muse%20dash%20bms%20editer/ViewModels/BulkObservableCollection.cs)** | 항목을 통째로 갈아 끼우고 알림은 한 번(`Reset`)만 내는 컬렉션입니다. 차트를 열 때 키음 수만큼 재집계가 돌던 것을 막습니다. |

---

## 🎨 5. Views & Controls (화면)

| 파일명 | 역할 및 렌더링 메커니즘 |
| :--- | :--- |
| **[MainWindow.axaml / .cs](../../muse%20dash%20bms%20editer/MainWindow.axaml)** | 최상위 창입니다. 메뉴 · 도구 모음 · 격자와 파형 · 오른쪽 패널(홀드 검사기, 영상, 헤더, 격자 설정, 음원 오프셋, 키음 목록) · 상태줄로 이뤄집니다. 창 전체 단축키(Ctrl+N/O/S/Z/Y, Space, Delete, 방향키, Esc), 저장 안 한 채 닫을 때의 3지선다, 보조 창을 종류당 하나만 띄우기, 가운데 버튼 스크럽, 재생 커서 따라가기를 처리합니다. |
| **[TimelineControlBase.cs](../../muse%20dash%20bms%20editer/Views/Controls/TimelineControlBase.cs)** | 격자와 파형이 같이 쓰는 바탕입니다. 줌 · BPM · 시간축 속성, 타임라인 길이 식(`GetTimelineHeight`), 격자선 열거(`EnumerateGridLines`), 음원 오프셋을 반영한 비율 변환(`AudioRatio`), 재생 커서와 BPM 변경 번쩍임을 그립니다. → [grid_specification.md](../specifications/grid_specification.md) |
| **[NoteGridControl.cs](../../muse%20dash%20bms%20editer/Views/Controls/NoteGridControl.cs)** | 격자판과 노트를 그립니다. 홀드 · 샌드백 몸통(노랑 · 분홍)을 노트보다 먼저 그리고, 짝이 어긋난 노트에 주황 점선을 두릅니다. 좌클릭 배치(격자 스냅 on/off), 우클릭 삭제, 드래그 선택(Ctrl/Shift 로 더하기)을 받습니다. 화면 밖을 건너뛰는 처리 없이 타임라인 전체를 그립니다. |
| **[OggWaveformControl.cs](../../muse%20dash%20bms%20editer/Views/Controls/OggWaveformControl.cs)** | 음원 파형 막대와 온셋 마커, 마디 번호 · 초 라벨(`#003 (5.7600s)`)을 그립니다. 좌클릭 · 드래그로 재생 위치를 옮깁니다. |
| **[VideoPreviewControl.cs](../../muse%20dash%20bms%20editer/Views/Controls/VideoPreviewControl.cs)** | WebView2 창을 격자 옆에 붙여 영상을 띄우고, 가상 호스트로 로컬 파일을 열며, 재생 위치를 0.2초마다 맞춥니다. → [video_ogg_principles.md](video_ogg_principles.md) |
| **[ConfirmWindow.axaml / .cs](../../muse%20dash%20bms%20editer/Views/ConfirmWindow.axaml)** | 확인/취소 · 알림 전용 · 저장/저장 안 함/취소 3지선다를 모두 맡는 대화상자입니다. 창을 그냥 닫으면 언제나 "취소"입니다. |
| **[ControlPanelWindow.axaml / .cs](../../muse%20dash%20bms%20editer/Views/ControlPanelWindow.axaml)** | 🎛️ 컨트롤 패널 창(모드리스)입니다. 목록 항목에는 명령을 걸지 않고, 누르는 것은 전부 목록 밖 버튼입니다. |
| **[NoteSearchWindow.axaml / .cs](../../muse%20dash%20bms%20editer/Views/NoteSearchWindow.axaml)** | 🧰 검색 / 삭제 / 교체 창(모드리스)입니다. Esc 로 닫힙니다. |
| **[NoteStatsWindow.axaml / .cs](../../muse%20dash%20bms%20editer/Views/NoteStatsWindow.axaml)** | 📊 통계 창(모드리스, 보기 전용)입니다. |
| **[WavPaletteWindow.axaml / .cs](../../muse%20dash%20bms%20editer/Views/WavPaletteWindow.axaml)** | 🎨 키음 팔레트 창(모드리스)입니다. 여러 WAV 를 한 번에 추가할 수 있습니다. |

---

## 🚀 6. 앱 진입점 · 배포

| 파일명 | 역할 |
| :--- | :--- |
| **[Program.cs](../../muse%20dash%20bms%20editer/Program.cs)** | Avalonia 앱을 띄웁니다. 처리되지 않은 예외는 exe 옆 `crash.log` 에 남깁니다. |
| **[App.axaml / .cs](../../muse%20dash%20bms%20editer/App.axaml)** | Fluent 테마를 걸고 메인 창을 만듭니다. |
| **[win-x64.pubxml](../../muse%20dash%20bms%20editer/Properties/PublishProfiles/win-x64.pubxml)** | 네이티브 라이브러리까지 exe 하나로 묶는 배포 설정입니다(`-p:PublishProfile=win-x64`). |

---

## 🧪 7. 테스트 (`muse dash bms editer.Tests/`)

xUnit + Avalonia Headless 입니다. 헤드리스 창은 디스패처 스레드 하나를 공유하므로 병렬 실행을 끕니다(`AssemblyInfo.cs`).

| 묶음 | 파일 |
| :--- | :--- |
| 파일 왕복 · 저장 안전 | `BmsRoundTripTests` · `RoundTripCleanlinessTests` · `SaveIntegrityTests`(조건 블록 포함) · `EncodingDetectionTests` · `AudioOffsetTests` |
| 시간축 · 격자 · 파형 | `ChartTimelineTests` · `GridLineTests` · `WaveformTimeAxisTests` |
| 편집 · 되돌리기 · 변경 표시 | `NoteEditingTests` · `UndoRedoTests` · `DirtyTrackingTests` |
| 홀드 짝 | `HoldPairingTests` · `HoldRenderingTests`(픽셀) · `HoldPanelSmokeTests` |
| 다운믹스 | `DownmixTests` · `DownmixRealChartTests` |
| 창 · 보조 창 | `WindowSmokeTests` · `UndoUiSmokeTests` · `ControlPanelTests` · `LoadPerformanceTests` |
| 오디오 · 기타 | `KeySoundPlayerTests` · `PlaybackSpeedAndKeySoundTests` · `FolderMediaTests` |

> `DownmixRealChartTests` 와 `DownmixTests` · `HoldPairingTests` 의 "실제 차트" 테스트는 이 개발 PC 의 게임 폴더(`H:\…`)를 읽습니다.
> 폴더가 없는 PC 에서는 한 줄만 남기고 **통과로** 끝납니다(건너뜀으로 세지 않습니다).

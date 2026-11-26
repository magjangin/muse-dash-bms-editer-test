# Muse Dash BMS Editor — Project Handover & Agent Guide

> [!CAUTION]
> ## 🚨 [필수 인수인계 원칙] 가장 중요한 1, 2, 3대 패키징 규칙
> 본 프로젝트의 유지보수 및 추가 개발 시 **아래 1, 2, 3번 규칙은 어떠한 경우에도 반드시 준수**해야 합니다:
>
> 1. **1번: 개인용 바이너리 (`publish/personal/muse dash 1 bms editer 1.exe`)**:
>    - **에셋 100% 영구 내장 단일 파일**: `EmbedNoteIcons=true` 옵션으로 빌드되어 모든 게임 스프라이트(120여 개 PNG)가 exe 내부에 완전히 임베드된 **순수 단일 실행 파일**입니다.
>    - **외부 폴더 절대 금지**: `publish/personal/` 디렉터리에는 외부 `note_icons/` 폴더를 복사하지 않고 오직 `muse dash 1 bms editer 1.exe` 단 하나만 존재해야 합니다.
>    - **시각적 식별**: 앱/창 아이콘은 **골드 앰버 + 숫자 1 뱃지**(`Assets/icon_personal.ico`), 창 제목 끝에 `(1. 개인용)` 이 자동 표기됩니다.
>    - **보안 및 저작권**: 게임 저작권 에셋이 내장되어 있으므로 **Git 추적에서 100% 영구 제외**(`.gitignore`)되어 있습니다.
>
> 2. **2번: 릴리즈용 바이너리 (`publish/release/muse dash 1 bms editer 2.exe`)**:
>    - **외부 폴더 분리형 패키지**: exe 자체는 에셋이 미내장된 순수 바이너리(`EmbedNoteIcons=false`)이며, **외부 `note_icons/` 폴더가 반드시 함께 있는 구성**입니다.
>    - **시각적 식별**: 앱/창 아이콘은 **사이언 블루 + 숫자 2 뱃지**(`Assets/icon_release.ico`), 창 제목 끝에 `(2. 릴리즈용)` 이 자동 표기됩니다.
>    - **용도**: 공개 깃허브 Releases 배포용 또는 외부 에셋 폴더를 직접 관리하는 사용자 대상 패키지입니다.
>
> 3. **3번: 퍼블리싱 전 클린 빌드 및 어셈블리명 고정 필수**:
>    - **사전 삭제 필수**: `.NET Single-File` 환경 특성상 기존 산출물이 남아있는 상태에서 덮어쓰기 빌드를 진행하면 캐시 꼬임 및 매니페스트 불일치가 발생합니다. 퍼블리싱 전 **반드시 대상 폴더의 기존 산출물을 완전 삭제 후 새로 패키징**해야 합니다 (`build_publish.bat`, `build_personal.bat`, `build_release.bat`에 기본 탑재).
>    - **임의 리네임 금지**: 빌드 후 파일 시스템에서 exe 이름을 임의 변경(`Move-Item`)하면 `HostFXR 0x80008092` 크래시가 발생하므로, 반드시 프로필의 `<AssemblyName>`(`muse dash 1 bms editer 1`, `muse dash 1 bms editer 2`)을 통해 컴파일 시점부터 파일명이 결정되도록 유지해야 합니다.

---

## 1. 대상 게임 사양 및 환경 고정

* **대상 게임**: *Muse Dash* (Steam PC / Mobile 포맷 호환)
* **Unity 버전 & 런타임 백엔드**: Unity 2021+ 기반, **Il2Cpp 백엔드**
* **차트 포맷**: `.bms` (BMS 2채널 지상 `13` / 공중 `14` 변형 포맷)
* **런타임 아키텍처**: 
  - 에디터 본체: .NET 10.0, Avalonia UI 12.0.5, SkiaSharp 기반 독립 데스크톱 애플리케이션 (Windows x64)
  - 인게임 연동: BMS 차트 및 BGM/BGA 주입 방식 (MelonLoader 모드 기반 로딩 대상)

---

## 2. 프로젝트 및 아키텍처 구조

```
muse dash bms editer/
├── Assets/                     # 앱 아이콘 (icon_personal.ico, icon_release.ico 등)
├── Models/                     # BMS 차트 데이터 모델 (BmsChart, BmsNote, BmsHeader, HoldModels 등)
├── Services/                   # 핵심 도메인 로직
│   ├── BmsParser.cs / BmsWriter.cs  # 원본 보존형 BMS 파서/라이터 (인코딩 자동 보존)
│   ├── ChartTimeline.cs        # 타임라인 계산 (time = tick * 240 / bpm)
│   ├── NoteIcons.cs            # UID(6자리) 기반 스프라이트 렌더링 및 스마트 폴더 탐색
│   ├── NoteIconImporter.cs     # Spine Support Tool 렌더링 에셋 가져오기
│   ├── Downmix/                # 건반 BMS -> 뮤즈 대시 2레인 자동 변환 엔진
│   └── Holds/                  # 홀드/샌드백 짝 검사 및 자동 연결
├── ViewModels/                 # MVVM ViewModel 계층 (CommunityToolkit.Mvvm)
├── Views/                      # Avalonia 뷰 및 커스텀 렌더 컨트롤 (NoteGridControl 등)
├── note_icons/                 # (로컬 전용) 게임 추출 스프라이트 에셋 (Git 제외)
└── Properties/PublishProfiles/ # 배포 프로필 (release.pubxml, personal.pubxml)
```

---

## 3. 핵심 설계 의도 & Rationale (우회 및 구현 이유)

1. **배포 바이너리 번호 및 아이콘 차별화**:
   - 사용자가 실행 시 헷갈리지 않도록 개인용은 **`1`번 (골드 아이콘, 에셋 100% 내장 단일 exe)**, 릴리즈용은 **`2`번 (블루 아이콘, 에셋 폴더 분리형 exe)**으로 파일명과 아이콘, 창 제목을 완전히 분리했습니다.
2. **원자적 저장 및 인코딩 자동 보존**:
   - Shift_JIS(CP932), CP949, UTF-8(BOM 유무)를 감지하여 원본 인코딩 그대로 저장합니다.
   - 임시 파일(`.tmp`)에 완전히 작성한 후 교체하는 원자적 교체 및 `.bak` 백업 방식을 적용하여 크래시 발생 시 차트 파일 유실을 방지합니다.
3. **타임라인 수식 표준**:
   - 틱(tick) 기반 시간 변환 공식 `time = tick * 240 / bpm`을 [`ChartTimeline.cs`](file:///h:/source/repos/muse%20dash%20bms%20editer/muse%20dash%20bms%20editer/Services/ChartTimeline.cs) 한곳에서 통합 관리하여 변속 구간 및 변박 마디에서도 격자와 재생 위치가 1ms 단위로 일치합니다.

4. **타임라인 렌더링 (재생 중 프레임 유지의 핵심)**:
   - 커서·격자 맞춤 번쩍임·드래그 사각형은 [`TimelineControlBase`](muse%20dash%20bms%20editer/Views/Controls/TimelineControlBase.cs) 의 **겹 컨트롤(`OverlayLayer`)** 이 따로 그립니다. 재생 중에 격자·파형을 다시 기록하지 않기 위해서입니다 (`RenderOverlay` 를 재정의).
   - 노트 그리기 루프 안에서는 StyledProperty(`IsHorizontalView`, `DurationSeconds`, `Timeline` 등)를 읽지 않습니다. `NoteGridControl` 은 렌더·클릭 한 번마다 `CaptureAxis()`/`CaptureNoteStyle()` 로 값을 한 번만 읽어 `TimelineAxis`·`NoteStyle` 에 담아 씁니다. `GetValue` 는 노트 수천 개 × 여러 번이면 프레임에 그대로 드러납니다.
   - 격자·파형은 `BeginTimelineRender()` 가 돌려준 구간만 그립니다. 뷰포트를 모르면(비트맵으로 뜰 때, ScrollViewer 밖) 전체를 그려 결과가 달라지지 않습니다.
   - 실측(180초 · 2,688노트 · 헤드리스 Debug): 커서가 화면에 있을 때 프레임 **31~34ms → 약 1.2ms**, 스크롤 약 9.5ms → 약 5.5ms.
5. **재생 경로에서 UI 스레드를 막지 않기**:
   - `KeySoundPlayer.Play` 는 재생 타이머 안에서 불리므로 디스크를 건드리면 안 됩니다. 캐시에 없으면 백그라운드로 넘기고, 없는 파일은 2초 동안 다시 보지 않습니다.
   - OGG 파형 계산과 노트 그림 읽기도 백그라운드입니다. 노트 그림은 준비되는 대로 교체되므로 잠깐 단색으로 보일 수 있습니다(정상).

6. **자동 저장·복구** ([`AutoSaveStore`](muse%20dash%20bms%20editer/Services/AutoSaveStore.cs), `MainWindowViewModel.AutoSave.cs`):
   - 사용자 데이터는 전부 `%LocalAppData%\muse dash bms editer\` 아래입니다: `autosave\<세션>\`(60초마다, 최근 5벌), `backups\<차트-해시>\`(저장 전 내용 10세대 + `original.bms`), `settings.json`, `logs\`.
   - **자동 저장 내용은 원본 차트 경로를 기준으로 만듭니다.** `BmsWriter` 가 저장 경로 기준으로 키음 상대 경로를 계산하므로, 자동 저장 폴더를 기준으로 쓰면 복구했을 때 키음이 전부 끊깁니다. 읽을 때도 `BmsParser.Parse(파일, 기준 폴더)` 로 같은 기준을 줍니다.
   - 정상 저장·"저장 안 함"이면 지웁니다. **다음 실행에 남아 있는 것은 곧 비정상 종료의 증거**입니다. 다른 창이 쓰는 폴더는 `session.lock` 으로 가립니다(프로세스가 죽으면 OS 가 풀어 줌).
   - `MainWindow.EnableDesktopServices()` 는 **실제 앱 시작(`App`)에서만** 부릅니다. 헤드리스 창 테스트가 사용자의 설정·자동 저장을 건드리면 안 됩니다. 같은 이유로 `AutoSave` 의 기본값은 null 입니다.

7. **줄바꿈 보존**: 파서가 첫 `\n` 앞의 `\r` 유무로 원본 줄바꿈을 감지해 `BmsChart.NewLine` 에 담고, `BmsWriter` 가 그대로 되돌려 씁니다. 저장만 해도 LF 파일이 CRLF 로 바뀌어 diff 가 전부 달라지는 일을 막습니다.
8. **단일 인스턴스 & 마디 상한**: 탐색기에서 차트를 더블클릭해도 창이 새로 뜨지 않고 이미 떠 있는 창으로 경로가 넘어갑니다(`Program.cs` 의 뮤텍스+파이프). 마디 번호는 `BmsParser.MaxMeasureNumber`(9999) 를 넘으면 노트로 읽지 않고 보존줄로 넘겨, 줄 하나 때문에 파일 전체를 못 여는 일을 막습니다. 단일 인스턴스로 넘어온 경로는 **이미 작업 중인 창**이 받으므로 `OpenChartPathAsync` 로 열어 저장 확인을 거치고, 차트 확장자(.bms/.bme/.bml)가 아니면 무시합니다(실행 직후 인자 열기만 확인 없이 `OpenExternalFileAsync`).
   - 파이프 이름에 **로그인 세션 번호**를 붙입니다. 뮤텍스는 `Local\` 이라 세션마다 따로인데 파이프는 PC 전체 이름이라, 다른 계정이 먼저 띄우면 이쪽 서버가 만들어지지 않고 예외 루프가 CPU 한 코어를 먹었습니다. 오류 시에는 500ms 쉬었다 다시 엽니다.
   - 창이 구독하기 전에 넘어온 경로는 `App.TakePendingExternalOpen()` 에 맡겨 두고, 시작(복구 질문·실행 인자 열기)이 끝나기 전이나 다른 외부 열기 처리 중에 온 경로는 마지막 하나만 미뤄 두었다가 엽니다. 예전에는 버려지거나 복구 질문과 저장 확인 창이 겹쳤습니다.

9. **업데이트 내성 — 끊긴 파일 연결 자동 복구** ([`FileAssociation.RepairIfStale`](muse%20dash%20bms%20editer/Services/FileAssociation.cs)):
   - 탐색기 연결은 등록 당시 exe 경로를 그대로 적습니다. 새 버전을 다른 폴더에 풀거나 exe 이름이 바뀌면(v0.1.6 의 `muse dash 1 bms editer N.exe` 개명) 더블클릭이 조용히 실패합니다.
   - 시작할 때(`EnableDesktopServices`, 백그라운드) **등록된 exe 가 디스크에서 사라졌을 때만** 지금 exe 로 다시 적고 `SHCNE_ASSOCCHANGED` 로 탐색기에 알립니다. 등록이 없거나(사용자가 연결을 안 함) 옛 exe 가 남아 있으면(1번·2번 같이 쓰기) **건드리지 않습니다** — 연결은 사용자의 선택입니다. `dotnet.exe`·`testhost` 로 뜬 경우도 제외합니다.
10. **되돌리기 메모리 예산** ([`EditHistory`](muse%20dash%20bms%20editer/Services/EditHistory.cs)):
   - 칸마다 노트를 통째 복제하므로 칸 수(100)만으로는 큰 차트(1만 노트↑, 건반 BMS 다운믹스)에서 수십~100MB 가 묶였습니다. 칸들의 노트 수 합을 `MaxTotalNotes`(60만)로 묶고, 그래도 최소 `MinDepth`(10)칸은 남깁니다.
11. **재생 키음 몰아 울림 방지** (`MainWindowViewModel.KeySoundWindow`):
   - 재생 타이머가 늦게 돌면(모달 창·큰 파일 열기로 UI 스레드가 막힘) 그 사이 노트가 한 프레임에 쏟아져 64보이스 상한을 채웠습니다. 최근 0.25초(`MaxKeySoundCatchUpSeconds`)만 울리고, 장치 위치가 뒤로 튀어도 기준(`_lastPlaybackPositionSeconds`)은 앞으로만 갑니다(중복 발음 방지).
12. **저장 분할 수 상한 넘침** ([`BmsWriter.SplitByResolution`](muse%20dash%20bms%20editer/Services/BmsWriter.cs)):
   - 한 마디·레인 노트들의 분모 최소공배수가 1920 을 넘으면 예전에는 1920 으로 잘라 반올림했습니다(7분할 줄 + 1/384 노트 → LCM 2688 → 노트가 저장마다 밀림). 이제 상한 안에 드는 노트끼리 한 줄로 묶고 나머지는 같은 마디·채널의 다음 줄로 씁니다. 보통 차트는 묶음이 하나라 출력이 예전과 같습니다.

13. **장애·버전 오가기 내성 (v0.1.8)**:
   - **설정 앞뒤 호환**: `AppSettings.UnknownFields`(`[JsonExtensionData]`)가 이 버전이 모르는 칸을 그대로 들고 있다가 다시 씁니다. `SchemaVersion` 은 `Math.Max` 로 낮추지 않습니다. 새 설정 칸을 추가할 때 옛 버전 사용자가 지우지 않으니 따로 이주 코드가 필요 없습니다.
   - **키음 디코딩 실패는 캐시하지 않습니다** (`KeySoundPlayer.GetOrLoad`). 실패의 흔한 원인이 "잠깐 잠긴 파일"이라, 영구 기억하면 차트를 다시 열 때까지 소리가 안 납니다. 대신 `_missingUntil` 로 2초 쉬어 재생 중 디스크를 두드리지 않습니다. 캐시에서 뺄 때는 `TryRemove(KeyValuePair)` 로 **같은 Lazy 일 때만** 뺍니다(`ClearCache` 뒤 새 항목 보호).
   - **임시 파일 청소**: `SafeFileWriter.CleanStaleTempFiles` 는 `<차트파일명>.<32자리 hex>.tmp` 형식과 정확히 맞고 1시간 넘은 것만 지웁니다(Windows 와일드카드가 8.3 이름까지 맞추므로 이름을 다시 검사). 설정 임시 파일은 1일.
   - **자동 저장 meta 유실**: 차트가 있으면 `FallbackMeta`(원본 경로 null, `NoteCount = -1` → 복구 창에 "알 수 없음")로 살립니다. 차트·잠금 없는 하루 지난 빈 폴더만 지웁니다.

14. **업데이트·동기화 내성 및 병목 해결 (v0.1.9)**:
   - **ReadOnly 및 파일 잠금(Sharing Violation) 내성**: `SafeFileWriter`, `AppSettings`, `AutoSaveStore`에서 파일 교체 전 대상 파일 및 `.bak`의 `ReadOnly` 속성을 자동 해제하고, 백신(Defender)·클라우드 동기화(OneDrive)의 일시적 파일 잠금에 대비한 3회 지수 백오프 재시도를 탑재했습니다.
   - **타임라인 연산 병목 제거**: `ChartTimeline` 생성 시 변속 위치를 사전 정규화하여 수천 개 노트 렌더링 시 매 호출마다 발생하던 `Math.Clamp`를 제거하고, 경계값 탐색 안전성을 강화했습니다.
   - **그리드 렌더링 최적화**: `NoteGridControl`에서 부동소수점 덧셈 비교 대신 정수 마디 우선 비교로 정렬 성능을 개선하고, 컬렉션 내 추가/삭제 시에도 즉시 갱신되도록 카운트 검증을 보강했습니다.
   - **BMS 헤더 무결성**: `#BMSEDITER_OFFSET`의 `double.IsFinite` 방어 및 `ToDenominator` NaN/음수 방어를 적용했습니다.

---

## 4. 커스텀 에셋 주입 파이프라인 & UID 규칙

* **UID 규칙**: 파일명 앞 6자리 `zzxxyy`
  - `zz`: 씬 번호 (01~12, 00=공통/오브젝트)
  - `xx`: 노트 종류 (01=보스, 02=하트, 03=음표, 04=씬전환, 05=복선, 15=해머 등)
  - `yy`: 모션 및 방향/속도
* **탐색 우선순위 ([`NoteIcons.cs`](file:///h:/source/repos/muse%20dash%20bms%20editer/muse%20dash%20bms%20editer/Services/NoteIcons.cs))**:
  1. 사용자 폴더 (`%LOCALAPPDATA%\muse dash bms editer\note_icons`)
  2. exe 인접 `note_icons/` 폴더 (`AppContext.BaseDirectory/note_icons`)
  3. 현재 작업 디렉터리 (`Directory.GetCurrentDirectory()/note_icons`)
  4. 상위 디렉터리 개발 경로 폴백 (`muse dash bms editer/note_icons`)
  5. 어셈블리 내장 리소스 (`note_icons/{uid}.png`, `EmbedNoteIcons=true` 시)

---

## 5. 시도했다가 실패한 것들 (Dead Ends / 주의사항)

* ❌ **Single-File exe를 파일 시스템에서 임의로 이름 바꾸기(Move-Item)**:
  - `.NET Single-File` 환경에서 파일명을 사후 변경하면 HostFXR이 번들 매니페스트를 찾지 못해 `2147516562` (0x80008092) 크래시 발생.
  - 해결: 빌드 프로필(`personal.pubxml`, `release.pubxml`)에 `<AssemblyName>`을 각각 `muse dash 1 bms editer 1`, `muse dash 1 bms editer 2`로 지정하여 .NET SDK가 처음부터 일치된 매니페스트로 빌드하도록 구성.
* ❌ **기존 산출물 덮어쓰기 빌드**:
  - `PublishSingleFile` 환경에서 기존 exe가 남아있는 상태로 덮어쓰기 빌드를 진행할 경우 구버전 매니페스트 캐시나 임베딩 충돌이 발생할 수 있음. 퍼블리싱 전 반드시 기존 파일 삭제 후 재패키징 필요.

* ❌ **재생 커서를 격자와 같은 컨트롤에 그리기**:
  - 커서가 움직일 때마다 곡 전체의 격자선·노트·파형이 다시 기록되어 프레임이 그대로 떨어졌습니다. `PlaybackPositionSeconds` 를 `AffectsRender` 에 다시 넣지 마십시오.
* ⚠️ **뷰포트 컬링의 함정 세 가지** (예전 컬링이 "불안정"하다고 되돌려진 적이 있습니다):
  - `EffectiveViewportChanged` 는 값이 **바뀔 때만** 옵니다. 첫 레이아웃에서는 안 올 수 있어 첫 렌더는 `EstimateViewport()` 로 직접 구합니다.
  - 스크롤하면 컨트롤의 `Bounds` 가 바뀌어 Avalonia 가 다시 그립니다. 그러니 뷰포트 여유분(`Overscan`)을 크게 잡아도 스크롤 비용만 늘어납니다.
  - **배경 사각형은 자르면 안 됩니다.** Avalonia 는 그린 자리만 클릭을 받습니다. 배경까지 자르면 아직 안 그린 구간이 클릭되지 않습니다.
* ❌ **`avares://` 경로에 어셈블리 이름 박기**:
  - 배포 프로필의 `<AssemblyName>` 이 `muse dash 1 bms editer 1` / `2` 라 고정 이름으로는 배포본에서만 그림을 못 찾았고, `try/catch` 에 묻혀 조용히 기본 아이콘이 떴습니다. `Assembly.GetName().Name` 을 쓰십시오.

* ❌ **파형 위에 온셋(어택) 세로선 그리기** (`6c1276f` 에서 제거):
  - 소리는 격자에 맞는데 선은 어긋나 보이는 곡(With 164 BPM)에서 오히려 판단을 흐렸습니다. 다시 그리지 마십시오. 온셋은 ⚡ 오프셋 자동 맞춤(`TryDetectAudioOffsetMs`)의 재료로만 남아 있습니다.
  - 대신 [`GridFitAdvisor`](muse%20dash%20bms%20editer/Services/GridFitAdvisor.cs) 가 "파형으로 맞춰도 되나 / 소리로 들어야 하나"만 안내합니다. 사용자가 **숫자(몇 ms 어긋남)는 원하지 않았습니다.**
* ❌ **노트 그림 구석에 속도(1~3) 색 동그라미 깔기** (`e2e4e3c` 에서 추가, 이후 제거):
  - 방향 삼각형 밑에 초록·파랑·빨강 동그라미를 깔았더니 삼각형의 위/아래가 한눈에 안 읽히고 게임 그림도 가렸습니다. 오른쪽으로 밀고 키워 봐도 사용자가 "혼동이 온다"며 삼각형만 있는 쪽을 골랐습니다. 다시 넣지 마십시오. `NoteIcon.Speed` 값은 남아 있지만 기본으로는 격자에 그리지 않습니다. v0.1.5 부터는 사용자가 켜는 **노트 속도 표시**(`ShowNoteSpeed`, 기본 꺼짐) 옵션으로만 그림 **왼쪽 위**에 숫자 배지를 따로 그립니다 — 방향 삼각형 밑에 깔지 마십시오.
* ❌ **격자 판정을 "격자 ±15ms 안에 드는 어택 비율"로 하기**:
  - 루바토·스윙·셋잇단처럼 원래 16분 격자 밖 소리가 많은 곡(your reality 0.23, 아기상어 0.20)까지 "소리로 들으세요"로 걸렀습니다. 지금은 "8마디 구간이 같은 쪽으로 25ms 이상 3구간 연속 밀림"을 봅니다. 보정 근거(11곡)는 `GridFitAdvisor.cs` 머리 주석에 있습니다.
* ⚠️ **자동 BPM 의 iBMSC 값은 일부러 원본 그대로입니다**:
  - [`IbmscBpmDetector`](muse%20dash%20bms%20editer/Services/IbmscBpmDetector.cs) 는 iBMSC 와 같은 숫자를 보려고 옮긴 것이라, cos 만 쓰는 점수 식·저음 필터의 채널 오해까지 그대로 둡니다. "고치면" 비교 기준이 사라집니다. 소수점은 [`PreciseBpmDetector`](muse%20dash%20bms%20editer/Services/PreciseBpmDetector.cs) 몫입니다.

* ❌ **테스트가 실제 사용자 폴더에 쓰기**:
  - `NoteIconImporter.Import` 가 대상과 상관없이 `%LocalAppData%\muse dash bms editer\note_icon_source.txt` 에 "마지막으로 가져온 폴더"를 적어서, `dotnet test` 를 돌릴 때마다 사용자의 기록이 곧 지워질 임시 경로로 덮어써졌습니다. 지금은 대상이 `UserFolder` 일 때만 적습니다. 새 테스트가 사용자 데이터 폴더를 건드리지 않는지 확인하십시오.

* ❌ **저장 해상도를 상한(1920)으로 잘라 반올림하기** (v0.1.7 에서 수정):
  - 1920 의 약수가 아닌 분모가 섞이면 노트가 경고 없이 밀립니다. 상한을 올리는 대신 줄을 나눠 씁니다(`SplitByResolution`). 해상도를 다시 한 줄로 강제하지 마십시오.
* ❌ **키음 디코딩 실패(null)를 캐시에 그대로 두기** (v0.1.8 에서 수정):
  - "파일은 있는데 실패 = 다시 읽어도 같음"이라는 가정이 틀렸습니다. 잠긴 파일·다시 내보내는 중인 파일도 실패합니다. 실패는 시간 제한(2초)으로만 기억하십시오.
* ❌ **설정 클래스에 없는 칸을 버리고 저장하기** (v0.1.8 에서 수정):
  - 1번·2번 exe 나 구·신 버전을 오가는 사용자가 많아 새 칸이 매번 초기화됐습니다. `UnknownFields` 를 지우지 마십시오.

* ❌ **파일 연결을 시작할 때마다 무조건 다시 등록하기**:
  - 사용자가 1번·2번 exe 를 같이 쓰거나 다른 프로그램으로 연결을 바꿨을 때 그 선택을 덮어씁니다. 복구는 "등록된 exe 가 사라졌을 때만" 입니다.

* ❌ **XAML 에 `NumericUpDown` 을 그대로 쓰기**:
  - Avalonia 12.0.5 의 자동화 연결이 `decimal` 을 UIA 로 못 넘겨, 화면 낭독기·접근성 도구가 붙어 있으면 값이 바뀔 때 예외가 납니다(차트를 여는 도중 문서가 반쯤만 들어옴). 평소에는 안 보입니다. [`NumberBox`](muse%20dash%20bms%20editer/Views/Controls/NumberBox.cs) 를 쓰십시오.

---

## 6. 빌드 & 패키징 스크립트 가이드

| 스크립트 | 대상 파일 | 설명 |
| :--- | :--- | :--- |
| **[`build_personal.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/build_personal.bat)** | [`publish/personal/muse dash 1 bms editer 1.exe`](file:///h:/source/repos/muse%20dash%20bms%20editer/publish/personal/muse%20dash%201%20bms%20editer%201.exe) | **[1번: 개인용]** 골드 아이콘 + 에셋 100% 내장 단일 exe 패키징 |
| **[`build_release.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/build_release.bat)** | [`publish/release/muse dash 1 bms editer 2.exe`](file:///h:/source/repos/muse%20dash%20bms%20editer/publish/release/muse%20dash%201%20bms%20editer%202.exe) | **[2번: 릴리즈용]** 블루 아이콘 + 외부 `note_icons` 폴더 세트 패키징 |
| **[`build_publish.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/build_publish.bat)** | 둘 다 | 기존 산출물 삭제 후 1번과 2번을 순차적으로 클린 패키징 |
| **[`run_personal.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/run_personal.bat)** | 실행 | 1번 개인용 exe를 바로 실행 (없으면 자동 빌드 후 실행) |

---

## 7. 검증 현황 & 문서 점검 체크리스트

* **현재 버전**: v0.1.9 (`.csproj` 의 `Version`). **테스트 507개 전부 통과** (2026-11-26, `dotnet test "muse dash bms editer.slnx"`). Release 빌드 경고 0개.
  일부 "실제 차트" 테스트는 개발 PC 의 게임 폴더를 읽고, 없으면 통과로 끝납니다.
* **문서를 고칠 때 같이 볼 곳**: 새 서비스/뷰모델을 추가하면 [`docs/architecture/code_explanation.md`](docs/architecture/code_explanation.md) 의 표와 테스트 묶음 표,
  버전·테스트 수가 바뀌면 `docs/issues/known_issues.md` 머리말과 이 절을 함께 갱신하십시오 (이전에 224개로 굳어 있던 적이 있습니다).
* **점검 요청의 범위**: "문서/코드를 봐 달라"는 요청은 문서를 코드에 맞추는 일입니다. 찾은 버그는 `docs/issues/` 에 적기만 하고, 코드 수정은 먼저 확인받으십시오.

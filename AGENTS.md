# Muse Dash BMS Editor — Project Handover & Agent Guide

> [!IMPORTANT]
> **패키징 이원화 및 클린 배포 규칙**:
> 1. **개인용 (`publish/personal/`)**: `EmbedNoteIcons=true`로 모든 스프라이트가 exe 내부에 100% 임베드되어 있어, **외부 폴더 없이 내장 exe 단독 파일**만 존재합니다 (Git 미포함).
> 2. **릴리즈용/기타 (`publish/release/`)**: exe 자체에는 에셋을 내장하지 않고, **외부 `note_icons` 폴더가 함께 있는 구성**으로 동작합니다.
> 3. **퍼블리싱 전 클린 빌드 권장**: `PublishSingleFile` 캐시 잔여 문제를 방지하기 위해 배포 스크립트 실행 시 기존 산출물을 자동 정리 후 새로 패키징합니다.

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

1. **배포 바이너리 형태 이원화**:
   - **개인용 (`publish/personal/`)**: 사용자가 단일 exe만 편리하게 보관 및 실행할 수 있도록 `EmbedNoteIcons=true` 옵션으로 **모든 PNG 스프라이트를 Single-File exe 내부에 100% 임베드**합니다. 외부 `note_icons` 폴더 없이 깔끔하게 **내장 exe 단독 파일만** 유지됩니다.
   - **릴리즈/배포용 (`publish/release/`)**: 공개 배포 시 저작권 바이너리 문제를 방지하기 위해 exe 자체는 순수 바이너리(`EmbedNoteIcons=false`)로 만들고, 에셋이 필요할 경우 외부 `note_icons` 폴더를 참조하는 분리형("폴더가 있는 exe") 구조로 제공합니다.
2. **원자적 저장 및 인코딩 자동 보존**:
   - Shift_JIS(CP932), CP949, UTF-8(BOM 유무)를 감지하여 원본 인코딩 그대로 저장합니다.
   - 임시 파일(`.tmp`)에 완전히 작성한 후 교체하는 원자적 교체 및 `.bak` 백업 방식을 적용하여 크래시 발생 시 차트 파일 유실을 방지합니다.
3. **타임라인 수식 표준**:
   - 틱(tick) 기반 시간 변환 공식 `time = tick * 240 / bpm`을 [`ChartTimeline.cs`](file:///h:/source/repos/muse%20dash%20bms%20editer/muse%20dash%20bms%20editer/Services/ChartTimeline.cs) 한곳에서 통합 관리하여 변속 구간 및 변박 마디에서도 격자와 재생 위치가 1ms 단위로 일치합니다.

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

* ❌ **personal 폴더에 내장 exe와 note_icons 폴더를 함께 두는 중복 구성**:
  - personal 버전은 exe 내부에 이미 모든 리소스가 100% 임베드되어 있으므로 외부 폴더를 둘 필요가 없으며, 단일 exe만 깔끔하게 유지해야 함.
* ❌ **배포(Publish) 시 `CopyToPublishDirectory="Never"` 유지 상태에서 외부 폴더 없이 배포**:
  - `EmbedNoteIcons=false` 상태로 단일 exe만 만들면 외부 `note_icons` 폴더가 없어 기본 단색 동그라미로 폴백됨. 따라서 릴리즈/외부 폴더 버전은 반드시 `note_icons` 폴더와 함께 패키징되어야 함.
* ❌ **기존 산출물 덮어쓰기 빌드**:
  - `PublishSingleFile` 환경에서 기존 exe가 남아있는 상태로 덮어쓰기 빌드를 진행할 경우 구버전 매니페스트 캐시나 임베딩 충돌이 발생할 수 있음. 퍼블리싱 전 반드시 기존 파일 삭제 후 재패키징 필요.

---

## 6. 빌드 & 패키징 스크립트 가이드

| 스크립트 | 대상 | 설명 |
| :--- | :--- | :--- |
| **[`build_personal.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/build_personal.bat)** | [`publish/personal/`](file:///h:/source/repos/muse%20dash%20bms%20editer/publish/personal/) | **[개인용]** 에셋이 100% 내장된 **단일 `.exe`만 단독으로** 패키징 (외부 폴더 없음, Git 미포함) |
| **[`build_release.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/build_release.bat)** | [`publish/release/`](file:///h:/source/repos/muse%20dash%20bms%20editer/publish/release/) | **[릴리즈용]** 순수 바이너리 `.exe` + **외부 `note_icons` 폴더가 함께 있는** 구성으로 패키징 |
| **[`build_publish.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/build_publish.bat)** | 둘 다 | 기존 산출물 완전 삭제 후 개인용(내장 exe 단독)과 릴리즈용(폴더 분리형)을 순차적으로 패키징 |
| **[`run_personal.bat`](file:///h:/source/repos/muse%20dash%20bms%20editer/run_personal.bat)** | 실행 | 개인용 내장 exe를 바로 실행 (없으면 자동 빌드 후 실행) |

# 📚 BMS Editer 문서 보관소 (Documentation Index)

BMS Editer 프로젝트의 설계 아키텍처, 기술적 구현 원리, 게임별 모딩 연동 가이드, 기능 사양서, 이슈 트래커를 정리한 공식 문서 허브입니다.

> [!TIP]
> **에디터를 쓰러 오셨다면 이 셋부터 보십시오.**
> * 📖 [사용 설명서](guides/editor_manual.md) — 어디를 누르면 무엇이 되는지, 마우스·키보드·보조 창 전부
> * 🧯 [문제 해결](guides/troubleshooting.md) — 안 열림 · 소리 안 남 · 키가 엉뚱한 데로 감 · 게임에서 다르게 나옴
> * ✅ [게임에 넣기 전 점검표](guides/release_checklist.md) — 저장 직전에 훑는 열 줄

---

## 📁 폴더별 문서 구성 (Structure)

```
docs/
├── README.md                          # [현재 파일] 문서 전체 인덱스 및 네비게이션 가이드
├── architecture/                      # 아키텍처 및 핵심 엔진 구현 원리
│   ├── code_explanation.md            # 소스 코드 전체 구조 및 클래스/모듈별 상세 설명서
│   ├── bpm_sync_principles.md         # BPM 변경(#xxx03/#xxx08) 및 변박(#xxx02) 동기화 원리
│   └── video_ogg_principles.md        # 로우레벨 오디오/비디오 연동 및 온셋(Onset) 탐지 원리
├── guides/                            # 🎮 작업 가이드 및 게임별 모딩·차트 주입 연동 가이드
│   ├── editor_manual.md               # 📖 사용 설명서 — 화면 구성, 마우스·키보드 조작, 보조 창, 저장·되돌리기
│   ├── troubleshooting.md             # 🧯 문제 해결 — 증상별 원인과 지금 할 일 (우회법 포함)
│   ├── release_checklist.md           # ✅ 게임에 넣기 전 점검표 — 에디터에선 멀쩡한데 게임에서만 틀어지는 것
│   ├── beat_sync_workflow.md          # 파형·BPM·재생 배속으로 박자 맞추는 작업 순서 가이드
│   ├── keyboard_bms_downmix.md        # 🎹 건반 BMS(4K/5K/7K) → 뮤즈 대시 2레인 가져오기 규칙·실측
│   ├── sixtar_gate_startrail.md       # 식스타 게이트: 스타트레일 (Mono) 커스텀 차트/키음 주입 가이드
│   ├── sixtar_gate_stargazer.md       # 식스타 게이트: 스타게이저 (Il2Cpp) 메타데이터/차트 주입 가이드
│   ├── muse_dash.md                   # 뮤즈 대시 (Il2Cpp) 커스텀 채보 매핑 및 영구 보존 가이드
│   └── gunvolt_records_cychronicle.md # 건볼트 레코즈 사이크로니클 (Mono) 6레인 채보 및 플릭/페어리 가이드
├── specifications/                    # 사양 및 규격 정의서
│   └── grid_specification.md          # 마디 내부 그리드 분할 규칙 및 기본 동작 사양서
├── plans/                             # 🗺️ 착수 전에 적어 두는 계획서 (확인된 사실 / 아직 추측 구분)
│   ├── muse_dash_specialization.md    # 뮤즈 대시 전용화 계획 — 짝 규칙 실측, 단계(P0~P4), 위험
│   └── note_circle_icon.md            # 동그라미 노트 안 이미지 — 되는지·크기(1:1, 64×64)·속도 측정
└── issues/                            # 품질 관리 및 이슈 추적
    ├── known_issues.md                # 버그 해결 기록, 미해결 과제, 실물 검증 체크리스트
    ├── authoring_time.md              # ⏱️ 채보 작성 시간 경고 · 실측 기록 · 개선 후보
    └── usability_review.md            # 🧭 실사용 점검 — 코드는 맞게 도는데 쓰는 사람이 막히는 것 (U-1~U-19)
```

---

## 📑 세부 문서 요약

### 1. 시스템 구조 및 원리 (`architecture/`)
* **[code_explanation.md](architecture/code_explanation.md)**: Models, Services, ViewModels, Views/Controls 내 모든 소스 파일의 책임과 아키텍처적 데이터 흐름을 상세히 설명하는 코드 설명서입니다.
* **[bpm_sync_principles.md](architecture/bpm_sync_principles.md)**: `ChartTimeline`을 통해 마디 위치 ↔ 절대 시각 변환을 단일화하고, 변박 및 가변 BPM 환경에서 그리드와 파형, 재생 헤드를 정밀 동기화하는 수학적 원리를 다룹니다.
* **[video_ogg_principles.md](architecture/video_ogg_principles.md)**: Win32 `waveOut` 저지연 오디오 스트리밍, `NVorbis` 기반 백그라운드 디코딩, 에너지 변화율 기반 오디오 온셋(Onset) 탐지, `WebView2` 영상 타임라인 실시간 락(Lock) 알고리즘을 설명합니다.

### 2. 작업 가이드 및 게임별 모딩 연동 (`guides/`)
* **[editor_manual.md](guides/editor_manual.md)**: 📖 **사용 설명서.** 화면 구성과 격자 색의 뜻, ✏️ 선택/편집 모드, 마우스·키보드 조작 전체(방향키 이동 규칙 포함), 재생, 보조 창 네 개(🧰 검색 · 🎨 팔레트 · 📊 통계 · 🎛️ 컨트롤 패널)의 쓰임, 키음·HEADER·격자 설정, 되돌리기 범위, 열기·저장 동작을 한곳에 모았습니다. **키가 엉뚱한 곳으로 가는 경우**와 그 우회법도 적었습니다.
* **[troubleshooting.md](guides/troubleshooting.md)**: 🧯 **문제 해결.** "파일을 열지 못했습니다(Access denied)", 키음이 안 들림, `Space` 가 다른 버튼을 누름, 홀드 검사기 🔴 메시지 읽는 법, 저장 경고와 `.bak`, 게임에서만 다르게 나오는 경우를 **증상 → 원인 → 할 일** 순서로 정리했습니다. 에디터 결함인 항목은 고쳐지기 전까지의 우회법을 적었습니다.
* **[release_checklist.md](guides/release_checklist.md)**: ✅ **게임에 넣기 전 점검표.** 음원 오프셋 0 · 홀드 짝 문제 0 · 게임이 안 읽는 레인(`16` `11` `12`) · 등록되지 않은 번호 · 지상/공중 UID · `#xxx03` BPM 변화 · 키음 파일 위치 · 저장 후 다시 열어 숫자 비교까지, **에디터 안에서 어디를 보면 되는지**와 함께 열 줄로 묶었습니다.
* **[keyboard_bms_downmix.md](guides/keyboard_bms_downmix.md)**: 🎹 **건반형 BMS(4K/5K/7K)를 뮤즈 대시 두 레인(지상 `13` · 공중 `14`)으로 접는 가져오기 기능**의 사용법과 규칙입니다. 읽는 채널·롱노트를 시작·끝 노트(게임 프로파일의 키음 값·파일명)로만 읽는 규칙, 레인 배분, 무엇을 옮기고 무엇을 버리는지, 자동 생성되는 키음 정의, 실제 차트 20곡 실측(생존율 96.4% · 짝 문제 0건)을 다룹니다. **나오는 것은 리듬 뼈대이지 완성된 채보가 아닙니다.**
* **[beat_sync_workflow.md](guides/beat_sync_workflow.md)**: 고정된 파형 위에 격자를 맞추는 작업 가이드입니다. **파형만 봐서는 어긋남을 가릴 수 없을 때 재생 배속(0.1x~1.0x)을 낮춰 귀로 확인하는 방법**과, 증상별 원인 구분(음원 오프셋 / BPM 소수점 / BPM 변화·변박·`#STOP` / 온셋 신뢰도)을 다룹니다.
* **[sixtar_gate_startrail.md](guides/sixtar_gate_startrail.md)**: Unity Mono 기반의 *Sixtar Gate: STARTRAIL* (`sxtg2`)에서 BMS Editer 레인을 Solar(4K)/Lunar(5K+Gate) 모드에 매핑하고, 롱노트(`02`/`03`)와 게이트 개폐(`04`/`05`)를 주입하는 가이드입니다.
* **[sixtar_gate_stargazer.md](guides/sixtar_gate_stargazer.md)**: Il2Cpp 기반의 *Sixtar Gate: STARGAZER*에서 4방향 회전형 레인(`16, 12, 13, 11`), `#WAV` 파일명 기반 롱노트 판별, 분수 무손실 `Area/BeatInfo` 주입 가이드입니다.
* **[muse_dash.md](guides/muse_dash.md)**: Il2Cpp 기반의 *Muse Dash* 2레인(지상/공중) 구조에 맞춘 채보 매핑(`13, 14, 15, 18`), 6자리 UID 오브젝트 지정, 홀드/샌드백 자동 매칭 및 영구 보존(Archive) 가이드입니다.
* **[gunvolt_records_cychronicle.md](guides/gunvolt_records_cychronicle.md)**: Unity Mono 기반의 *GUNVOLT RECORDS Cychronicle* (`GRC2`)에서 좌/우 6레인 매핑(`16, 11, 12` vs `14, 15, 18`), 8방향 플릭(`03~0A`) 및 페어리 아크(`11~18`, `1A/1B`) 주입 가이드입니다.

### 3. 규격 및 동작 사양 (`specifications/`)
* **[grid_specification.md](specifications/grid_specification.md)**: 마디당 기본 16분할(16비트 스냅) 그리드 렌더링 규칙, 확대/축소 비율, 주요 박자선(Beat Line) 구분 로직의 명세를 정의합니다.

### 4. 착수 전 계획 (`plans/`)
* **[muse_dash_specialization.md](plans/muse_dash_specialization.md)**: 🗺️ 뮤즈 대시 전용화 계획서입니다. 모드 소스로 확인한 짝 규칙 12건(F-1~F-12), 단계별 착수 순서(P0~P4), 위험과 **이 계획이 틀릴 수 있는 지점**, 아직 안 정해진 결정(Q-1~Q-4)을 적어 둡니다. **"무엇이 확인됐고 무엇이 아직 추측인가"를 먼저 적는 문서입니다.**
* **[note_circle_icon.md](plans/note_circle_icon.md)**: 🟢 격자의 동그라미 노트 안에 종류별 이미지를 넣는 방법입니다. 헤드리스 렌더러로 실제 픽셀까지 확인했고(되는지 · 1:1 64×64 권장 · 미리 구워 두는 방식이 가장 빠름), **코드는 아직 안 고쳤습니다.**

### 5. 이슈 및 품질 관리 (`issues/`)
* **[known_issues.md](issues/known_issues.md)**: 37건의 잠재 이슈 중 33건의 해결 과정(조건 블록 보존, 키음 네이티브 믹싱, 렌더 무결성, 🎛️ 컨트롤 패널 추가 등)과 **2026-09-20에 들어간 셋(되돌리기 · 홀드 짝 검사기 · 🎹 건반 BMS 다운믹스)**, 현재 남은 과제, 실물 테스트 확인 기록을 총망라합니다.
* **[usability_review.md](issues/usability_review.md)**: 🧭 **실사용 점검 (2026-09-24).** 코드는 맞게 도는데 쓰는 사람이 막히는 것 19건을 우선순위(🥇 매일 부딪히고 고치기 쉬운 것 → 🥉 있으면 편한 것)로 정리했습니다. 헤드리스 창과 실제 파서로 재현한 두 건(`Space` 가 방금 누른 버튼으로 감 · 드라이브 맨 위 차트를 못 엶)을 포함하고, 항목마다 **지금 / 왜 문제인가 / 고칠 방향**을 코드 위치와 함께 적었습니다.
* **[authoring_time.md](issues/authoring_time.md)**: ⚠️ **이 에디터로 채보를 만들 수는 있지만 시간이 너무 오래 걸립니다.** 뮤즈 대시 340노트 = 약 4시간의 실측 기록과 시간 배분, 무엇이 시간을 먹는지에 대한 분석(노트 종류가 화면에 안 드러남 · 짝 어긋남을 게임에서만 알 수 있음 · 되돌리기 없음), 그리고 **찾고 있는 개선 후보**를 모아 둔 문서입니다. 게임별 연동 가이드를 읽기 전에 함께 보십시오.

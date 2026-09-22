# BMS Editor - 비디오 및 OGG 오디오 처리 원리 (Video & OGG Audio Principles)

이 문서는 BMS 에디터에서 배경 비디오(BGA) 프리뷰를 렌더링하고, OGG 오디오를 지연 없이 로우 레벨에서 재생 및 시각화하는 핵심 기술 원리를 설명합니다.

---

## 🎥 1. 비디오 프리뷰 (BGA Sync) 원리

비디오 프리뷰는 크로스 플랫폼 UI 프레임워크인 Avalonia UI 내에서 고성능 비디오 재생을 구현하기 위해 **Microsoft WebView2 (Chromium 기반 웹뷰)** 기술을 차용하고 있습니다.

### ① 네이티브 창 핸들 연동 및 경계 동기화
- `VideoPreviewControl`은 컨트롤이 화면(Visual Tree)에 장착되는 시점에 TopLevel 윈도우의 플랫폼 핸들(Win32 Window Handle)을 획득합니다.
- `CoreWebView2Environment`를 통해 웹뷰 컨트롤러(`CoreWebView2Controller`)를 생성한 후, 해당 윈도우 핸들에 차일드로 부착합니다.
- 레이아웃이 갱신될 때마다(`OnLayoutUpdated`), 에디터 화면상의 컨트롤 좌표를 윈도우 핸들 기준 절대 좌표로 변환하고 **디스플레이 배율(DPI Scale)**을 반영하여 웹뷰 영역의 크기 및 가시성(`Bounds`, `IsVisible`)을 실시간 동기화합니다.

### ② CORS 및 로컬 파일 보안 정책 우회 (Virtual Host mapping)
- 로컬 디바이스에 있는 대용량 비디오 파일을 웹 표준 보안 정책(CORS 등)을 우회하면서 고속으로 로드하기 위해 WebView2의 **가상 호스트 폴더 매핑 기능**을 사용합니다.
- 비디오 파일이 속한 로컬 디렉토리를 `bms-video-preview.local`이라는 가상의 오리진(Origin)으로 매핑하여, 내부적으로 `https://bms-video-preview.local/video_name.mp4` 주소로 요청하여 HTML5 비디오 플레이어에 안전하게 로딩시킵니다.

### ③ JavaScript 인터랙션 및 프레임 동기화
- 뷰모델로부터 재생 시간 업데이트가 발생할 때마다 C#에서 WebView2 엔진으로 비동기 스크립트 실행 명령(`ExecuteScriptAsync`)을 보냅니다.
- 브라우저 내부의 HTML5 `<video>` 요소의 재생 시각(`currentTime`)과 에디터 내부 재생 타이머의 절대 시각을 실시간으로 비교합니다.
- **시간 허용 오차(Tolerance)**를 비교하여 차이가 발생할 경우에만 `currentTime`을 즉시 보정하고, 재생(`play()`) 및 일시정지(`pause()`) 상태를 맞물려 완벽한 프레임 동기화를 이끌어냅니다.

---

## 🎵 2. OGG 오디오 재생 및 분석 원리

배경 오디오(OGG)는 오디오 딜레이를 최소화하고 채보 배치 씽크를 극대화하기 위해 **Vorbis 디코더 라이브러리**와 **Windows 로우 레벨 오디오 API P/Invoke**를 조합하여 작동합니다.

### ① Vorbis 디코딩 (PCM 변환)
- OGG 파일은 압축 포맷이므로, **NVorbis** 라이브러리의 `VorbisReader`를 이용하여 압축 스트림을 실시간으로 읽어들입니다.
- 디코딩 루프를 수행하여 압축된 오디오 샘플 데이터를 메모리 상에 완전한 **16비트 리니어 PCM(Pulse Code Modulation) 바이트 배열** 형태로 직접 압축 해제합니다.

### ② Windows waveOut API 직접 제어 (초저지연 재생)
- 오디오 디바이스와의 직접 통신을 위해 Windows 멀티미디어 API인 `winmm.dll`의 `waveOut` 계열 네이티브 함수들을 C# 소스 생성기(`[LibraryImport]`)를 활용해 바인딩합니다.
- **메모리 고정 (Memory Pinning)**: 가비지 컬렉터(GC)에 의해 디코딩된 PCM 바이트 버퍼의 주소가 변경되는 것을 방지하기 위해 `GCHandle.Alloc(..., GCHandleType.Pinned)`로 메모리 포인터를 단단히 고정합니다.
- **부분 버퍼 재생 (Scrubbing 지원)**: 사용자가 원하는 임의의 재생 시작 초(Seconds)를 오디오 블록 정렬 단위(`BlockAlign`)에 맞추어 버퍼 시작 오프셋 바이트로 환산합니다. 이 위치의 메모리 주소를 `WaveHeader` 구조체에 실어 `waveOutPrepareHeader` 및 `waveOutWrite`로 사운드 드라이버의 재생 큐에 즉시 밀어넣음으로써 오디오 지연(Latency)을 최소화합니다.

### ③ 다운샘플링 파형화 및 온셋(Onset) 탐지
- **파형 진폭 추출**: `OggPeakLoader`는 곡의 총 길이에 따라 초당 분석 버킷 크기(기본 초당 80개)를 자동으로 산출합니다. 그 후 각 시간 블록 단위 내에서 RMS(제곱평균제곱근) 에너지 수치와 피크(Peak) 최댓값을 계산 및 가중 평균하여 화면에 렌더링할 0.0 ~ 1.0 범위의 파형 볼륨 데이터를 구축합니다.
- **온셋(Onset) 분석**: 드럼 비트나 신스음 등 볼륨이 순간적으로 급격하게 상승하는 시점(Transients)을 잡아내기 위해 직전 볼륨 에너지 대비 순간 상승 폭을 로컬 이동 평균 가중치와 비교하는 온셋 분석 알고리즘을 사용합니다. 이를 통해 얻어진 타격 마커(Onset)들을 파형 위에 밝은 가이드 라인으로 뿌려주어, 제작자가 그리드 씽크를 잡을 때 청각적 타격을 시각적으로 쉽게 연동하도록 보장합니다.

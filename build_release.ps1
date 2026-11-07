$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " [2번: 릴리즈용] Muse Dash BMS Editor 패키징 (폴더 분리형)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$targetDir = "publish/release"
$exePath = Join-Path $targetDir "muse dash bms editer 2.exe"

# 1. 기존 폴더 내용 정리
if (Test-Path $targetDir) {
    Write-Host "[정리] 기존 릴리즈 폴더 내용 정리 중..." -ForegroundColor Gray
    Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Recurse -Force
}

# 2. 2번 릴리즈 빌드 실행 (EmbedNoteIcons=false, AssemblyName="muse dash bms editer 2")
Write-Host "[빌드] 2번 릴리즈 exe (에셋 미내장 + 2번 블루 아이콘) 패키징 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=release
if ($LASTEXITCODE -ne 0) {
    Write-Error "릴리즈용 exe 빌드 실패!"
}

# 3. 외부 note_icons 폴더 배치 ("그 폴더가 있는 exe" 구성)
$iconsSrc = "muse dash bms editer/note_icons"
$iconsDst = Join-Path $targetDir "note_icons"
if (Test-Path $iconsSrc) {
    Write-Host "[에셋] note_icons 폴더 배치 중..." -ForegroundColor Gray
    if (-not (Test-Path $iconsDst)) {
        New-Item -ItemType Directory -Path $iconsDst -Force | Out-Null
    }
    Copy-Item -Path "$iconsSrc/*" -Destination $iconsDst -Recurse -Force
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " [2번: 릴리즈용] 빌드 완료!" -ForegroundColor Green
Write-Host " 실행 파일: $exePath" -ForegroundColor Green
Write-Host " 앱 아이콘: Assets\icon_release.ico (사이언 블루 + 2번 뱃지)" -ForegroundColor Green
Write-Host " 에셋 폴더: $iconsDst (폴더 포함 버전)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

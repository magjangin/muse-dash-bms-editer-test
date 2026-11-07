$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " [개인용] Muse Dash BMS Editor 패키징 (에셋 내장)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$targetDir = "publish/personal"
$exePath = Join-Path $targetDir "muse dash bms editer.exe"

# 1. 기존 exe 삭제 권장 정책 적용 (잔여 캐시 방지)
if (Test-Path $exePath) {
    Write-Host "[정리] 기존 개인용 exe 삭제 중..." -ForegroundColor Gray
    Remove-Item $exePath -Force
}

# 2. 개인용 빌드 실행 (EmbedNoteIcons=true)
Write-Host "[빌드] 게임 에셋을 포함하여 새로 패키징 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=personal
if ($LASTEXITCODE -ne 0) {
    Write-Error "개인용 exe 빌드 실패!"
}

# 3. 보조 에셋 폴더 복사
$iconsSrc = "muse dash bms editer/note_icons"
$iconsDst = Join-Path $targetDir "note_icons"
if (Test-Path $iconsSrc) {
    if (-not (Test-Path $iconsDst)) {
        New-Item -ItemType Directory -Path $iconsDst -Force | Out-Null
    }
    Copy-Item -Path "$iconsSrc/*" -Destination $iconsDst -Recurse -Force
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " 개인용 빌드 완료!" -ForegroundColor Green
Write-Host " 실행 경로: $exePath" -ForegroundColor Green
Write-Host " (Git 추적 제외: .gitignore 적용됨)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

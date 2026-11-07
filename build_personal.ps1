$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " [개인용] Muse Dash BMS Editor 패키징 (내장 exe 단독)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$targetDir = "publish/personal"
$exePath = Join-Path $targetDir "muse dash bms editer.exe"

# 1. 기존 exe 및 잔여 파일 정리 (퍼블리싱 전 삭제 권장 정책)
if (Test-Path $targetDir) {
    Write-Host "[정리] 기존 개인용 폴더 내용 정리 중..." -ForegroundColor Gray
    Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Recurse -Force
}

# 2. 개인용 빌드 실행 (EmbedNoteIcons=true: exe 내부에 모든 에셋 100% 임베드)
Write-Host "[빌드] 에셋을 exe 내부에 영구 내장하여 패키징 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=personal
if ($LASTEXITCODE -ne 0) {
    Write-Error "개인용 exe 빌드 실패!"
}

# 개인용 폴더에는 외부 note_icons 폴더 없이 오직 내장 exe만 단독으로 둡니다.

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " 개인용 빌드 완료!" -ForegroundColor Green
Write-Host " 실행 경로: $exePath (단독 내장 exe)" -ForegroundColor Green
Write-Host " (Git 추적 제외: .gitignore 적용됨)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

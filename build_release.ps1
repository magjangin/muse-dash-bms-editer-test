$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " [깃허브 릴리즈용] Muse Dash BMS Editor 패키징 (에셋 미포함)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$targetDir = "publish/release"
$exePath = Join-Path $targetDir "muse dash bms editer.exe"

# 1. 기존 exe 삭제 권장 정책 적용 (잔여 캐시 방지)
if (Test-Path $exePath) {
    Write-Host "[정리] 기존 릴리즈용 exe 삭제 중..." -ForegroundColor Gray
    Remove-Item $exePath -Force
}

# 2. 릴리즈 빌드 실행 (EmbedNoteIcons=false)
Write-Host "[빌드] 순수 공개 배포용으로 새로 패키징 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=release
if ($LASTEXITCODE -ne 0) {
    Write-Error "릴리즈용 exe 빌드 실패!"
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " 깃허브 릴리즈용 빌드 완료!" -ForegroundColor Green
Write-Host " 배포 경로: $exePath" -ForegroundColor Green
Write-Host " GitHub Releases 첨부용으로 사용하세요." -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

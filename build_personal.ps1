$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " [1번: 개인용] Muse Dash BMS Editor 패키징 (내장 exe)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$targetDir = "publish/personal"
$exePath = Join-Path $targetDir "muse dash bms editer 1.exe"

# 1. 기존 폴더 내용 정리
if (Test-Path $targetDir) {
    Write-Host "[정리] 기존 개인용 폴더 내용 정리 중..." -ForegroundColor Gray
    Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Recurse -Force
}

# 2. 1번 개인용 빌드 실행 (EmbedNoteIcons=true, AssemblyName="muse dash bms editer 1")
Write-Host "[빌드] 1번 개인용 exe (에셋 내장 + 1번 골드 아이콘) 패키징 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=personal
if ($LASTEXITCODE -ne 0) {
    Write-Error "개인용 exe 빌드 실패!"
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " [1번: 개인용] 빌드 완료!" -ForegroundColor Green
Write-Host " 실행 파일: $exePath (단독 내장 exe)" -ForegroundColor Green
Write-Host " 앱 아이콘: Assets\icon_personal.ico (골드 앰버 + 1번 뱃지)" -ForegroundColor Green
Write-Host " (Git 추적 제외: .gitignore 적용됨)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

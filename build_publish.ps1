$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Muse Dash BMS Editor 배포 빌드 (이원화 패키징)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. 릴리즈용 (exe + note_icons 폴더 분리형)
& "$PSScriptRoot\build_release.ps1"

# 2. 개인용 (내장 exe 단독)
& "$PSScriptRoot\build_personal.ps1"

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " 전체 빌드 완료!" -ForegroundColor Green
Write-Host " 1. 개인용:  publish/personal/  (내장 exe 단독)" -ForegroundColor Green
Write-Host " 2. 릴리즈:  publish/release/   (exe + note_icons 폴더)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

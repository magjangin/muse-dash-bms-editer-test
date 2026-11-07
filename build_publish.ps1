$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Muse Dash BMS Editor 배포 빌드 (1번 개인용 / 2번 릴리즈용)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# 1. 2번 릴리즈용 (muse dash bms editer 2.exe + note_icons 폴더)
& "$PSScriptRoot\build_release.ps1"

# 2. 1번 개인용 (muse dash bms editer 1.exe 내장 단독)
& "$PSScriptRoot\build_personal.ps1"

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " 전체 빌드 완료!" -ForegroundColor Green
Write-Host " [1번 개인용]: publish/personal/muse dash bms editer 1.exe (골드 아이콘, 내장 단독)" -ForegroundColor Green
Write-Host " [2번 릴리즈]: publish/release/muse dash bms editer 2.exe  (블루 아이콘 + 폴더)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

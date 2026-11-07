$ErrorActionPreference = "Stop"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Muse Dash BMS Editor 배포 빌드 (클린 패키징)" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

# [권장] 퍼블리싱할 때는 기존 exe를 지우고 새로 패키징
$releaseExe = "publish/release/muse dash bms editer.exe"
$personalExe = "publish/personal/muse dash bms editer.exe"

if (Test-Path $releaseExe) {
    Write-Host "[정리] 기존 릴리즈 exe 삭제 중..." -ForegroundColor Gray
    Remove-Item $releaseExe -Force
}
if (Test-Path $personalExe) {
    Write-Host "[정리] 기존 개인용 exe 삭제 중..." -ForegroundColor Gray
    Remove-Item $personalExe -Force
}

# 1. 깃허브 릴리즈용 (에셋 없는 공개 배포본)
Write-Host "`n[1/2] 깃허브 릴리즈용 (에셋 없는 버전) 빌드 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=release
if ($LASTEXITCODE -ne 0) {
    Write-Error "깃허브 릴리즈용 빌드 실패!"
}

# 2. 개인용 exe (에셋 내장: EmbedNoteIcons=true)
Write-Host "`n[2/2] 개인용 exe (에셋 내장 버전) 빌드 중..." -ForegroundColor Yellow
dotnet publish "muse dash bms editer/muse dash bms editer.csproj" -p:PublishProfile=personal
if ($LASTEXITCODE -ne 0) {
    Write-Error "개인용 exe 빌드 실패!"
}

# 보조: 개인용 폴더에 note_icons 폴더도 복사
$iconsSrc = "muse dash bms editer/note_icons"
$iconsDst = "publish/personal/note_icons"
if (Test-Path $iconsSrc) {
    Write-Host "`n[보조] 개인용 폴더에 note_icons 복사 중..." -ForegroundColor Gray
    if (-not (Test-Path $iconsDst)) {
        New-Item -ItemType Directory -Path $iconsDst -Force | Out-Null
    }
    Copy-Item -Path "$iconsSrc/*" -Destination $iconsDst -Recurse -Force
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " 빌드 완료!" -ForegroundColor Green
Write-Host " - 깃허브 릴리즈용 폴더: publish/release/" -ForegroundColor Green
Write-Host " - 개인용 exe 폴더:     publish/personal/ (Git 미포함)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green

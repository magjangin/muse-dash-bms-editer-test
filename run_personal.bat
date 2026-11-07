@echo off
set "EXE=%~dp0publish\personal\muse dash bms editer.exe"
if not exist "%EXE%" (
    echo 개인용 exe가 없습니다. 먼저 빌드를 진행합니다...
    call "%~dp0build_personal.bat"
)
start "" "%EXE%" %*

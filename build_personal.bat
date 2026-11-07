@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_personal.ps1"
if errorlevel 1 pause

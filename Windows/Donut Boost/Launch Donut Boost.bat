@echo off
setlocal
cd /d "%~dp0"

set "TARGET_EXE="
if exist "%~dp0DonutBoost.exe" set "TARGET_EXE=%~dp0DonutBoost.exe"
if not defined TARGET_EXE if exist "%~dp0publish\build-out\DonutBoost.exe" set "TARGET_EXE=%~dp0publish\build-out\DonutBoost.exe"
if not defined TARGET_EXE if exist "%~dp0publish\win-x64\DonutBoost.exe" set "TARGET_EXE=%~dp0publish\win-x64\DonutBoost.exe"
if not defined TARGET_EXE if exist "%~dp0bin\Release\net8.0-windows\DonutBoost.exe" set "TARGET_EXE=%~dp0bin\Release\net8.0-windows\DonutBoost.exe"

if defined TARGET_EXE (
    powershell -NoProfile -Command "Start-Process '%TARGET_EXE%' -Verb RunAs"
    exit /b 0
)

set "TARGET_DLL="
if exist "%~dp0DonutBoost.dll" set "TARGET_DLL=%~dp0DonutBoost.dll"
if not defined TARGET_DLL if exist "%~dp0publish\build-out\DonutBoost.dll" set "TARGET_DLL=%~dp0publish\build-out\DonutBoost.dll"
if not defined TARGET_DLL if exist "%~dp0publish\win-x64\DonutBoost.dll" set "TARGET_DLL=%~dp0publish\win-x64\DonutBoost.dll"

if defined TARGET_DLL (
    powershell -NoProfile -Command "Start-Process 'dotnet' -ArgumentList '""%TARGET_DLL%""' -Verb RunAs"
    exit /b 0
)

echo [ERROR] Could not locate DonutBoost.exe or DonutBoost.dll
pause

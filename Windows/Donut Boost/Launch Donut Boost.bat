@echo off
setlocal
cd /d "%~dp0"

set "TARGET_EXE="
if exist "%~dp0publish\current\DonutBoost.exe" set "TARGET_EXE=%~dp0publish\current\DonutBoost.exe"

if defined TARGET_EXE (
    start "" "%TARGET_EXE%"
    exit /b 0
)

set "TARGET_DLL="
if exist "%~dp0publish\current\DonutBoost.dll" set "TARGET_DLL=%~dp0publish\current\DonutBoost.dll"

if defined TARGET_DLL (
    start "" dotnet "%TARGET_DLL%"
    exit /b 0
)

echo [ERROR] Latest Donut Boost files are missing. Reinstall Donut Boost from the official website.
pause

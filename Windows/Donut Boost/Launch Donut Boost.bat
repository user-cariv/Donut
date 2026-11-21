@echo off
cd /d "%~dp0publish\win-x64"
if exist "DonutBoost.exe" (
    start "" "DonutBoost.exe"
) else (
    dotnet "%~dp0publish\win-x64\DonutBoost.dll"
)

@echo off
start "Donut Boost removal" powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall Donut Boost.ps1"
exit /b

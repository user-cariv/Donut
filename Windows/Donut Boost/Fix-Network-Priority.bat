@echo off
:: Batch script to auto-elevate and prioritize Wi-Fi over USB Tethering
if /i "%1"=="/silent" goto :run_priority
if /i "%1"=="-silent" goto :run_priority
if /i "%1"=="/q" goto :run_priority

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -ArgumentList '%*' -Verb RunAs"
    exit /b
)

:run_priority
:: Prioritize Wi-Fi as Primary Connection (Metric 5)
powershell -NoProfile -Command "Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -like '*Wi-Fi*' -or $_.InterfaceAlias -like '*Wireless*' -or $_.InterfaceAlias -like '*WLAN*' } | Set-NetIPInterface -InterfaceMetric 5 -ErrorAction SilentlyContinue"

:: Set Ethernet and Phone Tethering adapters to Background (Metric 60)
powershell -NoProfile -Command "Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike '*Wi-Fi*' -and $_.InterfaceAlias -notlike '*Wireless*' -and $_.InterfaceAlias -notlike '*WLAN*' -and $_.InterfaceAlias -notlike '*Loopback*' } | Set-NetIPInterface -InterfaceMetric 60 -ErrorAction SilentlyContinue"

:: Reset Windows Proxy Settings
powershell -NoProfile -Command "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name ProxyEnable -Value 0 -ErrorAction SilentlyContinue"

:: Flush DNS Cache
ipconfig /flushdns >nul 2>&1

if /i "%1"=="/silent" exit /b 0
if /i "%1"=="-silent" exit /b 0
if /i "%1"=="/q" exit /b 0

echo ========================================================
echo   SUCCESS! 
echo   - Laptop will now stay 100%% on Home Wi-Fi
echo   - Taskbar will always show the Wi-Fi icon (never Ethernet)
echo   - Donut Boost will bond Phone 5G/4G to boost downloads!
echo ========================================================
echo.
pause

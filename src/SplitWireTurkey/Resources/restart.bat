@echo off
taskkill /IM "SplitWire-Turkey.exe" /F >nul 2>&1
timeout /t 3 /nobreak >nul
start "" "%~dp0..\SplitWire-Turkey.exe"
exit

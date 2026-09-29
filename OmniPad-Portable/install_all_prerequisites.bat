@echo off
title OmniPad - Full Environment & Driver Installer
echo ======================================================
echo       OmniPad Full Environment & Driver Setup
echo ======================================================
echo.
echo This wizard checks and sets up:
echo   1. ViGEmBus Virtual Controller Driver (Xbox 360 / PS4)
echo   2. Microsoft .NET 8 Desktop Runtime (System Library)
echo   3. Windows Firewall Exceptions (Wi-Fi Multiplayer)
echo.
pause

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_driver.ps1" -InstallDriver -InstallDotNet

echo.
echo ======================================================
echo Environment check and setup complete!
echo Double-click OmniPadServer.exe or OmniPadServer.App.exe
echo to start gaming.
echo ======================================================
pause

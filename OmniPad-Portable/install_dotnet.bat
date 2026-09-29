@echo off
title OmniPad - Microsoft .NET 8 Desktop Runtime Installer
echo ======================================================
echo       Microsoft .NET 8 Desktop Runtime Installer
echo ======================================================
echo.
echo Checking and installing Microsoft .NET 8 Desktop Runtime...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_driver.ps1" -InstallDotNet

echo.
echo ======================================================
echo Setup completed.
echo Tip: OmniPadServer.exe can also run on ANY PC without
echo      installing .NET, because it is self-contained!
echo ======================================================
pause

@echo off
title OmniPad - ViGEmBus Driver Installer
echo ======================================================
echo             ViGEmBus Driver Installer
echo ======================================================
echo.
if exist "%~dp0ViGEmBus_Setup.exe" (
    echo Launching bundled ViGEmBus Setup...
    "%~dp0ViGEmBus_Setup.exe"
) else (
    echo Downloading official ViGEmBus installer from GitHub...
    powershell -Command "Invoke-WebRequest -Uri 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe' -OutFile '%~dp0ViGEmBus_Setup.exe'"
    "%~dp0ViGEmBus_Setup.exe"
)
echo.
echo Done. If installation completed, OmniPad will now spawn genuine Xbox/PS4 controllers.
pause

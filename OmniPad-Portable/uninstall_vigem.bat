@echo off
title OmniPad - ViGEmBus Driver Uninstaller
echo ======================================================
echo            ViGEmBus Driver Uninstaller
echo ======================================================
echo.
echo Launching official ViGEmBus uninstaller...
echo (If prompted by Windows User Account Control, click Yes)
echo.

if exist "%~dp0ViGEmBus_Setup.exe" (
    "%~dp0ViGEmBus_Setup.exe" /uninstall
) else (
    msiexec.exe /x {966606F3-2745-49E9-BF15-5C3EAA4E9077}
)

echo.
echo Done. You can verify uninstallation in Windows Installed Apps or Device Manager.
pause

@echo off
title OmniPad Legacinator
cd /d "%~dp0"
if exist "OmniPadLegacinator.exe" (
    start "" "OmniPadLegacinator.exe"
) else (
    echo [Error] OmniPadLegacinator.exe not found in this folder.
    pause
)

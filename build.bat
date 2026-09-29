@echo off
rem =========================================================================
rem OmniPad Universal Build Script Batch Wrapper
rem Forwards arguments to build.ps1 with ExecutionPolicy Bypass
rem
rem Examples:
rem   build.bat                  (Full build: Sync, PCServer, Tests, Publish, Android APK)
rem   build.bat -SkipAndroid     (PCServer, Tests, and Publish only)
rem   build.bat -SkipTests       (Skip unit tests)
rem   build.bat -Package         (Build everything and compress to OmniPad-Portable.zip)
rem =========================================================================

setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build.ps1" %*
exit /b %ERRORLEVEL%

@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1"
if errorlevel 1 (
    echo Build failed. Install the .NET 10 SDK or check the message above.
    pause
    exit /b 1
)

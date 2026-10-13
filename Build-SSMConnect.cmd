@echo off
rem Double-click to build SSMConnect. Self-elevates (needed to install .NET SDK / WiX), logs to build\build.log.
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WindowStyle Minimized"
    exit /b
)
cd /d "%~dp0"
if not exist build mkdir build
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { & .\build.ps1 *>&1 | Tee-Object -FilePath build\build.log; 'RESULT: SUCCESS' | Tee-Object -FilePath build\build.log -Append } catch { ($_ | Out-String) | Tee-Object -FilePath build\build.log -Append; 'RESULT: FAILED' | Tee-Object -FilePath build\build.log -Append }"
echo.
echo Done. See build\build.log and installer\output\
pause

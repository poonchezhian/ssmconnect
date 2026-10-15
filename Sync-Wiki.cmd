@echo off
setlocal
rem Pushes the wiki\ folder to the GitHub wiki. The first wiki page must be created once in the GitHub web UI.
set "SRC=%~dp0wiki"
set "WK=%TEMP%\ssmconnect-wiki"
set "LOG=%~dp0build\wiki-sync.log"
if not exist "%~dp0build" mkdir "%~dp0build"
call :run > "%LOG%" 2>&1
exit /b 0

:run
echo Started %DATE% %TIME%
where git || (echo RESULT: FAILED - git is not installed or not on PATH & exit /b 1)
if exist "%WK%" rmdir /s /q "%WK%"
git clone https://github.com/poonchezhian/ssmconnect.wiki.git "%WK%"
if errorlevel 1 (echo RESULT: FAILED - wiki repo not reachable. Create the first page in the GitHub Wiki tab, then run again. & exit /b 1)
robocopy "%SRC%" "%WK%" *.md /njh /njs /ndl /nc /ns /np
for /f "delims=" %%a in ('git -C "%~dp0." config user.name') do set "GN=%%a"
for /f "delims=" %%a in ('git -C "%~dp0." config user.email') do set "GE=%%a"
if not defined GN set "GN=Poonchezhian Periasamy"
if not defined GE set "GE=chezhian@indsysholdings.com"
cd /d "%WK%"
git add -A
git diff --cached --quiet
if not errorlevel 1 (echo RESULT: SUCCESS - wiki already up to date & exit /b 0)
git -c user.name="%GN%" -c user.email="%GE%" commit -m "Update wiki" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01MGLAeKPDXH7BTwYP5J1k62"
if errorlevel 1 (echo RESULT: FAILED - commit failed & exit /b 1)
git push origin HEAD
if errorlevel 1 (echo RESULT: FAILED - push rejected & exit /b 1)
echo RESULT: SUCCESS - wiki updated
exit /b 0

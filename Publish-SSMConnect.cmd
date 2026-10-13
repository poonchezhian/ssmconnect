@echo off
rem One-time / repeatable publish of D:\Projects\SSMConnect to https://github.com/poonchezhian/ssmconnect
rem Uses your own Git sign-in (a browser window may ask you to log in to GitHub the first time).
setlocal
set "REPO=D:\Projects\SSMConnect"
set "URL=https://github.com/poonchezhian/ssmconnect.git"
cd /d "%REPO%" || (echo Project folder not found: %REPO% & pause & exit /b 1)

where git >nul 2>&1
if errorlevel 1 (
    echo Git is not installed. Install it from https://git-scm.com/download/win and run this file again.
    pause & exit /b 1
)

rem the workflow file must live in .github\workflows
if exist release.workflow.yml (
    if not exist .github\workflows mkdir .github\workflows
    move /y release.workflow.yml .github\workflows\release.yml >nul
)

if not exist .git (
    git init -b main || goto :fail
    git remote add origin %URL% || goto :fail
    git fetch origin || goto :fail
    rem keep our files, adopt the existing history (the README that GitHub created)
    git reset origin/main || goto :fail
)

for /f "delims=" %%a in ('git config user.name') do set "HAVE_NAME=%%a"
if not defined HAVE_NAME git config user.name "Poonchezhian Periasamy"
for /f "delims=" %%a in ('git config user.email') do set "HAVE_MAIL=%%a"
if not defined HAVE_MAIL git config user.email "poonchezhian@gmail.com"

set /p VER=<VERSION
git add -A || goto :fail
echo.
echo Files going to GitHub:
git status --short
git commit -m "SSMConnect %VER%: source, build scripts and installer" || goto :fail
git push -u origin main || goto :fail
echo.
echo Published version %VER%. To release it:  git tag v%VER%  then  git push origin v%VER%
pause & exit /b 0

:fail
echo.
echo Something went wrong - see the messages above.
pause & exit /b 1

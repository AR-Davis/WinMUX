@echo off
setlocal EnableDelayedExpansion

:: Easy Raw Mode Test for WinMUX
:: Just run this file from CMD or PowerShell

echo =======================================
echo WinMUX Raw Mode Test
echo =======================================
set PIPE_NAME=rawtest
set SERVER="C:\Users\aaron\Projects\WinMUX\src\WinMUX.Server\bin\Release\net8.0\win-x64\publish\WinMUX.Server.exe"
set CLIENT="C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Release\net8.0\win-x64\publish\WinMUX.CLI.exe"
set LOG=%TEMP%\winmux-rawtest-server.log

:: Step 1: Clean up stale processes
echo [1/3] Cleaning up old WinMUX processes...
taskkill /F /IM WinMUX.Server.exe >nul 2>nul
taskkill /F /IM WinMUX.CLI.exe >nul 2>nul
timeout /t 1 /nobreak >nul

:: Step 2: Start server in background
echo [2/3] Starting WinMUX Server (pipe: %PIPE_NAME%)...
start /B "" %SERVER% %PIPE_NAME% cmd.exe > %LOG% 2>&1
timeout /t 2 /nobreak >nul

tasklist | findstr WinMUX.Server >nul
if errorlevel 1 (
    echo ERROR: Server failed to start.
    type %LOG%
    pause
    exit /b 1
)
echo      Server is running.
echo.

:: Step 3: Attach / Reattach loop
:LOOP
echo [3/3] Starting client... Use Ctrl+B then D to detach.
echo      =======================================
echo.
%CLIENT% %PIPE_NAME%
echo.
echo =======================================
echo You detached.
echo.
set /p CHOICE="Reattach to same session? (y/n): "
if /i "%CHOICE%"=="y" (
    echo.
    echo Reconnecting...
    echo.
    goto LOOP
)

:: Done — kill server and show log
echo.
echo Stopping server...
taskkill /F /IM WinMUX.Server.exe >nul 2>nul
echo.
echo --- Server Log ---
type %LOG%
echo.
pause

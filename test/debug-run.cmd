@echo off
echo [debug] Starting diagnostic...
set PIPE=wm-debug-test
set SERVER=C:\Users\aaron\Projects\WinMUX\src\WinMUX.Server\bin\Release\net8.0\win-x64\publish\WinMUX.Server.exe
set CLIENT=C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Release\net8.0\win-x64\publish\WinMUX.CLI.exe
set LOG=%TEMP%\wm-debug-run.log

taskkill /F /IM WinMUX.Server.exe >nul 2>nul
taskkill /F /IM WinMUX.CLI.exe >nul 2>nul
timeout /t 1 /nobreak >nul

echo [debug] Starting server...
start /B "" "%SERVER%" %PIPE% cmd.exe

echo [debug] Waiting 2s for server to start...
timeout /t 2 /nobreak >nul

echo [debug] Running client now (output will be in %LOG%)...
"%CLIENT%" %PIPE% > "%LOG%" 2>&1

echo.
echo [debug] Client has exited. Output captured to %LOG%
echo [debug] Showing log file now:
echo ============================================
type "%LOG%"
echo ============================================
echo.
echo [debug] Press any key to kill server and exit.
pause >nul

taskkill /F /IM WinMUX.Server.exe >nul 2>nul

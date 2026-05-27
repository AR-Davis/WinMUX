@echo off
cd /d C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Release\net8.0\win-x64\publish
echo ===== Running CLI with no args =====
WinMUX.CLI.exe > "%TEMP%\wm-cli-noargs.out" 2> "%TEMP%\wm-cli-noargs.err"
echo Exit code: %ERRORLEVEL%
type "%TEMP%\wm-cli-noargs.out"
type "%TEMP%\wm-cli-noargs.err"

echo.
echo ===== Running CLI with rawtest =====
WinMUX.CLI.exe rawtest > "%TEMP%\wm-cli-rawtest.out" 2> "%TEMP%\wm-cli-rawtest.err" & "echo Exit code: %ERRORLEVEL%"
timeout /t 2 >nul
taskkill /F /IM WinMUX.CLI.exe >nul 2>nul
echo Exit code from last run: %ERRORLEVEL%
echo --- STDOUT ---
type "%TEMP%\wm-cli-rawtest.out"
echo --- STDERR ---
type "%TEMP%\wm-cli-rawtest.err"
pause

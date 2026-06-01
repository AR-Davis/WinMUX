@echo off
REM WinMUX CLI wrapper
REM Place this in the same folder as WinMUX.CLI.exe, or add to PATH

set EXE_DIR=%~dp0
"%EXE_DIR%WinMUX.CLI.exe" %*

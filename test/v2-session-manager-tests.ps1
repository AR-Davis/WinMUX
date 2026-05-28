# WinMUX v0.2 Session Manager Test Script
# Tests: ls, new, attach, kill, daemon lifecycle

$ErrorActionPreference = "Stop"
$CLI = "$PSScriptRoot/../src/WinMUX.CLI/bin/Debug/net8.0/win-x64/WinMUX.CLI.exe"
$DAEMON = "$PSScriptRoot/../src/WinMUX.Daemon/bin/Debug/net8.0/WinMUX.Daemon.exe"

if (-not (Test-Path $CLI)) {
    Write-Error "CLI not found. Run: dotnet build"
    exit 1
}

Write-Host "═══ WinMUX v0.2 Session Manager Tests ═══" -ForegroundColor Cyan

# Test 1: Daemon status (should show not running initially)
Write-Host "`n[Test 1] Daemon status before start:" -ForegroundColor Yellow
& $CLI daemon 2>&1

# Test 2: ls with no daemon (auto-starts)
Write-Host "`n[Test 2] List sessions (triggers auto-start):" -ForegroundColor Yellow
& $CLI ls 2>&1

# Small delay for daemon to spin up
Start-Sleep -Milliseconds 500

# Test 3: Create session 'test1'
Write-Host "`n[Test 3] Create session 'test1':" -ForegroundColor Yellow
& $CLI new test1 cmd.exe 2>&1
Start-Sleep -Milliseconds 500

# Test 4: ls shows session
Write-Host "`n[Test 4] List sessions (should show test1):" -ForegroundColor Yellow
& $CLI ls 2>&1

# Test 5: Create second session
Write-Host "`n[Test 5] Create session 'test2' with PowerShell:" -ForegroundColor Yellow
$psPath = "powershell.exe"
& $CLI new test2 $psPath 2>&1
Start-Sleep -Milliseconds 500

Write-Host "`n[Test 5b] List both sessions:" -ForegroundColor Yellow
& $CLI ls 2>&1

# Test 6: Kill a session
Write-Host "`n[Test 6] Kill session 'test1':" -ForegroundColor Yellow
& $CLI kill test1 2>&1
Start-Sleep -Milliseconds 500

Write-Host "`n[Test 6b] List (test1 should be gone):" -ForegroundColor Yellow
& $CLI ls 2>&1

# Test 7: Kill remaining session
Write-Host "`n[Test 7] Kill 'test2':" -ForegroundColor Yellow
& $CLI kill test2 2>&1
Start-Sleep -Milliseconds 500

Write-Host "`n[Test 7b] List (should be empty):" -ForegroundColor Yellow
& $CLI ls 2>&1

# Test 8: Daemon still running
Write-Host "`n[Test 8] Daemon status after operations:" -ForegroundColor Yellow
& $CLI daemon status 2>&1

# Test 9: Stop daemon
Write-Host "`n[Test 9] Stop daemon:" -ForegroundColor Yellow
& $CLI daemon stop 2>&1

# Verify stopped
Write-Host "`n[Test 9b] Verify daemon stopped:" -ForegroundColor Yellow
& $CLI daemon status 2>&1

Write-Host "`n═══ All tests completed ═══" -ForegroundColor Green
Write-Host "`nTo test attach/detach manually:"
Write-Host "  1. winmux new main cmd.exe"
Write-Host "  2. winmux attach main"
Write-Host "  3. Type some commands, then press Ctrl+B, then D to detach"
Write-Host "  4. winmux attach main  (reconnects, scrollback replays)"

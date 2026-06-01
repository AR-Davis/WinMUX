# Raw Pipe I/O Test for WinMUX v0.2
# Tests the fixed duplex pipe communication

$daemon = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.Daemon\bin\Release\net8.0\WinMUX.Daemon.exe'
$cli = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Release\net8.0\win-x64\WinMUX.CLI.exe'
$stateDir = "$env:LOCALAPPDATA\WinMUX"

Write-Host "=== WinMUX Raw Pipe I/O Test ===" -ForegroundColor Cyan
Write-Host ""

# Cleanup any existing daemon
Get-Process WinMUX.Daemon -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item "$stateDir\daemon.lock" -ErrorAction SilentlyContinue

Write-Host "[1/5] Testing daemon status (should be: not running)..." -ForegroundColor Yellow
& $cli daemon status
Write-Host ""

Write-Host "[2/5] Starting daemon..." -ForegroundColor Yellow
$daemonProc = Start-Process -FilePath $daemon -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\daemon-test.out" -RedirectStandardError "$env:TEMP\daemon-test.err"
Start-Sleep -Seconds 2

Write-Host "[3/5] Testing daemon status (should be: running)..." -ForegroundColor Yellow
& $cli daemon status
Write-Host ""

Write-Host "[4/5] Testing 'winmux ls' (no sessions yet)..." -ForegroundColor Yellow
& $cli ls
Write-Host ""

Write-Host "[5/5] Creating test session..." -ForegroundColor Yellow
& $cli new test cmd.exe
Write-Host ""

Write-Host "[6/5] Listing sessions (should show 'test' session)..." -ForegroundColor Yellow
& $cli ls
Write-Host ""

Write-Host "[7/5] Killing test session..." -ForegroundColor Yellow
& $cli kill test
Write-Host ""

Write-Host "[8/5] Final status check..." -ForegroundColor Yellow
& $cli daemon status
Write-Host ""

# Cleanup
Write-Host "[Cleanup] Stopping daemon..." -ForegroundColor Yellow
& $cli daemon stop
Start-Sleep -Seconds 1

Write-Host "=== Test Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Daemon output:" -ForegroundColor Gray
Get-Content "$env:TEMP\daemon-test.out" -ErrorAction SilentlyContinue | Select-Object -First 20
Write-Host ""
Write-Host "Daemon errors:" -ForegroundColor Gray
Get-Content "$env:TEMP\daemon-test.err" -ErrorAction SilentlyContinue

Remove-Item "$env:TEMP\daemon-test.out" -ErrorAction SilentlyContinue
Remove-Item "$env:TEMP\daemon-test.err" -ErrorAction SilentlyContinue

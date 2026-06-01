# Self-Contained WinMUX End-to-End Test
# Tests all commands from the unified publish folder

$publishDir = 'C:\Users\aaron\Projects\WinMUX\publish'
$cli = Join-Path $publishDir 'WinMUX.CLI.exe'
$stateDir = "$env:LOCALAPPDATA\WinMUX"

Write-Host "=== WinMUX Self-Contained Test ===" -ForegroundColor Cyan
Write-Host "Publish Dir: $publishDir"
Write-Host ""

# Cleanup
Get-Process WinMUX.Daemon, WinMUX.Server, WinMUX.CLI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item "$stateDir\daemon.lock" -ErrorAction SilentlyContinue
Remove-Item "$stateDir\sessions.json" -ErrorAction SilentlyContinue

Write-Host "[1/6] Daemon status (should be 'not running')..." -ForegroundColor Yellow
& $cli daemon status
Write-Host ""

Write-Host "[2/6] Starting daemon (auto-start via CLI)..." -ForegroundColor Yellow
# Use 'ls' which auto-starts daemon
$env:TEMP_OUT = "$env:TEMP\daemon-test-out-$(Get-Random).log"
$env:TEMP_ERR = "$env:TEMP\daemon-test-err-$(Get-Random).log"
$daemonProc = Start-Process -FilePath $cli -ArgumentList "ls" -NoNewWindow -PassThru -RedirectStandardOutput $env:TEMP_OUT -RedirectStandardError $env:TEMP_ERR -WorkingDirectory $publishDir
Start-Sleep -Seconds 2
Stop-Process -Id $daemonProc.Id -Force -ErrorAction SilentlyContinue

# Now check status
& $cli daemon status -WorkingDirectory $publishDir
Write-Host ""

Write-Host "[3/6] Creating session 'test' with cmd.exe..." -ForegroundColor Yellow
& $cli new test cmd.exe -WorkingDirectory $publishDir
Write-Host ""
Start-Sleep -Seconds 1

Write-Host "[4/6] Listing sessions (should show 'test')..." -ForegroundColor Yellow
& $cli ls -WorkingDirectory $publishDir
Write-Host ""

Write-Host "[5/6] Killing session 'test'..." -ForegroundColor Yellow
& $cli kill test -WorkingDirectory $publishDir
Write-Host ""

Write-Host "[6/6] Final list (should be empty)..." -ForegroundColor Yellow
& $cli ls -WorkingDirectory $publishDir
Write-Host ""

Write-Host "[Cleanup] Stopping daemon..." -ForegroundColor Yellow
& $cli daemon stop -WorkingDirectory $publishDir
Write-Host ""

Write-Host "=== Test Complete ===" -ForegroundColor Green
Remove-Item $env:TEMP_OUT -ErrorAction SilentlyContinue
Remove-Item $env:TEMP_ERR -ErrorAction SilentlyContinue

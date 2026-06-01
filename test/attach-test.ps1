# WinMUX Attach Test
# Tests session attach/detach functionality

$publishDir = 'C:\Users\aaron\Projects\WinMUX\publish'
$cli = Join-Path $publishDir 'WinMUX.CLI.exe'
$stateDir = "$env:LOCALAPPDATA\WinMUX"

Write-Host "=== WinMUX Attach Test ===" -ForegroundColor Cyan
Write-Host ""

# Cleanup
Get-Process WinMUX.Daemon, WinMUX.Server, WinMUX.CLI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item "$stateDir\daemon.lock" -ErrorAction SilentlyContinue
Remove-Item "$stateDir\sessions.json" -ErrorAction SilentlyContinue

Write-Host "[1/5] Creating persistent session 'main'..." -ForegroundColor Yellow
& $cli new main cmd.exe -WorkingDirectory $publishDir
Start-Sleep -Seconds 1
Write-Host ""

Write-Host "[2/5] Verifying session exists..." -ForegroundColor Yellow
& $cli ls -WorkingDirectory $publishDir
Write-Host ""

Write-Host "[3/5] Testing attach (non-interactive mode)..." -ForegroundColor Yellow
Write-Host "        Sending 'echo ATTACH_TEST' then Ctrl+B D to detach" -ForegroundColor Gray
Write-Host ""

# Create input file: echo command + detach sequence
# Ctrl+B = 0x02 (STX), then D = 0x44
$inputBytes = [System.Text.Encoding]::UTF8.GetBytes("echo ATTACH_TEST`r`n`x02D")
$inputFile = "$env:TEMP\attach-input.bin"
[System.IO.File]::WriteAllBytes($inputFile, $inputBytes)

# Capture output
$outputFile = "$env:TEMP\attach-output-$(Get-Random).txt"
$ErrorFile = "$env:TEMP\attach-error-$(Get-Random).txt"

$attachProc = Start-Process -FilePath $cli -ArgumentList "attach","main" `
    -NoNewWindow -PassThru `
    -RedirectStandardInput $inputFile `
    -RedirectStandardOutput $outputFile `
    -RedirectStandardError $ErrorFile `
    -WorkingDirectory $publishDir

# Wait for process
Start-Sleep -Seconds 3
if (-not $attachProc.HasExited) {
    Stop-Process -Id $attachProc.Id -Force -ErrorAction SilentlyContinue
}

Write-Host "Attach process completed (or timeout)" -ForegroundColor Green
Write-Host ""

Write-Host "[4/5] Session status after attach..." -ForegroundColor Yellow
& $cli ls -WorkingDirectory $publishDir
Write-Host ""

Write-Host "[5/5] Cleanup - killing session..." -ForegroundColor Yellow
& $cli kill main -WorkingDirectory $publishDir -ErrorAction SilentlyContinue
& $cli daemon stop -WorkingDirectory $publishDir -ErrorAction SilentlyContinue
Write-Host ""

Write-Host "=== Test Output ===" -ForegroundColor Cyan
Write-Host "--- STDOUT ---" -ForegroundColor Gray
Get-Content $outputFile -ErrorAction SilentlyContinue | Select-Object -First 20
Write-Host ""
Write-Host "--- STDERR ---" -ForegroundColor Gray  
Get-Content $ErrorFile -ErrorAction SilentlyContinue
Write-Host ""

Remove-Item $inputFile -ErrorAction SilentlyContinue
Remove-Item $outputFile -ErrorAction SilentlyContinue
Remove-Item $ErrorFile -ErrorAction SilentlyContinue

Write-Host "=== Attach Test Complete ===" -ForegroundColor Green

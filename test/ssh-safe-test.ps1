# SSH-Safe WinMUX Test
# Tests daemon, session lifecycle, and state persistence
# Does NOT test interactive attach (needs real Windows console)
# Safe to run over SSH or in CI/CD

param(
    [string]$PublishDir = "..\publish",
    [switch]$Cleanup
)

$ErrorActionPreference = "Stop"
$cli = Join-Path (Resolve-Path $PublishDir) "winmux.bat"
$stateDir = "$env:LOCALAPPDATA\WinMUX"

if (-not (Test-Path $cli)) {
    Write-Error "winmux.bat not found in $PublishDir. Run .\publish.ps1 first."
    exit 1
}

Write-Host "=== WinMUX SSH-Safe Test ===" -ForegroundColor Cyan
Write-Host "CLI: $cli"
Write-Host "State: $stateDir"
Write-Host ""

# Pre-test cleanup
if ($Cleanup) {
    Write-Host "[Pre-test] Cleaning up..." -ForegroundColor Yellow
    Get-Process WinMUX.Daemon, WinMUX.Server -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Remove-Item "$stateDir\daemon.lock" -ErrorAction SilentlyContinue
    Remove-Item "$stateDir\sessions.json" -ErrorAction SilentlyContinue
}

$testsPassed = 0
$testsFailed = 0

function Test-Step {
    param($Name, [scriptblock]$Action)
    Write-Host "[TEST] $Name..." -ForegroundColor Yellow -NoNewline
    try {
        & $Action
        Write-Host " PASS" -ForegroundColor Green
        $script:testsPassed++
        return $true
    } catch {
        Write-Host " FAIL: $_" -ForegroundColor Red
        $script:testsFailed++
        return $false
    }
}

# Test 1: Daemon not running initially
Test-Step "Daemon initially stopped" {
    $output = & $cli daemon status 2>&1 | Out-String
    if ($output -notmatch "not running") { throw "Daemon should not be running" }
}

# Test 2: Create session auto-starts daemon
Test-Step "Create session auto-starts daemon" {
    $output = & $cli new test1 cmd.exe 2>&1 | Out-String
    if ($output -notmatch "Session 'test1' started") { throw "Session creation failed: $output" }
    Start-Sleep -Seconds 1
}

# Test 3: Daemon now running
Test-Step "Daemon is running after auto-start" {
    $output = & $cli daemon status 2>&1 | Out-String
    if ($output -notmatch "Daemon is running") { throw "Daemon not running: $output" }
}

# Test 4: Session exists in list
Test-Step "Session appears in list" {
    $output = & $cli ls 2>&1 | Out-String
    if ($output -notmatch "test1") { throw "test1 not in session list: $output" }
    if ($output -notmatch "running") { throw "Session not marked as running: $output" }
}

# Test 5: Session process actually exists
Test-Step "Session process exists" {
    $sessions = Get-Process WinMUX.Server -ErrorAction SilentlyContinue
    if (-not $sessions) { throw "No WinMUX.Server processes found" }
}

# Test 6: Create multiple sessions
Test-Step "Create second session" {
    $output = & $cli new test2 pwsh.exe 2>&1 | Out-String
    if ($output -notmatch "Session 'test2' started") { throw "Second session failed: $output" }
    Start-Sleep -Seconds 1
}

# Test 7: List shows multiple sessions
Test-Step "List shows multiple sessions" {
    $output = & $cli ls 2>&1 | Out-String
    if ($output -notmatch "test1" -or $output -notmatch "test2") { 
        throw "Both sessions not in list: $output" 
    }
}

# Test 8: State file exists
Test-Step "State file persisted" {
    if (-not (Test-Path "$stateDir\sessions.json")) { 
        throw "sessions.json not found" 
    }
    $content = Get-Content "$stateDir\sessions.json" -Raw
    if ($content -notmatch "test1" -or $content -notmatch "test2") {
        throw "State file missing sessions"
    }
}

# Test 9: Kill session
Test-Step "Kill session removes it" {
    $output = & $cli kill test1 2>&1 | Out-String
    if ($output -notmatch "Session 'test1' killed") { throw "Kill failed: $output" }
    Start-Sleep -Milliseconds 500
    
    $output = & $cli ls 2>&1 | Out-String
    if ($output -match "test1") { throw "test1 still in list after kill" }
}

# Test 10: Kill remaining session
Test-Step "Kill remaining session" {
    $output = & $cli kill test2 2>&1 | Out-String
    if ($output -notmatch "Session 'test2' killed") { throw "Kill failed: $output" }
}

# Test 11: List empty
Test-Step "List shows no sessions" {
    $output = & $cli ls 2>&1 | Out-String
    if ($output -notmatch "No active sessions") { throw "Expected empty list: $output" }
}

# Test 12: Stop daemon
Test-Step "Daemon stops cleanly" {
    $output = & $cli daemon stop 2>&1 | Out-String
    Start-Sleep -Milliseconds 500
    if ((Test-Path "$stateDir\daemon.lock")) { 
        throw "Lock file still exists after stop" 
    }
}

# Post-test cleanup
if ($Cleanup) {
    Write-Host ""
    Write-Host "[Post-test] Final cleanup..." -ForegroundColor Yellow
    Get-Process WinMUX.Daemon, WinMUX.Server -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item "$stateDir\daemon.lock" -ErrorAction SilentlyContinue
    Remove-Item "$stateDir\sessions.json" -ErrorAction SilentlyContinue
}

# Summary
Write-Host ""
Write-Host "=== Test Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $testsPassed" -ForegroundColor Green
Write-Host "Failed: $testsFailed" -ForegroundColor $(if ($testsFailed -gt 0) { "Red" } else { "Green" })

if ($testsFailed -gt 0) {
    exit 1
} else {
    Write-Host "All tests passed!" -ForegroundColor Green
    exit 0
}

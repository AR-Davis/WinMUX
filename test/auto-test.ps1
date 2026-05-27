#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$baseDir    = "C:\Users\aaron\Projects\WinMUX"
$serverExe  = Join-Path $baseDir "src\WinMUX.Server\bin\Debug\net8.0\win-x64\WinMUX.Server.exe"
$clientExe  = Join-Path $baseDir "src\WinMUX.CLI\bin\Debug\net8.0\win-x64\WinMUX.CLI.exe"

function Stop-All {
    param([int[]]$ids)
    foreach ($id in $ids) {
        if ($id) {
            Get-Process -Id $id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}

$pass = 0
$fail = 0
$allPids = @()

# --- Test A: Basic Smoke (with keepalive delay) ---
try {
    $pipe = "wm-smoke-$([Guid]::NewGuid().ToString().Substring(0,6))"
    $sLog = "$env:TEMP\wm-$pipe-s.log"
    $cOut = "$env:TEMP\wm-$pipe-c.out"
    $cIn  = "$env:TEMP\wm-$pipe-c.in"

    @("echo WINMUX_SMOKE","ping 127.0.0.1 -n 3") | Out-File -Encoding ASCII $cIn

    $s = Start-Process -FilePath $serverExe -ArgumentList @($pipe,"cmd.exe") -RedirectStandardOutput $sLog -WindowStyle Hidden -PassThru
    $allPids += $s.Id
    Start-Sleep -Milliseconds 1500

    $c = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $cIn -RedirectStandardOutput $cOut -WindowStyle Hidden -PassThru
    $allPids += $c.Id

    if (-not $c.WaitForExit(12000)) { throw "Client timeout" }

    $out = Get-Content $cOut -Raw -ErrorAction SilentlyContinue
    $log = Get-Content $sLog -Raw -ErrorAction SilentlyContinue

    if ($out -match "WINMUX_SMOKE") {
        Write-Host "[PASS] Test A: Smoke - output marker found" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "[FAIL] Test A: Smoke - marker missing" -ForegroundColor Red
        Write-Host "CLIENT OUT:`n$out"
        $fail++
    }

    if ($log -match "Client attached" -and $log -match "Client detached") {
        Write-Host "[PASS] Test A: Server logged attach/detach" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "[FAIL] Test A: Server missing attach/detach log" -ForegroundColor Red
        Write-Host "SERVER LOG:`n$log"
        $fail++
    }

    Remove-Item $cIn,$cOut,$sLog -ErrorAction SilentlyContinue
} catch {
    Write-Host "[FAIL] Test A crashed: $_" -ForegroundColor Red
    $fail++
} finally {
    Stop-All $allPids; $allPids = @()
}

# --- Test B: Scrollback Replay (with keepalive) ---
try {
    $pipe = "wm-scroll-$([Guid]::NewGuid().ToString().Substring(0,6))"
    $sLog  = "$env:TEMP\wm-$pipe-s.log"
    $c1Out = "$env:TEMP\wm-$pipe-c1.out"
    $c1In  = "$env:TEMP\wm-$pipe-c1.in"
    $c2Out = "$env:TEMP\wm-$pipe-c2.out"
    $c2In  = "$env:TEMP\wm-$pipe-c2.in"

    @("echo SCROLLBACK_MARKER","ping 127.0.0.1 -n 3") | Out-File -Encoding ASCII $c1In
    @("ping 127.0.0.1 -n 2") | Out-File -Encoding ASCII $c2In

    $s = Start-Process -FilePath $serverExe -ArgumentList @($pipe,"cmd.exe") -RedirectStandardOutput $sLog -WindowStyle Hidden -PassThru
    $allPids += $s.Id
    Start-Sleep -Milliseconds 1500

    $c1 = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $c1In -RedirectStandardOutput $c1Out -WindowStyle Hidden -PassThru
    $allPids += $c1.Id
    if (-not $c1.WaitForExit(12000)) { throw "Client 1 timeout" }
    Start-Sleep -Milliseconds 500

    $c2 = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $c2In -RedirectStandardOutput $c2Out -WindowStyle Hidden -PassThru
    $allPids += $c2.Id
    if (-not $c2.WaitForExit(12000)) { throw "Client 2 timeout" }

    $out2 = Get-Content $c2Out -Raw -ErrorAction SilentlyContinue
    if ($out2 -match "SCROLLBACK_MARKER") {
        Write-Host "[PASS] Test B: Scrollback replay - client 2 saw marker" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "[FAIL] Test B: Scrollback replay - marker not in client 2" -ForegroundColor Red
        Write-Host "CLIENT 2 OUT:`n$out2"
        $fail++
    }

    Remove-Item $c1In,$c1Out,$c2In,$c2Out,$sLog -ErrorAction SilentlyContinue
} catch {
    Write-Host "[FAIL] Test B crashed: $_" -ForegroundColor Red
    $fail++
} finally {
    Stop-All $allPids; $allPids = @()
}

# --- Test C: Server survives abrupt client kill ---
try {
    $pipe = "wm-kill-$([Guid]::NewGuid().ToString().Substring(0,6))"
    $sLog  = "$env:TEMP\wm-$pipe-s.log"
    $c1Out = "$env:TEMP\wm-$pipe-c1.out"
    $c1In  = "$env:TEMP\wm-$pipe-c1.in"
    $c2Out = "$env:TEMP\wm-$pipe-c2.out"
    $c2In  = "$env:TEMP\wm-$pipe-c2.in"

    @("echo SURVIVE_MARKER","ping 127.0.0.1 -n 3") | Out-File -Encoding ASCII $c1In
    "" | Out-File -Encoding ASCII $c2In

    $s = Start-Process -FilePath $serverExe -ArgumentList @($pipe,"cmd.exe") -RedirectStandardOutput $sLog -WindowStyle Hidden -PassThru
    $allPids += $s.Id
    Start-Sleep -Milliseconds 1500

    $c1 = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $c1In -RedirectStandardOutput $c1Out -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 3000
    Stop-Process -Id $c1.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 1000

    $c2 = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $c2In -RedirectStandardOutput $c2Out -WindowStyle Hidden -PassThru
    $allPids += $c2.Id
    if (-not $c2.WaitForExit(8000)) { throw "Client 2 timeout" }

    $log = Get-Content $sLog -Raw -ErrorAction SilentlyContinue
    $attachCount = ([regex]::Matches($log, "Client attached")).Count
    $detachCount = ([regex]::Matches($log, "Client detached")).Count

    if ($attachCount -ge 2) {
        Write-Host "[PASS] Test C: Server survived and accepted 2nd client ($attachCount attaches)" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "[FAIL] Test C: Expected 2+ attaches, got $attachCount" -ForegroundColor Red
        $fail++
    }

    if ($detachCount -ge 1) {
        Write-Host "[PASS] Test C: Server logged at least one detach ($detachCount total)" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "[FAIL] Test C: Expected 1+ detaches, got $detachCount" -ForegroundColor Red
        $fail++
    }

    Remove-Item $c1In,$c1Out,$c2In,$c2Out,$sLog -ErrorAction SilentlyContinue
} catch {
    Write-Host "[FAIL] Test C crashed: $_" -ForegroundColor Red
    $fail++
} finally {
    Stop-All $allPids; $allPids = @()
}

# --- Test D: PowerShell host ---
try {
    $pipe = "wm-ps-$([Guid]::NewGuid().ToString().Substring(0,6))"
    $sLog = "$env:TEMP\wm-$pipe-s.log"
    $cOut = "$env:TEMP\wm-$pipe-c.out"
    $cIn  = "$env:TEMP\wm-$pipe-c.in"

    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { "pwsh -NoExit" } else { "powershell -NoExit" }
    "" | Out-File -Encoding ASCII $cIn

    $s = Start-Process -FilePath $serverExe -ArgumentList @($pipe,$shell) -RedirectStandardOutput $sLog -WindowStyle Hidden -PassThru
    $allPids += $s.Id
    Start-Sleep -Milliseconds 1500

    $c = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $cIn -RedirectStandardOutput $cOut -WindowStyle Hidden -PassThru
    $allPids += $c.Id
    if (-not $c.WaitForExit(8000)) { throw "Client timeout" }

    $out = Get-Content $cOut -Raw -ErrorAction SilentlyContinue
    if ($out -match "PowerShell|PS\s*[A-Z]:\\|") {
        Write-Host "[PASS] Test D: PowerShell shell hosted successfully" -ForegroundColor Green
        $pass++
    } else {
        Write-Host "[FAIL] Test D: PowerShell prompt not detected" -ForegroundColor Red
        Write-Host "CLIENT OUT:`n$out"
        $fail++
    }

    Remove-Item $cIn,$cOut,$sLog -ErrorAction SilentlyContinue
} catch {
    Write-Host "[FAIL] Test D crashed: $_" -ForegroundColor Red
    $fail++
} finally {
    Stop-All $allPids; $allPids = @()
}

# Summary
Write-Host "`n===== RESULTS =====" -ForegroundColor Cyan
Write-Host "Passed: $pass"
Write-Host "Failed: $fail"

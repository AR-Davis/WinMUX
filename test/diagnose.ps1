$cli = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Release\net8.0\win-x64\publish\WinMUX.CLI.exe'

# Check EXE type
try {
    $bytes = [System.IO.File]::ReadAllBytes($cli)
    $subsystemOffset = [BitConverter]::ToInt32($bytes, 0x3C) + 0x5C
    $subsystem = [BitConverter]::ToUInt16($bytes, $subsystemOffset)
    $exeType = if ($subsystem -eq 2) { 'GUI (WinExe)' } elseif ($subsystem -eq 3) { 'CONSOLE' } else { "UNKNOWN ($subsystem)" }
    Write-Host "EXE subsystem: $exeType"
} catch {
    Write-Host "Failed to read EXE header: $_"
}

# Run with no args
echo "`n=== No args ==="
$proc = Start-Process -FilePath $cli -ArgumentList @() -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\wm-cli-no.out" -RedirectStandardError "$env:TEMP\wm-cli-no.err"
Write-Host "Exit: $($proc.ExitCode)"
Write-Host "STDOUT: $(Get-Content "$env:TEMP\wm-cli-no.out" -ErrorAction SilentlyContinue)"
Write-Host "STDERR: $(Get-Content "$env:TEMP\wm-cli-no.err" -ErrorAction SilentlyContinue)"

# Run with rawtest
echo "`n=== With rawtest ==="
$proc2 = Start-Process -FilePath $cli -ArgumentList 'rawtest' -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\wm-cli-raw.out" -RedirectStandardError "$env:TEMP\wm-cli-raw.err"
Start-Sleep -Seconds 3
if (-not $proc2.HasExited) { Stop-Process -Id $proc2.Id -Force }
Write-Host "HasExited: $($proc2.HasExited)   ExitCode: $($proc2.ExitCode)"
Write-Host "STDOUT: $(Get-Content "$env:TEMP\wm-cli-raw.out" -ErrorAction SilentlyContinue)"
Write-Host "STDERR: $(Get-Content "$env:TEMP\wm-cli-raw.err" -ErrorAction SilentlyContinue)"

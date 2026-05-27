$server = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.Server\bin\Release\net8.0\win-x64\publish\WinMUX.Server.exe'
$client = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Release\net8.0\win-x64\publish\WinMUX.CLI.exe'
$pipe = 'wm-e2e-' + [Guid]::NewGuid().ToString().Substring(0,6)

# Kill everything
Get-Process WinMUX.Server -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process WinMUX.CLI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Start server
echo "Starting server for pipe $pipe..."
$s = Start-Process -FilePath $server -ArgumentList $pipe,"cmd.exe" -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\wm-$pipe-s.out"
Start-Sleep -Seconds 2

# Feed the client a command + keepalive, capture everything
echo "Starting client..."
@("echo END2END_MARKER","ping 127.0.0.1 -n 2") | Out-File -Encoding ASCII "$env:TEMP\wm-$pipe-c.in"

$c = Start-Process -FilePath $client -ArgumentList $pipe -NoNewWindow -PassThru -RedirectStandardInput "$env:TEMP\wm-$pipe-c.in" -RedirectStandardOutput "$env:TEMP\wm-$pipe-c.out" -RedirectStandardError "$env:TEMP\wm-$pipe-c.err"
Start-Sleep -Seconds 4

if (-not $c.HasExited) {
    Stop-Process -Id $c.Id -Force
}

$cout = Get-Content "$env:TEMP\wm-$pipe-c.out" -Raw -ErrorAction SilentlyContinue
$sout = Get-Content "$env:TEMP\wm-$pipe-s.out" -Raw -ErrorAction SilentlyContinue

if ($cout -match 'END2END_MARKER') {
    echo "[PASS] Found END2END_MARKER in client output"
} else {
    echo "[FAIL] Marker missing"
    echo "--- CLIENT STDOUT ---"
    echo $cout
    echo "--- CLIENT STDERR ---"
    echo (Get-Content "$env:TEMP\wm-$pipe-c.err" -Raw -ErrorAction SilentlyContinue)
    echo "--- SERVER STDOUT ---"
    echo $sout
}

# Reattach test
Start-Sleep -Seconds 1
@("ping 127.0.0.1 -n 1") | Out-File -Encoding ASCII "$env:TEMP\wm-$pipe-c2.in"
$c2 = Start-Process -FilePath $client -ArgumentList $pipe -NoNewWindow -PassThru -RedirectStandardInput "$env:TEMP\wm-$pipe-c2.in" -RedirectStandardOutput "$env:TEMP\wm-$pipe-c2.out"
Start-Sleep -Seconds 2
if (-not $c2.HasExited) { Stop-Process -Id $c2.Id -Force }

$c2out = Get-Content "$env:TEMP\wm-$pipe-c2.out" -Raw -ErrorAction SilentlyContinue
if ($c2out -match 'END2END_MARKER') {
    echo "[PASS] Scrollback replay: reattach saw prior marker"
} else {
    echo "[FAIL] Scrollback replay missing"
    echo $c2out
}

Stop-Process -Id $s.Id -Force -ErrorAction SilentlyContinue
Remove-Item "$env:TEMP\wm-$pipe-*" -ErrorAction SilentlyContinue

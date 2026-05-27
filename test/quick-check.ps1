$pipe = 'wm-qc-' + [Guid]::NewGuid().ToString().Substring(0,6)
$serverExe = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.Server\bin\Debug\net8.0\win-x64\WinMUX.Server.exe'
$clientExe = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.CLI\bin\Debug\net8.0\win-x64\WinMUX.CLI.exe'

$log = "$env:TEMP\wm-$pipe-s.log"
$out = "$env:TEMP\wm-$pipe-c.out"
$in  = "$env:TEMP\wm-$pipe-c.in"

"echo WINMUX_CHECK"  | Out-File -Encoding ASCII $in
"ping 127.0.0.1 -n 3 >nul" | Add-Content -Encoding ASCII $in

$s = Start-Process -FilePath $serverExe -ArgumentList @($pipe,'cmd.exe') -RedirectStandardOutput $log -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 1500

$c = Start-Process -FilePath $clientExe -ArgumentList $pipe -RedirectStandardInput $in -RedirectStandardOutput $out -WindowStyle Hidden -PassThru
if (-not $c.WaitForExit(15000)) {
    Write-Host "Client timeout — force killing"
    Stop-Process -Id $c.Id -Force
}

$content = Get-Content $out -Raw
$logContent = Get-Content $log -Raw

if ($content -match 'WINMUX_CHECK') {
    Write-Host "[PASS] Found output marker in client" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Marker missing" -ForegroundColor Red
    Write-Host "--- CLIENT OUT ---"
    Write-Host $content
    Write-Host "--- SERVER LOG ---"
    Write-Host $logContent
}

Stop-Process -Id $s.Id -Force -ErrorAction SilentlyContinue
Remove-Item $in, $out, $log -ErrorAction SilentlyContinue

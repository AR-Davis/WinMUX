$pipe = 'wm-harness-' + [Guid]::NewGuid().ToString().Substring(0,6)
$serverExe = 'C:\Users\aaron\Projects\WinMUX\src\WinMUX.Server\bin\Debug\net8.0\win-x64\WinMUX.Server.exe'
$harness   = 'C:\Users\aaron\Projects\WinMUX\test\bin\Debug\net8.0\win-x64\TestHarness.exe'

$sLog = "$env:TEMP\wm-$pipe-s.log"

$s = Start-Process -FilePath $serverExe -ArgumentList @($pipe,'cmd.exe') -RedirectStandardOutput $sLog -WindowStyle Hidden -PassThru
Start-Sleep -Milliseconds 1500

Write-Host "Running harness against pipe $pipe ..."
& $harness $pipe
$exitCode = $LASTEXITCODE

Write-Host "Harness exit code: $exitCode"
Write-Host "Server log:"
Get-Content $sLog -ErrorAction SilentlyContinue

Stop-Process -Id $s.Id -Force -ErrorAction SilentlyContinue
Remove-Item $sLog -ErrorAction SilentlyContinue

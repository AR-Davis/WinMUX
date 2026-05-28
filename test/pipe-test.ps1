# Quick Test: Pipe connectivity test
\$pipeName = "WinMUX-control"

try {
    # Try to connect as client
    \$client = [System.IO.Pipes.NamedPipeClientStream]::new(".", \$pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    \$client.Connect(1000)
    echo "Connected successfully!"
    \$client.Close()
} catch {
    echo "Connection failed: \$_.Exception.Message"
    echo "Error code: \$(\$_.Exception.InnerException.HResult)"
}

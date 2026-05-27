using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

string pipeName = args.Length > 0 ? $"WinMUX-{args[0]}" : "WinMUX-default";
Console.WriteLine($"[TestHarness] Connecting to {pipeName}...");

using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
await client.ConnectAsync(5000);
Console.WriteLine("[TestHarness] Connected.");

var cts = new CancellationTokenSource();
var output = new StringBuilder();

var readTask = Task.Run(async () =>
{
    byte[] buf = new byte[4096];
    try
    {
        while (true)
        {
            int n = await client.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token);
            if (n == 0) break;
            output.Append(Encoding.UTF8.GetString(buf, 0, n));
        }
    }
    catch (OperationCanceledException) { }
    catch (IOException) { /* pipe closed */ }
});

// Send commands
byte[] cmd1 = Encoding.UTF8.GetBytes("echo HARNESSTEST\r\n");
await client.WriteAsync(cmd1, 0, cmd1.Length, cts.Token);
await client.FlushAsync(cts.Token);
Console.WriteLine("[TestHarness] Sent command A. Sleeping 2s...");
await Task.Delay(2000, cts.Token);

byte[] cmd2 = Encoding.UTF8.GetBytes("cd .\r\n");
await client.WriteAsync(cmd2, 0, cmd2.Length, cts.Token);
await client.FlushAsync(cts.Token);
Console.WriteLine("[TestHarness] Sent command B. Sleeping 500ms...");
await Task.Delay(500, cts.Token);

cts.Cancel();
try { client.Dispose(); } catch { }

await Task.WhenAny(readTask, Task.Delay(3000));

string result = output.ToString();
Console.WriteLine("[TestHarness] Output captured:");
Console.WriteLine(result);

if (result.Contains("HARNESSTEST"))
{
    Console.WriteLine("[PASS] Marker found.");
    Environment.Exit(0);
}
else
{
    Console.WriteLine("[FAIL] Marker missing.");
    Environment.Exit(1);
}

using System.IO.Pipes;
using System.Text;
using WinMUX.Core;

string pipeName = args.Length > 0 ? $"WinMUX-{args[0]}" : "WinMUX-default";
string shell = args.Length > 1 ? args[1] : "cmd.exe";

Console.WriteLine($"╔══════════════════════════════════════╗");
Console.WriteLine($"║         WinMUX Server v0.1           ║");
Console.WriteLine($"╠══════════════════════════════════════╣");
Console.WriteLine($"║ Pipe:  {pipeName,-26} ║");
Console.WriteLine($"║ Shell: {shell,-26} ║");
Console.WriteLine($"╚══════════════════════════════════════╝");
Console.WriteLine("Ctrl+C to kill server and session.");
Console.WriteLine();

using var session = new Session(shell, 120, 30);
session.Start();
Console.WriteLine($"[Session] Started PID {session.ProcessId} on ConPTY.");

// Scrollback buffer: keep last ~100KB of output for replay on reattach
var scrollback = new MemoryStream();
var scrollbackLock = new object();

// Current active channel writer for live clients. Null when nobody attached.
System.Threading.Channels.ChannelWriter<byte[]?>? liveWriter = null;
var writerLock = new object();

// Background pump: read from ConPTY output and route to both scrollback and any live client
_ = Task.Run(() =>
{
    if (session.OutputStream is null) return;
    byte[] buf = new byte[4096];
    try
    {
        while (true)
        {
            int read = session.OutputStream.Read(buf, 0, buf.Length);
            if (read == 0) break;

            // Copy to a new array for safe handoff
            byte[] payload = new byte[read];
            Array.Copy(buf, payload, read);

            // Append to scrollback (trim if too large)
            lock (scrollbackLock)
            {
                scrollback.Write(payload, 0, read);
                if (scrollback.Length > 100 * 1024)
                {
                    long keep = 50 * 1024;
                    byte[] temp = new byte[keep];
                    scrollback.Position = scrollback.Length - keep;
                    scrollback.Read(temp, 0, (int)keep);
                    scrollback.SetLength(0);
                    scrollback.Write(temp, 0, temp.Length);
                }
            }

            // Forward to live client (if any)
            lock (writerLock)
            {
                liveWriter?.TryWrite(payload);
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Pump] {ex.Message}");
    }
    finally
    {
        lock (writerLock) { liveWriter?.TryComplete(); }
        Console.WriteLine("[Pump] ConPTY output stream ended. Session likely died.");
    }
});

// Accept loop: one client at a time
while (true)
{
    using var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    Console.WriteLine("[Server] Waiting for client attach...");
    await pipe.WaitForConnectionAsync();
    Console.WriteLine("[Server] Client attached.");

    var cts = new CancellationTokenSource();
    var channel = System.Threading.Channels.Channel.CreateUnbounded<byte[]?>();

    lock (writerLock)
    {
        liveWriter = channel.Writer;
    }

    // TODO(v0.5): True scrollback replay requires a per-pane VT screen buffer.
    // Raw byte replay can corrupt cursor position / colors for complex apps (vim).
    // For MVP with cmd.exe, plain-text replay is acceptable.
    byte[] history;
    lock (scrollbackLock)
    {
        history = scrollback.ToArray();
    }

    if (history.Length > 0)
    {
        try
        {
            await pipe.WriteAsync(history, 0, history.Length, cts.Token);
            await pipe.FlushAsync(cts.Token);
        }
        catch { /* client gone during replay */ }
    }

    // Forward live output to client
    var forwardTask = Task.Run(async () =>
    {
        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(cts.Token))
            {
                if (chunk is null) continue;
                await pipe.WriteAsync(chunk, 0, chunk.Length, cts.Token);
                await pipe.FlushAsync(cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* pipe broken */ }
    });

    // Read client keyboard input and forward to ConPTY input
    var inputTask = Task.Run(async () =>
    {
        if (session.InputStream is null) return;
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                int read = await pipe.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (read == 0) break; // client disconnected gracefully
                await session.InputStream.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                await session.InputStream.FlushAsync(cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* pipe broken */ }
    });

    // Wait for either direction to die (disconnect)
    await Task.WhenAny(forwardTask, inputTask);

    cts.Cancel();
    lock (writerLock)
    {
        if (liveWriter == channel.Writer)
            liveWriter = null;
    }
    channel.Writer.TryComplete();

    try { await forwardTask; } catch { }
    try { await inputTask; } catch { }

    Console.WriteLine("[Server] Client detached. Session persists.");
}

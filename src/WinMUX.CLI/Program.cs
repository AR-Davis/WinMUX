using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using WinMUX.Core;

if (args.Length == 0)
{
    Console.WriteLine("Usage: WinMUX.CLI <session-name>");
    return;
}

string pipeName = $"WinMUX-{args[0]}";
Console.Error.WriteLine($"[DIAG] Connecting to pipe {pipeName}...");

using var client = new NamedPipeClientStream(
    ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

Console.Error.WriteLine("[DIAG] About to ConnectAsync...");
await client.ConnectAsync();
Console.Error.WriteLine("[DIAG] Connected to pipe.");

// ---------- Detect whether we own a real Windows console ----------
var hIn  = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
var hOut = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);

Console.Error.WriteLine($"[DIAG] hIn=0x{hIn:X}, hOut=0x{hOut:X}");

bool inIsConsole  = NativeMethods.GetConsoleMode(hIn,  out uint origInMode);
bool outIsConsole = NativeMethods.GetConsoleMode(hOut, out uint origOutMode);
bool isRealConsole = inIsConsole && outIsConsole;

Console.Error.WriteLine($"[DIAG] inIsConsole={inIsConsole}, outIsConsole={outIsConsole}, isRealConsole={isRealConsole}");

Action? restoreConsole = null;

if (isRealConsole)
{
    uint rawInMode = (origInMode
                      & ~(NativeMethods.ENABLE_LINE_INPUT
                           | NativeMethods.ENABLE_ECHO_INPUT))
                      | NativeMethods.ENABLE_VIRTUAL_TERMINAL_INPUT;

    Console.Error.WriteLine($"[DIAG] Setting console input mode to 0x{rawInMode:X}...");
    if (!NativeMethods.SetConsoleMode(hIn, rawInMode))
        throw new Win32Exception(Marshal.GetLastWin32Error());
    Console.Error.WriteLine("[DIAG] Input mode set OK.");

    uint vtOutMode = origOutMode | NativeMethods.ENABLE_VIRTUAL_TERMINAL_PROCESSING;
    Console.Error.WriteLine($"[DIAG] Setting console output mode to 0x{vtOutMode:X}...");
    if (!NativeMethods.SetConsoleMode(hOut, vtOutMode))
        throw new Win32Exception(Marshal.GetLastWin32Error());
    Console.Error.WriteLine("[DIAG] Output mode set OK.");

    restoreConsole = () =>
    {
        NativeMethods.SetConsoleMode(hIn,  origInMode);
        NativeMethods.SetConsoleMode(hOut, origOutMode);
    };

    Console.Error.WriteLine("[DIAG] RAW MODE ACTIVE. Press Ctrl+B then D to detach.");
    Console.WriteLine("Connected. Raw VT mode enabled.");
    Console.WriteLine("Prefix key is Ctrl+B.  Ctrl+B then D = detach.  Ctrl+B then Ctrl+B = send literal Ctrl+B.");
}
else
{
    Console.WriteLine("[warn] No Windows console detected.");
    Console.WriteLine("[warn] Falling back to line-buffered input.");
    Console.WriteLine("Connected. Press Ctrl+D (EOF) to detach.");
}

Console.CancelKeyPress += (s, e) =>
{
    Console.Error.WriteLine("[DIAG] CancelKeyPress fired!");
    e.Cancel = true;
    try { client.Dispose(); } catch { }
};

try
{
    var stdOut = Console.OpenStandardOutput();
    Console.Error.WriteLine("[DIAG] stdout handle opened.");

    Thread? inThread = null;

    if (isRealConsole)
    {
        Console.Error.WriteLine("[DIAG] Starting raw input thread...");
        inThread = new Thread(() =>
        {
            byte[] buf = new byte[1024];
            bool prefixActive = false;
            try
            {
                Console.Error.WriteLine("[DIAG] Input thread alive, about to ReadFile...");
                while (true)
                {
                    uint nRead = 0;
                    Console.Error.WriteLine("[DIAG] Calling ReadFile on hIn...");
                    bool ok = NativeMethods.ReadFile(hIn, buf, (uint)buf.Length, out nRead, IntPtr.Zero);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Console.Error.WriteLine($"[DIAG] ReadFile failed, err={err}. Disposing client.");
                        try { client.Dispose(); } catch { }
                        break;
                    }
                    Console.Error.WriteLine($"[DIAG] ReadFile returned {nRead} bytes");

                    if (nRead == 0)
                    {
                        Console.Error.WriteLine("[DIAG] ReadFile returned 0 (EOF). Disposing client.");
                        try { client.Dispose(); } catch { }
                        break;
                    }

                    int n = (int)nRead;
                    int i = 0;
                    while (i < n)
                    {
                        byte b = buf[i];

                        if (prefixActive)
                        {
                            prefixActive = false;

                            if (b == (byte)'d' || b == (byte)'D')
                            {
                                Console.Error.WriteLine("[DIAG] Detach command (Ctrl+B D). Disposing client.");
                                try { client.Dispose(); } catch { }
                                return;
                            }

                            if (b == 0x02)
                            {
                                client.Write(new byte[] { 0x02 }, 0, 1);
                                client.Flush();
                                i++;
                                continue;
                            }

                            client.Write(new byte[] { 0x02 }, 0, 1);
                            client.Write(buf, i, 1);
                            client.Flush();
                            i++;
                        }
                        else if (b == 0x02) // Ctrl+B
                        {
                            prefixActive = true;
                            i++;
                        }
                        else
                        {
                            int start = i;
                            while (i < n && buf[i] != 0x02) i++;
                            client.Write(buf, start, i - start);
                            client.Flush();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DIAG] Input thread exception: {ex.GetType().Name}: {ex.Message}");
            }
            Console.Error.WriteLine("[DIAG] Input thread exiting.");
        })
        {
            IsBackground = true,
            Name = "winmux-input"
        };
    }
    else
    {
        inThread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    string? line = Console.ReadLine();
                    if (line is null)
                    {
                        try { client.Dispose(); } catch { }
                        break;
                    }
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(line + "\r\n");
                    client.Write(data, 0, data.Length);
                    client.Flush();
                }
            }
            catch { }
        })
        {
            IsBackground = true,
            Name = "winmux-input-fallback"
        };
    }

    inThread.Start();
    Console.Error.WriteLine("[DIAG] Input thread started. Entering output loop...");

    byte[] outBuf = new byte[4096];
    try
    {
        while (true)
        {
            Console.Error.WriteLine("[DIAG] Calling client.Read()...");
            int read = client.Read(outBuf, 0, outBuf.Length);
            Console.Error.WriteLine($"[DIAG] client.Read() returned {read}");
            if (read == 0) break;
            stdOut.Write(outBuf, 0, read);
            stdOut.Flush();
        }
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"[DIAG] Output loop IOException: {ex.Message}");
    }
    catch (ObjectDisposedException)
    {
        Console.Error.WriteLine("[DIAG] Output loop ObjectDisposedException (expected after detach).");
    }

    Console.Error.WriteLine("[DIAG] Output loop ended. Waiting for input thread...");
    if (inThread != null && inThread.IsAlive)
    {
        inThread.Join(TimeSpan.FromSeconds(2));
    }
    Console.Error.WriteLine("[DIAG] Input thread joined (or timed out).");
}
finally
{
    Console.Error.WriteLine("[DIAG] finally block: restoring console...");
    restoreConsole?.Invoke();
}

Console.Error.WriteLine("[DIAG] Program exiting normally.");
Console.WriteLine("\nDetached from session.");

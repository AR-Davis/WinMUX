# WinMUX — Code Review Package for Gordon

## Project Summary

WinMUX is a native Windows persistent terminal multiplexer, built in C# / .NET 8, using Windows ConPTY (pseudo-console) APIs. It fills the gap where WSL is unavailable or disabled by policy.

### Architecture
```
Client (WinMUX.CLI)  ←→  Named Pipe  ←→  Server (WinMUX.Server)  ←→  ConPTY ←→ child shell (cmd.exe, pwsh)
```

### Components
| Project | Role |
|---------|------|
| WinMUX.Core | ConPTY P/Invoke, Session lifecycle |
| WinMUX.Server | Headless daemon: named pipe accept loop, scrollback buffer, client forwarding |
| WinMUX.CLI | Terminal client: raw VT mode, prefix-key detach, pipe reconnect |

---

## File: WinMUX.Core/NativeMethods.cs

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMUX.Core;

public static class NativeMethods
{
    public const uint STARTF_USESTDHANDLES = 0x00000100;
    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    public const uint HANDLE_FLAG_INHERIT = 0x00000001;

    // Console modes
    public const uint ENABLE_ECHO_INPUT = 0x0004;
    public const uint ENABLE_INSERT_MODE = 0x0020;
    public const uint ENABLE_LINE_INPUT = 0x0002;
    public const uint ENABLE_MOUSE_INPUT = 0x0010;
    public const uint ENABLE_PROCESSED_INPUT = 0x0001;
    public const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    public const uint ENABLE_WINDOW_INPUT = 0x0008;
    public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;
    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
        public COORD(short x, short y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public uint nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = false)]
    public static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = false)]
    public static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = false)]
    public static extern void ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, uint dwAttributeCount, uint dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = false)]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
```

---

## File: WinMUX.Core/Session.cs

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMUX.Core;

public class Session : IDisposable
{
    private readonly string _commandLine;
    private readonly short _width;
    private readonly short _height;

    private IntPtr _hPC;
    private IntPtr _inputRead;   // owned by ConPTY (must keep alive)
    private IntPtr _outputWrite; // owned by ConPTY (must keep alive)
    private IntPtr _procHandle;
    private uint _procId;

    private FileStream? _inputStream;  // wraps inputWrite (we write to this)
    private FileStream? _outputStream; // wraps outputRead (we read from this)

    private bool _disposed;

    public Stream? InputStream => _inputStream;
    public Stream? OutputStream => _outputStream;
    public uint ProcessId => _procId;

    public Session(string commandLine, short width = 120, short height = 30)
    {
        _commandLine = commandLine;
        _width = width;
        _height = height;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Session));

        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = (uint)Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };

        // Input pipe: we write to hWrite, PTY reads from hRead
        if (!NativeMethods.CreatePipe(out _inputRead, out IntPtr inputWrite, ref sa, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!NativeMethods.SetHandleInformation(inputWrite, NativeMethods.HANDLE_FLAG_INHERIT, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // Output pipe: PTY writes to hWrite, we read from hRead
        if (!NativeMethods.CreatePipe(out IntPtr outputRead, out _outputWrite, ref sa, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!NativeMethods.SetHandleInformation(outputRead, NativeMethods.HANDLE_FLAG_INHERIT, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        int hr = NativeMethods.CreatePseudoConsole(
            new NativeMethods.COORD(_width, _height),
            _inputRead,
            _outputWrite,
            0,
            out _hPC);

        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        IntPtr attrSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        IntPtr attrList = Marshal.AllocHGlobal(attrSize);
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!NativeMethods.UpdateProcThreadAttribute(
                attrList,
                0,
                (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var si = new NativeMethods.STARTUPINFOEX
            {
                StartupInfo = new NativeMethods.STARTUPINFO 
                { 
                    cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOEX>(),
                    hStdInput = new IntPtr(-1),
                    hStdOutput = new IntPtr(-1),
                    hStdError = new IntPtr(-1),
                    dwFlags = NativeMethods.STARTF_USESTDHANDLES
                },
                lpAttributeList = attrList
            };

            var pi = new NativeMethods.PROCESS_INFORMATION();
            bool created = NativeMethods.CreateProcess(
                null,
                _commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                null,
                ref si,
                out pi);

            NativeMethods.DeleteProcThreadAttributeList(attrList);

            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _procHandle = pi.hProcess;
            _procId = pi.dwProcessId;

            NativeMethods.CloseHandle(pi.hThread);
        }
        finally
        {
            Marshal.FreeHGlobal(attrList);
        }

        var safeInputWrite = new SafeFileHandle(inputWrite, true);
        _inputStream = new FileStream(safeInputWrite, FileAccess.Write, 4096, false);

        var safeOutputRead = new SafeFileHandle(outputRead, true);
        _outputStream = new FileStream(safeOutputRead, FileAccess.Read, 4096, false);
    }

    public void Resize(short width, short height)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Session));
        if (_hPC == IntPtr.Zero) throw new InvalidOperationException("Session not started.");
        NativeMethods.ResizePseudoConsole(_hPC, new NativeMethods.COORD(width, height));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _inputStream?.Dispose(); } catch { }
        try { _outputStream?.Dispose(); } catch { }

        if (_hPC != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }

        if (_inputRead != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_inputRead);
            _inputRead = IntPtr.Zero;
        }

        if (_outputWrite != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_outputWrite);
            _outputWrite = IntPtr.Zero;
        }

        if (_procHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_procHandle);
            _procHandle = IntPtr.Zero;
        }
    }
}
```

---

## File: WinMUX.Server/Program.cs

```csharp
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

var scrollback = new MemoryStream();
var scrollbackLock = new object();

System.Threading.Channels.ChannelWriter<byte[]?>? liveWriter = null;
var writerLock = new object();

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

            byte[] payload = new byte[read];
            Array.Copy(buf, payload, read);

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
        catch { }
    }

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
        catch (IOException) { }
    });

    var inputTask = Task.Run(async () =>
    {
        if (session.InputStream is null) return;
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                int read = await pipe.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (read == 0) break;
                await session.InputStream.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                await session.InputStream.FlushAsync(cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    });

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
```

---

## File: WinMUX.CLI/Program.cs

```csharp
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinMUX.Core;

if (args.Length == 0)
{
    Console.WriteLine("Usage: WinMUX.CLI <session-name>");
    return;
}

string pipeName = $"WinMUX-{args[0]}";
Console.WriteLine($"Connecting to \\\\.\\pipe\\{pipeName} ...");

using var client = new NamedPipeClientStream(
    ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

await client.ConnectAsync();

var hIn  = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
var hOut = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);

bool inIsConsole  = NativeMethods.GetConsoleMode(hIn,  out uint origInMode);
bool outIsConsole = NativeMethods.GetConsoleMode(hOut, out uint origOutMode);
bool isRealConsole = inIsConsole && outIsConsole;

Action? restoreConsole = null;

if (isRealConsole)
{
    uint rawInMode = (origInMode
                      & ~(NativeMethods.ENABLE_LINE_INPUT
                           | NativeMethods.ENABLE_ECHO_INPUT))
                      | NativeMethods.ENABLE_VIRTUAL_TERMINAL_INPUT;

    if (!NativeMethods.SetConsoleMode(hIn, rawInMode))
        throw new Win32Exception(Marshal.GetLastWin32Error());

    uint vtOutMode = origOutMode | NativeMethods.ENABLE_VIRTUAL_TERMINAL_PROCESSING;
    if (!NativeMethods.SetConsoleMode(hOut, vtOutMode))
        throw new Win32Exception(Marshal.GetLastWin32Error());

    restoreConsole = () =>
    {
        NativeMethods.SetConsoleMode(hIn,  origInMode);
        NativeMethods.SetConsoleMode(hOut, origOutMode);
    };

    Console.WriteLine("Connected. Raw VT mode enabled.");
    Console.WriteLine("Prefix key is Ctrl+B.  Ctrl+B then D = detach.  Ctrl+B then Ctrl+B = send literal Ctrl+B.");
}
else
{
    Console.WriteLine("[warn] No Windows console detected (e.g., running inside mintty).");
    Console.WriteLine("[warn] Falling back to line-buffered input. Arrow keys / vim will NOT work.");
    Console.WriteLine("[warn] Run from Windows Terminal or CMD for full interactive support.");
    Console.WriteLine("Connected. Press Ctrl+D (EOF) to detach.");
}

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    try { client.Dispose(); } catch { }
};

try
{
    var stdOut = Console.OpenStandardOutput();

    Thread inThread;

    if (isRealConsole)
    {
        var safeIn = new SafeFileHandle(hIn, ownsHandle: false);
        var rawIn = new FileStream(safeIn, FileAccess.Read, bufferSize: 256, isAsync: false);

        inThread = new Thread(() =>
        {
            byte[] buf = new byte[1024];
            bool prefixActive = false;
            try
            {
                while (true)
                {
                    int n = rawIn.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        try { client.Dispose(); } catch { }
                        break;
                    }

                    int i = 0;
                    while (i < n)
                    {
                        byte b = buf[i];

                        if (prefixActive)
                        {
                            prefixActive = false;

                            if (b == (byte)'d' || b == (byte)'D')
                            {
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
                        else if (b == 0x02)
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
            catch { }
            finally
            {
                rawIn.Dispose();
            }
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

    byte[] outBuf = new byte[4096];
    try
    {
        while (true)
        {
            int read = client.Read(outBuf, 0, outBuf.Length);
            if (read == 0) break;
            stdOut.Write(outBuf, 0, read);
            stdOut.Flush();
        }
    }
    catch (IOException) { }
    catch (ObjectDisposedException) { }

    inThread.Join(TimeSpan.FromSeconds(2));
}
finally
{
    restoreConsole?.Invoke();
}

Console.WriteLine("\nDetached from session.");
```

---

## File: WinMUX.Server.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinMUX.Core\WinMUX.Core.csproj" />
  </ItemGroup>
</Project>
```

## File: WinMUX.CLI.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WinMUX.Core\WinMUX.Core.csproj" />
  </ItemGroup>
</Project>
```

## File: WinMUX.Core.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

---

## Design Decisions & Known Issues

### 1. Server as `WinExe`
The server must be a `WinExe` (no console window). If it's a console app, `cmd.exe` children inherit the server's console handle and write to the server's stdout instead of the ConPTY. Combined with `STARTF_USESTDHANDLES` and `INVALID_HANDLE_VALUE` on std handles, this forces the child process to use the pseudoconsole exclusively.

### 2. `STARTF_USESTDHANDLES` + `INVALID_HANDLE_VALUE`
Even with `bInheritHandles=false` and `OutputType=WinExe`, Windows can still copy the parent's stdout handle into the child PEB. Setting `hStdInput/Output/Error = INVALID_HANDLE_VALUE` with `STARTF_USESTDHANDLES` explicitly severs this path.

### 3. Single Client at a Time
The server uses `NamedPipeServerStream` with `maxNumberOfServerInstances=1`. Only one client can attach. This is acceptable for MVP but will need to change for multi-pane or multi-client scenarios.

### 4. Raw Input Mode
`ENABLE_VIRTUAL_TERMINAL_INPUT` allows arrow keys, function keys, and mouse events to be sent as VT escape sequences. However, this also causes `Console.ReadKey` to emit raw bytes and bypasses `Console.CancelKeyPress` for Ctrl+C. We intercept `Ctrl+B` prefix locally in the input thread as a tmux-style escape hatch.

### 5. Scrollback is Raw Bytes
The scrollback buffer is a `MemoryStream` of raw VT bytes. Replay dumps these bytes verbatim. For simple shells this works. For apps that manipulate cursor position (vim, htop), replay will corrupt the client's terminal state. v0.5 will need a per-pane VT screen buffer (terminal emulator).

### 6. No Session Manager Yet
Only one hardcoded session per server. Named sessions, `winmux ls`, multi-session hosting are not implemented.

---

## Questions for Gordon's Review

1. **Is there a less drastic alternative to `WinExe` for the server?** Could we use `CREATE_NO_WINDOW` on `CreateProcess` instead? What are the tradeoffs?
2. **Is `INVALID_HANDLE_VALUE` (`new IntPtr(-1)`) the correct value for `hStd*Handles` when using `STARTF_USESTDHANDLES`?** Microsoft's ConPTY sample doesn't do this — it leaves the handles at zero. We found zero doesn't work in our testing. Is there documentation on this?
3. **Should the server keep the named pipe open across client detachments?** Currently we dispose and recreate `NamedPipeServerStream` with `maxNumberOfServerInstances=1`. Is there a pattern for keeping the server stream alive and waiting on a new connection?
4. **MemoryStream scrollback — acceptable for MVP or should we use a circular byte buffer now?** If circular, any recommended .NET implementation?
5. **Is there a race condition in the server between `Task.WhenAny(forward, input)` and the scrollback+channel writer assignment?** Could `liveWriter` be stale if a new client connects before the old one's tasks have fully exited?
6. **Any concerns with `FileStream` wrapping raw console handles** in the CLI's input thread (`FileStream(new SafeFileHandle(hIn, false), ...)`) under heavy VT output load?
7. **Should we rewrite the CLI using async over the console handle, or is synchronous `rawIn.Read` fine?**
8. **Security audit**: Named pipe ACLs, process token inheritance, and any handle leaks in the `Dispose()` path — anything obvious?
9. **Can you spot any `IntPtr` arithmetic or `Marshal.AllocHGlobal` leaks** in `Session.cs` startup path on failure?
10. **Recommendations for multi-session architecture**: should sessions run in separate threads, separate processes, or a single async event loop?

---

*Prepared for Docker code review agent (Gordon)*
*Date: 2026-05-26*

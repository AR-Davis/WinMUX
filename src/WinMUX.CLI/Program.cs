using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.ComponentModel;
using WinMUX.Core;

if (args.Length == 0)
{
    ShowUsage();
    return;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "ls":
    case "list":
        await ListSessions();
        break;
    case "new":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: winmux new <name> [shell]");
            return;
        }
        await CreateSession(args[1], args.Length >= 3 ? args[2] : GetDefaultShell());
        break;
    case "attach":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: winmux attach <name>");
            return;
        }
        await AttachToSession(args[1]);
        break;
    case "kill":
    case "stop":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: winmux kill <name>");
            return;
        }
        await KillSession(args[1]);
        break;
    case "daemon":
        await HandleDaemon(args.Skip(1).ToArray());
        break;
    case "help":
    case "--help":
    case "-h":
        ShowUsage();
        break;
    default:
        Console.WriteLine($"Unknown command: {command}");
        ShowUsage();
        break;
}

static void ShowUsage()
{
    Console.WriteLine("WinMUX v0.2 - Windows Terminal Multiplexer\n");
    Console.WriteLine("USAGE:");
    Console.WriteLine("    winmux <command> [args...]\n");
    Console.WriteLine("COMMANDS:");
    Console.WriteLine("    ls, list              List active sessions");
    Console.WriteLine("    new <name> [shell]    Create a new session");
    Console.WriteLine("    attach <name>         Attach to a session");
    Console.WriteLine("    kill <name>           Kill a session");
    Console.WriteLine("    daemon [start|stop|status]  Manage the daemon\n");
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine("    winmux new main cmd.exe");
    Console.WriteLine("    winmux new dev powershell.exe");
    Console.WriteLine("    winmux attach main");
    Console.WriteLine("    winmux ls");
    Console.WriteLine("    winmux kill dev\n");
    Console.WriteLine("DAEMON CONTROL:");
    Console.WriteLine("    winmux daemon         Show daemon status");
    Console.WriteLine("    winmux daemon start   Start background daemon");
    Console.WriteLine("    winmux daemon stop    Stop running daemon");
}

static string GetDefaultShell()
{
    var psPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    if (!File.Exists(psPath))
        psPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "pwsh.exe");
    
    return File.Exists(psPath) ? psPath : "cmd.exe";
}

static async Task EnsureDaemonRunning()
{
    if (await IsDaemonRunning())
        return;

    Console.WriteLine("Starting WinMUX daemon...");
    StartDaemon();

    // Wait for daemon to initialize pipe
    await Task.Delay(1000);

    for (int i = 0; i < 30; i++)
    {
        if (await IsDaemonRunning())
            return;
        await Task.Delay(500);
    }

    Console.Error.WriteLine("Failed to start daemon");
    Environment.Exit(1);
}

static async Task<bool> IsDaemonRunning()
{
    try
    {
        using var client = new NamedPipeClientStream(".", "WinMUX-control", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(2000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        using var reader = new StreamReader(client);
        await writer.WriteLineAsync("version");
        var response = await reader.ReadLineAsync();
        return !string.IsNullOrEmpty(response);
    }
    catch
    {
        return false;
    }
}

static void StartDaemon()
{
    var daemonPath = FindDaemonExecutable();
    if (daemonPath == null)
    {
        Console.Error.WriteLine("WinMUX.Daemon.exe not found");
        Environment.Exit(1);
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = daemonPath,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(daemonPath)
    };

    Process.Start(startInfo);
}

static async Task StopDaemon()
{
    var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMUX");
    var lockFile = Path.Combine(stateDir, "daemon.lock");

    if (!File.Exists(lockFile))
    {
        Console.WriteLine("Daemon not running (no lock file)");
        return;
    }

    var pidStr = await File.ReadAllTextAsync(lockFile);
    if (!int.TryParse(pidStr, out var pid))
    {
        Console.Error.WriteLine("Corrupt lock file");
        return;
    }

    try
    {
        var proc = Process.GetProcessById(pid);
        proc.Kill();
        Console.WriteLine("Daemon stopped");
    }
    catch (ArgumentException)
    {
        Console.WriteLine("Daemon not running (stale lock file, cleaning up)");
        File.Delete(lockFile);
    }
}

static async Task HandleDaemon(string[] args)
{
    var subcommand = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

    switch (subcommand)
    {
        case "start":
            if (await IsDaemonRunning())
            {
                Console.WriteLine("Daemon already running");
                return;
            }
            StartDaemon();
            Console.WriteLine("Daemon started");
            break;
        case "stop":
            await StopDaemon();
            break;
        case "status":
        default:
            var running = await IsDaemonRunning();
            Console.WriteLine(running ? "Daemon is running" : "Daemon is not running");
            
            var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMUX");
            var sessionsFile = Path.Combine(stateDir, "sessions.json");
            if (File.Exists(sessionsFile))
            {
                var content = await File.ReadAllTextAsync(sessionsFile);
                try
                {
                    var doc = JsonNode.Parse(content);
                    if (doc?["Sessions"] is JsonArray arr)
                    {
                        Console.WriteLine($"Sessions in state file: {arr.Count}");
                    }
                }
                catch { }
            }
            break;
    }
}

static async Task ListSessions()
{
    await EnsureDaemonRunning();

    try
    {
        using var client = new NamedPipeClientStream(".", "WinMUX-control", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        using var reader = new StreamReader(client);

        await writer.WriteLineAsync("ls");
        var response = await reader.ReadLineAsync();

        if (response == "[]")
        {
            Console.WriteLine("No active sessions");
            return;
        }

        var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(response ?? "[]");
        if (sessions == null || sessions.Count == 0)
        {
            Console.WriteLine("No active sessions");
            return;
        }

        Console.WriteLine($"{"Name",-12} {"PID",-8} {"Shell",-20} {"Created",-20} Status");
        Console.WriteLine(new string('-', 70));

        foreach (var s in sessions)
        {
            bool running = IsProcessRunning(s.ProcessId);
            var status = running ? "running" : "dead";
            var shell = Path.GetFileName(s.Shell);
            var created = s.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"{s.Name,-12} {s.ProcessId,-8} {shell,-20} {created,-20} {status}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error listing sessions: {ex.Message}");
    }
}

static async Task CreateSession(string name, string shell)
{
    await EnsureDaemonRunning();

    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
    {
        Console.Error.WriteLine("Session name must be alphanumeric with - or _ only");
        return;
    }

    try
    {
        using var client = new NamedPipeClientStream(".", "WinMUX-control", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        using var reader = new StreamReader(client);

        await writer.WriteLineAsync($"new {name} {shell}");
        var response = await reader.ReadLineAsync();

        if (response?.StartsWith("ok:") == true)
        {
            var pid = response[4..].Trim();
            Console.WriteLine($"Session '{name}' started (PID {pid})");
            Console.WriteLine($"Attach with: winmux attach {name}");
        }
        else if (response?.StartsWith("error:") == true)
        {
            Console.Error.WriteLine($"Failed to create session: {response[7..]}");
        }
        else
        {
            Console.Error.WriteLine($"Unexpected response: {response}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error creating session: {ex.Message}");
    }
}

static async Task KillSession(string name)
{
    await EnsureDaemonRunning();

    try
    {
        using var client = new NamedPipeClientStream(".", "WinMUX-control", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        using var reader = new StreamReader(client);

        await writer.WriteLineAsync($"kill {name}");
        var response = await reader.ReadLineAsync();

        if (response == "ok")
        {
            Console.WriteLine($"Session '{name}' killed");
        }
        else if (response?.StartsWith("error:") == true)
        {
            Console.Error.WriteLine($"Failed to kill session: {response[7..]}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error killing session: {ex.Message}");
    }
}

static async Task AttachToSession(string name)
{
    await EnsureDaemonRunning();

    string pipeName;
    try
    {
        using var client = new NamedPipeClientStream(".", "WinMUX-control", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        using var reader = new StreamReader(client);

        await writer.WriteLineAsync($"attach-info {name}");
        var response = await reader.ReadLineAsync();

        if (response?.StartsWith("ok:") == true)
        {
            pipeName = response[4..].Trim();
        }
        else if (response?.StartsWith("error:") == true)
        {
            Console.Error.WriteLine($"Cannot attach: {response[7..]}");
            return;
        }
        else
        {
            Console.Error.WriteLine($"Unexpected response: {response}");
            return;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error querying session: {ex.Message}");
        return;
    }

    Console.Error.WriteLine($"[DIAG] Connecting to {pipeName}...");
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    try
    {
        await pipe.ConnectAsync(10000);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to connect to session: {ex.Message}");
        Console.Error.WriteLine("Session may have exited. Run 'winmux ls' to verify.");
        return;
    }

    Console.Error.WriteLine("[DIAG] Connected.");

    var hIn = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
    var hOut = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);

    bool inIsConsole = NativeMethods.GetConsoleMode(hIn, out uint origInMode);
    bool outIsConsole = NativeMethods.GetConsoleMode(hOut, out uint origOutMode);
    bool isRealConsole = inIsConsole && outIsConsole;

    Action? restoreConsole = null;

    if (isRealConsole)
    {
        uint rawInMode = (origInMode
            & ~(NativeMethods.ENABLE_LINE_INPUT | NativeMethods.ENABLE_ECHO_INPUT))
            | NativeMethods.ENABLE_VIRTUAL_TERMINAL_INPUT;

        if (!NativeMethods.SetConsoleMode(hIn, rawInMode))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        uint vtOutMode = origOutMode | NativeMethods.ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        if (!NativeMethods.SetConsoleMode(hOut, vtOutMode))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        restoreConsole = () =>
        {
            NativeMethods.SetConsoleMode(hIn, origInMode);
            NativeMethods.SetConsoleMode(hOut, origOutMode);
        };

        Console.WriteLine("Connected. Raw VT mode enabled.");
        Console.WriteLine("Prefix key is Ctrl+B.  Ctrl+B then D = detach.");
    }
    else
    {
        Console.WriteLine("[warn] No Windows console detected.");
        Console.WriteLine("[warn] Falling back to line-buffered input.");
        Console.WriteLine("Connected. Press Ctrl+D (EOF) to detach.");
    }

    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        try { pipe.Dispose(); } catch { }
    };

    try
    {
        var stdOut = Console.OpenStandardOutput();
        Thread? inThread = null;

        if (isRealConsole)
        {
            inThread = new Thread(() =>
            {
                byte[] buf = new byte[1024];
                bool prefixActive = false;
                try
                {
                    while (true)
                    {
                        uint nRead = 0;
                        bool ok = NativeMethods.ReadFile(hIn, buf, (uint)buf.Length, out nRead, IntPtr.Zero);
                        if (!ok || nRead == 0)
                        {
                            try { pipe.Dispose(); } catch { }
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
                                    try { pipe.Dispose(); } catch { }
                                    return;
                                }

                                if (b == 0x02)
                                {
                                    pipe.Write(new byte[] { 0x02 }, 0, 1);
                                    pipe.Flush();
                                    i++;
                                    continue;
                                }

                                pipe.Write(new byte[] { 0x02 }, 0, 1);
                                pipe.Write(buf, i, 1);
                                pipe.Flush();
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
                                pipe.Write(buf, start, i - start);
                                pipe.Flush();
                            }
                        }
                    }
                }
                catch { }
            })
            { IsBackground = true, Name = "winmux-input" };
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
                            try { pipe.Dispose(); } catch { }
                            break;
                        }
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(line + "\r\n");
                        pipe.Write(data, 0, data.Length);
                        pipe.Flush();
                    }
                }
                catch { }
            })
            { IsBackground = true, Name = "winmux-input-fallback" };
        }

        inThread.Start();

        byte[] outBuf = new byte[4096];
        try
        {
            while (true)
            {
                int read = pipe.Read(outBuf, 0, outBuf.Length);
                if (read == 0) break;
                stdOut.Write(outBuf, 0, read);
                stdOut.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }

        if (inThread?.IsAlive == true)
        {
            inThread.Join(TimeSpan.FromSeconds(2));
        }
    }
    finally
    {
        restoreConsole?.Invoke();
    }

    Console.WriteLine("\nDetached from session.");
}

static bool IsProcessRunning(int pid)
{
    try
    {
        var proc = Process.GetProcessById(pid);
        return !proc.HasExited;
    }
    catch
    {
        return false;
    }
}

static string? FindDaemonExecutable()
{
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        // Same directory (published)
        Path.Combine(baseDir, "WinMUX.Daemon.exe"),
        // CLI is in win-x64 subfolder:
        // From win-x64: ../ = net8.0, ../../ = Debug, ../../../ = bin, ../../../../ = WinMUX.CLI, ../../../../../ = src
        Path.Combine(baseDir, "..", "..", "..", "..", "..", "WinMUX.Daemon", "WinMUX.Daemon.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "..", "WinMUX.Daemon", "bin", "Debug", "net8.0", "WinMUX.Daemon.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "..", "WinMUX.Daemon", "bin", "Release", "net8.0", "WinMUX.Daemon.exe"),
    };

    foreach (var candidate in candidates)
    {
        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(fullPath))
            return fullPath;
    }

    // Try searching up directory tree
    var searchDir = baseDir;
    for (int i = 0; i < 8; i++)
    {
        try
        {
            var found = Directory.GetFiles(searchDir, "WinMUX.Daemon.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found != null)
                return Path.GetFullPath(found);
        }
        catch (UnauthorizedAccessException) { /* continue */ }
        
        var nextDir = Path.GetDirectoryName(searchDir);
        if (string.IsNullOrEmpty(nextDir) || nextDir == searchDir) break;
        searchDir = nextDir;
    }

    var pathEx = Path.Combine("WinMUX.Daemon.exe");
    return File.Exists(pathEx) ? pathEx : null;
}

public record SessionInfo
{
    public string Name { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string Shell { get; init; } = "cmd.exe";
    public DateTime CreatedAt { get; init; }
    public DateTime? LastAttachedAt { get; init; }
}

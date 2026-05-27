using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Text;

namespace WinMUX.Daemon;

public record SessionInfo
{
    public string Name { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string Shell { get; init; } = "cmd.exe";
    public DateTime CreatedAt { get; init; }
    public DateTime? LastAttachedAt { get; init; }
}

public class SessionState
{
    public List<SessionInfo> Sessions { get; set; } = new();
}

class Program
{
    const string ControlPipeName = "WinMUX-control";
    const int CurrentDaemonVersion = 1;

    static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinMUX");
    static string StateFile => Path.Combine(StateDir, "sessions.json");
    static string LockFile => Path.Combine(StateDir, "daemon.lock");

    static readonly Dictionary<string, Process> RunningSessions = new();
    static readonly object StateLock = new();

    static async Task Main(string[] args)
    {
        Directory.CreateDirectory(StateDir);

        // Check if another daemon is already running
        if (IsDaemonRunning())
        {
            Console.WriteLine("Daemon already running. Use 'winmux daemon' to check status.");
            Environment.Exit(1);
            return;
        }

        // Write PID lock file
        await File.WriteAllTextAsync(LockFile, Process.GetCurrentProcess().Id.ToString());

        try
        {
            // Load existing state and reconnect to orphaned sessions
            await ReconnectSessions();

            Console.WriteLine($"WinMUX Daemon v0.2 (protocol {CurrentDaemonVersion})");
            Console.WriteLine($"Control pipe: {ControlPipeName}");
            Console.WriteLine($"State file: {StateFile}");
            Console.WriteLine("Daemon ready.");

            await RunControlLoop();
        }
        finally
        {
            try { File.Delete(LockFile); } catch { }
        }
    }

    static bool IsDaemonRunning()
    {
        if (!File.Exists(LockFile)) return false;

        var pidStr = File.ReadAllText(LockFile).Trim();
        if (int.TryParse(pidStr, out var pid))
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                return proc.ProcessName.Contains("WinMUX", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                // Process doesn't exist, stale lock
                return false;
            }
        }
        return false;
    }

    static async Task ReconnectSessions()
    {
        if (!File.Exists(StateFile))
        {
            SaveState(new SessionState());
            return;
        }

        var json = await File.ReadAllTextAsync(StateFile);
        var state = JsonSerializer.Deserialize(json, typeof(SessionState), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) as SessionState ?? new SessionState();

        // Prune dead sessions - check if processes are still running
        var aliveSessions = new List<SessionInfo>();
        foreach (var session in state.Sessions)
        {
            if (IsProcessRunning(session.ProcessId))
            {
                aliveSessions.Add(session);
                // Track in memory but we don't have the Process object
                // The session pipe will work directly
            }
            else
            {
                Console.WriteLine($"Session '{session.Name}' (PID {session.ProcessId}) is dead, removing.");
            }
        }

        state.Sessions = aliveSessions;
        SaveState(state);
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

    static async Task RunControlLoop()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ControlPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync();
                _ = Task.Run(() => HandleClient(pipe));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Daemon] Accept error: {ex.Message}");
                await Task.Delay(100);
            }
        }
    }

    static async Task HandleClient(NamedPipeServerStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };

            var request = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(request)) return;

            var parts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLowerInvariant();

            var response = command switch
            {
                "version" => CurrentDaemonVersion.ToString(),
                "ls" => await ListSessions(),
                "new" => parts.Length >= 2 ? await CreateSession(parts[1], parts.Length >= 3 ? parts[2] : "cmd.exe") : "error: usage: new <name> [shell]",
                "kill" => parts.Length >= 2 ? await KillSession(parts[1]) : "error: usage: kill <name>",
                "attach-info" => parts.Length >= 2 ? await GetAttachInfo(parts[1]) : "error: usage: attach-info <name>",
                "prune" => await PruneDeadSessions(),
                _ => $"error: unknown command '{command}'. Available: version, ls, new, kill, attach-info, prune"
            };

            await writer.WriteLineAsync(response);
            pipe.Disconnect();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Daemon] Client handler error: {ex.Message}");
        }
    }

    static SessionState LoadState()
    {
        if (!File.Exists(StateFile))
            return new SessionState();

        var json = File.ReadAllText(StateFile);
        return JsonSerializer.Deserialize(json, typeof(SessionState), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) as SessionState ?? new SessionState();
    }

    static void SaveState(SessionState state)
    {
        lock (StateLock)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFile, json);
        }
    }

    static void UpdateSession(string name, Func<SessionInfo, SessionInfo> updater)
    {
        lock (StateLock)
        {
            var state = LoadState();
            var idx = state.Sessions.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                state.Sessions[idx] = updater(state.Sessions[idx]);
                SaveState(state);
            }
        }
    }

    static SessionState GetState()
    {
        lock (StateLock) { return LoadState(); }
    }

    static Task<string> ListSessions()
    {
        var state = GetState();
        if (state.Sessions.Count == 0)
            return Task.FromResult("[]");

        // Validate which are actually still running
        var alive = state.Sessions.Where(s => IsProcessRunning(s.ProcessId)).ToList();
        
        // Refresh state if we found any dead ones
        if (alive.Count != state.Sessions.Count)
        {
            lock (StateLock)
            {
                var s = LoadState();
                s.Sessions.RemoveAll(x => !alive.Any(a => a.Name.Equals(x.Name)));
                SaveState(s);
            }
        }

        var json = JsonSerializer.Serialize(alive, new JsonSerializerOptions { WriteIndented = false });
        return Task.FromResult(json);
    }

    static Task<string> CreateSession(string name, string shell)
    {
        lock (StateLock)
        {
            var state = LoadState();
            if (state.Sessions.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult($"error: session '{name}' already exists");

            // Find server executable
            var serverPath = FindServerExecutable();
            if (serverPath == null)
                return Task.FromResult("error: WinMUX.Server.exe not found");

            // Start server process
            var startInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = $"\"{name}\" \"{shell}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                var proc = Process.Start(startInfo);
                if (proc == null)
                    return Task.FromResult("error: failed to start server process");

                // Wait a moment for the server to start and report its PID
                Thread.Sleep(500);

                if (proc.HasExited)
                {
                    var stderr = proc.StandardError.ReadToEnd();
                    return Task.FromResult($"error: server process exited immediately: {stderr}");
                }

                var session = new SessionInfo
                {
                    Name = name,
                    ProcessId = proc.Id,
                    Shell = shell,
                    CreatedAt = DateTime.UtcNow
                };

                state.Sessions.Add(session);
                SaveState(state);
                RunningSessions[name] = proc;

                // Clean up tracking when process exits
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) =>
                {
                    lock (StateLock)
                    {
                        var s = LoadState();
                        s.Sessions.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        SaveState(s);
                        RunningSessions.Remove(name);
                    }
                };

                return Task.FromResult($"ok: {proc.Id}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"error: {ex.Message}");
            }
        }
    }

    static Task<string> KillSession(string name)
    {
        lock (StateLock)
        {
            var state = LoadState();
            var session = state.Sessions.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (session == null)
                return Task.FromResult($"error: session '{name}' not found");

            try
            {
                var proc = Process.GetProcessById(session.ProcessId);
                proc.Kill();
                proc.WaitForExit(5000);

                state.Sessions.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                SaveState(state);
                RunningSessions.Remove(name);

                return Task.FromResult("ok");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"error: {ex.Message}");
            }
        }
    }

    static Task<string> GetAttachInfo(string name)
    {
        var state = GetState();
        var session = state.Sessions.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (session == null)
            return Task.FromResult($"error: session '{name}' not found");

        if (!IsProcessRunning(session.ProcessId))
            return Task.FromResult("error: session process is not running");

        // Update last attached time
        UpdateSession(name, s => s with { LastAttachedAt = DateTime.UtcNow });

        var pipeName = $"WinMUX-{name}";
        return Task.FromResult($"ok: {pipeName}");
    }

    static async Task<string> PruneDeadSessions()
    {
        await ReconnectSessions();
        var state = GetState();
        return $"ok: {state.Sessions.Count} sessions active";
    }

    static string? FindServerExecutable()
    {
        // Try directory of this executable first
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "WinMUX.Server.exe"),
            Path.Combine(baseDir, "..", "WinMUX.Server", "WinMUX.Server.exe"),
            Path.Combine(baseDir, "..", "..", "WinMUX.Server", "bin", "Debug", "net8.0", "WinMUX.Server.exe"),
            Path.Combine(baseDir, "..", "..", "WinMUX.Server", "bin", "Release", "net8.0", "WinMUX.Server.exe"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Try PATH
        var pathEx = Path.Combine("WinMUX.Server.exe");
        return File.Exists(pathEx) ? pathEx : null;
    }
}

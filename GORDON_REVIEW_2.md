# WinMUX Code Review — Focused Pass
## Restrictions: ANALYSIS ONLY. Do not write code patches.

## Current State
- 6 automated tests pass end-to-end
- Core engine works: ConPTY spawn, named pipe IPC, scrollback replay, reattach, multi-session ready
- **BUT: raw console input mode fails in user's real CMD window.** Client instant-exits when reading from stdin via `ReadFile` on console handle

## Files (Current HEAD)

### CLI/Program.cs — Raw Input Problem Area
```csharp
// Problem: SetConsoleMode sets VT input mode successfully,
// but ReadFile(GetStdHandle(STD_INPUT_HANDLE), ...) returns 
// immediately with nRead=0 or error 6 (ERROR_INVALID_HANDLE)
// ONLY in a real Windows console window. Redirected stdin works fine.

uint rawInMode = (origInMode
                  & ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT))
                  | ENABLE_VIRTUAL_TERMINAL_INPUT;
NativeMethods.SetConsoleMode(hIn, rawInMode); // succeeds

// ... connect to named pipe ...

// In background thread:
uint nRead = 0;
bool ok = NativeMethods.ReadFile(hIn, buf, (uint)buf.Length, out nRead, IntPtr.Zero);
// ok=false, GetLastWin32Error() == 6 (ERROR_INVALID_HANDLE)
// OR ok=true but nRead==0
```

**Question 1:** Why does `GetStdHandle(STD_INPUT_HANDLE)` return a handle that becomes `ERROR_INVALID_HANDLE` after we call `SetConsoleMode` with `ENABLE_VIRTUAL_TERMINAL_INPUT`? Could the console subsystem be reallocating the handle? Is there a documented interaction between VT input mode and handle validity?

**Question 2:** Microsoft's ConPTY sample uses `ReadFile` on the ConPTY output pipe, not the console handle. For terminal multiplexers, what is the *documented* way to read raw keyboard input from a Windows console? Is `ReadFile` on the stdin handle even valid after setting `ENABLE_VIRTUAL_TERMINAL_INPUT`?

**Question 3:** `ENABLE_VIRTUAL_TERMINAL_INPUT` causes console input to be delivered as VT escape sequences. But does it also change the handle type or validity? The MSDN documentation says this mode "allows the console to receive ANSI escape sequences as input" — does it imply the handle must be read differently?

**Question 4:** Our architecture: Server is `WinExe` (no console). Client is `Exe` (console app). Client connects to server via named pipe and must relay raw keystrokes. Is there a better primitive than `ReadFile(GetStdHandle(STD_INPUT_HANDLE))` for this? e.g., `ReadConsoleInput` with `KEY_EVENT` records, then manually encoding them as VT sequences?

### Session.cs — ConPTY Integration
```csharp
// Server is WinExe, child process uses:
// - STARTF_USESTDHANDLES + INVALID_HANDLE_VALUE on all std handles
// - EXTENDED_STARTUPINFO_PRESENT + PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE
// - bInheritHandles = false
```

**Question 5:** Is there a known issue with ConPTY `CreateProcess` where the child process `cmd.exe` might not fully initialize its console if the parent is a `WinExe`? Our child works (output comes through the pipe), but could stdin be partially initialized?

**Question 6:** Should we be using `CREATE_NEW_PROCESS_GROUP` (0x00000200) or `CREATE_UNICODE_ENVIRONMENT` in addition to `EXTENDED_STARTUPINFO_PRESENT`? Are there documented ConPTY requirements for creation flags beyond what's in the sample?

## Architecture Questions (No Code Needed)

**Question 7:** For WinMUX v0.5, we need multi-session support. Current design is one server process = one session. Options:
- A: Thread-per-session in one server process
- B: One server process per session (fully isolated)
- C: Single async event loop managing multiple sessions

Given that we're on Windows with ConPTY handles, which option has the least complexity for adding `winmux ls`, `winmux new <name>`, and `winmux kill <name>`?

**Question 8:** Named pipe security. Our pipes are `\\.\pipe\WinMUX-<name>` with default ACLs (user-only). When we add multi-session, should we consider:
- Pipe name collisions between users?
- Explicit `PipeSecurity` with `AllowCurrentUserOnly`?
- Any attack surface from untrusted clients connecting to the pipe?

## What NOT To Do
- Do NOT write code patches
- Do NOT rewrite Program.cs or Session.cs
- Focus on root-cause analysis and architectural recommendations

## What We Need
1. Root cause for why `ReadFile` on console handle fails after `SetConsoleMode(ENABLE_VIRTUAL_TERMINAL_INPUT)`
2. Recommended approach for raw keyboard input relay in a Windows console multiplexer
3. Multi-session architecture recommendation
4. Any ConPTY flags or behaviors we might be missing

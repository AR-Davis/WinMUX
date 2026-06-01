# WinMUX — Native Windows Persistent Terminal Multiplexer

> **Persistent terminal sessions for Windows. No WSL required.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## How to Use WinMUX (Right Now)

**The Problem:** You start a 2-hour build in PowerShell, close your laptop to go home, and the build dies because Windows closed your terminal window.

**The Solution:**

```powershell
# 1. Build once (or download release)
cd publish

# 2. Create a named session
.\WinMUX.CLI.exe new build pwsh.exe

# 3. You're now attached. Start your long-running thing
> npm run build:prod    # or ng build, cargo build, whatever

# 4. Detach (keep the build running)
# Press Ctrl+B, then D
# "Detached from session."

# 5. Close your laptop. Go home. Reopen.

# 6. Reattach from anywhere
.\WinMUX.CLI.exe attach build

# 7. Your build finished (or is still running). Check the output.
```

**Multiple Projects:**
```powershell
.\WinMUX.CLI.exe new frontend cmd.exe    # React app
.\WinMUX.CLI.exe new backend pwsh.exe     # API server  
.\WinMUX.CLI.exe new logs cmd.exe        # Log tail session

.\WinMUX.CLI.exe ls                      # See all sessions
.\WinMUX.CLI.exe attach frontend          # Switch to frontend
# Ctrl+B D                                # Detach
.\WinMUX.CLI.exe attach backend          # Switch to backend
```

**That's it.** Three commands: `new`, `attach`, `ls`.

---

## What It Is

WinMUX is a **native Windows session manager** that keeps terminal processes running after you disconnect. Built on Microsoft's ConPTY API, it fills the gap between "Windows Terminal tabs close when the window closes" and "tmux via WSL isn't allowed on this machine."

**Current Version: v0.2** — Session manager with named sessions, full test suite, self-contained deployment.

---

## Quick Start

**Requirements:** Windows 10 1809+ and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building only).

```powershell
# Clone
git clone https://github.com/AR-Davis/WinMUX.git
cd WinMUX

# Build self-contained executables (no .NET runtime needed on target machine)
.\publish.ps1

# Run
cd publish
.\WinMUX.CLI.exe new build cmd.exe    # Create session named 'build'
.\WinMUX.CLI.exe attach build           # Attach to it
# Ctrl+B, then D to detach

# Manage
.\WinMUX.CLI.exe ls                     # List sessions
.\WinMUX.CLI.exe kill build             # Terminate session
.\WinMUX.CLI.exe daemon stop            # Stop background daemon
```

**Outputs:** Three executables in `./publish/` (60MB each, self-contained):
- `WinMUX.CLI.exe` — User interface
- `WinMUX.Daemon.exe` — Session coordinator
- `WinMUX.Server.exe` — Per-session host

---

## What It's For

### Use Cases (Works Today)

| Scenario | Why WinMUX Helps |
|----------|------------------|
| **Long builds** | Start a build, detach, reconnect from another machine later |
| **Server logs** | `tail -f` equivalent that survives laptop sleep/close |
| **Remote admin** | SSH in, start session, disconnect without killing your process |
| **Multiple projects** | One session per repo, switch between them without reopening shells |

### Who It's For

- **Enterprise developers** on locked-down Windows (WSL disabled by policy)
- **DevOps/SRE** managing Windows servers via SSH
- **Cross-platform teams** who want tmux-like workflows on Windows hosts

### What It's NOT For

| Not Supported | Why |
|---------------|-----|
| Interactive vim/emacs with arrow keys | Raw key input not implemented (v0.5 deferred) |
| Split panes / windowing | Out of scope until v0.2-0.4 stabilize |
| Mouse support | Not planned for v0.x |
| Unix/Mac | Windows-only by design (uses ConPTY) |

---

## Architecture

```
WinMUX.CLI
    │ ls / new / kill → named pipe "WinMUX-control"
    ▼
WinMUX.Daemon (one per user)
    │ spawns processes, tracks in %LOCALAPPDATA%\WinMUX\sessions.json
    ▼
WinMUX.Server (one per session)
    │ owns ConPTY pseudo-console
    ▼
cmd.exe / powershell.exe / your shell
```

**Process isolation:** Each session is a separate Server process. If one crashes, others continue. The Daemon only tracks metadata — it's not in the data path during attach.

**Communication:**
- Control commands (ls/new/kill): JSON messages over named pipe
- Session I/O during attach: Raw byte stream over named pipe (v0.2 uses relay mode; v0.5+ may use screen sync)

---

## Current Status (v0.2)

**Shipped 2026-06-01:**
- ✅ Named sessions: `new`, `ls`, `attach`, `kill`
- ✅ Daemon with JSON state persistence
- ✅ Auto-start daemon on first command
- ✅ Raw pipe I/O protocol (fixed v0.1 deadlock)
- ✅ Self-contained single-file executables
- ✅ Automated test suite (PowerShell)
- ✅ Scrollback replay on reattach

**Known Limitations:**
- ⚠️ Raw keyboard input (arrow keys, Ctrl+arrows, function keys) not supported in real console windows
- ⚠️ Line-buffered input works; interactive TUIs (vim, emacs, htop) have limited key support
- ⚠️ One client attaches at a time per session (no multi-viewer)

**The v0.2 Tradeoff:** Works great for builds, logs, and long-running processes. Not yet a full terminal multiplexer for interactive apps.

---

## Roadmap

### ✅ v0.2 Shipped
Session manager core. Named sessions, daemon, self-contained deployment.

### 📋 v0.3 Config & Polish (Next)
- [ ] `config.toml` for default shell, scrollback size
- [ ] Named pipe security (restrict to current user)
- [ ] Better error messages when session/server crashes
- [ ] `winmux rename` command

### 📋 v0.4 Ring Buffer & Search
- [ ] Replace `MemoryStream` with circular scrollback buffer
- [ ] `winmux search <session> <pattern>` for session logs
- [ ] Configurable buffer size limits
- [ ] Optional log files: `winmux new build --log C:\logs\build.log`

### 🔮 v0.5+ Deferred / Speculative
These require significant new architecture (screen sync protocol, full VT parser). Will revisit after v0.4 ships and we have real user feedback.

- **Raw key input:** `ReadConsoleInput` P/Invoke + VT encoding table (thousands of edge cases)
- **Windowing:** Panes, splits, status bar
- **Windows Service mode:** Survive user logout
- **Integration:** Windows Terminal extension, PowerShell module, MSI installer

**Decision criteria for v0.5:** If v0.4 gets used and users actually request vim/emacs support, we'll invest in the screen sync architecture. If not, WinMUX stays a "persistent session manager" rather than a "full terminal multiplexer."

---

## Commands Reference

```bash
# Session lifecycle
winmux ls                           # List sessions with PID, status
winmux new <name> [shell]           # Create session (default: cmd.exe)
winmux attach <name>                # Attach (line-buffered input)
winmux kill <name>                  # Terminate session and server

# Detach from inside attached session:
#   Ctrl+B, then D

# Daemon management
winmux daemon status                # Show daemon state
winmux daemon start                 # Start daemon manually
winmux daemon stop                  # Stop daemon and clean up
```

---

## Development

Built by [@AR-Davis](https://github.com/AR-Davis) with the [Pi](https://github.com/marioschneiderman/pi) coding agent harness.

### Build

```powershell
.\publish.ps1              # Release build to ./publish/
.\publish.ps1 -Configuration Debug   # Debug build
```

### Test

```powershell
cd test
.\e2e-publish-test.ps1    # Full end-to-end test
.\attach-test.ps1         # Attach/detach validation
.\raw-pipe-test.ps1       # Pipe communication test
```

### Project Structure

```
WinMUX.sln
├── src/
│   ├── WinMUX.Core/       # Session, NativeMethods (P/Invoke)
│   ├── WinMUX.Server/     # Per-session ConPTY host
│   ├── WinMUX.Daemon/     # Session coordinator
│   └── WinMUX.CLI/        # User interface
├── test/                  # PowerShell test suite
├── publish.ps1            # Build script
└── README.md              # This file
```

---

## License

MIT. See [LICENSE](LICENSE).

---

*WinMUX: Because `ng build` shouldn't die when you close your laptop.*

# WinMUX — Native Windows Persistent Terminal Multiplexer

> **tmux for Windows. No WSL required.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## What It Is

WinMUX is a **native Windows terminal multiplexer** built on Microsoft's ConPTY API. It gives you persistent terminal sessions that survive client disconnect, window close, and even user logout (as a Windows Service).

**Key principle:** Bare Windows. No WSL. No MSYS2. No Cygwin. Just `cmd.exe`, `powershell.exe`, or any shell you want, running persistently via native Windows APIs.

---

## Quick Start

Requires: **Windows 10 1809+** and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Clone
git clone https://github.com/AR-Davis/WinMUX.git
cd WinMUX

# Build (self-contained, single-folder)
.\publish.ps1

# All executables in ./publish/
cd publish

# Start daemon and create session
.\WinMUX.CLI.exe new main cmd.exe        # Creates session 'main'
.\WinMUX.CLI.exe new dev powershell.exe  # Creates session 'dev'

# List sessions
.\WinMUX.CLI.exe ls

# Attach to a session
.\WinMUX.CLI.exe attach main

# Detach: Ctrl+B, then D

# Kill a session
.\WinMUX.CLI.exe kill dev
```

### Self-contained binaries (no .NET runtime needed)
```powershell
.\publish.ps1  # Builds all components to ./publish/
```

---

## What Works Today

| Feature | v0.1 | v0.2 🚢 |
|---------|------|---------|
| Persistent sessions (single default) | ✅ | ✅ |
| Named sessions with session manager | ❌ | ✅ |
| `ls / new / attach / kill` commands | ❌ | ✅ |
| Daemon auto-start | ❌ | ✅ |
| Scrollback replay on reattach | ✅ | ✅ |
| Named-pipe attach/detach/reattach | ✅ | ✅ |
| Server survives client disconnect | ✅ | ✅ |
| Multi-session (process-per-session) | ❌ | ✅ |
| Automated test suite | ✅ | ✅ |
| Self-contained single-file EXEs | ✅ | ✅ |

**v0.2 Status:** [Shipped 2026-06-01] — Pipe communication fix deployed. Raw I/O protocol replaces StreamReader/StreamWriter deadlock. Self-contained publish folder.

**Limitation:** Raw keyboard input (arrow keys, Ctrl sequences) in a real Windows console window is **not yet supported**. Redirected stdin and line-buffered input work. Full raw input via `ReadConsoleInput` + VT encoding is planned for v0.5.

---

## Why WinMUX?

| Problem | Existing Tool | Why It Fails |
|---------|-------------|--------------|
| Need tmux on Windows | tmux via WSL | WSL disabled by policy in many enterprises |
| Need tmux on Windows | MSYS2/Cygwin | Heavy compatibility layer, not native |
| Persistent sessions | Windows Terminal | Tabs/panes die when window closes |
| Persistent sessions | PowerShell remoting | Wrong abstraction, needs Admin/WinRM |
| Native multiplexer | **Nothing exists** | **This is the gap WinMUX fills** |

### Target Users

- **Enterprise developers** on locked-down Windows (WSL disabled)
- **DevOps engineers** managing Windows servers via SSH/WinRM
- **Cross-platform teams** standardizing on tmux workflows
- **Windows Server admins** — lightweight session management without RDP

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    WinMUX.Daemon                             │
│  Control Pipe: WinMUX-control                               │
│  State: %LOCALAPPDATA%\WinMUX\sessions.json                  │
│                                                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐          │
│  │ Spawns      │  │ Tracks PIDs │  │ Answers ls  │          │
│  │ Servers     │  │ & Metadata  │  │ / kill cmds │          │
│  └──────┬──────┘  └─────────────┘  └─────────────┘          │
└─────────┼────────────────────────────────────────────────────┘
          │ spawns
    ┌─────┴─────┐    ┌─────────────┐    ┌─────────────┐
    │ Server:   │    │ Server:     │    │ Server:     │
    │ "main"    │    │ "build"     │    │ "logs"      │
    │ └── Pipe  │    │ └── Pipe    │    │ └── Pipe    │
    │   WinMUX- │    │   WinMUX-   │    │   WinMUX-   │
    │   main    │    │   build     │    │   logs      │
    └───────────┘    └─────────────┘    └─────────────┘
          ▲
          └────────────┐
    ┌──────────────────┴─────────────────┐
    │         WinMUX.CLI                  │
    │  winmux ls          → control pipe  │
    │  winmux new bash    → daemon spawns │
    │  winmux attach main → direct pipe     │
    │  winmux kill build  → daemon kills  │
    └─────────────────────────────────────┘
```

**Process-per-session model:** Each session is an isolated Server process. The Daemon only tracks metadata and spawns/kills. Crash isolation between sessions.

---

## Roadmap

### v0.2 ✅ (Current)
- [x] Session manager (`winmux ls/new/attach/kill`)
- [x] Process-per-session architecture
- [x] Daemon with JSON state persistence
- [x] Auto-start daemon on first command

### v0.3 (Config & Polish)
- [ ] `config.toml` for default shell, scrollback size
- [ ] Named pipe security (restrict to current user)
- [ ] Better error messages
- [ ] Session attach counts (multiple clients view same session?)

### v0.4 (Ring Buffer)
- [ ] Circular scrollback buffer (replace MemoryStream)
- [ ] Scrollback search
- [ ] Configurable buffer size

### v0.5 (Raw Input)
- [ ] `ReadConsoleInput` P/Invoke
- [ ] VT encoding table for key events
- [ ] Full arrow key / function key support
- [ ] Multi-pane splits begin here

### v0.6 (Windowing)
- [ ] Panes, splits, pane navigation
- [ ] Status bar as VT overlay

### v0.7 (Integration)
- [ ] Windows Terminal profile JSON
- [ ] PowerShell module
- [ ] Keybindings customization

### v0.8 (Beta Release)
- [ ] MSI installer
- [ ] winget package
- [ ] Full documentation

### v1.0
- [ ] Windows Service mode (survive logout)
- [ ] Session restore on boot
- [ ] Mouse support

---

## Commands

```bash
# Session management
winmux ls                           # List active sessions
winmux new <name> [shell]           # Create named session (default: cmd.exe)
winmux attach <name>                # Attach to session
winmux kill <name>                  # Terminate session

# Daemon control
winmux daemon                       # Show daemon status
winmux daemon start                 # Start background daemon
winmux daemon stop                  # Stop running daemon

# General
winmux help                         # Show usage
```

---

## Development

Built by [@AR-Davis](https://github.com/AR-Davis) with the Pi coding agent harness.

### Project Structure
```
WinMUX.sln
├── src/
│   ├── WinMUX.Core/       # Session, NativeMethods
│   ├── WinMUX.Server/     # Per-session ConPTY host
│   ├── WinMUX.Daemon/     # Session manager coordinator
│   └── WinMUX.CLI/        # User-facing CLI
└── test/                   # Automated PowerShell tests
```

---

## License

MIT. See [LICENSE](LICENSE).

---

*WinMUX exists because locked-down Windows machines deserve tmux too.*

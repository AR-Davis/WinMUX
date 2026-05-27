# WinMUX — Native Windows Persistent Terminal Multiplexer

> **tmux for Windows. No WSL required.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## What It Is

WinMUX is a **native Windows terminal multiplexer** built on Microsoft's ConPTY API. It gives you persistent terminal sessions that survive client disconnect, window close, and even user logout (as a Windows Service).

If you've ever used tmux on Linux and then been forced onto a locked-down Windows machine with WSL disabled, you know the pain. WinMUX fills that gap.

**Key principle:** Bare Windows. No WSL. No MSYS2. No Cygwin. Just `cmd.exe`, `powershell.exe`, or any shell you want, running persistently via native Windows APIs.

---

## What Works Today (MVP)

| Feature | Status |
|---------|--------|
| Persistent `cmd.exe` sessions | ✅ |
| Persistent PowerShell sessions | ✅ |
| Named-pipe attach / detach / reattach | ✅ |
| Scrollback replay on reattach | ✅ |
| Server survives client disconnect/crash | ✅ |
| Self-contained single-file EXEs | ✅ |
| Automated test suite | ✅ (6 tests) |

**Limitation:** Raw keyboard input (arrow keys, Ctrl sequences) in a real Windows console window is **not yet supported** in the interactive client. Redirected stdin and line-buffered input work correctly. Full raw input via `ReadConsoleInput` + VT encoding is planned for v0.5.

---

## Quick Start

Requires: **Windows 10 1809+** and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Clone
git clone https://github.com/AR-Davis/WinMUX.git
cd WinMUX

# Build
dotnet build

# Terminal 1: Start the server (spawns cmd.exe in a headless ConPTY)
dotnet run --project src/WinMUX.Server -- default

# Terminal 2: Attach client
dotnet run --project src/WinMUX.CLI -- default

# Type commands. Close the terminal window.
# Server keeps running. Re-attach anytime with the same command.
```

### Self-contained binaries (no .NET runtime needed)
```powershell
dotnet publish src/WinMUX.Server -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
dotnet publish src/WinMUX.CLI   -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Outputs in bin/Release/net8.0/win-x64/publish/
```

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
- **DevOps engineers** managing Windows servers via SSH/WinRM who want persistent sessions
- **Cross-platform teams** standardizing on tmux workflows
- **Windows Server automation** — lightweight session management without RDP overhead

---

## Architecture

```
┌─────────────────────────────────────────────┐
│            Client (Terminal)                 │
│  ─ wt.exe, VS Code, WezTerm, etc.           │
│  ─ Reads VT from named pipe                  │
│  ─ Writes keystrokes / resize events back    │
└──────────────────┬──────────────────────────┘
                   │  IPC (named pipe)
┌──────────────────▼──────────────────────────┐
│           Session Server (daemon)            │
│  ─ Maintains ConPTY handles                  │
│  ─ Hosts Window → Pane → PTY tree            │
│  ─ Serializes scrollback per pane            │
│  ─ Rewrites VT for active client dimensions  │
└─────────────────────────────────────────────┘
```

### Stack
- **C# / .NET 8** — rapid iteration, excellent WinAPI interop
- **ConPTY** (Windows 10 1809+) — native pseudo-console API
- **Named pipes** — `System.IO.Pipes`, user-only ACLs by default
- **Self-contained publish** — single x64 `.exe`, no runtime dependency

---

## Comparison

| Tool | Persistent? | Native Windows? | No Admin? | Splits? | OSS? |
|------|-------------|-----------------|-----------|---------|------|
| tmux (WSL) | ✅ | ❌ (WSL only) | ✅ | ✅ | ✅ |
| Windows Terminal | ❌ | ✅ | ✅ | ✅ | ✅ |
| PowerShell PSSession | Partial | ✅ | ❌ (needs Admin/WinRM) | ❌ | ✅ |
| MSYS2 tmux | ✅ | Partial (layer) | ✅ | ✅ | ✅ |
| **WinMUX** | ✅ | ✅ | **Yes** | v0.5 | ✅ |

---

## Roadmap

### v0.5
- [ ] Raw VT input in real console (`ReadConsoleInput` + manual VT encoding)
- [ ] Named sessions (`winmux ls`, `winmux new`, `winmux kill`)
- [ ] Multi-pane splits (horizontal / vertical)
- [ ] Status bar rendered as VT overlay
- [ ] Scrollback ring buffer (replace `MemoryStream`)

### v1.0
- [ ] Windows Service mode (survive user logout)
- [ ] Config file (`%APPDATA%\winmux\config.toml`)
- [ ] Session restore on boot
- [ ] Mouse support (pane selection, resizing)
- [ ] Windows Terminal integration profile

### v2.0
- [ ] Native GUI client with GPU rendering
- [ ] Remote attach over TCP/TLS
- [ ] Serializable session state (survive reboot)

---

## Contributing

Built by [@AR-Davis](https://github.com/AR-Davis) with help from the Pi coding agent harness.

Pull requests welcome. Focus areas:
- `ReadConsoleInput` implementation for raw VT input
- Circular scrollback buffer
- Multi-session architecture (process-per-session model)
- Windows Terminal JSON profile integration

See `GORDON_REVIEW_2.md` for detailed technical analysis and architecture recommendations.

---

## License

MIT. See [LICENSE](LICENSE).

---

*WinMUX exists because locked-down Windows machines deserve tmux too.*

# WinMUX — Native Windows Persistent Terminal Multiplexer

> **Codename:** WinMUX  
> **Status:** MVP Spike Active  
> **Platform:** Windows 10 1809+ (ConPTY)  
> **License:** MIT (proposed)  
> **Language:** C# / .NET 8  
> **Created:** 2026-05-26 by Watts (Kinch)

---

## 1. Problem Statement

Windows has no native, persistent terminal multiplexer. Users who cannot or will not run WSL are stuck with:
- `wt.exe` (no process persistence across window close)
- SSH to Linux boxes (requires another machine)
- MSYS2/Cygwin (heavy compatibility layers, not native)
- PowerShell remoting (wrong abstraction, brittle for interactive use)

**WinMUX exists to fill this gap** with a lightweight, native, open-source persistent session manager that feels like tmux but runs on bare Windows using ConPTY.

---

## 2. Design Goals

| Goal | Description |
|------|-------------|
| **Native** | Built on Windows ConPTY. No WSL, no MSYS2, no Cygwin. |
| **Persistent** | Sessions survive client disconnect and even user logout (as a service). |
| **Tmux-Compatible** | Familiar commands: `new`, `attach`, `detach`, `ls`, `kill-session`, splits, panes. |
| **Zero-Elevation** | Install and run without Administrator for basic usage. (Service mode optionally requires it.) |
| **Fast** | C# core with native interop; VT parsing in managed code with raw byte relay. |
| **Embeddable** | Can be consumed by Windows Terminal, VS Code, WezTerm, etc. via a named-pipe / socket protocol. |

---

## 3. Architecture Overview

```
┌─────────────────────────────────────────────┐
│            Client (UI) Layer               │
│  ─ wt.exe profile, VS Code terminal, etc.  │
│  ─ Reads VT from named pipe / socket       │
│  ─ Writes keystrokes / resize events back  │
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

### 3.1 Core Concepts
- **Server:** A headless Windows service or background user process that owns ConPTY instances.
- **Session:** A named collection of Windows (like a tmux session).
- **Window:** A layout container holding one or more panes.
- **Pane:** A virtual terminal backed by a ConPTY + child process (e.g., `powershell.exe`, `cmd.exe`, `python.exe`).
- **Client:** Any terminal UI that connects to the server over IPC and renders VT output.

---

## 4. Technical Stack

| Component | Tech | Rationale |
|-----------|------|-----------|
| Server core | **C# / .NET 8** | Rapid iteration, excellent WinAPI interop via `[DllImport]`, `NamedPipeServerStream` built-in, easy service scaffolding. Self-contained publish yields a single `.exe`. |
| VT parser / emulator | Custom C# state machine (v0.5+) | We need to know cursor positions, scroll regions, and colors for pane borders. |
| IPC transport | Named pipes (`\\.\pipe\WinMUX-{sid}`) | `System.IO.Pipes` is production-ready on Windows. |
| Control protocol | Raw byte relay (MVP) → msgpack (v1) | MVP needs no protocol; just stream VT. |
| Service wrapper | C# `System.ServiceProcess.ServiceBase` | Native .NET support for Windows Services. |
| Installer | `dotnet publish --self-contained` + zip/MSIX | Single-file x64 exe, no runtime dependency. |

**Why C#?** Because WinMUX is *Windows-only*. PowerShell fluency maps directly to C# semantics. `FileStream` + `SafeFileHandle` gives us async I/O over ConPTY handles. The "daemon must be native" argument is weaker than "ship fast and iterate." We can always port the hot path to C++ later if profiling demands it.

---

## 5. Key APIs & Primitives

### 5.1 ConPTY (Windows 10 1809+)
- `CreatePseudoConsole()` — create a PTY master handle.
- `ResizePseudoConsole()` — resize on client reattach / split changes.
- `ClosePseudoConsole()` — cleanup.
- The child process is launched by the server via `CreateProcess` with the PTY as its `CONOUT$`/`CONIN$`.

### 5.2 Process Lifetime
- The child process (`pwsh.exe`, etc.) is a child of the WinMUX server.
- The server runs in a background job or Windows service.
- On client disconnect, the pipes close but the server retains the PTY `HANDLE` and `HPCON`.
- On server crash, optionally auto-restart with job objects or service recovery settings.

### 5.3 VT Rewriting for Multiplexer Mode
This is the **hardest part** and where most of the engineering lives:
- The server must maintain a virtual screen buffer per pane.
- When a client connects, the server **replays** the pane contents (or streams live output) wrapped in a layout of pane borders.
- For multi-pane windows, the server sends synthetic VT sequences:
  - Draw box-drawing borders (`┌─┐│└┘`)
  - Set active pane title in status bar (tmux-style)
  - Route keyboard input based on active pane focus.

This requires a **real terminal state machine** in the server, not just blind pipe relay.

---

## 6. MVP Scope vs Full Scope

### MVP (Active Spike)
- [x] C# solution scaffolded (`WinMUX.Core`, `WinMUX.Server`, `WinMUX.CLI`)
- [x] ConPTY P/Invoke wrapper (`CreatePseudoConsole`, `CreateProcess` with extended attributes)
- [x] Named-pipe attach/detach loop with line-buffered fallback
- [x] Raw VT output mode (`ENABLE_VIRTUAL_TERMINAL_PROCESSING`) on client
- [x] Session manager (`new`, `attach`, `ls`, `kill-session`) — single hardcoded session; multi-session → v0.5
- [ ] Raw VT **input** mode in a real Windows Console (CMD / Windows Terminal) — requires `ReadConsoleInput` + manual VT encoding; deferred to v0.5
  - **Current workaround:** Redirected stdin (`Console.ReadLine()` / piped input) works correctly. Run client via automation or pipe for full reattach/scrollback support.
- [ ] Config file (`winmux.conf` or `%APPDATA%\winmux\config.toml`)
- [ ] Scrollback ring buffer (replace `MemoryStream` with circular buffer)

### v0.5
- [ ] Tmux-compatible control mode (e.g., `%output`, `%window-add`).
- [ ] Multi-pane splits (horizontal / vertical).
- [ ] Status bar rendered as VT overlay.
- [ ] Scrollback buffer maintained per pane.
- [ ] Windows Terminal integration profile.

### v1.0
- [ ] Windows Service mode (survive logout).
- [ ] Config file (`winmux.conf` or `%APPDATA%\winmux\config.toml`).
- [ ] Session restore on boot (optional).
- [ ] Mouse support (pane selection, resizing).
- [ ] Plugin/scripting hooks (Lua or WASM?).

### v2.0 (Future)
- [ ] Native GUI client with GPU-accelerated rendering (Direct2D/Vulkan).
- [ ] Remote attach over TCP/TLS.
- [ ] Serializable session state for **survive-reboot**.

---

## 7. Open Questions & Risks

| Risk | Mitigation |
|------|------------|
| **ConPTY bugs / limitations** | Microsoft still fixes these. Ship with a supported Windows version matrix. |
| **VT rewriter complexity** | Start MVP without rewriting; add an incremental terminal emulator later. |
| **Unicode width & graphemes** | Use standard `System.Globalization` or `Wcwidth` port. |
| **Security (named pipe ACLs)** | Default to user-only ACLs. Document service hardening. |
| **Competition** | MSYS2 tmux exists for users who can install it. Target enterprise/secure environments where WSL is **disabled by policy**. |

---

## 8. Why This Is Worth Building

1. **Policy-gapped users:** Many enterprise/government Windows environments disable WSL via hypervisor policy. These users still need tmux.
2. **The gap is real:** There is no `apt install tmux` on native Windows. ConPTY exists; nobody has shipped a multiplexer on top of it in open source.
3. **Marketability:** Every Windows developer who has used WSL tmux and then been forced onto a locked-down Windows machine is a potential user.
4. **Technical portfolio:** Demonstrates deep Windows systems programming.

---

## 9. Comparison Matrix

| Tool | Persistent? | Native Windows? | No Admin? | Splits? | OSS? |
|------|-------------|-----------------|-----------|---------|------|
| tmux (WSL) | ✅ | ❌ (WSL only) | ✅ | ✅ | ✅ |
| Windows Terminal | ❌ | ✅ | ✅ | ✅ | ✅ |
| PowerShell PSSession | Partial | ✅ | ❌ (needs Admin/WinRM) | ❌ | ✅ |
| MSYS2 tmux | ✅ | Partial (MSYS2 layer) | ✅ | ✅ | ✅ |
| **WinMUX** | ✅ | ✅ | **MVP: Yes** | v0.5 | ✅ |

---

## Quick Start (Developer)

Requires: Windows 10 1809+ and [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### Known Limitation
**Raw VT input (arrow keys, interactive apps) in a real Windows console window is not yet supported.**
`ENABLE_VIRTUAL_TERMINAL_INPUT` + `ReadFile` on the console handle is an unsupported API combination on Windows. Raw input requires `ReadConsoleInput` + manual VT encoding (planned for v0.5).

**What works today:**
- Automated tests (redirected stdin) — full attach/detach/reattach, scrollback replay
- Line-buffered fallback mode (pipe stdin, or run inside mintty/MSYS2)
- PowerShell and cmd.exe as child shells

---

## 10. Immediate Next Steps

1. **Build & Verify:** Run the spike on Watts. Confirm ConPTY + named pipe reattachment works end-to-end.
2. **Raw Mode:** Replace line-buffered input in CLI with `Console.ReadKey(intercept: true)` for interactive apps.
3. **Scrollback Circular Buffer:** Replace `MemoryStream` with a ring buffer.
4. **Session Manager:** Support named sessions, `winmux ls`, and session lifecycle.
5. **Windows Service Host:** Add a service-mode project so sessions survive user logout.
6. **Research:** Audit Microsoft's `Terminal` repo for ConPTY edge cases.
7. **Open Source:** Create public GitHub repo once MVP passes manual test.

---

*Compiled by Watts.*

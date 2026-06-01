# Testing WinMUX Over SSH

**TL;DR:** SSH sessions can test daemon and session lifecycle, but NOT interactive attach (needs real Windows console).

## What Works Over SSH

| Feature | Status | Notes |
|---------|--------|-------|
| `winmux daemon start/stop/status` | ✅ Yes | Full functionality |
| `winmux new <name>` | ✅ Yes | Auto-starts daemon |
| `winmux ls` | ✅ Yes | Shows all sessions |
| `winmux kill <name>` | ✅ Yes | Terminates cleanly |
| `winmux attach` | ⚠️ Fallback | Line-buffered mode only |

## Quick SSH Test

```bash
# From your local machine
ssh windows-host

# On the Windows host via SSH
cd C:\Users\you\Projects\WinMUX\test
.\ssh-safe-test.ps1 -Cleanup
```

**Expected output:**
```
=== WinMUX SSH-Safe Test ===
[TEST] Daemon initially stopped... PASS
[TEST] Create session auto-starts daemon... PASS
[TEST] Daemon is running after auto-start... PASS
[TEST] Session appears in list... PASS
[TEST] Session process exists... PASS
[TEST] Create second session... PASS
[TEST] List shows multiple sessions... PASS
[TEST] State file persisted... PASS
[TEST] Kill session removes it... PASS
[TEST] Kill remaining session... PASS
[TEST] List shows no sessions... PASS
[TEST] Daemon stops cleanly... PASS

=== Test Summary ===
Passed: 12
Failed: 0
All tests passed!
```

## What This Proves

1. **Daemon IPC works** — Named pipes function over remote sessions
2. **Process spawning works** — Server.exe launches correctly
3. **State persistence works** — JSON file read/write
4. **Cleanup works** — No orphaned processes or lock files

## What This Does NOT Prove

- **Interactive terminal feel** — Arrow keys, colors, Ctrl+C handling
- **Detach/reattach flow** — The full tmux-like experience
- **Scrollback display** — Visual rendering of prior output

Those require a real Windows console window (RDP, local login, or Windows Terminal).

## Why Attach Falls Back

```bash
$ winmux attach test1
[warn] No Windows console detected.
[warn] Falling back to line-buffered input.
Connected. Press Ctrl+D (EOF) to detach.
Microsoft Windows [Version 10.0.26200.8457]
(c) Microsoft Corporation. All rights reserved.

C:\Users\you>dir
# You can type commands, output shows up
# But: arrow keys send VT codes (^[[A), tab completion is broken
```

## Recommendation

Use SSH testing for **CI/CD** and **automated validation**. For the actual experience, run locally in a Windows Terminal or CMD window.


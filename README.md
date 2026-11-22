# Ports In Use

**See which apps on your Windows PC are talking to the network, which ports they use, and who they talk to, and build that picture up over time.**

Ports In Use is a free, open-source, portable network dependency monitor. Think of it as `netstat` with a memory and a dark neon dashboard. It's a modern take on Microsoft's old *Application Dependency Mapper*.

- **One file, no install.** Copy `PortsInUse.exe` anywhere and run it.
- **No dependencies.** It only needs .NET Framework 4.x, which comes with Windows 10, 11 and Windows Server 2016 and later.
- **Nothing leaves your PC.** It reads local Windows tables. The only network traffic it makes is optional reverse-DNS lookups.
- **Smooth.** Graphs, counters and the map animate at your display's refresh rate (60 fps or higher).
- **Open source.** All the code is here under the [MIT License](LICENSE), so you can read exactly what it does, build it yourself, or change it.

---

## Contents

- [Download](#download)
- [Installing](#installing)
- [What it's for](#what-its-for)
- [Views](#views)
- [Using it](#using-it)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Saving and exporting](#saving-and-exporting)
- [How it works](#how-it-works)
- [Notes and limits](#notes-and-limits)
- [Privacy](#privacy)
- [Building from source](#building-from-source)
- [Project layout](#project-layout)
- [License](#license)

---

## Download

### ⬇ [Download PortsInUse.exe (latest version)](https://github.com/gth3299t4/portsinuse/releases/latest/download/PortsInUse.exe)

You can also find every version on the [Releases page](https://github.com/gth3299t4/portsinuse/releases).

> This repository is private, so the download link only works for people with access to it. Sign in to GitHub first.

### Verify your download (optional)

Each release lists the SHA-256 checksum of `PortsInUse.exe` in its notes. To confirm your copy is genuine and wasn't changed in transit, run this in PowerShell from the folder containing the exe:

```powershell
Get-FileHash .\PortsInUse.exe -Algorithm SHA256
```

The `Hash` value should exactly match the one in the release notes.

---

## Installing

There's no installer. The app is a single file that runs from wherever you put it.

1. **Download** `PortsInUse.exe` using the link above.
2. **Pick a home for it.** Any folder works. A permanent one is better than Downloads, because auto-save writes its file next to the exe. For example:
   ```
   C:\Tools\PortsInUse\PortsInUse.exe
   ```
3. **Unblock it (if needed).** Windows marks files downloaded from the internet. If the app won't open, right-click `PortsInUse.exe` → **Properties** → tick **Unblock** → **OK**.
4. **Run it.** Double-click `PortsInUse.exe`.
   - The app isn't code-signed, so Windows SmartScreen may show *"Windows protected your PC"*. Click **More info** → **Run anyway**. You only need to do this once.
   - Administrator rights are optional (see [Notes and limits](#notes-and-limits)).
5. **(Optional) Add a shortcut.** Right-click the exe → **Pin to Start**. For a desktop shortcut, right-click → **Show more options** → **Send to → Desktop (create shortcut)**.

Leave it running. The longer it runs, the more complete the dependency history gets.

### Requirements

- **Windows 10 or 11** (any edition, 64-bit or 32-bit)
- **Windows Server 2016, 2019, 2022 or 2025** with the *Desktop Experience* installed. It's a GUI app, so it won't run on Server Core. It works fine over Remote Desktop.
- .NET Framework 4.x, already included in all of the above, so there's nothing extra to install

| Windows version | Works | Notes |
| --- | --- | --- |
| Windows 11 | ✅ | Full look, including the dark title bar and coloured window border |
| Windows 10 1903 or later | ✅ | Dark title bar and dark scrollbars |
| Windows 10 before 1903 | ✅ | Everything works; scrollbars stay light |
| Windows Server 2025 | ✅ | Same as Windows 11 |
| Windows Server 2022 / 2019 | ✅ | Same as Windows 10 1903+ |
| Windows Server 2016 | ✅ | Same as Windows 10 before 1903 |
| Server Core (any version) | ❌ | No desktop, so the window can't open |
| Windows Server 2012 R2 / Windows 8.1 and older | ⚠️ | Not supported or tested |

No admin rights are needed. All connections and process names show either way. On servers you may want to **Run as administrator** so the full file path also shows for system and service processes. Without it, some of those paths are blank.

### Updating

Download the latest `PortsInUse.exe` and replace the old one. Your auto-save file next to it is kept.

### Uninstalling

Close the app and delete the folder. It doesn't add registry entries, services or start-up items. If the exe was in a read-only folder, also delete `%LOCALAPPDATA%\PortsInUse\`, where auto-save goes in that case.

---

## What it's for

- **Firewall and migration planning:** find out exactly which servers, ports and services an application depends on before you move it or lock it down.
- **Troubleshooting:** see what an app is connecting to right now, and whether the connection is LAN, Internet or loopback.
- **Auditing:** list every listening port on a machine, who owns it, and whether it's exposed on all interfaces or only on loopback.
- **Learning what's normal:** run it for a day, save the session, and compare it with another machine or another day.

---

## Views

| View | Shows |
|---|---|
| **Summary** | Live tiles, scrolling glow graphs of connections and remote hosts, and the top apps and hosts right now |
| **Applications** | Each app seen on the network: status, live connections, listening ports, inbound clients, outbound hosts and top destinations |
| **Connections** | The live socket table (the netstat view) with process, state, direction, scope and reverse DNS |
| **Dependencies** | History of every *app ⇄ remote endpoint* pairing: first seen, last seen, total connections and most connections at once |
| **Listening** | Every port open for incoming traffic, who owns it, and whether it's on all interfaces or loopback only |
| **Remote Hosts** | Every remote device seen, and which local apps and ports it uses |
| **Map** | A dependency map with inbound clients on the left, the app (or the whole PC) in the middle and outbound destinations on the right, colour-coded Internet / LAN / loopback |

### Direction and service ports

A connection is **Inbound** if its local port is a listening port on this PC, and **Outbound** otherwise. Random client-side (ephemeral) ports are folded away, so the history records the *service* port. For example, `chrome.exe → 1.2.3.4:443`, not one row per random source port.

### Windows services

For `svchost.exe` and other host processes, the Windows services running inside them are shown as well, e.g. `svchost.exe [Dnscache]`, so you can tell which service owns a connection.

---

## Using it

- **Sampling speed:** choose 1, 2, 5 or 30 seconds. Use *High (1 s)* to catch short-lived connections, or a slower speed for long, low-impact monitoring.
- **Filter** (Ctrl+F) works on every view. Separate words with spaces; every word must match. Press Esc to clear it.
- **Toggles:** *Hide loopback*, *Active only*, *Hide ephemeral listeners*.
- **Show on map:** double-click a row, or use the *Show on map* button, to open that app on the map.
- **⋯ menu:** Save/Load session, Export to CSV, Save map as PNG, reverse DNS on/off, Auto-save, Visual effects on/off, Always on top and Restart as Administrator.

---

## Keyboard shortcuts

| Keys | Action |
|---|---|
| `Ctrl+1` … `Ctrl+7` | Switch view (Summary → Map) |
| `Ctrl+F` | Jump to the filter box |
| `Esc` | Clear the filter (while in the filter box) |
| `F5` | Take a sample now |
| `Ctrl+C` | Copy the selected rows |

---

## Saving and exporting

| Feature | Details |
|---|---|
| **Save session** | Writes the full history to a `.tsv` file, named `PortsInUse-<PC name>-<date-time>.tsv` by default |
| **Load session** | Merges a saved `.tsv` into the current history, so you can combine runs or machines |
| **Auto-save** | Saves every 5 minutes to `PortsInUse-autosave.tsv` next to the exe. If that folder is read-only, it uses `%LOCALAPPDATA%\PortsInUse\` instead |
| **Export CSV** | Exports the list you're looking at, with its current filter applied |
| **Save map as PNG** | Saves an image of the current dependency map |

---

## How it works

1. **Sampling.** On each tick, it reads the Windows TCP and UDP tables with the `GetExtendedTcpTable` and `GetExtendedUdpTable` APIs. These are the same tables `netstat -ano` reads, but there's no text to parse, so it's fast.
2. **Ownership.** Each socket is matched to its process ID, process name and executable path. Hosted Windows services are looked up for shared host processes.
3. **History.** Each connection is folded into an *app ⇄ endpoint ⇄ service port* record that tracks first seen, last seen, total count and most at once.
4. **Name lookup.** Remote addresses are optionally resolved with reverse DNS in the background and cached.
5. **Rendering.** A background thread waits on `DwmFlush`, which returns once per display refresh, then asks the UI thread to draw one frame. Only one frame is ever queued, so if drawing is slow the frame rate drops gradually instead of lagging behind. You can turn visual effects off in the ⋯ menu.

---

## Notes and limits

- **Administrator is optional.** Most details work without it. Run elevated (⋯ → *Restart as Administrator*) to see the owners of some protected system processes.
- **It samples.** A connection that opens and closes between two samples can be missed. Use the 1-second speed when this matters.
- **UDP has no remote endpoint** in the Windows tables, so UDP appears only as listening or bound ports.
- **`System` (PID 4)** owns kernel-mode listeners such as SMB (445) and HTTP.sys (80, 443, 5985, …). Those connections are real, but they belong to the kernel, not a normal app.

---

## Privacy

Ports In Use reads only local operating-system tables. It doesn't phone home, collect telemetry or upload anything. The only traffic it creates is **reverse-DNS lookups** for remote addresses, sent to your normal DNS server, and you can turn them off in the ⋯ menu.

Saved sessions and exports contain process names, IP addresses and hostnames from your machine, so take care when sharing them.

---

## Building from source

No SDK or Visual Studio is needed. Open PowerShell in the repo folder and run:

```powershell
.\build.ps1
```

This uses the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and writes a single `PortsInUse.exe` to the repo root.

If PowerShell blocks the script, run it once with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

**Requirements:** Windows 10 or 11, or Windows Server 2016 or later, with .NET Framework 4.x. The source is written in C# 5 so the built-in compiler can build it.

---

## Project layout

```
portsinuse/
├── build.ps1          Build script (built-in csc → PortsInUse.exe)
├── PortsInUse.exe     Prebuilt portable app
└── src/
    ├── Program.cs     Entry point
    ├── MainForm.cs    Main window, views, sampling loop, menus, save/load
    ├── Model.cs       Process/service lookup, connection history, DNS cache
    ├── Native.cs      Win32 interop (TCP/UDP tables, process paths)
    ├── Controls.cs    Custom neon controls: tiles, graphs, grids, buttons
    ├── MapPanel.cs    Dependency map rendering
    ├── Animator.cs    Vsync-paced frame clock for smooth animation
    └── app.manifest   DPI awareness and Windows compatibility settings
```

---

## License

Released under the [MIT License](LICENSE). You're free to use, copy, modify and share it. It comes with no warranty.

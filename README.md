# OmniWin — Windows Deep Control Plane, Optimizer & MCP Agent Platform

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0--windows-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C# Native](https://img.shields.io/badge/Language-C%23%2013-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![WPF / Direct3D](https://img.shields.io/badge/GUI-WPF%20%2F%20Hardware--Accelerated-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![MCP Protocol](https://img.shields.io/badge/MCP-28%20Tools%20Enabled-8A2BE2)](https://modelcontextprotocol.io/)
[![Tests](https://img.shields.io/badge/Tests-109%2F109%20Passing%20(100%25)-brightgreen)](docs/E2E_VM_TESTING_GUIDE.md)
[![Anti-Cheat](https://img.shields.io/badge/Anti--Cheat-100%25%20Safe%20(Layered%20No--Injection)-blue)](OmniWin.UI/Views/GamingOverlayWindow.xaml.cs)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> **OmniWin** is a native, ultra-high-performance Windows 10/11 engineering, gaming optimization, and observability control plane written in compiled C# (.NET 9) with direct Win32 & NT Kernel API integrations.
> It replaces 8+ separate fragmented utilities (*HWMonitor, Process Hacker, Autoruns, FanControl, CCleaner, RTSS OSD, Simplewall, IObit Unlocker, EarTrumpet*) into a single zero-bloat standalone executable, backed by a 28-tool **Model Context Protocol (MCP)** server for autonomous AI agents.

---

## 🌟 Key Architecture & Capabilities

### 1. ⚡ NT Kernel Memory Management (`MemoryService`)
* **Atomic RAM Purge**: Leverages `NtSetSystemInformation` with `SYSTEM_MEMORY_LIST_COMMAND` to flush:
  * Modified Page List
  * Standby Priority Lists (0 to 7)
  * System Cache and Working Sets
* **Zero Process Restarting**: Unlike rudimentary scripts that kill explorer or services, OmniWin signals the NT Memory Manager directly, safely reclaiming hundreds of megabytes of physical RAM instantly.

### 2. 🔓 Windows Restart Manager File Unlocker (`FileLockService`)
* Direct integration with `rstrtmgr.dll` (`RmStartSession`, `RmRegisterResources`, `RmGetList`).
* Pinpoints the exact Process IDs (PIDs), application names, and window titles locking any file or directory on NTFS, with 1-click safe unlock and termination.

### 3. ⏱️ 0.50 ms Esports Multimedia Timer (`PowerService`)
* Sets system interrupt clock resolution down from the standard 15.6 ms to **0.50 ms** via `timeBeginPeriod` / `NtSetTimerResolution`.
* Drastically reduces input lag, frame-time variance, and mouse jitter in competitive gaming titles.

### 4. 🎮 Customizable Gaming HUD Overlay (`GamingOverlayWindow`)
* **100% Anti-Cheat Safe**: Uses a layered Click-Through window (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`) with global Windows hotkeys. Never hooks DirectX/Vulkan game pipelines or injects DLLs.
* **3 Selectable Styles**:
  1. **RivaTuner / RTSS OSD Mode**: Pure floating telemetry text over 3D game rendering without any background "card" or borders, backed by high-contrast perimeter drop shadow (`DropShadowEffect`).
  2. **Glassmorphic Card Mode**: Modern translucent panel with mini progress bars, thermal alert banners, and session timers.
  3. **Compact Single-Line Bar**: Minimalist horizontal edge status bar.
* **Granular Customization**: Continuous opacity slider (0% pure transparent to 100% solid), scale slider (80% to 160%), individual metric toggles (CPU, Temp CPU, GPU, Temp GPU, RAM, Ping, Session Timer, Clock), and 4-corner snap buttons.
* **Global Hotkeys**:
  * <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>O</kbd>: Toggle HUD visibility anywhere, anytime.
  * <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>L</kbd>: Toggle Locked In-Game Mode (mouse passes through directly to the game) vs Unlocked Configuration Mode.
* **RTSS Shared Memory Sync**: Bi-directional shared memory pipe with RivaTuner Statistics Server when running.

### 5. 🔬 Deep Silicon & PCIe Lane Negotiation Inspector (`HardwareTelemetryControl`)
* GPU-Z style diagnostic engine interrogating NVIDIA / AMD / Intel drivers via SetupAPI, DXGI, and WMI.
* Verifies physical vs negotiated PCIe link width (e.g. alerts if a GPU is running degraded at `x1 Gen 4` instead of `x16 Gen 4`).
* Inspects VBIOS version, Resizable BAR (BAR1 aperture) support, and memory bus widths.

### 6. 📊 Built-in Prometheus Metrics Exporter (`MetricsExporterService`)
* Lightweight embedded HTTP listener on port `9182` (`/metrics`).
* Exposes 8 real-time Prometheus gauges (CPU usage %, RAM used/available, disk queue, system uptime, thread/handle counts) ready for Grafana dashboards.

### 7. 🛡️ Network Stack Healing & Live WFP Firewall Monitor
* **Atomic TCP/IP Stack Reset**: Executes `/flushdns`, `netsh winsock reset`, IPv4/IPv6 stack repair, and ARP cache clearing in a single atomic pipeline.
* **Windows Filtering Platform (WFP) Monitor**: Real-time inspection of active outbound connections, remote IP addresses, PIDs, and Windows Defender Firewall rule states.
* **Encrypted DNS (DoH / DoT) Benchmark**: Latency benchmarks across Cloudflare (1.1.1.1), Google (8.8.8.8), Quad9 (9.9.9.9), and NextDNS with 1-click system configuration.

### 8. 🎯 Intelligent Game Profiler (`GameProfilerControl`)
* Automatically detects active foreground esports games (`cs2.exe`, `valorant.exe`, `cod.exe`, `overwatch.exe`, etc.).
* Dynamically elevates process CPU priority to `High` and assigns High I/O priority, while constraining secondary background processes to `EcoQoS` (efficiency cores).

### 9. 📱 OmniCompanion — Remote Mobile / Tablet Dashboard
* Embedded Kestrel Web & WebSocket server allowing players to monitor live telemetry, inspect thermals, trigger RAM purges, or switch performance profiles from a phone or tablet.
* Zero-friction setup via dynamic QR Code pairing on the local network.

### 10. 🤖 28-Tool MCP Server for AI Agents (`OmniWin.Mcp`)
* Native JSON-RPC stdio Model Context Protocol (MCP) server compatible with Claude Desktop, Antigravity, Cursor, and Ollama.
* Empowers AI agents to diagnose system health, purge memory, manage startup items, heal network issues, unlock files, and audit security events autonomously.

---

## 📸 Screenshots & Visual Interface

| RivaTuner Style (Pure OSD Floating Text) | Glassmorphic Card Style |
|:---:|:---:|
| ![RivaTuner OSD](docs/assets/screenshots/23_gaming_hud_rivatuner.png) | ![Card HUD](docs/assets/screenshots/24_gaming_hud_card.png) |

| HUD Customization Drawer (Scale, Opacity, Metrics) | Compact In-Game Bar (Single-Line) |
|:---:|:---:|
| ![HUD Drawer](docs/assets/screenshots/26_gaming_hud_drawer.png) | ![Compact Bar](docs/assets/screenshots/25b_gaming_hud_compact_locked.png) |

| Deep Silicon & PCIe BAR1 Inspector | Live WFP Firewall & Connection Monitor |
|:---:|:---:|
| ![Hardware Telemetry](docs/assets/screenshots/02_telemetria_live.png) | ![Firewall Monitor](docs/assets/screenshots/21_firewall_monitor.png) |

---

## 🏗️ Repository Architecture

```text
OmniWin/
├── OmniWin.Core/              # Core domain services (NT Memory, Hardware, WFP, Disk, Network, Dism, etc.)
│   ├── Services/              # AppSettingsService, HardwareService, RtssService, MemoryService, etc.
│   └── Models/                # Strongly-typed data models and system telemetry snapshots
├── OmniWin.UI/                # Hardware-accelerated WPF Desktop Application (.NET 9)
│   ├── Views/                 # Specialized panels (GamingOverlay, HardwareTelemetry, GameProfiler, etc.)
│   └── Assets/                # Application icons and vector graphics
├── OmniWin.Cli/               # Standalone headless command-line interface ('omni')
├── OmniWin.Mcp/               # Model Context Protocol (MCP) Server for AI Agents (28 tools)
├── OmniWin.Tests/             # 109 automated unit and STA visual tests (RenderTargetBitmap)
├── distribution/              # Packaging configurations, manifests and WinGet definitions
├── docs/                      # Technical documentation, E2E VM lab guides, ROADMAP.md, and assets
└── scripts/                   # PowerShell distribution build and packaging automation
```

---

## 🛠️ Build & Test Instructions

### Prerequisites
* Windows 10 (Build 19045+) or Windows 11 (Build 22621+)
* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### 1. Build Solution
```bash
dotnet build OmniWin.sln -c Release
```

### 2. Run Test Suite (109 Tests)
```bash
dotnet test OmniWin.Tests/OmniWin.Tests.csproj -c Release
```

### 3. Create Self-Contained Win-x64 Release Archive
```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-distribution.ps1
```
Generates `publish/omniwin-sc.zip` containing CoreCLR embedded with zero external runtime prerequisites.

---

## 🔬 Isolated Lab Testing & Ground-Truth Verification
OmniWin is rigorously audited inside isolated Hyper-V Windows 10 and Windows 11 virtual machines to ensure that all registry tweaks, service optimizations, RAM purges, and kernel resolutions produce measurable changes on real Windows installations.
* See [docs/E2E_VM_TESTING_GUIDE.md](docs/E2E_VM_TESTING_GUIDE.md) for full replication procedures.

---

## 🗺️ Roadmap & Next-Gen Research
For planned features (Ring-0 signed driver integration via PawnIO, dynamic JSON debloat engine, Sysinternals VMMap virtual memory breakdown, DirectStorage BypassIO diagnostics), consult [docs/ROADMAP.md](docs/ROADMAP.md).

---

## 📄 License
OmniWin is open-source software licensed under the [MIT License](LICENSE).

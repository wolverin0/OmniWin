# OmniWin - Windows Deep Optimization, Gaming & Observability Platform (2026)

## 1. Overview & Vision
OmniWin is a high-performance, native .NET 9 WPF application and headless CLI / MCP server designed for Windows 10 and 11. Unlike conventional PowerShell-based script tweakers, OmniWin uses compiled C# with direct Win32 APIs, NT kernel functions, and hardware-level inspection. It has zero external bloat, is 100% anti-cheat safe, and provides rigorous Ground-Truth verification in isolated Windows test labs.

## 2. Current Architecture & Modules

### A. Memory & Kernel Management
- **NT Kernel RAM Purge**: Employs `NtSetSystemInformation` with `SYSTEM_MEMORY_LIST_COMMAND` to flush Modified Page List, Standby Priority Lists (0-7), and Empty Working Sets without restarting processes.
- **Restart Manager (File Locks)**: Uses the Windows Restart Manager API (`RmStartSession`, `RmRegisterResources`, `RmGetList`) to identify which processes or services are holding locks on stubborn files and safely terminate or release them.
- **Windows Multimedia Timer**: Modifies NT timer resolution from standard 15.6 ms down to 0.50 ms via `timeBeginPeriod` for minimum input lag in competitive esports titles.

### B. Hardware Telemetry & Deep Silicon Inspection
- **PCIe Link & BAR1 Inspection (GPU-Z Style)**: Interrogates NVIDIA / AMD drivers via WMI, DXGI and SetupAPI to inspect real-time PCIe lane negotiation (e.g. x16 Gen 4 @ x16 Gen 4 vs downgraded x1/x4 link errors), VBIOS version, and Resizable BAR support.
- **Thermal Alerter & Sensor Aggregator**: Real-time CPU/GPU thermal monitoring with visual alert banners when thresholds (>82°C GPU, >85°C CPU) are breached.
- **Prometheus Metric Exporter**: Built-in HTTP server listening on port 9182 (`/metrics`) exposing 8 live system telemetry gauges for Grafana, Prometheus, or home server dashboards.

### C. Gaming HUD Overlay (Layered Direct3D Window)
- **RivaTuner / RTSS OSD Style**: Crisp floating text over 3D game rendering with zero background card, zero borders, and black contour drop shadow for high legibility over both dark and bright games.
- **Glassmorphic Card & Compact Bar Styles**: Alternating visual presentations with mini progress bars or single-line edge telemetry.
- **Customizable Metrics & Scale**: Granular toggles for CPU, CPU Temp, GPU, GPU Temp, RAM, Ping latency, Session Timer and Clock. Opacity slider (0% pure to 100% solid) and scale (80% to 160%).
- **Anti-Cheat Safe**: Layered transparent click-through window (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`) with global hotkeys:
  - `Ctrl + Shift + O`: Toggle HUD visibility
  - `Ctrl + Shift + L`: Toggle Lock (In-Game Click-Through) / Unlock (Interactive Mouse Dragging & Settings Drawer).
- **RivaTuner Statistics Server Sync**: Bi-directional shared memory integration with RTSS if installed.

### D. Network, Firewall & DNS Security
- **TCP/IP Stack Healing**: Flushes DNS resolver cache, resets Winsock catalog (`netsh winsock reset`), resets IPv4/IPv6 stacks and clears ARP tables in one atomic sequence.
- **Live Windows Filtering Platform (WFP) / Firewall Monitor**: Captures live outbound connections, remote endpoints, process PIDs, and active firewall rules.
- **Encrypted DNS (DoH / DoT) & Resolver Benchmark**: Benchmarks response latencies across Cloudflare (1.1.1.1), Google (8.8.8.8), Quad9 (9.9.9.9) and NextDNS with one-click secure DNS configuration.

### E. Game Profiler & Priority Affinity Optimizer
- Automated process detection for active game executables (`cs2.exe`, `valorant.exe`, `cod.exe`, `overwatch.exe`, etc.).
- Dynamically assigns CPU Priority Class (`High` / `AboveNormal`) and I/O Priority to the active foreground game while restricting background CPU hogs to EcoQoS or low efficiency cores.

### F. OmniCompanion (Remote Mobile / Tablet Web Dashboard)
- Embedded lightweight Kestrel HTTP/WebSocket server allowing gamers to monitor telemetry, trigger RAM purge, or switch profiles from a smartphone, tablet, or secondary screen via a frictionless QR Code pairing.

### G. Isolated Testing & Ground-Truth Verification
- Complete automated E2E testing suite in Hyper-V Windows 10 and Windows 11 virtual machines, auditing real Registry keys, Kernel states, and Service Control Manager statuses.
- 109 passing unit and visual tests with RenderTargetBitmap UI certification.

## 3. Areas for Future Expansion & Research (2026-2027)
1. **Ring-0 Driver Integration**: Signed, HVCI-compliant kernel driver (e.g. PawnIO) to query CPU MSR registers directly (per-core temps, clock stretching) and Super-I/O motherboard chips (fan RPMs, VRM temps) without WMI overhead.
2. **Dynamic JSON Optimization Engine**: Community-shareable debloat profiles (e.g. `Minimal-Esports.json`, `Audio-DAW-LowLatency.json`, `Developer-Docker.json`).
3. **Microsoft Defender Attack Surface Reduction (ASR) Gaming Rules**: Smart exemptions for anti-cheats and mod loaders without disabling Defender protection entirely.
4. **Virtual Memory & Pagefile Fragmentation Analyzer**: Sysinternals VMMap-style breakdown of Working Set, Private Bytes, and Paged/Non-Paged Pools.
5. **DirectStorage & NVMe Bypass Diagnostics**: Telemetry for Windows 11 DirectStorage bypassIO status on PCIe Gen 4/5 SSDs.

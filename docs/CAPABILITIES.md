# OmniWin — Catálogo Maestro de Capacidades, Módulos y Arquitectura

> **Versión**: 1.2.0 Pro  
> **Arquitectura**: 64-bit Native (.NET 9 Self-Contained)  
> **Aceleración Gráfica**: WPF / Direct3D (Sin Electron, consumo < 40 MB RAM)  
> **Servidor IA**: Protocolo MCP (Model Context Protocol) JSON-RPC sobre stdio  
> **Entorno de Pruebas**: Laboratorio Dual Hyper-V (Windows 10 Pro 22H2 & Windows 11 Pro 23H2)

---

## 1. Resumen Ejecutivo

OmniWin es el plano de control definitivo para Windows. Reemplaza más de 8 herramientas de terceros (CCleaner, HWMonitor, Autoruns, Process Hacker, WinUtil, EarTrumpet, IObit Unlocker, Simplewall) en una aplicación de alto rendimiento con tres interfaces complementarias:
1. **Interfaz Gráfica (GUI)**: 22 módulos organizados con navegación por píldoras segmentadas en Fluent Dark Mode.
2. **Servidor MCP para IA**: 28 herramientas expuestas a modelos de lenguaje (Claude Desktop, Cursor, Antigravity, Gemini).
3. **Consola CLI (`omni`)**: 18 comandos para automatización y administración remota o por scripts.

---

## 2. Los 22 Módulos de la Interfaz Gráfica (GUI)

| Pestaña | Nombre | Función Principal | Tecnologías y Mecanismos |
| :---: | :--- | :--- | :--- |
| **1** | **Dashboard Central** | Métricas en tiempo real de CPU, RAM, GPU, 7 unidades de disco y procesos principales. | LibreHardwareMonitor, WMI, DriveInfo, NtQuerySystemInformation |
| **2** | **Telemetría & Hardware** | Sparklines en vivo (60s), métricas Prometheus en `:9182/metrics`, voltajes y ventiladores. | MetricsExporterService, HTTP Listener nativo, Canvas WPF |
| **3** | **OmniCompanion Móvil** | Dashboard web PWA para móvil/tablet emparejado en pantalla mediante código QR sin contraseñas. | CompanionServerService (HTTP+WebSocket :8766), QRCoder, PngByteQRCode |
| **4** | **Forense de Procesos** | Árbol jerárquico padre-hijo, módulos DLL cargados, hilos y sockets TCP en vivo por proceso. | GetExtendedTcpTable, Toolhelp32Snapshot, ProcessDeepDiagService |
| **5** | **Memoria RAM & Purga** | Mapa de bloques de memoria, purga atómica de Standby List y Working Sets en 1 clic. | NtSetSystemInformation (MemoryPurgeStandbyList, EmptyWorkingSets) |
| **6** | **Game & App Profiler** | Detección automática de juegos, enforce de P-Cores (Intel 12ª-14ª/AMD X3D), timer 0.5ms y purga de RAM. | GameProfilerService, NtSetTimerResolution, ProcessorAffinity Mask |
| **7** | **Energía & CPU Cores** | Gestión de planes, modo Ultimate Performance, Core Parking y scheduler de P/E-Cores. | PowerCfg, NtSetTimerResolution, CpuOptimizationService |
| **8** | **Limpieza de Disco** | Análisis y purga segura de temporales, WinUpdate, crash dumps, prefetch y papelera. | DiskService, Shell32, Win32 I/O seguro |
| **9** | **Espacio en Disco** | Analizador visual tipo WizTree: carpetas más pesadas, archivos gigantes (>50MB) y tipos. | Recorrido recursivo optimizado, Fast Directory Walker |
| **10** | **Tweaks & Debloat** | 55 optimizaciones de Registro/Red/Kernel, exportador/importador `.omniwin` y desinstalador UWP. | ExpandedTweakService, SystemSnapshotMigrationService, VSS Restore Points |
| **11** | **Mantenimiento** | Limpieza de almacén WinSxS con DISM (`/ResetBase`) y escaneo SFC sin salir de la app. | DismService, WinSxS, Sfc.exe |
| **12** | **Consola de Reparación** | Pipeline secuencial automatizado de 6 pasos de recuperación y reparación de Windows. | RepairPipelineService, Output streaming en vivo |
| **13** | **Red & Sockets** | Diagnóstico de ping, resolución DNS, interfaces y reparación de red (Flush DNS, Winsock). | HealNetworkAsync, Netsh, Iphlpapi |
| **14** | **Cortafuegos Visual** | Inspección en tiempo real de sockets TCP activos y bloqueo en 1 clic en Windows Firewall. | FirewallMonitorService, GetExtendedTcpTable (iphlpapi.dll), Netsh advfirewall |
| **15** | **DNS Seguro & HOSTS** | Benchmark de latencia DNS (Cloudflare, Quad9, Google, AdGuard) y bloqueo masivo StevenBlack (60k+). | DnsSecurityService, TcpClient latency probing, StevenBlack unified blocklist |
| **16** | **Desbloqueo de Archivos** | File Locksmith: identifica procesos bloqueadores y los termina para liberar archivos. | Restart Manager nativo de Windows (rstrtmgr.dll) |
| **17** | **Programas de Inicio** | Enumeración y alternancia de programas en HKCU/HKLM Run, Startup y Tareas Programadas. | StartupService, RegistryKey, TaskScheduler |
| **18** | **Mezclador de Audio** | Control de volumen individual (0-100%) y mute por aplicación activa. | Windows CoreAudio API (IAudioSessionManager2, ISimpleAudioVolume) |
| **19** | **Reglas Defender ASR** | Matriz de las 16 reglas de Attack Surface Reduction con perfiles de 1 clic (Gamer, Máximo). | AsrRulesService, Defender PowerShell provider, WMI |
| **20** | **Caja Negra & BSOD** | Decodificador de minidumps, códigos BugCheck y registro de eventos críticos Kernel-Power 41. | BsodForensicControl, Minidump reader, Windows EventLog |
| **21** | **Software & Drivers** | Actualizador masivo WinGet, desinstalador profundo con rastros, drivers OEM e info de BIOS. | Winget CLI, Pnputil, DriverStore, WMI Motherboard |
| **22** | **Servidor IA / MCP** | Estado de conectividad MCP, monitor de llamadas JSON-RPC y auto-registro en Claude Desktop. | OmniWin.Mcp stdio server, claude_desktop_config.json |

---

## 3. Asistente Guiado de Optimización (7 Pasos "De Bache en Bache")

Inspirado en la filosofía de **Yamicsoft Windows Manager**, el asistente guía al usuario de forma progresiva a través de 7 pantallas especializadas:

1. **Paso 1: Perfil de Hardware y Detección de Uso**:
   - Detección de rol: *Desktop* (rendimiento fijo), *Laptop* (equilibrio de batería) o *Workstation/Server* (prioridad en segundo plano).
   - Identificación de almacenamiento: Bloqueo de desfragmentación y validación TRIM en SSDs/NVMe (`DisableDeleteNotify = 0`).
2. **Paso 2: Kernel & Sistema de Archivos**:
   - `NtfsMemoryUsage = 2` (Aumento de pool de paginación para I/O de disco).
   - `NtfsDisable8dot3NameCreation = 1` (Desactivación de nombres cortos MS-DOS para acelerar carpetas masivas).
   - `LargeSystemCache = 1` (Caché expandida en RAM para transacciones del sistema).
   - `AutoEndTasks = 1` (Cierre automático de programas colgados al apagar).
   - `HungAppTimeout = 1000ms` y `WaitToKillAppTimeout = 2000ms`.
3. **Paso 3: Arranque y Apagado**:
   - `StartupDelayInMSec = 0` (Elimina la espera artificial de 10s al iniciar sesión).
   - `WaitToKillServiceTimeout = 2000ms` (Apagado ultra rápido de servicios).
   - `AutoChkTimeOut = 2s` (Reduce la espera previa a chkdsk en booteo).
   - Control de Inicio Rápido (`HiberbootEnabled`).
4. **Paso 4: Red e Internet de Baja Latencia**:
   - `autotuninglevel = normal`, `ecncapability = enabled`, `rss = enabled` (Receive Side Scaling en CPU multi-núcleo).
   - `NetworkThrottlingIndex = 0xFFFFFFFF` (Elimina el estrangulamiento de paquetes de red).
   - `SystemResponsiveness = 0` (Asigna el 100% de prioridad de procesamiento a gaming y streaming).
   - DNS Caching optimizado: `MaxCacheTtl = 86400` y `MaxNegativeCacheTtl = 5`.
   - `MaxConnectionsPerServer = 16` (Descargas paralelas masivas en navegadores y APIs).
5. **Paso 5: Fluidez Visual & Explorador**:
   - `MenuShowDelay = 10ms` (Apertura instantánea de menús desplegables).
   - Menú contextual clásico de Windows 10 habilitado en Windows 11.
   - Desactivación de sombras DWM innecesarias y animaciones de maximizado/minimizado lentas.
6. **Paso 6: Servicios en Segundo Plano & Privacidad**:
   - Desactivación de telemetría agresiva (`DiagTrack`, `dmwappushservice`).
   - Bloqueo de 14 servidores de rastreo mediante archivo `HOSTS`.
   - Desactivación de Advertising ID y Windows Error Reporting (`WerSvc`).
7. **Paso 7: Manifiesto de Revisión y Aplicación**:
   - Lista comparativa de valores actuales vs nuevos.
   - Creación obligatoria/opcional de Punto de Restauración del Sistema VSS.
   - Purga de Standby RAM y reinicio opcional.

---

## 4. Servidor MCP para Agentes IA (28 Herramientas JSON-RPC)

El servidor MCP permite que agentes autónomos (Claude, Gemini, ChatGPT) ejecuten diagnósticos y reparaciones directamente:

1. `win_get_system_health`: Telemetría (quick, performance, network, security, full).
2. `win_purge_ram`: Purga de Standby List y Working Sets.
3. `win_analyze_disk_bloat`: Auditoría de temporales, cachés y crash dumps.
4. `win_clean_disk`: Limpieza parametrizada con vaciado de papelera.
5. `win_test_network`: Diagnóstico de red, DNS, ping y sockets TCP.
6. `win_heal_network`: Reparación completa de pila de red.
7. `win_get_security_audit`: Antivirus, UAC y eventos de error o BSOD.
8. `win_get_software_updates`: Consulta a WinGet por paquetes desactualizados.
9. `win_upgrade_all_software`: Actualización silenciosa masiva de apps.
10. `win_clean_dism_store`: Purga profunda de WinSxS con `/ResetBase`.
11. `win_run_sfc_scan`: Comprobador de archivos del sistema SFC.
12. `win_list_drivers`: Auditoría de paquetes OEM en DriverStore.
13. `win_delete_driver`: Eliminación de controladores viejos o duplicados.
14. `win_get_power_schemes`: Planes de energía y timer resolution.
15. `win_set_power_scheme`: Activación de Ultimate Performance o timer 0.5ms.
16. `win_list_processes`: Procesos ordenados por CPU o RAM.
17. `win_kill_process`: Terminación de procesos por PID.
18. `win_list_startup_items`: Programas al inicio en Registro y Tareas.
19. `win_toggle_startup_item`: Habilitar o deshabilitar programas de inicio.
20. `win_list_tweaks`: Estado del catálogo de 55 optimizaciones.
21. `win_apply_tweak`: Aplicación con respaldo VSS.
22. `win_rollback_tweak`: Reversión atómica de cualquier optimización.
23. `win_find_file_locks`: Detección de procesos bloqueadores con Restart Manager.
24. `win_unlock_file`: Liberación forzosa de archivos bloqueados.
25. `win_list_windows_services`: Auditoría de servicios de Windows.
26. `win_set_service_state`: Configuración de inicio y detención de servicios.
27. `win_optimize_services`: Desactivación en 1 clic de servicios de telemetría.
28. `win_list_context_menus` & `win_toggle_context_menu`: Menús contextuales de clic derecho.

---

## 5. Consola CLI (`omni`)

Comandos ejecutables desde PowerShell, CMD o tareas automatizadas:
* `omni status [--profile quick|perf|net|sec|full]`
* `omni ram [purge]`
* `omni clean [--scan|--all]`
* `omni dism [--reset-base]`
* `omni sfc`
* `omni apps` & `omni upgrade`
* `omni drivers`
* `omni power [--ultimate|--timer]`
* `omni process [--top N] [--kill <pid>]`
* `omni startup [--toggle ...]`
* `omni tweak [--apply|--rollback <id>]`
* `omni net [target|heal]`
* `omni security`
* `omni locks <ruta_archivo>` & `omni unlock <ruta_archivo>`
* `omni services [--bloat|--optimize]`
* `omni contextmenu`
* `omni mcp` (inicia el protocolo stdio)

---

## 6. Infraestructura de Pruebas y Validación E2E en Hyper-V

Para garantizar cero regresiones y validación fidedigna de cambios:
* **Entornos Limpios**: `OmniWin-Lab-Win10` (Windows 10 22H2) y `OmniWin-Lab-Win11` (Windows 11 23H2).
* **Conexión Directa**: PowerShell Direct sobre VMBus (`-VMId`) sin dependencia de red.
* **UI Automation**: Conducción programática mediante `InvokePattern` y `AutomationId`.
* **Ground-Truth Matrix**: Verificación directa de claves de registro reales en el sistema operativo para confirmar que cada tweak aplicado persiste en Windows.
* **Suite de Pruebas Automatizadas**: 86 tests unitarios y visuales en xUnit / .NET 9.

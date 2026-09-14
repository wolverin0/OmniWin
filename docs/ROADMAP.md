# OmniWin Roadmap — Windows Control Plane, Optimizer & MCP Agent

Proyecto: **OmniWin** (Mega App Windows: UI Nativa Ultra Veloz + Servidor MCP para IA + CLI de Automatización)  
Ubicación: `C:\Users\pauol\Source\Repos\OmniWin`  
Tecnología base: **C# .NET 9 (Single-File)** + **WPF / Direct3D** (Estilo Optimizer) + **Protocolo MCP (JSON-RPC stdio)**

---

## 🎯 Visión del Proyecto
1. **Centro de Control Unificado**: Reemplazar más de 8 utilidades dispersas (HWMonitor, FanControl, CCleaner, Autoruns, WinUtil, Process Hacker, Simplewall, EarTrumpet, IObit Unlocker) en un único ejecutable sin bloatware.
2. **Servidor MCP para Agentes IA (32 Herramientas)**: Permitir que LLMs (Antigravity, Claude Desktop, Cursor, Ollama) diagnostiquen, limpien, reparen y optimicen el sistema en tiempo real con herramientas nativas.
3. **UI Instantánea (<100ms)**: Interfaz gráfica sin navegadores internos (sin Electron), acelerada por hardware nativo con DirectX en WPF, con consumo menor a 35 MB de RAM y 10 pestañas especializadas.
4. **Seguridad y Transaccionalidad**: Motor de optimizaciones con detección de estado, respaldo automático mediante Puntos de Restauración VSS y capacidad de rollback completo.

---

## 📋 Lista de Tareas y Progreso

### Fase 1: Infraestructura y Núcleo del Sistema (`OmniWin.Core`)
- [x] **1.1** Configuración de la solución .NET 9 en `C:\Users\pauol\Source\Repos\OmniWin`
- [x] **1.2** Módulo de Memoria (`MemoryService`): Purga de *Standby List*, *Working Sets* y *System Cache* mediante API nativa NT (`NtSetSystemInformation`)
- [x] **1.3** Módulo de Almacenamiento & Limpieza (`DiskService`): Escaneo de carpetas temporales, caché de Windows Update (`SoftwareDistribution`), `WinSxS`, miniaturas y papelera
- [x] **1.4** Módulo de Procesos (`ProcessService`): Enumeración de procesos con CPU, memoria privada/compartida, ruta, firma digital y acciones (`kill`, `suspend`, `set_priority`)
- [x] **1.5** Módulo de Telemetría & Hardware (`HardwareService`): Integración con `LibreHardwareMonitorLib` para temperaturas de CPU/GPU, clocks, voltajes y ventiladores
- [x] **1.6** Módulo de Persistencia & Inicio (`StartupService`): Detección de programas en `HKLM/HKCU Run/RunOnce`, carpetas Startup y Tareas Programadas
- [x] **1.7** Módulo de Optimización & Recetas (`TweakService`): Catálogo de tweaks (telemetría, latencia, gaming, privacidad) con creación previa de Puntos de Restauración VSS y rollback
- [x] **1.8** Módulo de Diagnóstico y Reparación de Red (`NetworkService`): Análisis de adaptadores activos, ping a DNS/Gateways, resolución DNS, sockets TCP y `HealNetworkAsync` (Flush DNS, reset Winsock, ARP)
- [x] **1.9** Módulo de Auditoría y Eventos (`SecurityAuditService`): Detección de Antivirus, UAC y eventos críticos de Windows (crashes y BSODs ID 41)
- [x] **1.10** Módulo de Software y Paquetes (`SoftwareService`): Integración desatendida con `winget` para listar apps desactualizadas e instalación/actualización masiva silenciosa
- [x] **1.11** Módulo de Mantenimiento Profundo (`DismService`): Limpieza profunda de WinSxS (`DISM /Online /Cleanup-Image /StartComponentCleanup`), escaneo y reparación SFC (`sfc /scannow`)
- [x] **1.12** Módulo de Controladores (`DriverService`): Detección y desinstalación de controladores OEM de terceros en el DriverStore mediante `pnputil`
- [x] **1.13** Módulo de Energía y Latencia (`PowerService`): Gestión de esquemas de energía (`powercfg`), desbloqueo del plan oculto *Ultimate Performance* y forzado de temporizador de 0.5ms (`NtSetTimerResolution`)
- [x] **1.14** Módulo Desbloqueador de Archivos (`FileLockService`): Detección exacta de procesos que bloquean archivos/carpetas y liberación forzosa usando la API nativa de Windows *Restart Manager* (`rstrtmgr.dll`)
- [x] **1.15** Módulo de Servicios de Windows (`WindowsServiceService`): Identificación y desactivación en 1 clic de servicios de telemetría y diagnósticos pesados (`DiagTrack`, `dmwappushservice`, `MapsBroker`, etc.)
- [x] **1.16** Módulo de Menús Contextuales (`ContextMenuService`): Auditoría y activación/desactivación no destructiva de extensiones shell de clic derecho en Windows

### Fase 2: Servidor MCP para IA (`OmniWin.Core.Mcp` — 32 Herramientas)
- [x] **2.1** Implementación del protocolo MCP (JSON-RPC sobre `stdio`) compatible con clientes Anthropic, Gemini, OpenAI y Claude Desktop
- [x] **2.2** Catálogo completo de 32 herramientas operativas:
  - `win_get_system_health`: Telemetría multi-perfil (quick, performance, network, security, full)
  - `win_purge_ram`: Liberación instantánea de RAM en Standby List y Working Sets
  - `win_analyze_disk_bloat` & `win_clean_disk`: Diagnóstico y limpieza segura de archivos basura
  - `win_test_network` & `win_heal_network`: Latencia, DNS y reparación completa de pila de conexión
  - `win_get_security_audit`: Estado de protección y auditoría de eventos BSOD
  - `win_get_software_updates` & `win_upgrade_all_software`: Actualización desatendida con WinGet
  - `win_clean_dism_store` & `win_run_sfc_scan`: Mantenimiento WinSxS y comprobación de integridad
  - `win_list_drivers` & `win_delete_driver`: Auditoría y limpieza de drivers OEM
  - `win_get_power_schemes` & `win_set_power_scheme`: Planes de energía y timer de 0.5ms
  - `win_list_processes` & `win_kill_process`: Inspección y control de procesos
  - `win_list_startup_items` & `win_toggle_startup_item`: Gestión de programas de arranque
  - `win_list_tweaks`, `win_apply_tweak` & `win_rollback_tweak`: Ajustes con respaldo VSS
  - `win_find_file_locks` & `win_unlock_file`: Identificación y desbloqueo de archivos bloqueados
  - `win_list_windows_services`, `win_set_service_state` & `win_optimize_services`: Gestión y optimización de telemetría
  - `win_list_context_menus` & `win_toggle_context_menu`: Control de menús contextuales de clic derecho
  - `win_pcie_doctor`: Diagnóstico profundo de velocidad y ancho de enlace PCIe y ReBAR
  - `win_bypassio_doctor`: Validación de compatibilidad DirectStorage 1.2 y filtros NVMe
  - `win_stutter_investigate`: Detección forense de jitter de interrupción kernel (`NtDelayExecution`) y DPC
  - `win_cpu_topology`: Auditoría de P-Cores vs E-Cores (`GetSystemCpuSetInformation`) y EcoQoS
- [x] **2.3** Auto-registro configurado en `C:\Users\pauol\AppData\Roaming\Claude\claude_desktop_config.json` para Claude Desktop

### Fase 3: Interfaz de Línea de Comandos (`OmniWin.Cli`)
- [x] **3.1** Comandos ejecutables directamente por scripts o usuarios:
  - `omni status [--profile quick|perf|net|sec|full]`
  - `omni ram [purge]`
  - `omni clean [--scan|--all]`
  - `omni dism [--reset-base]`
  - `omni sfc`
  - `omni apps` & `omni upgrade`
  - `omni drivers`
  - `omni power [--ultimate|--timer]`
  - `omni process [--top N] [--kill <pid>]`
  - `omni startup [--toggle ...]`
  - `omni tweak [--apply|--rollback <id>]`
  - `omni net [target|heal]`
  - `omni security`
  - `omni locks <ruta_archivo>` & `omni unlock <ruta_archivo>`
  - `omni services [--bloat|--optimize]`
  - `omni contextmenu`
  - `omni mcp` (servidor MCP stdio para agentes IA)

### Fase 4: Panel Gráfico Ultrarrápido estilo Optimizer (`OmniWin.UI`)
- [x] **4.1** Ventana WPF nativa acelerada por GPU Direct3D con Fluent Design / Modo Oscuro
- [x] **4.2** Pestaña 1: **📊 Dashboard** (CPU i9-14900K, GPU UHD 770 temp, RAM, 7 unidades y procesos top)
- [x] **4.3** Pestaña 2: **⚡ RAM** (Visualizador de bloques y purga en 1 clic con NT APIs)
- [x] **4.4** Pestaña 3: **🧹 Disco** (Análisis y limpieza de temporales, crash dumps y caché)
- [x] **4.5** Pestaña 4: **🛠️ Tweaks** (Interruptores instantáneos con punto de restauración VSS)
- [x] **4.6** Pestaña 5: **📦 Software** (Actualizador desatendido WinGet y auditor de drivers OEM)
- [x] **4.7** Pestaña 6: **⚙️ Sistema** (Limpieza WinSxS con DISM /ResetBase, SFC y Timer 0.5ms)
- [x] **4.8** Pestaña 7: **🌐 Red** (Diagnóstico, latencia DNS, sockets TCP y Reparación de pila de red)
- [x] **4.9** Pestaña 8: **🔒 Desbloqueo** (File Locksmith con Restart Manager para liberar archivos bloqueados)
- [x] **4.10** Pestaña 9: **🚀 Inicio** (Programas de inicio y desactivador de servicios de telemetría)
- [x] **4.11** Pestaña 10: **🤖 IA / MCP** (Estado del servidor, botón de auto-registro en Claude Desktop y catálogo)

### Fase 5: Binarios Publicados
- [x] **5.1** CLI + Servidor MCP integrado: `C:\Users\pauol\Source\Repos\OmniWin\publish\cli\OmniWin.Cli.exe`
- [x] **5.2** Interfaz Gráfica Instantánea: `C:\Users\pauol\Source\Repos\OmniWin\publish\ui\OmniWin.UI.exe`

### Fase 6: Arquitectura Avanzada de Reparación, Métricas & Resiliencia Multi-SO (100% Completada)
- [x] **6.1 Gestor de Reglas ASR (Attack Surface Reduction) de Microsoft Defender (`AsrRulesService`)**:
  - Matriz completa de las 16 reglas ASR con GUIDs nativos de mitigación de exploits.
  - Opciones de configuración por regla: Bloquear (1), Auditar (2), Desactivado (0).
  - Perfiles inteligentes de 1 clic: Máxima Protección, Desarrollador/Gamer, Desactivar Todas.
  - Control de interfaz: `AsrManagerControl.xaml`.
- [x] **6.2 Pipeline Secuencial de Reparación y Mantenimiento (`RepairPipelineService`)**:
  - Implementación del orden estricto de Microsoft:
    1. `DISM /Online /Cleanup-Image /ScanHealth`
    2. `DISM /Online /Cleanup-Image /RestoreHealth`
    3. `sfc /scannow`
    4. `DISM /Online /Cleanup-Image /StartComponentCleanup /ResetBase`
    5. Reseteo de catálogo de Windows Update (`SoftwareDistribution`, `catroot2`).
    6. Reseteo de pila de red Winsock, TCP/IP y WMI.
  - Consola interactiva embebida con stream en tiempo real (`Consolas`), logs hacker y barra de progreso.
  - Control de interfaz: `RepairConsoleControl.xaml`.
- [x] **6.3 Prometheus Windows Metrics Exporter Embebido (`MetricsExporterService`)**:
  - Servidor HTTP ligero compatible con scrape de Prometheus en `http://localhost:9182/metrics`.
  - Colectores expuestos en tiempo real: CPU, memoria física (total, usada, disponible), espacio en disco C:, uptime, hilos y handles.
  - Control de interfaz: `HardwareTelemetryControl.xaml`.
- [x] **6.4 Diagnóstico Forense y Memoria Virtual de Procesos (`ProcessDeepDiagService`)**:
  - Árbol jerárquico de procesos (Parent-Child Process Tree con PIDs).
  - Enumeración de módulos DLL cargados por proceso (nombre, ruta, versión).
  - Sockets de red TCP activos por proceso en vivo con IP local/remota, puertos y estado (`GetExtendedTcpTable`).
  - Control de interfaz: `ProcessExplorerControl.xaml`.
- [x] **6.5 Catálogo Masivo de 50+ Tweaks & Desinstalador UWP (`ExpandedTweakService` & `UwpDebloatService`)**:
  - 50+ optimizaciones categorizadas: Gaming & Latencia (`Win32PrioritySeparation`, `TcpAckFrequency`, HAGS), Privacidad radical (bloqueador de telemetría a nivel archivo `HOSTS`), Personalización Windows 11 (menú clásico, barra de tareas, widgets), Rendimiento.
  - Desinstalador de Bloatware UWP de fábrica con selección inteligente segura.
  - Control de interfaz: `TweaksDebloatControl.xaml`.

### Fase 7: Pruebas Automatizadas E2E, Cobertura y Verificación Visual de la UI
- [x] **7.1 Suite de Tests Unitarios e Integración (`OmniWin.Tests`)**:
  - Proyecto de pruebas xUnit / .NET 9 testeando todos los servicios del núcleo (`MemoryService`, `DiskService`, `ProcessService`, `NetworkService`, `TweakService`, `SecurityAuditService`, `WindowsServiceService`, `FileLockService`).
  - Pruebas del protocolo MCP (validación de esquemas JSON-RPC, serialización, despacho y manejo de errores). 53 tests automatizados 100% pasando sin fallas.
- [x] **7.2 Automatización E2E de Interfaz Gráfica (`scripts/test-ui.ps1` y `--test-ui`)**:
  - Navegación automatizada por todas las pestañas mediante WPF Dispatcher & Windows UI Automation.
  - Disparo de acciones clave (liberación de RAM, análisis de bloatware, lectura de controladores OEM, diagnóstico de red, auditoría de seguridad).
  - Captura y persistencia de screenshots en `C:\Users\pauol\Pictures\Screenshots\OmniWinTests\`.
- [x] **7.3 Análisis Visual Semántico de Screenshots**:
  - Verificación de renderizado Direct3D, tema Fluent Dark Mode, visualización de métricas de telemetría, alineación de controles y ausencia de fallos visuales en las vistas capturadas.

### Fase 8: Módulos Power-User, Audio CoreAudio y Telemetría Gráfica en Vivo (100% Completada)
- [x] **8.1 Mezclador de Audio por Aplicación (`AudioMixerService`)**:
  - Control de volumen individual (0-100%) y mute por proceso activo mediante Windows CoreAudio API (`IAudioSessionManager2`, `ISimpleAudioVolume`).
  - Detección reactiva de procesos de audio y nombres de aplicación.
  - Control de interfaz: `AudioMixerControl.xaml`.
- [x] **8.2 Telemetría Gráfica Avanzada y Gráficos Sparkline en Vivo (`HardwareTelemetryControl`)**:
  - Gráficos tipo sparklines / minicharts en tiempo real con historial de 60 segundos de CPU (verde degradado) y RAM (cian degradado).
  - Integración directa con servidor de métricas Prometheus en `http://localhost:9182/metrics`.

### Fase 9: Hardware Universal Multi-Vendor (AMD Ryzen, Intel Core, NVIDIA GeForce) (100% Completada)
- [x] **9.1 Optimización Dinámica de CPU y Core Parking (`CpuOptimizationService`, `DynamicThermalProfileService`)**:
  - Control inteligente de EPP (Energy Performance Preference), Core Parking y scheduler de P-Cores / E-Cores.
  - Perfiles automáticos al detectar juegos o tareas de alto rendimiento.
- [x] **9.2 Diagnóstico Profundo NVIDIA (`NvidiaGpuTuningService`)**:
  - Detección de ancho y velocidad de enlace PCIe (Link Width / Speed), versión de VBIOS, ReBAR y advertencia de cuello de botella PCIe.
  - Control de interfaz: `GpuPowerTuningControl.xaml`.

### Fase 10: Analizador de Espacio en Disco Estilo WizTree / TreeSize (100% Completada)
- [x] **10.1 Exploración Visual de Almacenamiento (`DiskSpaceAnalyzerControl.xaml`)**:
  - Recorrido eficiente de sistemas de archivos para listar carpetas más pesadas.
  - Filtro dedicado para archivos gigantes (>50MB).
  - Identificación visual de caché oculta del sistema y volcados de memoria.

### Fase 11: Caja Negra Forense de BSOD y Confiabilidad (100% Completada)
- [x] **11.1 Auditoría de Pantallazos Azules (`BsodForensicControl.xaml`)**:
  - Lectura directa de minidumps en `C:\Windows\Minidump`.
  - Decodificación de códigos BugCheck (`CRITICAL_PROCESS_DIED`, `WHEA_UNCORRECTABLE_ERROR`, `KMODE_EXCEPTION_NOT_HANDLED`).
  - Registro forense de apagados inesperados (Eventos Kernel-Power ID 41) y cierres de sesión anómalos.

### Fase 12: Modo Cafeína Inteligente y Widget Flotante de Escritorio (100% Completada)
- [x] **12.1 Servicio de Prevención de Suspensión (`AwakeService`)**:
  - Implementación con `SetThreadExecutionState` nativo de Windows (modos Indefinido y Temporizado con display activo).
- [x] **12.2 Widget Flotante Compacto (`TrafficMonitorWidgetWindow.xaml`)**:
  - Ventana flotante semi-transparente estilo HUD para monitoreo de tráfico de red, CPU, RAM y temperaturas mientras se juega o trabaja.

### Fase 13: Asistente de Optimización Guiado de 7 Pasos "De Bache en Bache" (100% Completada)
- [x] **13.1 Arquitectura de Flujo Guiado Paso a Paso (`OnboardingWizardControl.xaml`)**:
  - Evolución desde perfiles genéricos (*Gamer/Seguridad*) hacia un asistente guiado categoría por categoría inspirado en Yamicsoft Windows Manager, pero 100% nativo *in-process*.
  - **Paso 1: Sistema**: Detección viva de CPU, RAM, almacenamiento y rol de equipo (`Desktop`, `Laptop`, `Workstation`).
  - **Paso 2: Kernel & Filesystem**: `NtfsMemoryUsage = 2`, desactivación de nombres cortos DOS 8.3, supresión de `LastAccessUpdate`, `Win32PrioritySeparation = 0x26`, `LargeSystemCache = 1`.
  - **Paso 3: Arranque & Apagado**: `StartupDelayInMSec = 0`, `WaitToKillServiceTimeout = 2000`, `HungAppTimeout = 1000`, `AutoChkTimeOut = 2s`, `ClearPageFileAtShutdown = 0`.
  - **Paso 4: Red & Baja Latencia**: Desactivación de Algoritmo de Nagle (`TcpAckFrequency = 1`, `TCPNoDelay = 1`), `NetworkThrottlingIndex = 0xFFFFFFFF`, `SystemResponsiveness = 0`, `MaxConnectionsPerServer = 16`, `MaxCacheTtl = 86400s`.
  - **Paso 5: Fluidez Visual & Explorador**: `MenuShowDelay = 10ms`, menú contextual clásico de Windows 10 en Win11, mostrar extensiones, modo compacto, ocultar widgets de barra de tareas.
  - **Paso 6: Servicios & Privacidad**: Detención y desactivación de `DiagTrack`, bloqueo de 14 dominios de telemetría en archivo `HOSTS`, desactivación de Advertising ID y Windows Error Reporting (WER).
  - **Paso 7: Revisión y Aplicación en 1-Click**: Manifiesto interactivo con recuento 24/25, creación opcional de Punto de Restauración VSS, purga atómica de Standby RAM y transición al Dashboard.

### Fase 14: Infraestructura de Pruebas E2E y Validación Ground-Truth en Hyper-V (100% Completada)
- [x] **14.1 Pipeline de Despliegue Autónomo (*Self-Contained*)**:
  - Empaquetado de `OmniWin.UI` con `-r win-x64 --self-contained true` integrando `coreclr.dll`, eliminando la dependencia del runtime de .NET en entornos de laboratorio limpios.
- [x] **14.2 Conducción Automatizada de UI Automation con `AutomationId` e `InvokePattern`**:
  - Conducción determinista de los 7 pasos del asistente sin depender de resolución o cursor del ratón.
- [x] **14.3 Validación Ground-Truth en Sistema Operativo Dual (Win 10 & Win 11)**:
  - Verificación directa mediante PowerShell Direct en el registro de Windows de que los 6 valores críticos (`StartupDelayInMSec`, `MenuShowDelay`, `MaxConnectionsPerServer`, `NtfsMemoryUsage`, `AutoChkTimeOut`, `MaxCacheTtl`) pasan de valores predeterminados a optimizados.
  - 85 pruebas unitarias 100% pasando en `OmniWin.Tests`.

### Fase 15: Rediseño Global de Pestañas Segmentadas & Eliminación de Inversión de Filas (100% Completada)
- [x] **15.1 Eliminación de la Inversión de Filas de `TabPanel`**:
  - Sustitución de la plantilla clásica de `TabPanel` por un contenedor de fila única horizontal (`ScrollViewer` + `StackPanel Orientation="Horizontal" IsItemsHost="True"`).
  - Las pestañas ahora permanecen 100% fijas horizontalmente, sin envoltura de múltiples filas ni salto/intercambio de posición vertical al hacer clic.
- [x] **15.2 Estilo Segmentado en Píldora/Cápsula (`Luxury Segmented Pill`)**:
  - Fondo de contenedor `#090D18` con esquinas redondeadas (`CornerRadius="8"`).
  - Pestaña seleccionada resaltada en su totalidad como píldora/tarjeta (`#0E243C`), borde vibrante en Cyan Acento (`#0284C7`), texto en blanco puro en negrita y microinteracciones de hover fluidas.
- [x] **15.3 Paridad en Toda la Aplicación**:
  - Herencia global del estilo para todas las vistas con sub-pestañas: `SoftwareDriversControl`, `DiskSpaceAnalyzerControl`, `ProcessExplorerControl` y `TweaksDebloatControl`.

### Fase 16: Pruebas de Regresión Visual Automatizadas & Documentación Maestra (100% Completada)
- [x] **16.1 Test de Regresión Visual xUnit (`TabNavigationVisualTests.cs`)**:
  - Instanciación y renderizado en hilo STA de `SoftwareDriversControl`.
  - Verificación matemática de coordenadas `Y` (`Delta Y = 0`) al alternar entre pestañas.
  - Generación de capturas fidedignas `Tab-Test-Controladores.png` y `Tab-Test-PlacaMadre.png`. Total: 86 tests pasando.
- [x] **16.2 Habilidad de Antigravity (`omniwin-e2e-vm-test`)**:
  - Especificación en `C:\Users\pauol\.gemini\antigravity\skills\omniwin-e2e-vm-test\SKILL.md` con flujos progresivos para pruebas continuas en ambas VMs.
- [x] **16.3 Catálogo Maestro de Capacidades (`docs/CAPABILITIES.md`)**:
  - Especificación técnica exhaustiva de los 18 módulos, 55 tweaks, 28 herramientas MCP y 18 comandos CLI.

---

### Fase 17: Atajos Globales de Teclado & Persistencia de Widget Flotante (100% Completada)
- [x] **17.1 Hook de Teclado Global Nativo (`GlobalHotkeyService`)**:
  - Implementación con API Win32 `RegisterHotKey` y `UnregisterHotKey` vinculada al HwndSource de la ventana principal.
  - Atajos globales no intrusivos registrados:
    - `Ctrl + Shift + O`: Alternar Gaming HUD Overlay (`GamingOverlayWindow`) por encima de cualquier juego o app.
    - `Ctrl + Shift + W`: Alternar Widget Flotante de Red y Temperaturas (`TrafficMonitorWidgetWindow`).
    - `Ctrl + Shift + P`: Purga rápida de memoria RAM en Standby List y Working Sets.
- [x] **17.2 Persistencia de Posición y Configuración (`AppSettingsService`)**:
  - Guardado automático de coordenadas `TrafficWidgetX` y `TrafficWidgetY` al arrastrar el widget (`LocationChanged`).
  - Almacenamiento en `AppData/Local/OmniWin/settings.json` con restauración milimétrica en el siguiente inicio.

### Fase 18: Generador de Reportes de Auditoría en HTML Post-Optimización (100% Completada)
- [x] **18.1 Servicio Autónomo de Reportes (`OptimizationReportService`)**:
  - Generación de informe interactivo y auto-contenido en HTML (sin dependencias externas de CDN).
  - Diseño Glassmorphism / Dark Mode de lujo (`#07090E`), tipografía Segoe UI Variable, y soporte para impresión/PDF.
  - Secciones incluidas:
    - Tarjeta de especificaciones del host (CPU, RAM, GPU, versión exacta de Windows, Perfil de optimización).
    - Tarjetas KPI con total de tweaks aplicados, RAM purgada en vivo, espacio recuperado y estado de Punto de Restauración VSS.
    - Tabla comparativa con categorías, rutas del registro, valores previos y nuevos valores optimizados.
- [x] **18.2 Puntos de Integración en la Interfaz Gráfica**:
  - Botón integrado en el Paso 7 del Asistente Guiado (`OnboardingWizardControl`).
  - Botón integrado en el catálogo de ajustes (`TweaksDebloatControl`).
  - Botón `📄 Reporte` en la barra superior de acciones globales de `MainWindow`.

### Fase 19: Empaquetado Oficial, Publicación WinGet y Actualizaciones (100% Completada)
- [x] **19.1 Manifiestos de Distribución Oficial WinGet**:
  - Estructura estándar de Windows Package Manager en `distribution/winget/`:
    - `pauol.OmniWin.yaml`: Manifiesto de versión (v1.2.0, esquema 1.6.0).
    - `pauol.OmniWin.installer.yaml`: Manifiesto de instalador portable ZIP con comando alias `omniwin`.
    - `pauol.OmniWin.locale.en-US.yaml`: Metadatos completos y descripción en inglés.
    - `pauol.OmniWin.locale.es-ES.yaml`: Metadatos completos y descripción en español.
- [x] **19.2 Script de Compilación y Distribución Automatizado (`scripts/build-distribution.ps1`)**:
  - Publicación desatendida de compilación *Self-Contained* (`win-x64`).
  - Compresión en `publish/omniwin-sc.zip`.
  - Cálculo automático de hash SHA256 e inyección en los manifiestos de WinGet.
- [x] **19.3 Servicio de Comprobación de Actualizaciones (`UpdateCheckService`)**:
  - Conexión ligera a la API de GitHub Releases con análisis SemVer (`1.2.0`).
  - Botón interactivo en la barra de estado inferior (`BtnCheckUpdates`) con notificación y enlace de descarga.

### Fase 20: Sistema de Localización Multi-idioma y Bandeja del Sistema (100% Completada)
- [x] **20.1 Motor de Localización Multi-Idioma (`LocalizationService`)**:
  - Diccionarios en memoria para Español (`es`) y English (`en`) con notificación de eventos de cambio.
  - Botón de alternancia en 1 clic (`BtnLang`) en la barra superior de acciones.
  - Persistencia de la preferencia de idioma en `settings.json`.
- [x] **20.2 Integración Nativa con Bandeja del Sistema (`SystemTrayService`)**:
  - Implementación pura P/Invoke con `Shell_NotifyIconW` (sin conflictos de referencias cruzadas WinForms).
  - Menú contextual rápido: Abrir Dashboard, Purga de RAM, Alternar HUD, Alternar Widget y Salir.
  - Minimización silenciosa a la bandeja con globo de notificación interactivo.

### Fase 21: Módulos Avanzados de Nueva Generación & OmniCompanion Móvil/Tablet (100% Completada)
- [x] **21.1 OmniCompanion (Dashboard Web Móvil/Tablet con Emparejamiento por QR)**:
  - Servidor HTTP y WebSocket local de baja latencia en puerto 8766 (`CompanionServerService`).
  - Generador gestionado de códigos QR (`QRCoder` / `PngByteQRCode`) renderizado en la UI para conexión instantánea desde smartphones o tablets (iOS/Android) en la misma red Wi-Fi sin contraseñas ni fricción.
  - Interfaz web PWA Glassmorphism / Dark Mode transmitiendo telemetría continua (CPU, RAM, GPU, Red) cada segundo.
  - Botones de acción remota desde pantalla táctil: Purga de RAM en caliente, Alternar Cafeína / Awake, y Alternar Gaming HUD Overlay en PC.
  - Control de interfaz: `CompanionServerControl.xaml` con botón `📱 Móvil QR` en barra de acciones superior.
- [x] **21.2 Auto-Game & App Profiler (`GameProfilerService`)**:
  - Detección en tiempo real de juegos y software pesado (CS2, Valorant, Apex, Fortnite, Cyberpunk, Blender, Premiere, etc.).
  - Enforce de afinidad a P-Cores en procesadores híbridos (Intel Core 12ª-14ª Gen / AMD 3D V-Cache) para erradicar el stuttering provocado por E-Cores.
  - Activación de temporizador de ultra-alta precisión a 0.5ms (`NtSetTimerResolution`) para reducir input lag.
  - Purga automática de Standby List en RAM al iniciar la sesión de juego.
  - Control de interfaz: `GameProfilerControl.xaml` con alta y gestión de perfiles.
- [x] **21.3 Cortafuegos Visual & Monitor de Sockets TCP (`FirewallMonitorService`)**:
  - Mapeo en vivo de sockets TCP hacia su ejecutable y PID real mediante P/Invoke a la tabla MIB extendida de `iphlpapi.dll`.
  - Bloqueo en 1 clic de tráfico entrante y saliente en el Firewall de Windows (`OmniWin_Block_`).
  - Gestor de reglas bloqueadas activas con opción de desbloqueo inmediato.
  - Control de interfaz: `FirewallMonitorControl.xaml`.
- [x] **21.4 DNS Seguro (DoH) & Bloqueador Masivo por HOSTS (`DnsSecurityService`)**:
  - Benchmark en tiempo real de latencia hacia Cloudflare (1.1.1.1), Quad9 (9.9.9.9), Google (8.8.8.8) y AdGuard (94.140.14.14).
  - Configuración automática de adaptadores activos y purga de caché (`ipconfig /flushdns`).
  - Sincronización oficial de la lista unificada comunitaria de StevenBlack (60.000+ dominios de telemetría y anuncios redirigidos a `0.0.0.0`) con restauración de HOSTS limpio.
  - Control de interfaz: `DnsSecurityControl.xaml`.
- [x] **21.5 Snapshot & Migrador de Configuración de Sistema (`SystemSnapshotMigrationService`)**:
  - Exportación e importación de paquetes `.omniwin` para clonar o respaldar el estado de los 55+ tweaks, 16 reglas ASR y configuración general.
  - Creación automática de Punto de Restauración VSS previo a la importación.
  - Botones integrados en el encabezado de `TweaksDebloatControl.xaml`.
- [x] **21.6 Cobertura de Pruebas Unitarias Fase 21 (`OmniWin.Tests`)**:
  - 100 pruebas unitarias pasando con 0 fallos (`NewFeaturesAdvancedTests.cs` con 8 tests específicos de QR, telemetría JSON, perfiles, tabla TCP, benchmark DNS y serialización de snapshots).

### Fase 22: Gaming HUD Avanzado estilo RivaTuner (RTSS) & Overlay Desacoplado (100% Completada)
- [x] **22.1 Modo OSD RivaTuner (Pure Floating Text)**:
  - Telemetría flotante limpia sobre render 3D sin tarjetas opacas ni bordes, con sombreado perimetral de alto contraste (`DropShadowEffect`) y legibilidad perfecta en cualquier fondo de juego.
- [x] **22.2 Estilos Alternativos de HUD**:
  - Modo Card Glassmorphism con tarjetas translúcidas, indicadores visuales de color y alertas térmicas.
  - Modo Compact Single-Line Bar para visualización perimetral mínima en el borde superior o inferior de la pantalla.
- [x] **22.3 Cajón de Personalización Continua (`GamingOverlayWindow`)**:
  - Deslizador de opacidad continua de 0% (completamente transparente) a 100% (sólido).
  - Deslizador de escala visual del 80% al 160% para monitores 1080p, 1440p y 4K UHD.
  - Selección individual de métricas: CPU %, Temp CPU, GPU %, Temp GPU, RAM, Ping, Reloj y Tiempo de Sesión.
  - Botones de anclaje rápido a las 4 esquinas de la pantalla con fijación magnética.
- [x] **22.4 Atajos Globales de Teclado In-Game**:
  - `Ctrl + Shift + O`: Alternar visibilidad del HUD sobre cualquier juego sin minimizar.
  - `Ctrl + Shift + L`: Alternar entre modo interactivo de arrastre y modo bloqueado Click-Through (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`).
- [x] **22.5 Canal de Memoria Compartida con RTSS (`RtssService`)**:
  - Integración nativa bidireccional con RivaTuner Statistics Server para sincronización de frametimes y FPS exactos.

### Fase 23: Motores de Diagnóstico de Próxima Generación & MCP 32-Tools (100% Completada)
- [x] **23.1 PCIe Link Health Doctor (`PcieHealthService`, `win_pcie_doctor`)**:
  - Diagnóstico profundo de GPU interrogando controladores NVIDIA, AMD e Intel vía SetupAPI, DXGI y WMI.
  - Detección de degradación del enlace físico (ej. advertencia si una GPU corre a `x1 Gen 4` en vez de `x16 Gen 4`).
  - Verificación de apertura Resizable BAR (BAR1 aperture) y soporte de bus de memoria.
- [x] **23.2 DirectStorage 1.2 BypassIO Doctor (`DirectStorageService`, `win_bypassio_doctor`)**:
  - Validación de compatibilidad con la ruta directa de I/O de Windows 11 para descompresión de texturas por GPU.
  - Detección de controladores de filtro de almacenamiento anticuados que degradan o inhabilitan BypassIO.
- [x] **23.3 Investigador de Micro-Stuttering & Latencia DPC (`StutterInvestigatorService`, `win_stutter_investigate`)**:
  - Detección forense de picos de retardo en interrupciones de sistema, correlación de DPC/ISR y análisis de varianza de frame-times.
- [x] **23.4 Topología de Núcleos Híbridos P/E-Core (`CpuTopologyService`, `win_cpu_topology`)**:
  - Detección de núcleos en silicio real mediante `GetSystemCpuSetInformation`.
  - Diferenciación precisa entre Performance Cores (P-Cores) y Efficiency Cores (E-Cores) y aplicación de `EcoQoS` (`PROCESS_POWER_THROTTLING_EXECUTION_SPEED`) a procesos secundarios.

### Fase 24: Auditoría de Red-Team, Saneamiento Forense del Kernel & Detección XMP/EXPO (100% Completada)
- [x] **24.1 Jitter Forense Real de Kernel (`KernelLatencyService`)**:
  - Reemplazo de bucles de spin vacíos por retardo real de interrupción mediante `NtDelayExecution(-10000)` (1ms relativo).
  - Medición fidedigna de micro-retrasos en el despachador de hilos del kernel de Windows sin consumo sintético de CPU.
- [x] **24.2 Protección de `Dnscache` contra CPU Lock & Dual-Stack DNS (`DnsSecurityService`)**:
  - Límite de seguridad de 2.000 dominios para el archivo `HOSTS`, previniendo que `svchost.exe` (`Dnscache`) entre en bucles de escaneo síncrono del 100% de CPU.
  - Manejo seguro de atributos de archivo de solo lectura (`FileAttributes.ReadOnly`).
  - Soporte completo Dual-Stack IPv4 e IPv6 para servidores DNS (Cloudflare, Quad9, Google, AdGuard) para erradicar fugas de resolución IPv6.
- [x] **24.3 Saneamiento de Consola de Reparación DISM (`RepairPipelineService`)**:
  - Eliminación de la directiva destructiva `/resetbase` del pipeline de mantenimiento automatizado, conservando la capacidad del usuario de desinstalar actualizaciones problemáticas de Windows.
- [x] **24.4 Aislamiento de Tweak de Red Nagle (`ExpandedTweakService`)**:
  - Restricción del tweak `TcpAckFrequency` / `TCPNoDelay` exclusivamente a adaptadores de red físicos con puerta de enlace predeterminada activa, evitando corromper la latencia en adaptadores virtuales (Hyper-V, WSL, VPNs).
- [x] **24.5 Emergency Thermal Guard Dinámico (`EmergencyThermalGuard`)**:
  - Limitación dinámica por software (`PROCTHROTTLEMAX 70`) vía `powercfg` si los planes de energía del sistema no exponen el GUID estándar de Ahorro de Energía.
- [x] **24.6 Enumeración de Disco a Prueba de Fallos (`DiskService`)**:
  - Uso de `EnumerationOptions` con `IgnoreInaccessible = true`, `RecurseSubdirectories = true` y omisión de `ReparsePoint` para evitar excepciones no controladas en enlaces simbólicos y carpetas protegidas.
- [x] **24.7 Detección de Sub-Frecuencia de RAM JEDEC vs XMP/EXPO (`MotherboardBiosService`)**:
  - Detección automática en `MotherboardBiosService` de memorias DDR4/DDR5 operando por debajo del perfil de fábrica (ej. 4800 MT/s en lugar de 6000 MT/s XMP/EXPO) y advertencia de configuración Single-Channel.
- [x] **24.8 Suite de Pruebas Automatizadas 100% Verde (`OmniWin.Tests`)**:
  - 121 pruebas unitarias y de integración pasando sin errores en .NET 9.

---

### Fase 25: Próximos Pasos de Evolución (Propuestas Activas de Innovación)
- [ ] **25.1 OmniCompanion 2.0 (Mobile/Tablet Touch PWA)**:
  - Soporte completo para abrirse desde celular o tablet en la red local mediante código QR sin login ni fricción.
  - Controles táctiles en el celular: selector de estilos de HUD, deslizadores de opacidad/escala en tiempo real, disparador de perfil competitivo y monitor de temperatura de bolsillo.
- [ ] **25.2 Personalización Total del HUD Overlay (Estilo RivaTuner Avanzado)**:
  - Selección de familias tipográficas monoespaciadas (Consolas, Cascadia Code, JetBrains Mono) con renderizado DirectWrite nítido sobre 3D.
  - Selector de métricas activas directamente desde el menú contextual o ventana de configuración (FPS, Frametime ms, 1% Low FPS, 0.1% Low, Temp CPU, Temp GPU, VRAM, RAM, Reloj).
  - Paletas de colores personalizables (Cyan Cyberpunk, Verde Clásico RivaTuner, Blanco Monocromo, Naranja Precisión).
- [ ] **25.3 Motor de Benchmark Empírico A/B (Frametime Diff Científico)**:
  - Grabación de 60 segundos de telemetría de frame-times in-game.
  - Comparativa A/B antes y después de aplicar un ajuste (ej. Timer 0.5ms vs 15.6ms, afinidad P-Cores on/off, EcoQoS on/off).
  - Cálculo estadístico de confianza (percentiles 1% Low, 0.1% Low, desviación estándar de micro-stuttering) para demostrar con datos matemáticos empíricos si el tweak realmente mejoró los frametimes o si fue un placebo.
- [ ] **25.4 Hibernador Inteligente de Launchers & WebViews en Juego**:
  - Detección cuando un juego entra a pantalla completa o primer plano.
  - Asignación automática de EcoQoS y reducción de conjunto de trabajo (Working Set) o suspensión a los procesos secundarios embebidos en navegadores y launchers (`Discord.exe`, `SteamWebHelper.exe`, `EpicGamesLauncher.exe`, `Battle.net.exe`).
  - Restauración instantánea y transparente al salir del juego.




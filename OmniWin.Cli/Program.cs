using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OmniWin.Cli.Commands;
using OmniWin.Core.Services;

namespace OmniWin.Cli;

public class Program
{
    private static readonly MemoryService _memoryService = new();
    private static readonly DiskService _diskService = new();
    private static readonly ProcessService _processService = new();
    private static readonly StartupService _startupService = new();
    private static readonly TweakService _tweakService = new();
    private static readonly NetworkService _networkService = new();
    private static readonly SecurityAuditService _securityAuditService = new();
    private static readonly SoftwareService _softwareService = new();
    private static readonly DismService _dismService = new();
    private static readonly DriverService _driverService = new();
    private static readonly PowerService _powerService = new();
    private static readonly FileLockService _fileLockService = new();
    private static readonly WindowsServiceService _windowsServiceService = new();
    private static readonly ContextMenuService _contextMenuService = new();

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return;
        }

        string cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "status":
                PrintStatus(args);
                break;

            case "ram":
                if (args.Length > 1 && args[1].ToLowerInvariant() == "purge")
                {
                    PurgeRam();
                }
                else
                {
                    PrintRam();
                }
                break;

            case "clean":
                await CleanDiskAsync(args);
                break;

            case "dism":
                await HandleDismAsync(args);
                break;

            case "sfc":
                await HandleSfcAsync();
                break;

            case "apps":
            case "updates":
                await HandleAppsAsync();
                break;

            case "upgrade":
                await HandleUpgradeAsync();
                break;

            case "drivers":
                await Phase27Commands.HandleDriversAsync(args);
                break;

            case "power":
                await HandlePowerAsync(args);
                break;

            case "process":
            case "procs":
                if (args.Contains("--intel"))
                {
                    await Phase27Commands.HandleIntelAsync(args);
                }
                else
                {
                    HandleProcesses(args);
                }
                break;

            case "startup":
                HandleStartup(args);
                break;

            case "tweak":
            case "tweaks":
                HandleTweaks(args);
                break;

            case "net":
            case "network":
                await HandleNetworkAsync(args);
                break;

            case "security":
                HandleSecurity();
                break;

            case "locks":
            case "lockfind":
                HandleLockFind(args);
                break;

            case "unlock":
                HandleUnlock(args);
                break;

            case "services":
            case "svc":
                HandleServices(args);
                break;

            case "contextmenu":
            case "ctx":
                Phase28Commands.HandleContextMenu(args);
                break;

            case "duplicate":
            case "dup":
                await Phase28Commands.HandleDuplicateAsync(args);
                break;

            case "usb":
                await Phase28Commands.HandleUsbAsync(args);
                break;

            case "privacy":
                Phase28Commands.HandlePrivacy(args);
                break;

            case "battery":
                Phase28Commands.HandleBattery(args);
                break;

            case "canary":
                Phase28Commands.HandleCanary(args);
                break;

            case "migrate":
            case "appmove":
                await Phase29Commands.HandleMigrateAsync(args);
                break;

            case "qos":
            case "limit":
                await Phase29Commands.HandleQosAsync(args);
                break;

            case "idle":
                await Phase29Commands.HandleIdleAsync(args);
                break;

            case "spotlight":
                await Phase29Commands.HandleSpotlightAsync(args);
                break;

            case "port":
            case "ports":
                await Phase29Commands.HandlePortAsync(args);
                break;

            case "intel":
            case "inspect":
                await Phase27Commands.HandleIntelAsync(args);
                break;

            case "recover":
            case "recovery":
                await Phase27Commands.HandleRecoveryAsync(args);
                break;

            case "mcp":
                var mcp = new OmniWin.Core.Mcp.McpServer();
                await mcp.RunAsync();
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Comando desconocido: '{cmd}'");
                Console.ResetColor();
                PrintHelp();
                break;
        }
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ____                  _ _       ___       
  / __ \____ ___  ____  (_) |     / (_)___   
 / / / / __ `__ \/ __ \/ /| | /| / / / __ \  
/ /_/ / / / / / / / / / / | |/ |/ / / / / /  
\____/_/ /_/ /_/_/ /_/_/  |__/|__/_/_/ /_/   
  Windows Control Plane, Optimizer & MCP Agent
");
        Console.ResetColor();
        Console.WriteLine("Uso: omni <comando> [opciones]\n");
        Console.WriteLine("Comandos del Sistema:");
        Console.WriteLine("  status [--profile quick|perf|net|sec|full]  Muestra el estado según perfil");
        Console.WriteLine("  ram [purge]                                Muestra RAM o purga Standby y Working Sets");
        Console.WriteLine("  clean [--scan|--all]                       Escanea o limpia temporales y caché");
        Console.WriteLine("  dism [--reset-base]                        Limpieza profunda del almacén WinSxS con DISM");
        Console.WriteLine("  sfc                                        Comprueba y repara archivos corruptos de Windows");
        Console.WriteLine("  apps                                       Lista programas desactualizados (WinGet)");
        Console.WriteLine("  upgrade                                    Actualiza todos los programas obsoletos");
        Console.WriteLine("  drivers [--backup|--restore|--check|--verify] Centro oficial y seguro de controladores");
        Console.WriteLine("  power [--ultimate|--timer]                 Planes de energía y temporizador de 0.5ms");
        Console.WriteLine("  process [--top N] [--kill <pid>] [--intel] Lista procesos o audita con inteligencia");
        Console.WriteLine("  intel [nombre.exe]                         Monitor de inteligencia y seguridad de procesos");
        Console.WriteLine("  recover [--recycle|--shadow|--carve]       Recuperación forense de archivos y deep undelete");
        Console.WriteLine("  startup [--toggle ...]                     Lista o administra inicio de Windows");
        Console.WriteLine("  tweak [--apply|--rollback <id>]            Lista o aplica optimizaciones del sistema");
        Console.WriteLine("  net [target|heal]                          Diagnóstico de red o reparación completa de pila");
        Console.WriteLine("  security                                   Auditoría de Antivirus, UAC y eventos BSOD");
        Console.WriteLine("  locks <ruta_archivo>                       Identifica procesos que bloquean un archivo");
        Console.WriteLine("  unlock <ruta_archivo>                      Termina procesos bloqueadores y libera el archivo");
        Console.WriteLine("  duplicate [--path <dir>] [--hardlink|--delete] Deduplicador con Zero-Copy NTFS");
        Console.WriteLine("  usb [--list] [--eject <x:>] [--test-fake]  Expulsor seguro y test de pendrives falsos");
        Console.WriteLine("  privacy [--audit] [--profile rec|strict|gamer] Centro de privacidad y anti-telemetría");
        Console.WriteLine("  battery [--status] [--extreme-saver on|off] Reporte de degradación de batería y Watts");
        Console.WriteLine("  canary [--status|--start|--stop]           Trampas señuelo contra ransomware en tiempo real");
        Console.WriteLine("  contextmenu [--classic on|off]             Menú clásico de Windows 10 y limpiador de shell");
        Console.WriteLine("  migrate [--source <dir>] [--target <dir>]  Mueve juegos/apps a otro disco con Junctions NTFS");
        Console.WriteLine("  qos [--app <exe>] [--limit <kbps>]         Limita ancho de banda por proceso (Gaming Preset)");
        Console.WriteLine("  idle [--status|--run|--start]              Mantenimiento inteligente en inactividad (Re-Trim, RAM)");
        Console.WriteLine("  spotlight [--scan|--export|--set-wallpaper] Extrae fondos 4K de Windows Spotlight y Bing");
        Console.WriteLine("  port [<puerto>] [--list|--kill <puerto>]   Diagnóstico y liberación de puertos en conflicto");
        Console.WriteLine("  mcp                                        Inicia servidor MCP stdio para agentes IA\n");
    }

    private static void PrintStatus(string[] args)
    {
        string profile = "full";
        int pIdx = Array.IndexOf(args, "--profile");
        if (pIdx != -1 && pIdx + 1 < args.Length) profile = args[pIdx + 1].ToLowerInvariant();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"=== ESTADO DEL SISTEMA (OmniWin - Perfil: {profile.ToUpper()}) ===");
        Console.ResetColor();

        var mem = _memoryService.GetMemoryStats();

        if (profile is "quick")
        {
            Console.WriteLine($"RAM: {mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0):N1} / {mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0):N1} GB ({mem.UsagePercentage:N1}%)");
            var c = _diskService.GetDriveVolumes().FirstOrDefault(d => d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
            if (c != null) Console.WriteLine($"Disco C: {c.FreeBytes / (1024.0 * 1024.0 * 1024.0):N1} GB libres de {c.TotalBytes / (1024.0 * 1024.0 * 1024.0):N1} GB ({c.UsagePercent:N1}% ocupado)");
            Console.WriteLine($"Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours:N1} horas");
            return;
        }

        Console.WriteLine($"\n[MEMORIA RAM]");
        Console.WriteLine($"  Total:      {mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0):N2} GB");
        Console.WriteLine($"  Usada:      {mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0):N2} GB ({mem.UsagePercentage:N1}%)");
        Console.WriteLine($"  Disponible: {mem.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0):N2} GB");

        if (profile is "performance" or "full")
        {
            try
            {
                using var hw = new HardwareService();
                var tele = hw.GetTelemetrySnapshot();
                Console.WriteLine($"\n[HARDWARE & TEMPERATURAS]");
                Console.WriteLine($"  CPU: {tele.CpuName} | Carga: {tele.CpuLoadPercent:N1}% | Temp: {(tele.CpuTemperatureCelsius.HasValue ? $"{tele.CpuTemperatureCelsius:N0}°C" : "N/D")}");
                Console.WriteLine($"  GPU: {tele.GpuName} | Carga: {tele.GpuLoadPercent:N1}% | Temp: {(tele.GpuTemperatureCelsius.HasValue ? $"{tele.GpuTemperatureCelsius:N0}°C" : "N/D")}");

            }
            catch { }
        }

        if (profile is "full")
        {
            Console.WriteLine($"\n[ALMACENAMIENTO]");
            foreach (var drive in _diskService.GetDriveVolumes())
            {
                Console.WriteLine($"  Unidad {drive.Name} [{drive.Label}]: {drive.FreeBytes / (1024.0 * 1024.0 * 1024.0):N1} GB libres de {drive.TotalBytes / (1024.0 * 1024.0 * 1024.0):N1} GB ({drive.UsagePercent:N1}% ocupado)");
            }
        }
    }

    private static void PrintRam()
    {
        var mem = _memoryService.GetMemoryStats();
        Console.WriteLine($"Memoria RAM Total:      {mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0):N2} GB");
        Console.WriteLine($"Memoria RAM Usada:      {mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0):N2} GB ({mem.UsagePercentage:N1}%)");
        Console.WriteLine($"Memoria RAM Disponible: {mem.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0):N2} GB");
    }

    private static void PurgeRam()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Iniciando purga de memoria RAM...");
        Console.ResetColor();

        var res = _memoryService.PurgeMemory(true, true);
        Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(res.Message);
        Console.ResetColor();
    }

    private static async Task CleanDiskAsync(string[] args)
    {
        bool doClean = args.Contains("--all") || args.Contains("--clean");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Analizando basura en disco...");
        Console.ResetColor();

        var bloat = await _diskService.AnalyzeBloatAsync();
        Console.WriteLine($"\nEspacio recuperable detectado: {bloat.TotalBloatBytes / (1024.0 * 1024.0):N0} MB ({bloat.TotalBloatFiles:N0} archivos)");
        Console.WriteLine("----------------------------------------------------------------------");
        foreach (var cat in bloat.Categories)
        {
            Console.WriteLine($"  • [{cat.Id}] {cat.Name}: {cat.SizeBytes / (1024.0 * 1024.0):N0} MB ({cat.FileCount} archivos)");
        }

        if (doClean)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nEjecutando limpieza...");
            Console.ResetColor();

            var cleanRes = await _diskService.CleanBloatAsync(new DiskCleanOptions
            {
                CategoryIdsToClean = bloat.Categories.Select(c => c.Id).ToList(),
                EmptyRecycleBin = true
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(cleanRes.Summary);
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("\nPara limpiar estos archivos, ejecute: omni clean --all");
        }
    }

    private static async Task HandleDismAsync(string[] args)
    {
        bool resetBase = args.Contains("--reset-base");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Iniciando limpieza profunda del almacén de componentes (DISM)... (ResetBase: {resetBase})");
        Console.WriteLine("Este proceso puede tardar un par de minutos mientras Windows purga actualizaciones previas.");
        Console.ResetColor();

        var res = await _dismService.CleanComponentStoreAsync(resetBase);
        Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(res.Summary);
        if (!string.IsNullOrWhiteSpace(res.Output)) Console.WriteLine(res.Output.Trim());
        Console.ResetColor();
    }

    private static async Task HandleSfcAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Iniciando Comprobador de Archivos de Sistema (sfc /scannow)...");
        Console.ResetColor();

        var res = await _dismService.RunSfcScanAsync();
        Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(res.Summary);
        if (!string.IsNullOrWhiteSpace(res.Output)) Console.WriteLine(res.Output.Trim());
        Console.ResetColor();
    }

    private static async Task HandleAppsAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Buscando aplicaciones con actualizaciones pendientes en WinGet...");
        Console.ResetColor();

        var apps = await _softwareService.GetUpgradableAppsAsync();
        if (apps.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ Todas las aplicaciones gestionadas por WinGet están actualizadas a la última versión.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\nSe encontraron {apps.Count} aplicaciones con actualización disponible:\n");
        Console.ResetColor();
        Console.WriteLine($"{"NOMBRE",-30} {"ID",-35} {"INSTALADA",-15} {"DISPONIBLE"}");
        Console.WriteLine(new string('-', 95));

        foreach (var a in apps)
        {
            Console.WriteLine($"{a.Name,-30} {a.Id,-35} {a.InstalledVersion,-15} {a.AvailableVersion}");
        }

        Console.WriteLine("\nPara actualizar todas automáticamente: omni upgrade");
    }

    private static async Task HandleUpgradeAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Iniciando actualización masiva y desatendida vía WinGet...");
        Console.ResetColor();

        string res = await _softwareService.UpgradeAllAsync();
        Console.WriteLine(res);
    }



    private static async Task HandlePowerAsync(string[] args)
    {
        if (args.Contains("--ultimate"))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Desbloqueando plan oculto 'Ultimate Performance'...");
            Console.ResetColor();
            string msg = await _powerService.UnlockUltimatePerformanceSchemeAsync();
            Console.WriteLine(msg);
        }

        if (args.Contains("--timer"))
        {
            var (ok, res) = _powerService.SetHighPrecisionTimer(true);
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(ok ? $"Temporizador ajustado a alta precisión: {res:N1} ms." : "No se pudo cambiar resolución de temporizador.");
            Console.ResetColor();
        }

        var schemes = await _powerService.GetPowerSchemesAsync();
        var (min, max, cur) = _powerService.GetTimerResolution();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\nPlanes de Energía:");
        Console.ResetColor();
        foreach (var s in schemes)
        {
            string act = s.IsActive ? " [ACTIVO] *" : "";
            Console.WriteLine($"  • {s.Name,-30} (GUID: {s.Guid}){act}");
        }

        Console.WriteLine($"\nResolución de Temporizador del Sistema: {cur:N2} ms (Min: {min:N1} ms, Max: {max:N1} ms)");
    }

    private static void HandleProcesses(string[] args)
    {
        if (args.Contains("--kill"))
        {
            int idx = Array.IndexOf(args, "--kill");
            if (idx + 1 < args.Length && int.TryParse(args[idx + 1], out int killPid))
            {
                var res = _processService.KillProcess(killPid);
                Console.WriteLine(res.Message);
                return;
            }
        }

        int top = 15;
        if (args.Contains("--top"))
        {
            int idx = Array.IndexOf(args, "--top");
            if (idx + 1 < args.Length && int.TryParse(args[idx + 1], out int parsedTop)) top = parsedTop;
        }

        var procs = _processService.GetRunningProcesses(top, "memory");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\nTop {top} Procesos con Mayor Consumo de RAM:\n");
        Console.ResetColor();
        Console.WriteLine($"{"PID",-8} {"RAM (MB)",-12} {"PROCESO",-25} {"TÍTULO"}");
        Console.WriteLine(new string('-', 70));

        foreach (var p in procs)
        {
            string title = p.Title.Length > 25 ? p.Title.Substring(0, 22) + "..." : p.Title;
            Console.WriteLine($"{p.Pid,-8} {p.MemoryMB,-12:N1} {p.Name,-25} {title}");
        }
    }

    private static void HandleStartup(string[] args)
    {
        var items = _startupService.GetStartupItems();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\nProgramas en Inicio de Windows:\n");
        Console.ResetColor();
        Console.WriteLine($"{"ESTADO",-12} {"ÁMBITO",-8} {"NOMBRE",-25} {"UBICACIÓN"}");
        Console.WriteLine(new string('-', 75));

        foreach (var it in items)
        {
            string status = it.IsEnabled ? "[ACTIVADO]" : "[DESACTIV.]";
            Console.ForegroundColor = it.IsEnabled ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.WriteLine($"{status,-12} {it.Scope,-8} {it.Name,-25} {it.Location}");
            Console.ResetColor();
        }
    }

    private static void HandleTweaks(string[] args)
    {
        var tweaks = _tweakService.GetTweaks();

        if (args.Contains("--apply"))
        {
            int idx = Array.IndexOf(args, "--apply");
            if (idx + 1 < args.Length)
            {
                string id = args[idx + 1];
                var res = _tweakService.ApplyTweak(id, true);
                Console.WriteLine(res.Message);
                return;
            }
        }

        if (args.Contains("--rollback"))
        {
            int idx = Array.IndexOf(args, "--rollback");
            if (idx + 1 < args.Length)
            {
                string id = args[idx + 1];
                var res = _tweakService.RollbackTweak(id);
                Console.WriteLine(res.Message);
                return;
            }
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\nOptimizaciones del Sistema (Tweaks):\n");
        Console.ResetColor();
        Console.WriteLine($"{"ESTADO",-12} {"CATEGORÍA",-20} {"ID",-28} {"NOMBRE"}");
        Console.WriteLine(new string('-', 85));

        foreach (var tw in tweaks)
        {
            string status = tw.IsApplied ? "[APLICADO]" : "[DEFAULT]";
            Console.ForegroundColor = tw.IsApplied ? ConsoleColor.Green : ConsoleColor.Gray;
            Console.WriteLine($"{status,-12} {tw.Category,-20} {tw.Id,-28} {tw.Name}");
            Console.ResetColor();
        }

        Console.WriteLine("\nPara aplicar una optimización:  omni tweak --apply <id>");
        Console.WriteLine("Para revertir una optimización: omni tweak --rollback <id>");
    }

    private static async Task HandleNetworkAsync(string[] args)
    {
        if (args.Length > 1 && args[1].ToLowerInvariant() == "heal")
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Iniciando reparación integral de la pila de red de Windows...");
            Console.ResetColor();

            var heal = await _networkService.HealNetworkAsync();
            foreach (var step in heal.StepsExecuted)
            {
                Console.WriteLine($"  {step}");
            }

            Console.ForegroundColor = heal.Success ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"\n[RESULTADO]: {heal.Summary}");
            Console.ResetColor();
            return;
        }

        string? target = args.Length > 1 ? args[1] : null;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Ejecutando diagnóstico de red...");
        Console.ResetColor();

        var report = await _networkService.RunDiagnosticsAsync(target);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[ACCESO A INTERNET]: {(report.HasInternetAccess ? "Conectado ✔" : "Sin Conexión ✖")}");
        Console.WriteLine($"[RESOLUCIÓN DNS]:    {(report.DnsResolutionTimeMs >= 0 ? $"{report.DnsResolutionTimeMs:N1} ms" : "Falló")}");
        Console.WriteLine($"[SOCKETS TCP ACTIVOS]: {report.ActiveTcpConnections}");
        Console.ResetColor();

        Console.WriteLine("\n[PRUEBAS DE LATENCIA PING]");
        foreach (var p in report.PingTests)
        {
            string time = p.Success ? $"{p.RoundtripTimeMs} ms" : "Tiempo agotado";
            Console.WriteLine($"  • {p.Host,-18} [{p.IpAddress,-15}] -> {time}");
        }

        Console.WriteLine("\n[INTERFACES DE RED ACTIVAS]");
        foreach (var ni in report.ActiveInterfaces)
        {
            Console.WriteLine($"  • {ni.Name} ({ni.Description})");
            Console.WriteLine($"    IPs: {string.Join(", ", ni.Ipv4Addresses)} | Gateway: {ni.Gateway} | Velocidad: {ni.SpeedMbps} Mbps");
            Console.WriteLine($"    Tráfico: Enviados {ni.BytesSent / (1024.0 * 1024.0):N1} MB / Recibidos {ni.BytesReceived / (1024.0 * 1024.0):N1} MB");
        }
    }

    private static void HandleSecurity()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Ejecutando auditoría rápida de seguridad...");
        Console.ResetColor();

        var audit = _securityAuditService.GetSecurityAudit();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n=== ESTADO DE PROTECCIÓN ===");
        Console.ResetColor();
        Console.WriteLine($"Antivirus detectado: {audit.AntivirusProduct}");
        Console.WriteLine($"UAC (Control de Cuentas): {(audit.UacEnabled ? "Habilitado ✔" : "Deshabilitado ⚠")}");

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n=== EVENTOS CRÍTICOS RECIENTES (Últimas 48h): {audit.RecentCriticalEvents.Count} ===");
        Console.ResetColor();

        if (audit.RecentCriticalEvents.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ No se detectaron fallos críticos o eventos de Kernel-Power (BSOD) recientes.");
            Console.ResetColor();
        }
        else
        {
            foreach (var ev in audit.RecentCriticalEvents)
            {
                Console.WriteLine($"  • [{ev.TimeGenerated:yyyy-MM-dd HH:mm}] ID {ev.EventId,-5} ({ev.Source}): {ev.Message}");
            }
        }
    }

    private static void HandleLockFind(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Uso: omni locks <ruta_archivo>");
            return;
        }

        string path = args[1];
        Console.WriteLine($"Buscando procesos que bloquean: {path} ...");
        var locks = _fileLockService.GetLockingProcesses(path);

        if (locks.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ El archivo no está bloqueado por ningún proceso activo.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\nSe encontraron {locks.Count} proceso(s) bloqueando el archivo:\n");
        Console.ResetColor();
        Console.WriteLine($"{"PID",-8} {"PROCESO",-20} {"TIPO",-15} {"APLICACIÓN"}");
        Console.WriteLine(new string('-', 65));
        foreach (var l in locks)
        {
            Console.WriteLine($"{l.ProcessId,-8} {l.ProcessName,-20} {l.ApplicationType,-15} {l.AppName}");
        }

        Console.WriteLine($"\nPara desbloquear forzosamente: omni unlock \"{path}\"");
    }

    private static void HandleUnlock(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Uso: omni unlock <ruta_archivo>");
            return;
        }

        string path = args[1];
        Console.WriteLine($"Liberando archivo: {path} ...");
        var res = _fileLockService.UnlockFile(path, true);

        Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(res.Message);
        Console.ResetColor();
    }

    private static void HandleServices(string[] args)
    {
        bool optimize = args.Contains("--optimize");
        bool bloatOnly = args.Contains("--bloat") || !optimize;

        if (optimize)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Optimizando y desactivando servicios de telemetría...");
            Console.ResetColor();
            var disabled = _windowsServiceService.OptimizeTelemetryServices();
            foreach (var d in disabled)
            {
                Console.WriteLine($"  ✔ Desactivado y detenido: {d}");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nSe optimizaron {disabled.Count} servicios con éxito.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Listando servicios de Windows (Filtro bloat/telemetría: {bloatOnly})...");
        Console.ResetColor();

        var svcs = _windowsServiceService.GetServices(bloatOnly);
        Console.WriteLine($"\n{"SERVICIO",-22} {"ESTADO",-12} {"INICIO",-12} {"DESCRIPCIÓN"}");
        Console.WriteLine(new string('-', 90));

        foreach (var s in svcs)
        {
            if (s.IsRecommendedToDisable) Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{s.Name,-22} {s.Status,-12} {s.StartMode,-12} {(s.DisplayName.Length > 40 ? s.DisplayName[..40] : s.DisplayName)}");
            Console.ResetColor();
        }

        Console.WriteLine("\nPara optimizar servicios de telemetría en 1 clic: omni services --optimize");
    }

    private static void HandleContextMenu(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Examinando extensiones del Menú Contextual de Windows (Clic derecho)...");
        Console.ResetColor();

        var items = _contextMenuService.GetContextMenuItems();
        Console.WriteLine($"\n{"ESTADO",-12} {"UBICACIÓN",-30} {"NOMBRE"}");
        Console.WriteLine(new string('-', 75));

        foreach (var item in items)
        {
            Console.ForegroundColor = item.IsEnabled ? ConsoleColor.Green : ConsoleColor.DarkGray;
            string status = item.IsEnabled ? "[ACTIVO]" : "[DESACT]";
            Console.WriteLine($"{status,-12} {item.Location,-30} {item.Name}");
            Console.ResetColor();
        }
    }
}

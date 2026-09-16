using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniWin.Core.Services;

namespace OmniWin.Cli.Commands;

public static class Phase28Commands
{
    private static readonly DiskDuplicateService _dupService = DiskDuplicateService.Instance;
    private static readonly UsbDoctorService _usbService = new();
    private static readonly PrivacyShieldService _privacyService = new();
    private static readonly BatteryHealthService _batteryService = new();
    private static readonly RansomwareCanaryService _canaryService = RansomwareCanaryService.Instance;
    private static readonly ContextMenuService _contextMenuService = new();

    // ==========================================
    // 1. DEDUPLICADOR ZERO-COPY (NTFS HARDLINKS)
    // ==========================================

    public static async Task HandleDuplicateAsync(string[] args)
    {
        string targetDir = Directory.GetCurrentDirectory();
        bool useHardlinks = args.Contains("--hardlink") || args.Contains("-l");
        bool deleteDupes = args.Contains("--delete") || args.Contains("-d");

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--path" or "-p" && i + 1 < args.Length)
            {
                targetDir = args[i + 1];
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n=== OMNIWIN ZERO-COPY DEDUPLICATOR ===");
        Console.WriteLine($"Analizando carpeta: {targetDir}");
        Console.ResetColor();

        var progress = new Progress<(int scanned, int foundGroups)>(p =>
        {
            Console.Write($"\rArchivos escaneados: {p.scanned:N0} | Grupos duplicados encontrados: {p.foundGroups}   ");
        });

        var dupes = await _dupService.FindDuplicatesAsync(targetDir, minSizeBytes: 1024 * 100, progress: progress);
        Console.WriteLine("\n");

        if (dupes.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ No se encontraron archivos duplicados mayores a 100 KB.");
            Console.ResetColor();
            return;
        }

        long totalWasted = dupes.Sum(d => d.WastedBytes);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Se encontraron {dupes.Count} grupos de archivos duplicados.");
        Console.WriteLine($"Espacio total desperdiciado: {totalWasted / (1024.0 * 1024.0):N2} MB\n");
        Console.ResetColor();

        int shown = Math.Min(10, dupes.Count);
        for (int i = 0; i < shown; i++)
        {
            var g = dupes[i];
            Console.WriteLine($"[{i + 1}] Tamaño: {g.FileSizeBytes / 1024.0:N1} KB • SHA-256: {g.Sha256Hash[..8]}... ({g.FilePaths.Count} copias)");
            foreach (var p in g.FilePaths)
            {
                Console.WriteLine($"    -> {p}");
            }
        }

        if (useHardlinks)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n[Acción] Reemplazando duplicados por Enlaces Duros NTFS (Zero-Copy)...");
            Console.ResetColor();

            long savedTotal = 0;
            int filesCount = 0;
            foreach (var g in dupes)
            {
                string primary = g.FilePaths[0];
                var res = _dupService.DeduplicateGroupWithHardLinks(g, primary);
                savedTotal += res.BytesSaved;
                filesCount += res.FilesProcessed;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ Proceso completado: {filesCount} archivos convertidos a Hardlinks.");
            Console.WriteLine($"✔ Espacio físico recuperado en disco: {savedTotal / (1024.0 * 1024.0):N2} MB (ambos archivos siguen accesibles).");
            Console.ResetColor();
        }
        else if (deleteDupes)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n[Acción] Eliminando copias redundantes enviándolas a la Papelera...");
            Console.ResetColor();

            int delCount = 0;
            foreach (var g in dupes)
            {
                for (int i = 1; i < g.FilePaths.Count; i++)
                {
                    if (_dupService.DeleteDuplicateFile(g.FilePaths[i], sendToRecycleBin: true))
                    {
                        delCount++;
                    }
                }
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ {delCount} archivos duplicados enviados a la Papelera de Reciclaje.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("\nPara optimizar espacio usa:");
            Console.WriteLine("  omni duplicate --path <dir> --hardlink    (Crea Hardlinks NTFS: ahorra 100% de espacio sin borrar)");
            Console.WriteLine("  omni duplicate --path <dir> --delete      (Envía copias secundarias a la papelera)");
        }
    }

    // ==========================================
    // 2. USB DOCTOR & FAKE FLASH TEST
    // ==========================================

    public static async Task HandleUsbAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN USB & STORAGE DOCTOR ===");
        Console.ResetColor();

        if (args.Contains("--eject"))
        {
            int idx = Array.IndexOf(args, "--eject");
            if (idx + 1 >= args.Length)
            {
                Console.WriteLine("Error: Especifique la letra de unidad (ej: omni usb --eject E:)");
                return;
            }
            string drive = args[idx + 1];
            bool force = args.Contains("--force");

            Console.WriteLine($"Intentando expulsar {drive} de forma segura...");
            var res = await _usbService.SafelyEjectDriveAsync(drive, force);
            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ÉXITO] {res.Message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BLOQUEADO] {res.Message}");
                if (res.LockingProcesses.Count > 0)
                {
                    Console.WriteLine("Procesos reteniendo la unidad:");
                    foreach (var p in res.LockingProcesses)
                    {
                        Console.WriteLine($"  - PID {p.ProcessId}: {p.ProcessName} ({p.ApplicationType})");
                    }
                    Console.WriteLine("\nEjecute con '--force' para terminar estos procesos y forzar la expulsión.");
                }
            }
            Console.ResetColor();
            return;
        }

        if (args.Contains("--test-fake"))
        {
            int idx = Array.IndexOf(args, "--test-fake");
            if (idx + 1 >= args.Length)
            {
                Console.WriteLine("Error: Especifique la unidad USB a probar (ej: omni usb --test-fake E:)");
                return;
            }
            string drive = args[idx + 1];
            long testBytes = 1024L * 1024 * 1024; // 1 GB test por defecto

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--size-gb" && i + 1 < args.Length && int.TryParse(args[i + 1], out int gb))
                {
                    testBytes = (long)gb * 1024 * 1024 * 1024;
                }
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Iniciando prueba de memoria flash en {drive} ({testBytes / (1024.0 * 1024.0 * 1024.0):N1} GB)...");
            Console.WriteLine("Escribiendo y verificando patrones criptográficos. No desconectes la unidad.\n");
            Console.ResetColor();

            var progress = new Progress<(long verified, double mbSec)>(p =>
            {
                Console.Write($"\rProgreso: {p.verified / (1024.0 * 1024.0):N1} MB | Velocidad: {p.mbSec:N1} MB/s   ");
            });

            var res = await _usbService.ValidateFlashCapacityAsync(drive, testBytes, progress);
            Console.WriteLine("\n");

            if (res.Passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[INTEGRIDAD CONFIRMADA] {res.Details}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ALERTA DE FALSIFICACIÓN] {res.Details}");
            }
            Console.ResetColor();
            return;
        }

        // Listar unidades extraíbles
        var drives = _usbService.GetRemovableDrives();
        Console.WriteLine($"Unidades extraíbles / USB detectadas: {drives.Count}\n");

        foreach (var d in drives)
        {
            Console.WriteLine($"• Unidad {d.DriveLetter} [{d.VolumeLabel}] - Formato: {d.FileSystem} ({d.DriveType})");
            Console.WriteLine($"  Capacidad: {d.TotalSizeGB:N1} GB | Libre: {d.FreeSizeGB:N1} GB ({100 - d.UsedPercent:N0}%)");

            var locks = _usbService.GetLockingProcesses(d.DriveLetter);
            if (locks.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠️ {locks.Count} proceso(s) bloqueando la unidad: {string.Join(", ", locks.Select(l => l.ProcessName))}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  ✔ Lista para expulsión segura (0 bloqueos)");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }

    // ==========================================
    // 3. CENTRO DE PRIVACIDAD & ANTI-TELEMETRÍA
    // ==========================================

    public static void HandlePrivacy(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN PRIVACY SHIELD (ANTI-TELEMETRÍA) ===");
        Console.ResetColor();

        if (args.Contains("--profile"))
        {
            int idx = Array.IndexOf(args, "--profile");
            string profStr = idx + 1 < args.Length ? args[idx + 1].ToLowerInvariant() : "recommended";

            PrivacyProfile profile = profStr switch
            {
                "strict" or "max" => PrivacyProfile.StrictPrivacy,
                "gamer" => PrivacyProfile.GamerZeroTelemetry,
                _ => PrivacyProfile.Recommended
            };

            Console.WriteLine($"Aplicando perfil de privacidad '{profile}' con respaldo transaccional...");
            var res = _privacyService.ApplyProfile(profile);
            Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(res.Message);
            Console.ResetColor();
            return;
        }

        if (args.Contains("--toggle"))
        {
            int idx = Array.IndexOf(args, "--toggle");
            if (idx + 1 < args.Length)
            {
                string id = args[idx + 1];
                bool ok = _privacyService.ApplySetting(id, true);
                Console.WriteLine(ok ? $"✔ Ajuste '{id}' activado con éxito." : $"❌ No se pudo aplicar el ajuste '{id}'.");
            }
            return;
        }

        if (args.Contains("--rollback"))
        {
            int idx = Array.IndexOf(args, "--rollback");
            if (idx + 1 < args.Length)
            {
                string id = args[idx + 1];
                bool ok = _privacyService.RollbackSetting(id);
                Console.WriteLine(ok ? $"✔ Rollback completado para '{id}'." : $"❌ No hay transacción previa para revertir '{id}'.");
            }
            return;
        }

        // Auditoría general
        var audit = _privacyService.GetPrivacyAudit();
        Console.WriteLine($"Estado de protecciones de privacidad ({audit.Count(a => a.IsProtected)}/{audit.Count} activas):\n");

        foreach (var item in audit)
        {
            string status = item.IsProtected ? "[PROTEGIDO ✔]" : "[TELEMETRÍA ACTIVA ⚠️]";
            ConsoleColor col = item.IsProtected ? ConsoleColor.Green : ConsoleColor.Yellow;

            Console.ForegroundColor = col;
            Console.Write($"{status,-22} ");
            Console.ResetColor();
            Console.WriteLine($"{item.Title} ({item.SafetyLevel})");
            Console.WriteLine($"   ID: {item.Id} - {item.Description}\n");
        }
    }

    // ==========================================
    // 4. DOCTOR DE BATERÍA & DEGRADACIÓN
    // ==========================================

    public static void HandleBattery(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN BATTERY & POWER DOCTOR ===");
        Console.ResetColor();

        var rep = _batteryService.GetBatteryReport();
        if (!rep.HasBattery)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Este equipo está conectado a corriente fija o no dispone de batería ACPI (PC de escritorio).");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Dispositivo: {rep.DeviceName} ({rep.Manufacturer})");
        Console.WriteLine($"Química:     {rep.Chemistry}");
        Console.WriteLine($"Estado AC:   {(rep.IsPluggedIn ? "Conectado al cargador 🔌" : "Operando con batería 🔋")}");
        Console.WriteLine($"Carga:       {rep.ChargePercent}% {(rep.IsCharging ? "(Cargando...)" : "")}");

        if (rep.EstimatedTimeRemaining.HasValue)
        {
            Console.WriteLine($"Autonomía:   {rep.EstimatedTimeRemaining.Value.Hours}h {rep.EstimatedTimeRemaining.Value.Minutes}m restantes");
        }

        Console.WriteLine($"\n--- SALUD Y DEGRADACIÓN DE CELDAS ---");
        Console.WriteLine($"Capacidad Diseño:  {rep.DesignCapacityMWh:N0} mWh");
        Console.WriteLine($"Capacidad Actual:  {rep.FullChargeCapacityMWh:N0} mWh");
        Console.ForegroundColor = rep.HealthPercent >= 80 ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine($"Salud de Batería:  {rep.HealthPercent:N1}% ({rep.HealthGrade})");
        Console.WriteLine($"Desgaste (Wear):   {rep.WearLevelPercent:N1}%");
        Console.ResetColor();

        if (rep.CycleCount > 0)
        {
            Console.WriteLine($"Ciclos de Carga:   {rep.CycleCount}");
        }

        if (rep.CurrentDischargeRateWatts > 0)
        {
            Console.WriteLine($"Consumo Actual:    {rep.CurrentDischargeRateWatts:N2} Watts");
        }

        if (args.Contains("--extreme-saver"))
        {
            int idx = Array.IndexOf(args, "--extreme-saver");
            bool enable = idx + 1 < args.Length && args[idx + 1].Equals("on", StringComparison.OrdinalIgnoreCase);
            _batteryService.ApplyExtremeBatterySaver(enable);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nModo Ahorro Extremo {(enable ? "ACTIVADO" : "DESACTIVADO")}.");
            Console.ResetColor();
        }
    }

    // ==========================================
    // 5. TRAMPAS CANARIO ANTI-RANSOMWARE
    // ==========================================

    public static void HandleCanary(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN RANSOMWARE CANARY SHIELD ===");
        Console.ResetColor();

        if (args.Contains("--start"))
        {
            _canaryService.StartShield();
            var st = _canaryService.GetStatus();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ Escudo Canario Anti-Ransomware ACTIVO.");
            Console.WriteLine($"Carpetas protegidas ({st.MonitoredFolders.Count}):");
            foreach (var f in st.MonitoredFolders) Console.WriteLine($"  - {f}");
            Console.WriteLine($"Trampas señuelo sembradas: {st.ActiveCanaries.Count}");
            Console.ResetColor();
            return;
        }

        if (args.Contains("--stop"))
        {
            _canaryService.StopShield();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("✔ Escudo Canario detenido y archivos trampa eliminados limpiamente.");
            Console.ResetColor();
            return;
        }

        var status = _canaryService.GetStatus();
        Console.WriteLine($"Estado: {(status.IsActive ? "ACTIVO Y PROTEGIENDO ✔" : "INACTIVO ⏸")}");
        Console.WriteLine($"Trampas canario activas: {status.ActiveCanaries.Count}");
        Console.WriteLine($"Alertas / Ataques bloqueados: {status.BreachesDetected}\n");

        var alerts = _canaryService.GetRecentAlerts();
        if (alerts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Historial de Alertas Recientes:");
            foreach (var a in alerts)
            {
                Console.WriteLine($"[{a.Timestamp:HH:mm:ss}] PID {a.OffendingPid}: {a.OffendingProcessName} -> {a.ActionTaken}");
            }
            Console.ResetColor();
        }
    }

    // ==========================================
    // 6. GESTOR DE MENÚ CONTEXTUAL (CLIC DERECHO)
    // ==========================================

    public static void HandleContextMenu(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN CONTEXT MENU MANAGER ===");
        Console.ResetColor();

        if (args.Contains("--classic"))
        {
            int idx = Array.IndexOf(args, "--classic");
            bool enable = idx + 1 < args.Length && args[idx + 1].Equals("on", StringComparison.OrdinalIgnoreCase);
            bool ok = _contextMenuService.ToggleWindows11ClassicContextMenu(enable);
            if (ok)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✔ Menú contextual clásico de Windows 10 {(enable ? "ACTIVADO" : "DESACTIVADO")}.");
                Console.ResetColor();

                if (args.Contains("--restart-explorer"))
                {
                    _contextMenuService.RestartWindowsExplorer();
                    Console.WriteLine("✔ Explorador de Windows reiniciado.");
                }
                else
                {
                    Console.WriteLine("Reinicie el explorador con 'omni contextmenu --restart-explorer' para ver los cambios.");
                }
            }
            return;
        }

        if (args.Contains("--restart-explorer"))
        {
            _contextMenuService.RestartWindowsExplorer();
            Console.WriteLine("✔ Explorador de Windows reiniciado.");
            return;
        }

        bool isClassic = _contextMenuService.IsWindows11ClassicContextMenuEnabled();
        Console.WriteLine($"Estilo de menú en Windows 11: {(isClassic ? "Clásico (Windows 10 completo)" : "Moderno (con 'Mostrar más opciones')")}\n");

        var items = _contextMenuService.GetContextMenuItems();
        Console.WriteLine($"Total de extensiones de menú encontradas: {items.Count}\n");

        var groups = items.GroupBy(i => i.Location);
        foreach (var g in groups)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{g.Key}]");
            Console.ResetColor();
            foreach (var item in g.Take(6))
            {
                string state = item.IsEnabled ? "[ACTIVO]" : "[DESACTIVADO]";
                Console.WriteLine($"  {state,-15} {item.Name} ({item.ClsidOrCommand[..Math.Min(18, item.ClsidOrCommand.Length)]})");
            }
            Console.WriteLine();
        }
    }
}

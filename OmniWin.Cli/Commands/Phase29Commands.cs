using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmniWin.Core.Services;

namespace OmniWin.Cli.Commands;

public static class Phase29Commands
{
    private static readonly AppMigrationService _migrationService = AppMigrationService.Instance;
    private static readonly NetworkQosService _qosService = NetworkQosService.Instance;
    private static readonly IdleMaintenanceService _idleService = IdleMaintenanceService.Instance;
    private static readonly SpotlightWallpaperService _spotlightService = SpotlightWallpaperService.Instance;
    private static readonly PortConflictService _portService = PortConflictService.Instance;

    // ==========================================
    // 1. MIGRADOR DE APPS & JUEGOS (JUNCTIONS)
    // ==========================================

    public static async Task HandleMigrateAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN APP & GAME MIGRATOR (NTFS JUNCTIONS) ===");
        Console.ResetColor();

        if (args.Contains("--list") || args.Length <= 1)
        {
            var active = _migrationService.GetActiveMigrations();
            if (active.Count == 0)
            {
                Console.WriteLine("No hay migraciones activas registradas.");
            }
            else
            {
                Console.WriteLine($"Migraciones activas ({active.Count}):");
                foreach (var m in active)
                {
                    Console.WriteLine($"• {m.SourcePath} -> {m.TargetPath} ({(m.TotalBytes / (1024.0 * 1024.0 * 1024.0)):F2} GB) [{m.Timestamp:yyyy-MM-dd}]");
                }
            }
            return;
        }

        if (args.Contains("--candidates"))
        {
            string drive = "C:\\";
            int idx = Array.IndexOf(args, "--candidates");
            if (idx + 1 < args.Length && !args[idx + 1].StartsWith("-")) drive = args[idx + 1];

            var cands = _migrationService.FindCandidateFolders(drive);
            Console.WriteLine($"Candidatos encontrados en {drive} ({cands.Count}):");
            foreach (var c in cands.Take(25)) Console.WriteLine($"  📁 {c}");
            return;
        }

        if (args.Contains("--rollback"))
        {
            int idx = Array.IndexOf(args, "--rollback");
            if (idx + 1 >= args.Length)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Especifique la ruta de origen para revertir. Ej: omni migrate --rollback \"C:\\Games\\Doom\"");
                Console.ResetColor();
                return;
            }
            string source = args[idx + 1];
            Console.WriteLine($"Revirtiendo migración de: {source}...");
            var res = await _migrationService.RollbackMigrationAsync(source);
            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✔ {res.Message} ({res.FilesMigrated} archivos restaurados en {res.DurationMs} ms)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✘ {res.Message}");
            }
            Console.ResetColor();
            return;
        }

        string sourcePath = "";
        string targetRoot = "";
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--source" or "-s" && i + 1 < args.Length) sourcePath = args[i + 1];
            if (args[i] is "--target" or "-t" && i + 1 < args.Length) targetRoot = args[i + 1];
        }

        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetRoot))
        {
            Console.WriteLine("Uso:");
            Console.WriteLine("  omni migrate --source \"C:\\Games\\Cyberpunk\" --target \"D:\\Games\"");
            Console.WriteLine("  omni migrate --analyze \"C:\\Games\\Cyberpunk\" --target \"D:\\Games\"");
            Console.WriteLine("  omni migrate --rollback \"C:\\Games\\Cyberpunk\"");
            Console.WriteLine("  omni migrate --candidates C:\\");
            Console.WriteLine("  omni migrate --list");
            return;
        }

        var plan = _migrationService.AnalyzeFolder(sourcePath, targetRoot);
        Console.WriteLine($"Tamaño: {(plan.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0)):F2} GB ({plan.TotalFiles:N0} archivos)");
        Console.WriteLine($"Espacio libre en destino ({plan.TargetDrive}): {(plan.TargetFreeSpaceBytes / (1024.0 * 1024.0 * 1024.0)):F2} GB");

        if (args.Contains("--analyze"))
        {
            Console.WriteLine($"¿Espacio suficiente?: {(plan.HasEnoughSpace ? "SÍ" : "NO")}");
            return;
        }

        Console.WriteLine("Iniciando migración...");
        var prog = new Progress<(long copied, long total, string file)>(p =>
        {
            double pct = p.total > 0 ? (double)p.copied / p.total * 100 : 0;
            Console.Write($"\rProgreso: {pct:F1}% | {p.file,-30}   ");
        });

        var migResult = await _migrationService.MigrateFolderAsync(sourcePath, targetRoot, prog);
        Console.WriteLine("\n");

        if (migResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ {migResult.Message}");
            Console.WriteLine($"  Archivos migrados: {migResult.FilesMigrated:N0} ({(migResult.BytesMigrated / (1024.0 * 1024.0 * 1024.0)):F2} GB) en {migResult.DurationMs} ms");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✘ Fallo: {migResult.Message}");
        }
        Console.ResetColor();
    }

    // ==========================================
    // 2. LIMITADOR DE ANCHO DE BANDA QOS
    // ==========================================

    public static async Task HandleQosAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN NETWORK QOS THROTTLER ===");
        Console.ResetColor();

        if (args.Contains("--gaming"))
        {
            Console.WriteLine("Aplicando Preset Gaming (limitando Steam, Epic, Chrome, Discord a 300 KB/s)...");
            var res = await _qosService.ApplyGamingPresetAsync(300);
            Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(res.Message);
            Console.ResetColor();
            return;
        }

        if (args.Contains("--remove-all"))
        {
            Console.WriteLine("Eliminando todas las políticas QoS creadas por OmniWin...");
            int count = await _qosService.RemoveAllOmniWinLimitsAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✔ {count} políticas eliminadas con éxito.");
            Console.ResetColor();
            return;
        }

        string appName = "";
        long limitKbps = 500;
        bool remove = false;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--app" or "-a" && i + 1 < args.Length) appName = args[i + 1];
            if (args[i] is "--limit" or "-l" && i + 1 < args.Length && long.TryParse(args[i + 1], out var l)) limitKbps = l;
            if (args[i] is "--remove" or "-r") remove = true;
        }

        if (!string.IsNullOrEmpty(appName))
        {
            if (remove)
            {
                var res = await _qosService.RemoveProcessLimitAsync(appName);
                Console.WriteLine(res.Message);
            }
            else
            {
                var res = await _qosService.SetProcessLimitAsync(appName, limitKbps);
                Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(res.Message);
                Console.ResetColor();
            }
            return;
        }

        var policies = await _qosService.ListPoliciesAsync();
        if (policies.Count == 0)
        {
            Console.WriteLine("No hay políticas QoS activas en el sistema.");
        }
        else
        {
            Console.WriteLine($"Políticas QoS activas ({policies.Count}):");
            foreach (var p in policies)
            {
                Console.WriteLine($"• [{p.Name}] App: {p.AppPathName} -> Límite: {p.ThrottleRateKbps:N0} KB/s ({p.ThrottleRateMbps:F2} MB/s)");
            }
        }
    }

    // ==========================================
    // 3. ROBOT DE MANTENIMIENTO EN INACTIVIDAD
    // ==========================================

    public static async Task HandleIdleAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN IDLE MAINTENANCE ROBOT ===");
        Console.ResetColor();

        if (args.Contains("--run") || args.Contains("-r"))
        {
            Console.WriteLine("Ejecutando mantenimiento ahora (Re-Trim SSD, Limpieza temporales, Purga RAM si >80%)...");
            string log = await _idleService.TriggerMaintenanceNowAsync();
            Console.WriteLine(log);
            return;
        }

        if (args.Contains("--start"))
        {
            _idleService.Start();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✔ Monitor de inactividad iniciado.");
            Console.ResetColor();
            return;
        }

        if (args.Contains("--stop"))
        {
            _idleService.Stop();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Monitor de inactividad detenido.");
            Console.ResetColor();
            return;
        }

        var status = _idleService.GetStatus();
        Console.WriteLine($"Estado monitor: {(status.IsMonitoring ? "ACTIVO" : "INACTIVO")}");
        Console.WriteLine($"Inactividad actual del usuario: {status.CurrentIdleSeconds:F1} seg (Umbral: {status.ThresholdSeconds:F0} seg)");
        Console.WriteLine($"Último resumen: {status.LastRunSummary}");
    }

    // ==========================================
    // 4. EXTRACTOR FONDOS WINDOWS SPOTLIGHT 4K
    // ==========================================

    public static async Task HandleSpotlightAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN SPOTLIGHT 4K WALLPAPER EXTRACTOR ===");
        Console.ResetColor();

        if (args.Contains("--set-wallpaper"))
        {
            int idx = Array.IndexOf(args, "--set-wallpaper");
            if (idx + 1 < args.Length)
            {
                string img = args[idx + 1];
                bool ok = _spotlightService.SetAsDesktopWallpaper(img);
                Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(ok ? "✔ Fondo de escritorio actualizado exitosamente." : "✘ Error al aplicar fondo.");
                Console.ResetColor();
            }
            return;
        }

        var wallpapers = _spotlightService.ScanSpotlightAssets();
        Console.WriteLine($"Fondos HD/4K descubiertos en la caché de Windows: {wallpapers.Count}");

        foreach (var w in wallpapers.Take(10))
        {
            Console.WriteLine($"• {w.FileName} | {w.Resolution} | {(w.FileSizeBytes / (1024.0 * 1024.0)):F2} MB | {w.DateCreated:yyyy-MM-dd}");
        }

        if (args.Contains("--export") || args.Contains("-e"))
        {
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Spotlight Wallpapers");
            int count = await _spotlightService.ExportWallpapersAsync(wallpapers.Select(w => w.OriginalFilePath), dest);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✔ {count} fondos exportados con éxito a: {dest}");
            Console.ResetColor();
        }
    }

    // ==========================================
    // 5. RESOLUTOR DE CONFLICTOS EN PUERTOS
    // ==========================================

    public static async Task HandlePortAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n=== OMNIWIN NETWORK PORT CONFLICT RESOLVER ===");
        Console.ResetColor();

        if (args.Contains("--kill") || args.Contains("-k"))
        {
            int idx = Array.IndexOf(args, "--kill") != -1 ? Array.IndexOf(args, "--kill") : Array.IndexOf(args, "-k");
            if (idx + 1 < args.Length && int.TryParse(args[idx + 1], out int portToKill))
            {
                Console.WriteLine($"Liberando puerto {portToKill}...");
                bool ok = await _portService.ReleasePortAsync(portToKill);
                Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(ok ? $"✔ Puerto {portToKill} liberado exitosamente." : $"✘ No se pudo liberar el puerto {portToKill}.");
                Console.ResetColor();
                return;
            }
        }

        for (int i = 1; i < args.Length; i++)
        {
            if (int.TryParse(args[i], out int specificPort))
            {
                var diag = _portService.DiagnosePort(specificPort);
                if (diag.IsOccupied && diag.OccupyingProcess != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[PUERTO {specificPort} EN USO]");
                    Console.WriteLine($"• Proceso: {diag.OccupyingProcess.ProcessName} (PID {diag.OccupyingProcess.ProcessId})");
                    Console.WriteLine($"• Ruta: {diag.OccupyingProcess.ExecutablePath}");
                    Console.WriteLine($"• Servicio: {diag.OccupyingProcess.ServiceTag}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✔ Puerto {specificPort} libre de conflictos.");
                }
                Console.ResetColor();
                return;
            }
        }

        if (args.Contains("--list") || args.Contains("-l"))
        {
            var listeners = _portService.GetListeningPorts();
            Console.WriteLine($"Puertos TCP en escucha activa ({listeners.Count}):");
            foreach (var l in listeners.Take(40))
            {
                Console.WriteLine($"  TCP {l.Port,-6} | PID {l.ProcessId,-6} | {l.ProcessName,-20} | {l.ServiceTag}");
            }
            return;
        }

        Console.WriteLine("Diagnóstico rápido de puertos habituales (Web, Dev, BD):");
        var common = _portService.DiagnoseCommonPorts();
        foreach (var c in common)
        {
            if (c.IsOccupied)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [!] Puerto {c.Port,-6} OCUPADO  -> {c.Description}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  [✔] Puerto {c.Port,-6} DISPONIBLE");
                Console.ResetColor();
            }
        }
    }
}

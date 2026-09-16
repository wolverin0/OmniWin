using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmniWin.Core.Services;

namespace OmniWin.Cli.Commands;

public static class Phase27Commands
{
    private static readonly DriverCenterService _driverCenter = DriverCenterService.Instance;
    private static readonly ProcessIntelligenceService _processIntel = new();
    private static readonly FileRecoveryService _fileRecovery = new();

    // ==========================================
    // 1. DRIVER CENTER COMMANDS
    // ==========================================

    public static async Task HandleDriversAsync(string[] args)
    {
        if (args.Contains("--backup"))
        {
            int idx = Array.IndexOf(args, "--backup");
            string backupDir = (idx + 1 < args.Length && !args[idx + 1].StartsWith("--"))
                ? args[idx + 1]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OmniWin_Driver_Backup");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Iniciando respaldo OEM completo de controladores hacia:\n  {backupDir}\n");
            Console.ResetColor();

            var res = await _driverCenter.BackupAllDriversAsync(backupDir);
            if (res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ÉXITO] {res.Message}");
                Console.WriteLine($"Total controladores exportados: {res.ExportedCount}");
                Console.WriteLine($"Carpeta de respaldo: {res.DestinationPath}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {res.Message}");
            }
            Console.ResetColor();
            return;
        }

        if (args.Contains("--restore"))
        {
            int idx = Array.IndexOf(args, "--restore");
            if (idx + 1 >= args.Length || args[idx + 1].StartsWith("--"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Debe especificar la carpeta de controladores a restaurar.");
                Console.WriteLine("Uso: omni drivers --restore <carpeta_con_archivos_inf>");
                Console.ResetColor();
                return;
            }

            string restoreDir = args[idx + 1];
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Instalando controladores desde: {restoreDir}...");
            Console.ResetColor();

            var res = await _driverCenter.RestoreDriversAsync(restoreDir);
            Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(res.Message);
            Console.ResetColor();
            return;
        }

        if (args.Contains("--check"))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Consultando servidores oficiales de Microsoft Update (WHQL)...");
            Console.ResetColor();

            var wuUpdates = await _driverCenter.CheckWindowsUpdateDriversAsync();
            var nvdUpdate = await _driverCenter.CheckNvidiaDriverUpdateAsync();

            if (wuUpdates.Count == 0 && nvdUpdate == null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Todos los controladores están al día según los servidores oficiales de Microsoft Update.");
                Console.ResetColor();
            }
            else
            {
                if (wuUpdates.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\nActualizaciones WHQL oficiales disponibles ({wuUpdates.Count}):");
                    Console.ResetColor();
                    foreach (var u in wuUpdates)
                    {
                        Console.WriteLine($"  • {u.DeviceName} (Versión: {u.LatestVersion}, Proveedor: {u.Provider})");
                    }
                }

                if (nvdUpdate != null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nGPU NVIDIA detectada: {nvdUpdate.DeviceName}");
                    Console.WriteLine($"  Versión instalada: {nvdUpdate.CurrentVersion}");
                    Console.WriteLine($"  Descarga oficial directa: {nvdUpdate.DownloadUrl}");
                    Console.ResetColor();
                }
            }
            return;
        }

        if (args.Contains("--verify"))
        {
            int idx = Array.IndexOf(args, "--verify");
            if (idx + 1 >= args.Length)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Uso: omni drivers --verify <archivo_ejecutable_o_sys>");
                Console.ResetColor();
                return;
            }

            string fPath = args[idx + 1];
            var sig = DriverCenterService.VerifyFileSignature(fPath);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"=== Verificación Authenticode: {Path.GetFileName(fPath)} ===");
            Console.ResetColor();
            Console.WriteLine($"  Firmado digitalmente: {(sig.IsSigned ? "SÍ" : "NO")}");
            Console.WriteLine($"  Certificado confiable: {(sig.IsTrusted ? "SÍ" : "NO")}");
            Console.WriteLine($"  Firmante: {sig.SignerName}");
            Console.WriteLine($"  Emisor: {sig.IssuerName}");
            Console.WriteLine($"  Estado: {sig.StatusMessage}");
            return;
        }

        if (args.Contains("--history"))
        {
            var hist = _driverCenter.GetDriverHistory();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"=== Historial de Controladores ({hist.Count} registros) ===");
            Console.ResetColor();
            foreach (var h in hist)
            {
                Console.WriteLine($"[{h.Timestamp:yyyy-MM-dd HH:mm}] {h.Action,-8} {h.DeviceName,-30} Ver: {h.InstalledVersion} ({h.Source})");
            }
            return;
        }

        // Default: Enumerate categorized devices and drivers
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Analizando dispositivos y controladores instalados en Windows...");
        Console.ResetColor();

        var drivers = await _driverCenter.EnumerateDriversAsync();
        var groups = drivers.GroupBy(d => d.Category).OrderBy(g => g.Key.ToString());

        foreach (var group in groups)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n=== Categoría: {group.Key.ToString().ToUpper()} ({group.Count()} dispositivos) ===");
            Console.ResetColor();
            Console.WriteLine($"{"DISPOSITIVO",-40} {"PROVEEDOR",-22} {"FECHA/VERSIÓN",-20} {"INF"}");
            Console.WriteLine(new string('-', 100));

            foreach (var d in group.Take(15))
            {
                string devName = d.DeviceName.Length > 38 ? d.DeviceName[..38] : d.DeviceName;
                string prov = d.ProviderName.Length > 20 ? d.ProviderName[..20] : d.ProviderName;
                string ver = d.DriverVersion.Length > 18 ? d.DriverVersion[..18] : d.DriverVersion;
                Console.WriteLine($"{devName,-40} {prov,-22} {ver,-20} {d.InfName}");
            }
        }

        Console.WriteLine("\nOpciones de controladores:");
        Console.WriteLine("  omni drivers --backup [ruta]    Respaldo OEM completo de todos los controladores a carpeta");
        Console.WriteLine("  omni drivers --restore <ruta>   Restaura e instala controladores desde respaldo");
        Console.WriteLine("  omni drivers --check            Consulta actualizaciones oficiales WHQL de Microsoft y NVIDIA");
        Console.WriteLine("  omni drivers --verify <archivo> Verifica firma digital Authenticode contra alteraciones");
        Console.WriteLine("  omni drivers --history          Muestra historial de actualizaciones y respaldos");
    }

    // ==========================================
    // 2. PROCESS INTELLIGENCE COMMANDS
    // ==========================================

    public static async Task HandleIntelAsync(string[] args)
    {
        // Case A: Specific process query (e.g. "omni intel svchost.exe")
        string? targetName = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var def = _processIntel.GetProcessInfo(targetName);
            if (def != null)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n=== Inteligencia de Proceso: {def.ProcessName} ===");
                Console.ResetColor();
                Console.WriteLine($"  Nombre descriptivo: {def.DisplayName}");
                Console.WriteLine($"  Desarrollador:      {def.Company}");
                Console.WriteLine($"  Categoría:          {def.Category}");
                Console.WriteLine($"  Esencial de Windows:{(def.IsEssential ? "SÍ" : "NO")}");
                Console.WriteLine($"  Seguridad al cerrar:{def.SafetyImpact}");
                Console.WriteLine($"\n  ¿Qué es y qué hace?:");
                Console.WriteLine($"  {def.Description}");
                Console.WriteLine($"\n  Influencia en el sistema:");
                Console.WriteLine($"  {def.RoleAndInfluence}");
                Console.WriteLine($"\n  Consecuencias al terminar:");
                Console.WriteLine($"  {def.ImpactDetails}");
                if (def.ExpectedPathPattern != null)
                {
                    Console.WriteLine($"\n  Ruta legítima obligatoria: {def.ExpectedPathPattern}");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"El proceso '{targetName}' no está en la base de datos predeterminada.");
                Console.WriteLine($"Ejecuta 'omni intel' para auditarlo activamente en memoria.");
                Console.ResetColor();
            }
            return;
        }

        // Case B: Full system process audit
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Realizando auditoría de seguridad e inteligencia sobre todos los procesos activos...");
        Console.ResetColor();

        var audit = await _processIntel.AuditRunningProcessesAsync(topMemoryCount: 15);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n=== RESUMEN DE PROCESOS ACTIVOS ===");
        Console.ResetColor();
        Console.WriteLine($"  Total de procesos escaneados:   {audit.TotalProcesses}");
        Console.WriteLine($"  Procesos del Núcleo / Windows:  {audit.SystemCoreCount}");
        Console.WriteLine($"  Procesos de Terceros/Software:  {audit.ThirdPartyCount}");
        Console.WriteLine($"  Procesos Sospechosos:           {audit.SuspiciousCount}");
        Console.WriteLine($"  Alertas de Suplantación:        {audit.MasqueradingThreatsCount}\n");

        if (audit.FlaggedProcesses.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("⚠️ ALERTAS DE SEGURIDAD DETECTADAS:");
            Console.ResetColor();
            foreach (var flag in audit.FlaggedProcesses)
            {
                Console.WriteLine($"  • PID {flag.Pid,-6} {flag.ProcessName,-20} [{flag.ThreatLevel}]");
                Console.WriteLine($"    Ruta: {flag.ExecutablePath}");
                Console.WriteLine($"    Veredicto: {flag.SecurityVerdict}");
                if (!string.IsNullOrEmpty(flag.OnlineLookupUrl))
                {
                    Console.WriteLine($"    Análisis online: {flag.OnlineLookupUrl}");
                }
            }
            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== Top Procesos por Consumo de Memoria RAM ===");
        Console.ResetColor();
        Console.WriteLine($"{"PID",-7} {"PROCESO",-24} {"EMPRESA",-25} {"RAM (MB)",-10} {"CATEGORÍA",-16} {"SEGURIDAD"}");
        Console.WriteLine(new string('-', 100));

        foreach (var p in audit.TopMemoryProcesses)
        {
            string pName = p.ProcessName.Length > 22 ? p.ProcessName[..22] : p.ProcessName;
            string comp = p.Company.Length > 23 ? p.Company[..23] : p.Company;
            Console.WriteLine($"{p.Pid,-7} {pName,-24} {comp,-25} {p.MemoryMB,-10:N1} {p.Category,-16} {p.SafetyImpact}");
        }

        Console.WriteLine("\nConsejo: Para saber qué hace un proceso exacto, escribe: omni intel <nombre.exe>");
    }

    // ==========================================
    // 3. FILE RECOVERY COMMANDS
    // ==========================================

    public static async Task HandleRecoveryAsync(string[] args)
    {
        if (args.Contains("--shadow"))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Consultando instantáneas de volumen (Volume Shadow Copies / VSS)...");
            Console.ResetColor();

            var shadows = await _fileRecovery.GetShadowCopiesAsync();
            if (shadows.Count == 0)
            {
                Console.WriteLine("No se encontraron instantáneas de volumen activas en el equipo.");
                Console.WriteLine("Puedes crear puntos de restauración del sistema para habilitar versiones anteriores.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nInstantáneas de volumen encontradas: {shadows.Count}\n");
                Console.ResetColor();
                foreach (var s in shadows)
                {
                    Console.WriteLine($"  • ID: {s.ShadowCopyId}");
                    Console.WriteLine($"    Volumen original: {s.OriginalVolume}");
                    Console.WriteLine($"    Fecha de creación: {s.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
                    Console.WriteLine($"    Ruta de instantánea: {s.ShadowVolumeName}\n");
                }
            }
            return;
        }

        if (args.Contains("--restore-recycle"))
        {
            int idx = Array.IndexOf(args, "--restore-recycle");
            if (idx + 1 >= args.Length)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Uso: omni recover --restore-recycle <id_o_nombre> [carpeta_destino]");
                Console.ResetColor();
                return;
            }

            string token = args[idx + 1];
            string destDir = (idx + 2 < args.Length && !args[idx + 2].StartsWith("--"))
                ? args[idx + 2]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OmniWin_Recovered");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Buscando elemento '{token}' en papelera forense...");
            Console.ResetColor();

            var items = await _fileRecovery.EnumerateRecycleBinAsync();
            var target = items.FirstOrDefault(i => i.Id.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                                                   i.FileName.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                                                   i.RFilePath.Contains(token, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"No se encontró el elemento '{token}' en la papelera.");
                Console.ResetColor();
                return;
            }

            bool ok = await _fileRecovery.RestoreRecycleBinItemAsync(target, destDir);
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(ok ? $"[ÉXITO] Archivo restaurado correctamente en: {destDir}\\{target.FileName}" : "[ERROR] No se pudo restaurar el archivo.");
            Console.ResetColor();
            return;
        }

        if (args.Contains("--carve"))
        {
            int idx = Array.IndexOf(args, "--carve");
            if (idx + 1 >= args.Length)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Uso: omni recover --carve <ruta_archivo_o_imagen> [carpeta_destino]");
                Console.ResetColor();
                return;
            }

            string src = args[idx + 1];
            if (!File.Exists(src))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Archivo o imagen no existe: {src}");
                Console.ResetColor();
                return;
            }

            string outDir = (idx + 2 < args.Length && !args[idx + 2].StartsWith("--"))
                ? args[idx + 2]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OmniWin_Carved");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Ejecutando Deep Byte Carving sobre '{src}'...");
            Console.ResetColor();

            using var fs = File.OpenRead(src);
            var res = await _fileRecovery.CarveFilesAsync(fs, outDir);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[ÉXITO] Deep Carving completado en {res.Duration.TotalSeconds:N1}s");
            Console.WriteLine($"Archivos recuperados: {res.TotalFilesCarved} ({res.TotalBytesCarved / 1024.0:N1} KB)");
            Console.WriteLine($"Carpeta de salida: {outDir}");
            Console.ResetColor();

            foreach (var f in res.Files.Take(20))
            {
                Console.WriteLine($"  • [{f.FileType.ToUpper()}] Offset 0x{f.StreamOffset:X8} - {f.FileSizeBytes / 1024.0:N1} KB -> {Path.GetFileName(f.FilePath)}");
            }
            return;
        }

        // Default: Scan Recycle Bin across all drives
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Analizando Papelera de Reciclaje ($Recycle.Bin) a nivel forense en todas las unidades...");
        Console.ResetColor();

        var rbList = await _fileRecovery.EnumerateRecycleBinAsync();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\nElementos eliminados recuperables encontrados: {rbList.Count}\n");
        Console.ResetColor();

        Console.WriteLine($"{"ID",-8} {"ARCHIVO ORIGINAL",-35} {"TAMAÑO",-12} {"FECHA ELIMINACIÓN",-20} {"DISPONIBLE"}");
        Console.WriteLine(new string('-', 90));

        foreach (var item in rbList.Take(30))
        {
            string name = item.FileName.Length > 33 ? item.FileName[..33] : item.FileName;
            string size = $"{item.FileSizeBytes / 1024.0:N1} KB";
            string date = item.DeletedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            string avail = item.IsContentAvailable ? "[SÍ]" : "[NO]";

            Console.WriteLine($"{item.Id,-8} {name,-35} {size,-12} {date,-20} {avail}");
        }

        Console.WriteLine("\nOpciones de recuperación:");
        Console.WriteLine("  omni recover --restore-recycle <id> [dest]   Restaura archivo con nombre y timestamp original");
        Console.WriteLine("  omni recover --shadow                       Explora copias previas de volumen (VSS)");
        Console.WriteLine("  omni recover --carve <archivo> [dest]       Deep carving por firmas mágicas (JPG, PNG, PDF, ZIP)");
    }
}

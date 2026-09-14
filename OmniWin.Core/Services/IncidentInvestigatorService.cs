using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public enum IncidentCategory
{
    BSOD,
    KernelPower,
    Shutdown,
    HardwareWHEA,
    AppHang
}

public class IncidentRecord
{
    public DateTime Timestamp { get; set; }
    public IncidentCategory Category { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public string BugcheckCodeHex { get; set; } = string.Empty;
    public string BugcheckName { get; set; } = string.Empty;
    public string InitiatorOrProcess { get; set; } = string.Empty;
    public string DumpPath { get; set; } = string.Empty;
    public string RawDetails { get; set; } = string.Empty;
    public string DiagnosticVerdict { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = "#3B82F6";
    public string Icon { get; set; } = "ℹ️";
}

public class IncidentInvestigatorService
{
    private static readonly Dictionary<string, (string Name, string Description, string Recommendation)> KnownBugchecks = new(StringComparer.OrdinalIgnoreCase)
    {
        { "0x00000133", ("DPC_WATCHDOG_VIOLATION", "Un controlador del kernel se colgó o ejecutó en un nivel de interrupción DPC excesivamente largo sin ceder el control.", "Actualiza los controladores de GPU (NVIDIA/AMD), chipset de placa madre o almacenamiento NVMe. Desactiva perfiles de undervolt inestables.") },
        { "0x1000007e", ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Un hilo del sistema en segundo plano generó una excepción que el manejador de errores no pudo capturar.", "Generalmente causado por controladores de terceros defectuosos o memoria RAM inestable (XMP/EXPO). Ejecuta sfc /scannow y actualiza drivers.") },
        { "0x0000007e", ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Excepción no controlada en hilo del kernel.", "Comprueba archivos del sistema corruptos y controladores de periféricos recientemente instalados.") },
        { "0x00000050", ("PAGE_FAULT_IN_NONPAGED_AREA", "El sistema operativo intentó acceder a una dirección de memoria virtual inválida que no residía en memoria física.", "Fallo en módulo de memoria RAM física, timings de XMP inestables, o fallo de controlador de antivirus/anticheat.") },
        { "0x0000000a", ("IRQL_NOT_LESS_OR_EQUAL", "Un proceso en modo kernel intentó acceder a memoria paginable a un nivel de IRQL demasiado elevado.", "Driver incompatible o corrupto. Reinstala controladores de red o tarjeta gráfica en limpio.") },
        { "0x0000003b", ("SYSTEM_SERVICE_EXCEPTION", "Ocurrió una excepción mientras se ejecutaba una rutina desde el código de transición de servicio del sistema.", "Muy frecuente por drivers de video (DirectX/nvlddmkm) o corrupción de archivos DLL de Windows.") },
        { "0x000000d1", ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Un controlador intentó acceder a memoria paginable a un nivel de petición de interrupción (IRQL) no permitido.", "Identifica el archivo .sys culpable en los detalles del volcado y reinstala ese controlador.") },
        { "0x00000124", ("WHEA_UNCORRECTABLE_ERROR", "Error de hardware fatal detectado por la Arquitectura de Errores de Hardware de Windows (WHEA).", "Inestabilidad física de hardware: CPU con voltaje insuficiente (undervolt agresivo), sobrecalentamiento crítico de CPU/VRMs o fallo en bus PCIe.") },
        { "0x00000116", ("VIDEO_TDR_FAILURE", "El controlador gráfico no respondió a tiempo a la señal del planificador y Windows ejecutó una recuperación por timeout (TDR).", "Driver de GPU colgado por overclock inestable de VRAM, fallo de alimentación en PCIe o driver corrupto. Ejecuta DDU y reinstala.") },
        { "0x0000009f", ("DRIVER_POWER_STATE_FAILURE", "Un controlador no completó un cambio de estado de energía (suspensión/reanudación) en el tiempo asignado.", "Controlador de Wi-Fi, Bluetooth o USB que no gestiona correctamente los estados de energía de Windows.") },
        { "0x0000001e", ("KMODE_EXCEPTION_NOT_HANDLED", "El programa del kernel generó una excepción no capturada.", "Fallo de memoria o incompatibilidad severa de software de bajo nivel.") },
        { "0x000000c2", ("BAD_POOL_CALLER", "El hilo actual del procesador realizó una petición no válida al pool de memoria del kernel.", "Driver liberando memoria ya liberada o accediendo a punteros corruptos.") }
    };

    public Task<List<IncidentRecord>> GetIncidentsAsync(int maxEvents = 60)
    {
        return Task.Run(() =>
        {
            var results = new List<IncidentRecord>();

            try
            {
                // Query Windows Event Log: System for BugCheck (1001), Kernel-Power (41), User32 (1074)
                string queryXml = "*[System[(EventID=1001 or EventID=41 or EventID=1074) and (Level=1 or Level=2 or Level=4)]]";
                var query = new EventLogQuery("System", PathType.LogName, queryXml)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(query);
                EventRecord? record;
                int count = 0;

                while ((record = reader.ReadEvent()) != null && count < maxEvents)
                {
                    count++;
                    var item = ProcessSystemEvent(record);
                    if (item != null)
                    {
                        results.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new IncidentRecord
                {
                    Timestamp = DateTime.Now,
                    Category = IncidentCategory.KernelPower,
                    EventTitle = "Aviso de Lectura de Visor de Eventos",
                    RawDetails = $"No se pudo completar la consulta de eventos del sistema: {ex.Message}",
                    DiagnosticVerdict = "Se requieren permisos de administrador o el servicio de Registro de Eventos está pausado.",
                    BadgeColor = "#F59E0B",
                    Icon = "⚠️"
                });
            }

            // Check Minidump directory
            try
            {
                string dumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                if (Directory.Exists(dumpDir))
                {
                    var dumpFiles = Directory.GetFiles(dumpDir, "*.dmp");
                    foreach (var dump in dumpFiles)
                    {
                        var fi = new FileInfo(dump);
                        // If not already in results with identical date
                        bool exists = results.Exists(r => Math.Abs((r.Timestamp - fi.LastWriteTime).TotalMinutes) < 2);
                        if (!exists)
                        {
                            results.Add(new IncidentRecord
                            {
                                Timestamp = fi.LastWriteTime,
                                Category = IncidentCategory.BSOD,
                                EventTitle = $"Volcado de Minidump Detectado ({fi.Name})",
                                DumpPath = dump,
                                RawDetails = $"Archivo de volcado crash minidump: {dump} (Tamaño: {fi.Length / 1024} KB)",
                                DiagnosticVerdict = "Volcado de memoria generado tras un bloqueo BSOD del sistema.",
                                RecommendedAction = "Revisa los controladores actualizados recientemente o analiza el dump.",
                                BadgeColor = "#EF4444",
                                Icon = "💥"
                            });
                        }
                    }
                }
            }
            catch { }

            results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return results;
        });
    }

    private static IncidentRecord? ProcessSystemEvent(EventRecord record)
    {
        string desc = string.Empty;
        try { desc = record.FormatDescription() ?? string.Empty; } catch { }

        DateTime time = record.TimeCreated ?? DateTime.Now;

        switch (record.Id)
        {
            case 1001: // WER / BugCheck
                {
                    var matchBugcheck = Regex.Match(desc, @"0x[0-9a-fA-F]{8,16}");
                    string codeHex = matchBugcheck.Success ? matchBugcheck.Value.ToLowerInvariant() : "Desconocido";
                    // Pad to 8 hex chars if needed
                    if (codeHex.StartsWith("0x") && codeHex.Length == 10)
                    {
                        // e.g. 0x00000133
                    }

                    string dumpPath = string.Empty;
                    var matchDump = Regex.Match(desc, @"([A-Za-z]:\\[^:\n\r]+\.dmp)");
                    if (matchDump.Success) dumpPath = matchDump.Value;

                    var (name, info, action) = ResolveBugcheck(codeHex);

                    return new IncidentRecord
                    {
                        Timestamp = time,
                        Category = IncidentCategory.BSOD,
                        EventTitle = $"Pantallazo Azul (BSOD): {name}",
                        BugcheckCodeHex = codeHex,
                        BugcheckName = name,
                        DumpPath = dumpPath,
                        RawDetails = desc,
                        DiagnosticVerdict = info,
                        RecommendedAction = action,
                        BadgeColor = "#EF4444",
                        Icon = "💀"
                    };
                }

            case 41: // Kernel-Power
                {
                    return new IncidentRecord
                    {
                        Timestamp = time,
                        Category = IncidentCategory.KernelPower,
                        EventTitle = "Apagado Inesperado o Pérdida de Energía (Kernel-Power)",
                        InitiatorOrProcess = "Kernel-Power (Hardware/Energía)",
                        RawDetails = desc,
                        DiagnosticVerdict = "El equipo se apagó o reinició abruptamente sin que Windows pudiera cerrar procesos de forma limpia.",
                        RecommendedAction = "Comprueba microcortes eléctricos, estabilidad de la fuente de poder (PSU), botón de encendido presionado o inestabilidad térmica en CPU.",
                        BadgeColor = "#F59E0B",
                        Icon = "⚡"
                    };
                }

            case 1074: // User32 Shutdown / Restart
                {
                    string process = "Desconocido";
                    string type = "Apagado / Reinicio";
                    string user = "Usuario";

                    var procMatch = Regex.Match(desc, @"process\s+([^\s\(\)]+)", RegexOptions.IgnoreCase);
                    if (procMatch.Success) process = Path.GetFileName(procMatch.Groups[1].Value);

                    var typeMatch = Regex.Match(desc, @"Shutdown Type:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (typeMatch.Success) type = typeMatch.Groups[1].Value.Trim();

                    var userMatch = Regex.Match(desc, @"user\s+([^\s]+)", RegexOptions.IgnoreCase);
                    if (userMatch.Success) user = userMatch.Groups[1].Value;

                    return new IncidentRecord
                    {
                        Timestamp = time,
                        Category = IncidentCategory.Shutdown,
                        EventTitle = $"Orden de {char.ToUpper(type[0]) + type[1..]} iniciada por {process}",
                        InitiatorOrProcess = $"{process} ({user})",
                        RawDetails = desc,
                        DiagnosticVerdict = $"El proceso '{process}' solicitó legítimamente la acción '{type}' en nombre de '{user}'.",
                        RecommendedAction = "Operación normal del sistema o actualización automática programada.",
                        BadgeColor = "#3B82F6",
                        Icon = "🔄"
                    };
                }

            default:
                return null;
        }
    }

    private static (string Name, string Description, string Recommendation) ResolveBugcheck(string codeHex)
    {
        foreach (var kvp in KnownBugchecks)
        {
            if (codeHex.EndsWith(kvp.Key.Replace("0x", ""), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codeHex, kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return ("CRASH_BUGCHECK_GENÉRICO", 
            $"Código de comprobación de error: {codeHex}. Excepción crítica no recuperable en el espacio del kernel.", 
            "Comprueba los controladores de dispositivos recientes, verifica la integridad del sistema con SFC y comprueba la temperatura de componentes.");
    }
}

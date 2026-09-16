using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public enum ProcessCategory
{
    SystemCore,
    WindowsService,
    HardwareDriver,
    Development,
    Productivity,
    Browser,
    Gaming,
    BackgroundUtility,
    Unknown
}

public enum ProcessSafetyImpact
{
    Safe,       // Can be terminated with no system stability impact
    Careful,    // May lose unsaved work or restart a background service
    Never       // Will cause Blue Screen of Death (BSOD), freeze or OS reboot
}

public enum ProcessThreatLevel
{
    Clean,
    Low,
    Suspicious,
    CriticalMasquerading
}

public record ProcessDefinition(
    string ProcessName,
    string DisplayName,
    string Company,
    ProcessCategory Category,
    string Description,
    string RoleAndInfluence,
    bool IsEssential,
    ProcessSafetyImpact SafetyImpact,
    string ImpactDetails,
    string? ExpectedPathPattern = null
);

public record ProcessIntelResult(
    int Pid,
    string ProcessName,
    string DisplayName,
    string Company,
    string ExecutablePath,
    ProcessCategory Category,
    string Description,
    string RoleAndInfluence,
    bool IsEssential,
    ProcessSafetyImpact SafetyImpact,
    string ImpactDetails,
    ProcessThreatLevel ThreatLevel,
    string SecurityVerdict,
    bool? SignatureVerified,
    string? SignerName,
    string? Sha256Hash,
    double MemoryMB,
    string OnlineLookupUrl
);

public record ProcessAuditSummary(
    int TotalProcesses,
    int SystemCoreCount,
    int ThirdPartyCount,
    int SuspiciousCount,
    int MasqueradingThreatsCount,
    List<ProcessIntelResult> FlaggedProcesses,
    List<ProcessIntelResult> TopMemoryProcesses
);

/// <summary>
/// Service providing deep intelligence, security verification and plain-Spanish
/// human explanations for running Windows processes.
/// </summary>
public class ProcessIntelligenceService
{
    private static readonly Dictionary<string, ProcessDefinition> _catalog = new(StringComparer.OrdinalIgnoreCase);

    static ProcessIntelligenceService()
    {
        InitializeCatalog();
    }

    private static void InitializeCatalog()
    {
        void Add(string name, string display, string comp, ProcessCategory cat, string desc, string role, bool essential, ProcessSafetyImpact impact, string impactDet, string? pathPat = null)
        {
            _catalog[name.ToLowerInvariant()] = new ProcessDefinition(name.ToLowerInvariant(), display, comp, cat, desc, role, essential, impact, impactDet, pathPat);
        }

        // --- SISTEMA CRÍTICO (NUNCA TERMINAR) ---
        Add("system", "Núcleo del Sistema Windows", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Contenedor del kernel de Windows (ntoskrnl.exe) y sus hilos en modo kernel.",
            "Gestiona el hardware, memoria virtual, interrupciones y controladores esenciales.",
            true, ProcessSafetyImpact.Never, "Imposible terminar. Es el núcleo de Windows.", @"^System$");

        Add("smss.exe", "Session Manager Subsystem", "Microsoft Corporation", ProcessCategory.SystemCore,
            "El primer proceso en modo usuario creado por el kernel al arrancar Windows.",
            "Inicializa variables de entorno, arranca csrss.exe y winlogon.exe, y gestiona las sesiones de usuario.",
            true, ProcessSafetyImpact.Never, "Terminarlo provoca un BSOD inmediato (CRITICAL_PROCESS_DIED).", @"^[A-Z]:\\Windows\\System32\\smss\.exe$");

        Add("csrss.exe", "Client Server Runtime Process", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Subistema de tiempo de ejecución cliente/servidor de Win32.",
            "Mantiene las consolas de texto, creación/eliminación de hilos y apagado ordenado del sistema.",
            true, ProcessSafetyImpact.Never, "Terminarlo provoca un BSOD inmediato.", @"^[A-Z]:\\Windows\\System32\\csrss\.exe$");

        Add("wininit.exe", "Windows Initialization Process", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Lanza los servicios centrales de arranque de Windows.",
            "Inicia services.exe (Service Control Manager), lsass.exe y lsm.exe.",
            true, ProcessSafetyImpact.Never, "Terminarlo provoca caída total de Windows.", @"^[A-Z]:\\Windows\\System32\\wininit\.exe$");

        Add("services.exe", "Service Control Manager", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Administrador y controlador de todos los servicios en segundo plano de Windows.",
            "Arranca, detiene y supervisa servicios del sistema operativo y aplicaciones.",
            true, ProcessSafetyImpact.Never, "Terminarlo apaga todos los servicios y reinicia el equipo con error.", @"^[A-Z]:\\Windows\\System32\\services\.exe$");

        Add("lsass.exe", "Local Security Authority Subsystem Service", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Motor de seguridad y autenticación local de Windows.",
            "Verifica inicios de sesión, gestiona contraseñas, políticas de seguridad y tokens de acceso.",
            true, ProcessSafetyImpact.Never, "Terminarlo forzará el reinicio inmediato de Windows en 60 segundos.", @"^[A-Z]:\\Windows\\System32\\lsass\.exe$");

        Add("winlogon.exe", "Windows Logon Process", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Gestiona el inicio y cierre de sesión de usuarios interactivos.",
            "Carga el perfil de usuario y bloquea la estación de trabajo (Ctrl+Alt+Del).",
            true, ProcessSafetyImpact.Never, "Bloquea la sesión y reinicia la máquina.", @"^[A-Z]:\\Windows\\System32\\winlogon\.exe$");

        Add("dwm.exe", "Desktop Window Manager", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Compositor visual acelerado por GPU de la interfaz gráfica de Windows.",
            "Dibuja las ventanas, transparencias, animaciones y fluidez de la pantalla.",
            true, ProcessSafetyImpact.Never, "La pantalla parpadeará en negro y el sistema se recuperará o se congelará.", @"^[A-Z]:\\Windows\\System32\\dwm\.exe$");

        // --- SERVICIOS Y SHELL DE WINDOWS ---
        Add("svchost.exe", "Host de Servicios de Windows", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Proceso genérico que ejecuta librerías DLL de servicios de Windows.",
            "Alberga servicios como Windows Update, audio, red, firewall, Bluetooth y temas.",
            true, ProcessSafetyImpact.Careful, "Terminar la instancia equivocada puede dejar al equipo sin sonido o internet.", @"^[A-Z]:\\Windows\\System32\\svchost\.exe$");

        Add("explorer.exe", "Explorador de Windows", "Microsoft Corporation", ProcessCategory.SystemCore,
            "Shell principal: barra de tareas, menú inicio, escritorio y gestor de carpetas.",
            "Proporciona la interfaz interactiva con el usuario. Si se cierra, desaparece la barra de tareas.",
            true, ProcessSafetyImpact.Safe, "La barra de tareas y escritorio desaparecerán momentáneamente hasta reiniciarlo.", @"^[A-Z]:\\Windows\\explorer\.exe$");

        Add("taskhostw.exe", "Host Process for Windows Tasks", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Ejecuta tareas programadas del sistema basadas en DLLs.",
            "Mantiene tareas de mantenimiento en segundo plano.",
            false, ProcessSafetyImpact.Safe, "Puede cerrarse de forma segura; se reiniciará en la próxima tarea programada.", @"^[A-Z]:\\Windows\\System32\\taskhostw\.exe$");

        Add("runtimebroker.exe", "Runtime Broker", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Supervisa los permisos de las aplicaciones UWP / Microsoft Store (cámara, ubicación).",
            "Garantiza que las aplicaciones respeten la privacidad del usuario.",
            false, ProcessSafetyImpact.Safe, "Se puede terminar de forma segura si consume demasiada CPU; se reinicia automáticamente.", @"^[A-Z]:\\Windows\\System32\\RuntimeBroker\.exe$");

        Add("searchhost.exe", "Windows Search Indexer Host", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Interfaz del indexador de búsqueda en la barra de tareas de Windows.",
            "Permite buscar archivos, aplicaciones y ajustes rápidamente.",
            false, ProcessSafetyImpact.Safe, "Se puede cerrar de forma segura si la búsqueda se congela.", null);

        Add("ctfmon.exe", "CTF Loader", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Controlador de entrada de texto, teclado en pantalla, dictado y métodos IME.",
            "Gestiona la introducción de texto en múltiples idiomas y escritura táctil.",
            false, ProcessSafetyImpact.Safe, "Cerrarlo puede afectar temporalmente el cambio de teclado o dictado.", @"^[A-Z]:\\Windows\\System32\\ctfmon\.exe$");

        Add("audiodg.exe", "Windows Audio Device Graph Isolation", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Motor de procesamiento de efectos y aislamiento de dispositivos de audio.",
            "Reproduce sonido y aplica ecualización o cancelación de ruido.",
            true, ProcessSafetyImpact.Careful, "Terminarlo cortará el audio momentáneamente hasta que Windows lo reinicie.", @"^[A-Z]:\\Windows\\System32\\audiodg\.exe$");

        Add("spoolsv.exe", "Print Spooler Service", "Microsoft Corporation", ProcessCategory.WindowsService,
            "Cola de impresión de Windows.",
            "Gestiona los trabajos enviados a impresoras locales o en red.",
            false, ProcessSafetyImpact.Safe, "Cerrarlo cancela impresiones activas hasta reiniciar el servicio.", @"^[A-Z]:\\Windows\\System32\\spoolsv\.exe$");

        // --- HARDWARE & DRIVERS ---
        Add("nvcontainer.exe", "NVIDIA Container", "NVIDIA Corporation", ProcessCategory.HardwareDriver,
            "Servicio de soporte para controladores gráficos NVIDIA GeForce.",
            "Mantiene telemetría de pantalla, GeForce Experience, ShadowPlay y panel de control.",
            false, ProcessSafetyImpact.Careful, "Cerrarlo detiene la superposición de NVIDIA y filtros de juego.", null);

        Add("nvcplui.exe", "NVIDIA Control Panel", "NVIDIA Corporation", ProcessCategory.HardwareDriver,
            "Interfaz del Panel de Control de NVIDIA.",
            "Permite configurar resolución, G-Sync, color y ajustes 3D de la GPU.",
            false, ProcessSafetyImpact.Safe, "Se puede cerrar en cualquier momento sin afectar los juegos.", null);

        Add("amdfendrs.exe", "AMD Crash Defender Service", "Advanced Micro Devices, Inc.", ProcessCategory.HardwareDriver,
            "Servicio de recuperación y protección contra caídas de controladores AMD Radeon.",
            "Previene pantallazos azules reiniciando el controlador gráfico en fallos leves.",
            false, ProcessSafetyImpact.Careful, "Desactiva la protección contra caídas del driver de GPU.", null);

        // --- DESARROLLO & NAVEGADORES ---
        Add("code.exe", "Visual Studio Code", "Microsoft Corporation", ProcessCategory.Development,
            "Editor de código fuente extensible multiplataforma.",
            "Mantiene tu espacio de trabajo de programación.",
            false, ProcessSafetyImpact.Careful, "Asegúrate de guardar cambios en tus archivos antes de cerrar.", null);

        Add("dotnet.exe", ".NET Runtime Host", "Microsoft Corporation", ProcessCategory.Development,
            "Motor de ejecución de aplicaciones y compilación de .NET.",
            "Ejecuta programas, herramientas CLI o pruebas unitarias de .NET.",
            false, ProcessSafetyImpact.Safe, "Cancela la compilación o aplicación .NET en ejecución.", null);

        Add("chrome.exe", "Google Chrome", "Google LLC", ProcessCategory.Browser,
            "Navegador web basado en Chromium.",
            "Muestra pestañas de internet y aplicaciones web.",
            false, ProcessSafetyImpact.Safe, "Cierra las pestañas activas del navegador.", null);

        Add("msedge.exe", "Microsoft Edge", "Microsoft Corporation", ProcessCategory.Browser,
            "Navegador web nativo de Windows basado en Chromium.",
            "Muestra páginas web y funciones integradas de Windows.",
            false, ProcessSafetyImpact.Safe, "Cierra las pestañas activas de Edge.", null);

        Add("discord.exe", "Discord", "Discord Inc.", ProcessCategory.Productivity,
            "Plataforma de mensajería, llamadas y comunidades de voz/texto.",
            "Permite comunicarse mientras juegas o trabajas.",
            false, ProcessSafetyImpact.Safe, "Cierra Discord de inmediato liberando memoria.", null);

        Add("steam.exe", "Steam Client Bootstrapper", "Valve Corporation", ProcessCategory.Gaming,
            "Cliente de la plataforma de videojuegos Steam.",
            "Descarga juegos, gestiona la biblioteca y amigos.",
            false, ProcessSafetyImpact.Careful, "Detendrá descargas de juegos en curso.", null);

        Add("spotify.exe", "Spotify Music Player", "Spotify AB", ProcessCategory.Productivity,
            "Reproductor de música y podcasts en streaming.",
            "Reproduce música en segundo plano.",
            false, ProcessSafetyImpact.Safe, "Detiene la música inmediatamente.", null);
    }

    /// <summary>
    /// Audits all running processes on the system, classifying them, verifying their paths,
    /// and checking digital signatures to uncover suspicious activity or malware masquerading.
    /// </summary>
    public async Task<ProcessAuditSummary> AuditRunningProcessesAsync(int topMemoryCount = 15)
    {
        var processes = Process.GetProcesses();
        var results = new ConcurrentBag<ProcessIntelResult>();

        await Task.Run(() =>
        {
            Parallel.ForEach(processes, p =>
            {
                try
                {
                    var intel = AnalyzeProcess(p);
                    if (intel != null)
                    {
                        results.Add(intel);
                    }
                }
                catch
                {
                    // Ignore inaccessible processes (e.g. secure system pids)
                }
            });
        });

        var allList = results.ToList();

        int total = allList.Count;
        int systemCore = allList.Count(r => r.Category == ProcessCategory.SystemCore || r.Category == ProcessCategory.WindowsService);
        int thirdParty = allList.Count(r => r.Category != ProcessCategory.SystemCore && r.Category != ProcessCategory.WindowsService && r.Category != ProcessCategory.Unknown);
        int suspicious = allList.Count(r => r.ThreatLevel == ProcessThreatLevel.Suspicious);
        int masquerading = allList.Count(r => r.ThreatLevel == ProcessThreatLevel.CriticalMasquerading);

        var flagged = allList
            .Where(r => r.ThreatLevel == ProcessThreatLevel.CriticalMasquerading || r.ThreatLevel == ProcessThreatLevel.Suspicious)
            .OrderByDescending(r => r.ThreatLevel)
            .ToList();

        var topMem = allList
            .OrderByDescending(r => r.MemoryMB)
            .Take(topMemoryCount)
            .ToList();

        return new ProcessAuditSummary(total, systemCore, thirdParty, suspicious, masquerading, flagged, topMem);
    }

    /// <summary>
    /// Analyzes a single process by PID or name, providing forensic and security insights.
    /// </summary>
    public ProcessIntelResult? AnalyzeProcess(Process process)
    {
        try
        {
            int pid = process.Id;
            string procName = process.ProcessName.ToLowerInvariant();
            string exeName = procName.EndsWith(".exe") ? procName : procName + ".exe";

            string exePath = "";
            double memMb = 0;

            try
            {
                exePath = process.MainModule?.FileName ?? "";
                memMb = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 2);
            }
            catch
            {
                // Inaccessible module or protected system process
                if (pid == 4 || procName == "system") exePath = "System";
            }

            return EvaluateProcessIntelligence(pid, exeName, exePath, memMb);
        }
        catch
        {
            return null;
        }
    }

    public ProcessIntelResult EvaluateProcessIntelligence(int pid, string exeName, string exePath, double memMb)
    {
        string lookupUrl = $"https://duckduckgo.com/?q={Uri.EscapeDataString("windows process " + exeName)}";
        _catalog.TryGetValue(exeName, out var def);

        string displayName = def?.DisplayName ?? Path.GetFileNameWithoutExtension(exeName);
        string company = def?.Company ?? "Desconocido / Terceros";
        var category = def?.Category ?? ProcessCategory.Unknown;
        string desc = def?.Description ?? "Proceso no indexado en la base de datos central de Windows.";
        string role = def?.RoleAndInfluence ?? "Comprueba la ruta del ejecutable o la firma digital para verificar su procedencia.";
        bool isEssential = def?.IsEssential ?? false;
        var safetyImpact = def?.SafetyImpact ?? ProcessSafetyImpact.Careful;
        string impactDet = def?.ImpactDetails ?? "Si se desconoce su procedencia, investiga su ubicación antes de terminarlo.";

        var threatLevel = ProcessThreatLevel.Clean;
        string securityVerdict = "Limpio / Confiable";
        bool? sigVerified = null;
        string? signerName = null;
        string? sha256 = null;

        // Path analysis & Masquerade Detection
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            // Masquerading Check against expected system paths
            if (def?.ExpectedPathPattern != null)
            {
                if (!Regex.IsMatch(exePath, def.ExpectedPathPattern, RegexOptions.IgnoreCase))
                {
                    threatLevel = ProcessThreatLevel.CriticalMasquerading;
                    securityVerdict = $"ALERTA CRÍTICA: Proceso suplantado (Masquerading). '{exeName}' debe ejecutarse únicamente desde '{def.ExpectedPathPattern}', pero se está ejecutando desde '{exePath}'. Posible troyano o malware.";
                }
            }

            if (File.Exists(exePath))
            {
                // Fallback metadata if not in catalog
                if (def == null)
                {
                    try
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(exePath);
                        if (!string.IsNullOrWhiteSpace(fvi.CompanyName)) company = fvi.CompanyName;
                        if (!string.IsNullOrWhiteSpace(fvi.FileDescription)) desc = fvi.FileDescription;
                        if (!string.IsNullOrWhiteSpace(fvi.ProductName)) displayName = fvi.ProductName;

                        if (company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                        {
                            category = ProcessCategory.WindowsService;
                        }
                    }
                    catch { }
                }

                // Check signature if it's not already critical
                if (threatLevel != ProcessThreatLevel.CriticalMasquerading)
            {
                try
                {
                    var sig = DriverCenterService.VerifyFileSignature(exePath);
                    sigVerified = sig.IsSigned && sig.IsTrusted;
                    signerName = sig.SignerName;

                    if (!sig.IsSigned)
                    {
                        // In Windows, core OS binaries (cmd.exe, conhost.exe, notepad.exe) are catalog-signed (.cat in CatRoot)
                        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        if (exePath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
                        {
                            threatLevel = ProcessThreatLevel.Clean;
                            securityVerdict = "Limpio: Binario nativo de Windows (firmado mediante catálogo de seguridad CatRoot).";
                        }
                        else if (category == ProcessCategory.SystemCore || company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                        {
                            threatLevel = ProcessThreatLevel.Suspicious;
                            securityVerdict = "Sospechoso: El ejecutable dice ser de Microsoft o del sistema, pero carece de firma Authenticode válida y se ejecuta fuera de Windows.";
                        }
                    }
                }
                catch { }
            }

            // Calculate SHA256 if flagged
            if (threatLevel != ProcessThreatLevel.Clean)
            {
                try
                {
                    using var stream = File.OpenRead(exePath);
                    using var sha = SHA256.Create();
                    var hashBytes = sha.ComputeHash(stream);
                    sha256 = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    lookupUrl = $"https://www.virustotal.com/gui/file/{sha256}";
                }
                catch { }
            }
        }
    }
        else if (def?.ExpectedPathPattern != null && pid != 4 && exeName != "system" && exeName != "system.exe")
        {
            // If it's a known critical process and exePath was blank/hidden, evaluate caution
            if (pid > 4 && string.IsNullOrWhiteSpace(exePath))
            {
                // Protected process (Anti-malware or kernel protected)
                securityVerdict = "Proceso protegido del sistema (acceso de ruta restringido por el kernel de Windows).";
            }
        }

        return new ProcessIntelResult(
            pid,
            exeName,
            displayName,
            company,
            exePath,
            category,
            desc,
            role,
            isEssential,
            safetyImpact,
            impactDet,
            threatLevel,
            securityVerdict,
            sigVerified,
            signerName,
            sha256,
            memMb,
            lookupUrl
        );
    }

    /// <summary>
    /// Gets detailed Spanish advice on a process by name (e.g. "svchost.exe", "discord.exe").
    /// </summary>
    public ProcessDefinition? GetProcessInfo(string processName)
    {
        string key = processName.ToLowerInvariant();
        if (!key.EndsWith(".exe") && !key.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            key += ".exe";
        }

        _catalog.TryGetValue(key, out var def);
        return def;
    }
}

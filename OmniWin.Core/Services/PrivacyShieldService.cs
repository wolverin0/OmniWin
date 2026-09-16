using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public enum PrivacyCategory
{
    TelemetryAndDiagnostics,
    WindowsAIAndRecall,
    SearchAndAdvertising,
    ActivityAndLocation,
    BackgroundServicesAndTasks
}

public enum PrivacyProfile
{
    Recommended,
    StrictPrivacy,
    GamerZeroTelemetry
}

public record PrivacySettingItem(
    string Id,
    PrivacyCategory Category,
    string Title,
    string Description,
    string SafetyLevel, // "Recomendado", "Opcional", "Avanzado"
    bool IsProtected
);

public class PrivacyShieldResult
{
    public bool Success { get; set; }
    public int AppliedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class PrivacyShieldService
{
    private readonly TransactionService _tx = TransactionService.Instance;

    public List<PrivacySettingItem> GetPrivacyAudit()
    {
        var list = new List<PrivacySettingItem>
        {
            // 1. Telemetría y Diagnóstico
            new(
                Id: "telemetry_disable",
                Category: PrivacyCategory.TelemetryAndDiagnostics,
                Title: "Desactivar Telemetría y Datos de Diagnóstico",
                Description: "Evita que Windows envíe volcados de memoria, logs y telemetría diagnóstica a servidores de Microsoft.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0
            ),
            new(
                Id: "feedback_disable",
                Category: PrivacyCategory.TelemetryAndDiagnostics,
                Title: "Desactivar Notificaciones y Peticiones de Feedback",
                Description: "Bloquea los avisos emergentes que piden calificar o responder encuestas de Windows.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_CURRENT_USER\Software\Microsoft\Siuf\Rules", "NumberOfSIFUrlRuns") == 0
            ),

            // 2. Windows AI & Copilot / Recall
            new(
                Id: "recall_snapshots_disable",
                Category: PrivacyCategory.WindowsAIAndRecall,
                Title: "Bloquear Capturas Continuas de Windows Recall",
                Description: "Impide que Windows 11 tome capturas constantes de la pantalla para el historial de IA.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis") == 1
            ),
            new(
                Id: "copilot_taskbar_disable",
                Category: PrivacyCategory.WindowsAIAndRecall,
                Title: "Desactivar Integración de Windows Copilot",
                Description: "Oculta y desactiva el botón y servicios residentes de Windows Copilot.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot") == 1
            ),

            // 3. Búsqueda y Publicidad
            new(
                Id: "bing_search_start_disable",
                Category: PrivacyCategory.SearchAndAdvertising,
                Title: "Desactivar Búsqueda Web de Bing en Menú Inicio",
                Description: "Hace que el menú Inicio busque solo archivos y programas locales, eliminando la lentitud de Bing.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions") == 1
            ),
            new(
                Id: "advertising_id_disable",
                Category: PrivacyCategory.SearchAndAdvertising,
                Title: "Desactivar ID de Publicidad de Usuario",
                Description: "Impide que las aplicaciones usen tu identificador único de publicidad para mostrar anuncios dirigidos.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") == 0
            ),
            new(
                Id: "tailored_experiences_disable",
                Category: PrivacyCategory.SearchAndAdvertising,
                Title: "Desactivar 'Experiencias Personalizadas' (Anuncios en Configuración)",
                Description: "Bloquea sugerencias comerciales y promociones dentro de la aplicación de Configuración.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled") == 0
            ),

            // 4. Actividad y Ubicación
            new(
                Id: "activity_history_disable",
                Category: PrivacyCategory.ActivityAndLocation,
                Title: "Desactivar Historial de Actividades (Timeline)",
                Description: "Evita que Windows registre aplicaciones abiertas y sitios navegados para sincronización en la nube.",
                SafetyLevel: "Recomendado",
                IsProtected: ReadDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities") == 0
            ),
            new(
                Id: "location_tracking_disable",
                Category: PrivacyCategory.ActivityAndLocation,
                Title: "Desactivar Sensores de Geolocalización Global",
                Description: "Impide que Windows y servicios en segundo plano soliciten tu ubicación geográfica física.",
                SafetyLevel: "Opcional",
                IsProtected: ReadDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation") == 1
            ),

            // 5. Servicios y Tareas Programadas de Telemetría
            new(
                Id: "diagtrack_service_disable",
                Category: PrivacyCategory.BackgroundServicesAndTasks,
                Title: "Desactivar Servicio DiagTrack (Experiencias y Telemetría)",
                Description: "Deshabilita el servicio residente en segundo plano 'DiagTrack' (Connected User Experiences and Telemetry).",
                SafetyLevel: "Recomendado",
                IsProtected: IsServiceDisabled("DiagTrack")
            ),
            new(
                Id: "ceip_tasks_disable",
                Category: PrivacyCategory.BackgroundServicesAndTasks,
                Title: "Desactivar Tareas CEIP (Customer Experience Improvement)",
                Description: "Deshabilita tareas programadas de recopilación periódica de hardware y datos de uso.",
                SafetyLevel: "Recomendado",
                IsProtected: AreCeipTasksDisabled()
            )
        };

        return list;
    }

    public PrivacyShieldResult ApplyProfile(PrivacyProfile profile)
    {
        var audit = GetPrivacyAudit();
        var targetSettings = new List<string>();

        switch (profile)
        {
            case PrivacyProfile.Recommended:
                targetSettings = audit.Where(a => a.SafetyLevel == "Recomendado").Select(a => a.Id).ToList();
                break;
            case PrivacyProfile.StrictPrivacy:
                targetSettings = audit.Select(a => a.Id).ToList();
                break;
            case PrivacyProfile.GamerZeroTelemetry:
                targetSettings = audit.Where(a => a.Category == PrivacyCategory.TelemetryAndDiagnostics ||
                                                  a.Category == PrivacyCategory.BackgroundServicesAndTasks ||
                                                  a.Id == "bing_search_start_disable").Select(a => a.Id).ToList();
                break;
        }

        int count = 0;
        foreach (var id in targetSettings)
        {
            if (ApplySetting(id, true))
            {
                count++;
            }
        }

        return new PrivacyShieldResult
        {
            Success = count > 0,
            AppliedCount = count,
            Message = $"Perfil '{profile}' aplicado: {count} ajustes de privacidad configurados con respaldo transaccional."
        };
    }

    public bool ApplySetting(string settingId, bool enableProtection)
    {
        string txId = $"privacy_{settingId}";
        try
        {
            _tx.BeginTransaction(txId, $"Ajuste de privacidad: {settingId}");

            switch (settingId)
            {
                case "telemetry_disable":
                    _tx.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", enableProtection ? 0 : 3);
                    break;

                case "feedback_disable":
                    _tx.SetDword(Registry.CurrentUser, @"Software\Microsoft\Siuf\Rules", "NumberOfSIFUrlRuns", enableProtection ? 0 : 1);
                    _tx.SetDword(Registry.CurrentUser, @"Software\Microsoft\Siuf\Rules", "DoNotShowFeedbackNotifications", enableProtection ? 1 : 0);
                    break;

                case "recall_snapshots_disable":
                    _tx.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", enableProtection ? 1 : 0);
                    _tx.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecall", enableProtection ? 0 : 1);
                    break;

                case "copilot_taskbar_disable":
                    _tx.SetDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", enableProtection ? 1 : 0);
                    break;

                case "bing_search_start_disable":
                    _tx.SetDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", enableProtection ? 1 : 0);
                    _tx.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", enableProtection ? 0 : 1);
                    break;

                case "advertising_id_disable":
                    _tx.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", enableProtection ? 0 : 1);
                    break;

                case "tailored_experiences_disable":
                    _tx.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", enableProtection ? 0 : 1);
                    break;

                case "activity_history_disable":
                    _tx.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", enableProtection ? 0 : 1);
                    _tx.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", enableProtection ? 0 : 1);
                    break;

                case "location_tracking_disable":
                    _tx.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", enableProtection ? 1 : 0);
                    break;

                case "diagtrack_service_disable":
                    _tx.CaptureServicePreState("DiagTrack");
                    if (enableProtection)
                    {
                        RunProcess("sc.exe", "config DiagTrack start= disabled");
                        RunProcess("sc.exe", "stop DiagTrack");
                    }
                    else
                    {
                        RunProcess("sc.exe", "config DiagTrack start= auto");
                        RunProcess("sc.exe", "start DiagTrack");
                    }
                    break;

                case "ceip_tasks_disable":
                    string cmd = enableProtection ? "/change /disable" : "/change /enable";
                    RunProcess("schtasks.exe", $"{cmd} /tn \"\\Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator\"");
                    RunProcess("schtasks.exe", $"{cmd} /tn \"\\Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip\"");
                    break;

                default:
                    _tx.RollbackInFlightTransaction();
                    return false;
            }

            _tx.CommitTransaction(txId);
            return true;
        }
        catch
        {
            _tx.RollbackInFlightTransaction();
            return false;
        }
    }

    public bool RollbackSetting(string settingId)
    {
        string txId = $"privacy_{settingId}";
        return _tx.RollbackTransaction(txId, out _);
    }

    private static int? ReadDword(string fullPath, string valueName)
    {
        try
        {
            RegistryKey? baseKey = null;
            string subPath = "";

            if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            {
                baseKey = Registry.LocalMachine;
                subPath = fullPath["HKEY_LOCAL_MACHINE\\".Length..];
            }
            else if (fullPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            {
                baseKey = Registry.CurrentUser;
                subPath = fullPath["HKEY_CURRENT_USER\\".Length..];
            }

            if (baseKey == null) return null;

            using var key = baseKey.OpenSubKey(subPath);
            if (key == null) return null;

            var val = key.GetValue(valueName);
            if (val is int intVal) return intVal;
            if (val is long longVal) return (int)longVal;
            if (int.TryParse(val?.ToString(), out int parsed)) return parsed;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsServiceDisabled(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key != null)
            {
                var startVal = key.GetValue("Start");
                if (startVal is int s && s == 4) return true; // 4 = Disabled
            }
        }
        catch { }
        return false;
    }

    private static bool AreCeipTasksDisabled()
    {
        try
        {
            // Tarea de telemetría consolidator
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\Microsoft\Windows\Customer Experience Improvement Program\Consolidator");
            return key == null;
        }
        catch
        {
            return false;
        }
    }

    private static void RunProcess(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }
}

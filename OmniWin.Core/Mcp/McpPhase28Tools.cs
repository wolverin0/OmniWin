using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OmniWin.Core.Services;

namespace OmniWin.Core.Mcp;

public static class McpPhase28Tools
{
    private static readonly DiskDuplicateService _dupService = DiskDuplicateService.Instance;
    private static readonly UsbDoctorService _usbService = new();
    private static readonly PrivacyShieldService _privacyService = new();
    private static readonly BatteryHealthService _batteryService = new();
    private static readonly RansomwareCanaryService _canaryService = RansomwareCanaryService.Instance;
    private static readonly ContextMenuService _contextMenuService = new();

    public static void RegisterTools(JsonArray tools)
    {
        // 39. win_duplicate_manager
        tools.Add(new JsonObject
        {
            ["name"] = "win_duplicate_manager",
            ["description"] = "Deduplicador inteligente de archivos con tecnología Zero-Copy (Enlaces Duros NTFS). Escanea carpetas, detecta duplicados por SHA-256 y permite reemplazarlos por Hardlinks liberando el 100% del espacio en disco sin perder los archivos ni romper accesos directos.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "scan", "deduplicate_hardlinks", "delete_duplicates" }, ["description"] = "Acción a realizar" },
                    ["directory_path"] = new JsonObject { ["type"] = "string", ["description"] = "Ruta de la carpeta a analizar" },
                    ["min_size_kb"] = new JsonObject { ["type"] = "integer", ["description"] = "Tamaño mínimo de archivo en KB a considerar (por defecto 100)" }
                },
                ["required"] = new JsonArray { "action", "directory_path" }
            }
        });

        // 40. win_usb_doctor
        tools.Add(new JsonObject
        {
            ["name"] = "win_usb_doctor",
            ["description"] = "Gestor y médico de unidades USB y almacenamiento externo. Lista dispositivos extraíbles, detecta procesos bloqueando el pendrive, realiza expulsión segura forzada o normal (FSCTL dismount), y ejecuta pruebas criptográficas de celda flash para detectar pendrives falsos o trucados.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "list", "eject", "test_fake_flash", "wipe_free_space" }, ["description"] = "Acción a realizar" },
                    ["drive_letter"] = new JsonObject { ["type"] = "string", ["description"] = "Letra de unidad (ej. 'E:' o 'E:\\')" },
                    ["force_close"] = new JsonObject { ["type"] = "boolean", ["description"] = "Forzar cierre de procesos bloqueadores al expulsar" },
                    ["test_size_gb"] = new JsonObject { ["type"] = "integer", ["description"] = "Gigabytes a probar para detectar chips falsos (por defecto 1)" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 41. win_privacy_shield
        tools.Add(new JsonObject
        {
            ["name"] = "win_privacy_shield",
            ["description"] = "Centro de privacidad, anti-espía y anti-telemetría para Windows 11 y 10 con respaldo transaccional. Audita y desactiva telemetría, capturas de Windows Recall, búsquedas Bing en el inicio, tracking de ID de anuncios y tareas de diagnóstico.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "audit", "apply_profile", "toggle_setting", "rollback_setting" }, ["description"] = "Acción a ejecutar" },
                    ["profile"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "Recommended", "StrictPrivacy", "GamerZeroTelemetry" }, ["description"] = "Perfil a aplicar" },
                    ["setting_id"] = new JsonObject { ["type"] = "string", ["description"] = "ID del ajuste específico a alternar o revertir" },
                    ["enable_protection"] = new JsonObject { ["type"] = "boolean", ["description"] = "True para activar protección (bloquear telemetría), False para restaurar" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 42. win_battery_health
        tools.Add(new JsonObject
        {
            ["name"] = "win_battery_health",
            ["description"] = "Diagnóstico integral de salud, degradación y consumo de energía para laptops y consolas portátiles (ROG Ally, Legion Go, Steam Deck). Lee capacidad de diseño vs carga completa actual, tasa de descarga en Watts, ciclos y tiempo restante.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "report", "extreme_saver" }, ["description"] = "Acción a ejecutar" },
                    ["enable_saver"] = new JsonObject { ["type"] = "boolean", ["description"] = "Activar o desactivar el perfil de ahorro extremo" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 43. win_ransomware_canary
        tools.Add(new JsonObject
        {
            ["name"] = "win_ransomware_canary",
            ["description"] = "Escudo de detección temprana y congelación anti-ransomware mediante archivos trampa (canarios). Si un malware intenta tocar o cifrar los archivos señuelo, el servicio congela inmediatamente el proceso culpable con NtSuspendProcess y emite alertas.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "status", "start", "stop", "alerts" }, ["description"] = "Acción a ejecutar" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 44. win_context_menu
        tools.Add(new JsonObject
        {
            ["name"] = "win_context_menu",
            ["description"] = "Administrador del menú contextual (clic derecho) de Windows. Permite listar extensiones de shell activas, activar el menú contextual clásico de Windows 10 en Windows 11, y reiniciar explorer.exe.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "list", "toggle_classic_win11", "restart_explorer" }, ["description"] = "Acción a ejecutar" },
                    ["enable_classic"] = new JsonObject { ["type"] = "boolean", ["description"] = "Activar o desactivar el menú clásico de Windows 10" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });
    }

    public static async Task<bool> TryExecuteToolAsync(string toolName, JsonObject args, JsonArray content)
    {
        switch (toolName)
        {
            case "win_duplicate_manager":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "scan";
                string dir = args["directory_path"]?.GetValue<string>() ?? Directory.GetCurrentDirectory();
                int minKb = args["min_size_kb"]?.GetValue<int>() ?? 100;

                var dupes = await _dupService.FindDuplicatesAsync(dir, minSizeBytes: minKb * 1024L);

                if (action == "deduplicate_hardlinks")
                {
                    long totalSaved = 0;
                    int count = 0;
                    foreach (var g in dupes)
                    {
                        var res = _dupService.DeduplicateGroupWithHardLinks(g, g.FilePaths[0]);
                        totalSaved += res.BytesSaved;
                        count += res.FilesProcessed;
                    }
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { success = true, files_linked = count, bytes_saved_mb = Math.Round(totalSaved / (1024.0 * 1024.0), 2) }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "delete_duplicates")
                {
                    int delCount = 0;
                    foreach (var g in dupes)
                    {
                        for (int i = 1; i < g.FilePaths.Count; i++)
                        {
                            if (_dupService.DeleteDuplicateFile(g.FilePaths[i], sendToRecycleBin: true)) delCount++;
                        }
                    }
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { success = true, duplicates_deleted = delCount }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var summary = dupes.Select(d => new
                    {
                        file_size_kb = Math.Round(d.FileSizeBytes / 1024.0, 1),
                        copies_count = d.FilePaths.Count,
                        wasted_mb = Math.Round(d.WastedBytes / (1024.0 * 1024.0), 2),
                        sample_paths = d.FilePaths.Take(3).ToList()
                    });
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_usb_doctor":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "list";
                string drive = args["drive_letter"]?.GetValue<string>() ?? "";

                if (action == "eject")
                {
                    bool force = args["force_close"]?.GetValue<bool>() ?? false;
                    var res = await _usbService.SafelyEjectDriveAsync(drive, force);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "test_fake_flash")
                {
                    int gb = args["test_size_gb"]?.GetValue<int>() ?? 1;
                    long testBytes = gb * 1024L * 1024 * 1024;
                    var res = await _usbService.ValidateFlashCapacityAsync(drive, testBytes);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "wipe_free_space")
                {
                    var res = await _usbService.WipeFreeSpaceAsync(drive);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var drives = _usbService.GetRemovableDrives();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(drives, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_privacy_shield":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "audit";

                if (action == "apply_profile")
                {
                    string profStr = args["profile"]?.GetValue<string>() ?? "Recommended";
                    if (!Enum.TryParse<PrivacyProfile>(profStr, true, out var prof)) prof = PrivacyProfile.Recommended;
                    var res = _privacyService.ApplyProfile(prof);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "toggle_setting")
                {
                    string settingId = args["setting_id"]?.GetValue<string>() ?? "";
                    bool enable = args["enable_protection"]?.GetValue<bool>() ?? true;
                    bool ok = _privacyService.ApplySetting(settingId, enable);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { setting = settingId, applied = ok }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "rollback_setting")
                {
                    string settingId = args["setting_id"]?.GetValue<string>() ?? "";
                    bool ok = _privacyService.RollbackSetting(settingId);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { setting = settingId, rolled_back = ok }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var audit = _privacyService.GetPrivacyAudit();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_battery_health":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "report";
                if (action == "extreme_saver")
                {
                    bool enable = args["enable_saver"]?.GetValue<bool>() ?? true;
                    bool ok = _batteryService.ApplyExtremeBatterySaver(enable);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { extreme_saver_enabled = enable, success = ok }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var rep = _batteryService.GetBatteryReport();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(rep, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_ransomware_canary":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "status";
                if (action == "start")
                {
                    _canaryService.StartShield();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_canaryService.GetStatus(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "stop")
                {
                    _canaryService.StopShield();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_canaryService.GetStatus(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "alerts")
                {
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_canaryService.GetRecentAlerts(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_canaryService.GetStatus(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_context_menu":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "list";
                if (action == "toggle_classic_win11")
                {
                    bool enable = args["enable_classic"]?.GetValue<bool>() ?? true;
                    bool ok = _contextMenuService.ToggleWindows11ClassicContextMenu(enable);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { classic_win10_menu = enable, success = ok }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "restart_explorer")
                {
                    bool ok = _contextMenuService.RestartWindowsExplorer();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { explorer_restarted = ok }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var items = _contextMenuService.GetContextMenuItems();
                    bool isClassic = _contextMenuService.IsWindows11ClassicContextMenuEnabled();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { is_win11_classic = isClassic, total_items = items.Count, items }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            default:
                return false;
        }
    }
}

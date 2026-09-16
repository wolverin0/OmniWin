using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OmniWin.Core.Services;

namespace OmniWin.Core.Mcp;

public static class McpPhase29Tools
{
    private static readonly AppMigrationService _migrationService = AppMigrationService.Instance;
    private static readonly NetworkQosService _qosService = NetworkQosService.Instance;
    private static readonly IdleMaintenanceService _idleService = IdleMaintenanceService.Instance;
    private static readonly SpotlightWallpaperService _spotlightService = SpotlightWallpaperService.Instance;
    private static readonly PortConflictService _portService = PortConflictService.Instance;

    public static void RegisterTools(JsonArray tools)
    {
        // 45. win_app_migration
        tools.Add(new JsonObject
        {
            ["name"] = "win_app_migration",
            ["description"] = "Migrador de juegos y aplicaciones entre unidades con Enlaces Junction NTFS transparentes. Analiza carpetas pesadas, valida espacio libre, mueve archivos a otra unidad y crea un Junction sin romper accesos directos ni configuraciones.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "analyze", "migrate", "rollback", "list", "candidates" }, ["description"] = "Acción a realizar" },
                    ["source_path"] = new JsonObject { ["type"] = "string", ["description"] = "Ruta de la carpeta a migrar o revertir" },
                    ["target_root"] = new JsonObject { ["type"] = "string", ["description"] = "Ruta base del disco destino (ej. 'D:\\Games' o 'E:\\Apps')" },
                    ["drive_letter"] = new JsonObject { ["type"] = "string", ["description"] = "Letra de unidad para buscar candidatos (ej. 'C:\\')" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 46. win_network_qos
        tools.Add(new JsonObject
        {
            ["name"] = "win_network_qos",
            ["description"] = "Limitador de ancho de banda por proceso mediante políticas nativas de Windows QoS. Permite limitar velocidad de descarga/subida de ejecutables (Steam, Discord, Torrent, Chrome) para evitar saturación de red en videollamadas o juegos.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "list", "set_limit", "remove_limit", "remove_all", "gaming_preset" }, ["description"] = "Acción a realizar" },
                    ["app_name"] = new JsonObject { ["type"] = "string", ["description"] = "Nombre del ejecutable (ej. 'steam.exe' o 'qbittorrent.exe')" },
                    ["limit_kbps"] = new JsonObject { ["type"] = "integer", ["description"] = "Límite máximo de velocidad en KB/s (ej. 300 o 1000)" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 47. win_idle_maintenance
        tools.Add(new JsonObject
        {
            ["name"] = "win_idle_maintenance",
            ["description"] = "Robot inteligente de mantenimiento en inactividad. Detecta cuando el usuario no usa el equipo (vía GetLastInputInfo) y ejecuta en segundo plano Re-Trim de SSDs, purga de RAM si está saturada (>80%) y limpieza de temporales obsoletos.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "status", "trigger_now", "start_monitor", "stop_monitor", "configure" }, ["description"] = "Acción a realizar" },
                    ["threshold_minutes"] = new JsonObject { ["type"] = "integer", ["description"] = "Minutos de inactividad requeridos (por defecto 10)" },
                    ["trim_ssd"] = new JsonObject { ["type"] = "boolean", ["description"] = "Habilitar optimización Re-Trim de SSD" },
                    ["clean_temp"] = new JsonObject { ["type"] = "boolean", ["description"] = "Habilitar limpieza de archivos temporales" },
                    ["purge_ram"] = new JsonObject { ["type"] = "boolean", ["description"] = "Habilitar purga de RAM si la carga supera el 80%" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 48. win_spotlight_wallpapers
        tools.Add(new JsonObject
        {
            ["name"] = "win_spotlight_wallpapers",
            ["description"] = "Extractor de fondos 4K y Full HD de Windows Spotlight y Bing. Descubre imágenes ocultas de la pantalla de bloqueo sin extensión, analiza resolución por cabeceras JPEG/PNG y permite exportarlas como .jpg o ponerlas de fondo de escritorio.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "scan", "export", "set_wallpaper" }, ["description"] = "Acción a realizar" },
                    ["min_width"] = new JsonObject { ["type"] = "integer", ["description"] = "Ancho mínimo en píxeles (por defecto 1920 para FHD/4K)" },
                    ["destination_folder"] = new JsonObject { ["type"] = "string", ["description"] = "Carpeta donde exportar los fondos encontrados" },
                    ["image_path"] = new JsonObject { ["type"] = "string", ["description"] = "Ruta de la imagen a aplicar como fondo de pantalla" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });

        // 49. win_port_conflict
        tools.Add(new JsonObject
        {
            ["name"] = "win_port_conflict",
            ["description"] = "Detector y liberador de conflictos en puertos de red (TCP/UDP). Identifica instantáneamente qué proceso está ocupando un puerto crítico (80, 443, 3000, 5000, 8080, etc.) y permite liberarlo o cerrarlo en 1 clic.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "list_listening", "diagnose", "diagnose_common", "release_port" }, ["description"] = "Acción a realizar" },
                    ["port"] = new JsonObject { ["type"] = "integer", ["description"] = "Número de puerto a diagnosticar o liberar (ej. 3000, 8080)" }
                },
                ["required"] = new JsonArray { "action" }
            }
        });
    }

    public static async Task<bool> TryExecuteToolAsync(string toolName, JsonObject args, JsonArray content)
    {
        switch (toolName)
        {
            case "win_app_migration":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "list";
                string source = args["source_path"]?.GetValue<string>() ?? "";
                string target = args["target_root"]?.GetValue<string>() ?? "";

                if (action == "analyze")
                {
                    var plan = _migrationService.AnalyzeFolder(source, target);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "migrate")
                {
                    var res = await _migrationService.MigrateFolderAsync(source, target);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "rollback")
                {
                    var res = await _migrationService.RollbackMigrationAsync(source);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "candidates")
                {
                    string drive = args["drive_letter"]?.GetValue<string>() ?? "C:\\";
                    var cands = _migrationService.FindCandidateFolders(drive);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { drive, candidates = cands }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var list = _migrationService.GetActiveMigrations();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_network_qos":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "list";
                string app = args["app_name"]?.GetValue<string>() ?? "";
                long limit = args["limit_kbps"]?.GetValue<long>() ?? 500;

                if (action == "set_limit")
                {
                    var res = await _qosService.SetProcessLimitAsync(app, limit);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "remove_limit")
                {
                    var res = await _qosService.RemoveProcessLimitAsync(app);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "remove_all")
                {
                    int removed = await _qosService.RemoveAllOmniWinLimitsAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { removed_count = removed, message = $"{removed} políticas eliminadas." }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "gaming_preset")
                {
                    var res = await _qosService.ApplyGamingPresetAsync(limit);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var policies = await _qosService.ListPoliciesAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(policies, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_idle_maintenance":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "status";

                if (action == "trigger_now")
                {
                    string log = await _idleService.TriggerMaintenanceNowAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { log, status = _idleService.GetStatus() }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "start_monitor")
                {
                    _idleService.Start();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_idleService.GetStatus(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "stop_monitor")
                {
                    _idleService.Stop();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_idleService.GetStatus(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "configure")
                {
                    if (args.ContainsKey("threshold_minutes"))
                        _idleService.Config.IdleThresholdMinutes = args["threshold_minutes"]!.GetValue<int>();
                    if (args.ContainsKey("trim_ssd"))
                        _idleService.Config.TrimSsdEnabled = args["trim_ssd"]!.GetValue<bool>();
                    if (args.ContainsKey("clean_temp"))
                        _idleService.Config.CleanTempEnabled = args["clean_temp"]!.GetValue<bool>();
                    if (args.ContainsKey("purge_ram"))
                        _idleService.Config.PurgeRamIfHighEnabled = args["purge_ram"]!.GetValue<bool>();

                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { config = _idleService.Config, status = _idleService.GetStatus() }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(_idleService.GetStatus(), new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_spotlight_wallpapers":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "scan";

                if (action == "export")
                {
                    string dest = args["destination_folder"]?.GetValue<string>()
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Spotlight Wallpapers");
                    var items = _spotlightService.ScanSpotlightAssets();
                    int count = await _spotlightService.ExportWallpapersAsync(items.Select(i => i.OriginalFilePath), dest);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { exported_count = count, destination = dest }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "set_wallpaper")
                {
                    string img = args["image_path"]?.GetValue<string>() ?? "";
                    bool ok = _spotlightService.SetAsDesktopWallpaper(img);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { success = ok, image = img }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    int minW = args["min_width"]?.GetValue<int>() ?? 1920;
                    var items = _spotlightService.ScanSpotlightAssets(minWidth: minW);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            case "win_port_conflict":
            {
                string action = args["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "diagnose_common";
                int port = args["port"]?.GetValue<int>() ?? 80;

                if (action == "list_listening")
                {
                    var listeners = _portService.GetListeningPorts();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(listeners, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "diagnose")
                {
                    var diag = _portService.DiagnosePort(port);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else if (action == "release_port")
                {
                    bool ok = await _portService.ReleasePortAsync(port);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { port, released = ok }, new JsonSerializerOptions { WriteIndented = true }) });
                }
                else
                {
                    var common = _portService.DiagnoseCommonPorts();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(common, new JsonSerializerOptions { WriteIndented = true }) });
                }
                return true;
            }

            default:
                return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OmniWin.Core.Services;

namespace OmniWin.Core.Mcp;

public class McpServer
{
    private readonly MemoryService _memoryService = new();
    private readonly DiskService _diskService = new();
    private readonly ProcessService _processService = new();
    private readonly StartupService _startupService = new();
    private readonly TweakService _tweakService = new();
    private readonly NetworkService _networkService = new();
    private readonly SecurityAuditService _securityAuditService = new();
    private readonly SoftwareService _softwareService = new();
    private readonly DismService _dismService = new();
    private readonly DriverService _driverService = new();
    private readonly PowerService _powerService = new();
    private readonly FileLockService _fileLockService = new();
    private readonly WindowsServiceService _windowsServiceService = new();
    private readonly ContextMenuService _contextMenuService = new();
    private HardwareService? _hardwareService;

    public async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        try
        {
            _hardwareService = new HardwareService();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HardwareService Init Warn]: {ex.Message}");
        }

        Console.Error.WriteLine("[OmniWin MCP] Servidor iniciado en modo stdio.");

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null) break;

            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonNode.Parse(line);
                if (request is JsonObject obj)
                {
                    await HandleJsonRpcMessageAsync(obj);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Error parsing JSON]: {ex.Message}");
            }
        }
    }

    private async Task HandleJsonRpcMessageAsync(JsonObject req)
    {
        var id = req["id"];
        var method = req["method"]?.GetValue<string>();

        if (string.IsNullOrEmpty(method)) return;

        if (id == null)
        {
            if (method == "notifications/initialized")
            {
                Console.Error.WriteLine("[OmniWin MCP] Cliente inicializado.");
            }
            return;
        }

        JsonObject response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone()
        };

        try
        {
            switch (method)
            {
                case "initialize":
                    response["result"] = new JsonObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = new JsonObject()
                        },
                        ["serverInfo"] = new JsonObject
                        {
                            ["name"] = "omniwin-mcp",
                            ["version"] = "1.2.0"
                        }
                    };
                    break;

                case "ping":
                    response["result"] = new JsonObject();
                    break;

                case "tools/list":
                    response["result"] = new JsonObject
                    {
                        ["tools"] = GetToolsList()
                    };
                    break;

                case "tools/call":
                    var paramsObj = req["params"] as JsonObject;
                    var toolName = paramsObj?["name"]?.GetValue<string>() ?? "";
                    var arguments = paramsObj?["arguments"] as JsonObject ?? new JsonObject();

                    var toolResult = await ExecuteToolAsync(toolName, arguments);
                    response["result"] = toolResult;
                    break;

                default:
                    response["error"] = new JsonObject
                    {
                        ["code"] = -32601,
                        ["message"] = $"Método no encontrado: {method}"
                    };
                    break;
            }
        }
        catch (Exception ex)
        {
            response["error"] = new JsonObject
            {
                ["code"] = -32603,
                ["message"] = $"Error interno: {ex.Message}"
            };
        }

        string jsonResponse = response.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        Console.WriteLine(jsonResponse);
    }

    public static JsonArray GetToolsList()
    {
        var tools = new JsonArray();

        // 1. win_get_system_health (with profiles)
        tools.Add(new JsonObject
        {
            ["name"] = "win_get_system_health",
            ["description"] = "Obtiene telemetría y salud del sistema según el perfil solicitado: 'quick', 'performance', 'network', 'security' o 'full'.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["profile"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "quick", "performance", "network", "security", "full" },
                        ["description"] = "Perfil de diagnóstico: 'quick', 'performance', 'network', 'security' o 'full'."
                    }
                }
            }
        });

        // 2. win_purge_ram
        tools.Add(new JsonObject
        {
            ["name"] = "win_purge_ram",
            ["description"] = "Libera memoria RAM instantáneamente purgando Standby List y Working Sets.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["purge_standby"] = new JsonObject { ["type"] = "boolean" },
                    ["purge_workingsets"] = new JsonObject { ["type"] = "boolean" }
                }
            }
        });

        // 3. win_analyze_disk_bloat
        tools.Add(new JsonObject
        {
            ["name"] = "win_analyze_disk_bloat",
            ["description"] = "Analiza el espacio ocupado por archivos temporales, caché de Windows Update, crash dumps y prefetch.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 4. win_clean_disk
        tools.Add(new JsonObject
        {
            ["name"] = "win_clean_disk",
            ["description"] = "Ejecuta la limpieza segura de categorías seleccionadas de archivos innecesarios y vaciado de papelera de reciclaje.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["categories"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    ["empty_recycle_bin"] = new JsonObject { ["type"] = "boolean" }
                },
                ["required"] = new JsonArray { "categories" }
            }
        });

        // 5. win_test_network
        tools.Add(new JsonObject
        {
            ["name"] = "win_test_network",
            ["description"] = "Diagnóstico de red: interfaces activas, ping a Cloudflare/Google, tiempo de resolución DNS y sockets TCP.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["target"] = new JsonObject { ["type"] = "string", ["description"] = "Host o IP adicional a evaluar" }
                }
            }
        });

        // 6. win_get_security_audit
        tools.Add(new JsonObject
        {
            ["name"] = "win_get_security_audit",
            ["description"] = "Auditoría de seguridad: estado de Antivirus, UAC y lectura de eventos críticos del sistema (crashes de apps, BSODs ID 41).",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 7. win_get_software_updates (WinGet)
        tools.Add(new JsonObject
        {
            ["name"] = "win_get_software_updates",
            ["description"] = "Consulta WinGet para listar todos los programas instalados que tienen actualizaciones de versión disponibles.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 8. win_upgrade_all_software (WinGet)
        tools.Add(new JsonObject
        {
            ["name"] = "win_upgrade_all_software",
            ["description"] = "Ejecuta la actualización desatendida y silenciosa de todas las aplicaciones obsoletas mediante WinGet.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 9. win_clean_dism_store (DISM)
        tools.Add(new JsonObject
        {
            ["name"] = "win_clean_dism_store",
            ["description"] = "Limpieza profunda del almacén de componentes de Windows (WinSxS) con DISM para recuperar entre 5 y 25 GB de parches viejos.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["reset_base"] = new JsonObject { ["type"] = "boolean", ["description"] = "Descartar versiones anteriores de componentes (no se podrán desinstalar updates previos)" }
                }
            }
        });

        // 10. win_run_sfc_scan (SFC)
        tools.Add(new JsonObject
        {
            ["name"] = "win_run_sfc_scan",
            ["description"] = "Ejecuta el Comprobador de Archivos de Sistema (sfc /scannow) para diagnosticar y reparar DLLs o archivos corruptos de Windows.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 11. win_list_drivers (PnPUtil)
        tools.Add(new JsonObject
        {
            ["name"] = "win_list_drivers",
            ["description"] = "Lista todos los paquetes de controladores de terceros (OEM) almacenados en el DriverStore del sistema.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 12. win_get_power_schemes
        tools.Add(new JsonObject
        {
            ["name"] = "win_get_power_schemes",
            ["description"] = "Lista los planes de energía disponibles y activos de Windows, así como la resolución actual del temporizador del sistema (timer resolution).",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 13. win_set_power_scheme
        tools.Add(new JsonObject
        {
            ["name"] = "win_set_power_scheme",
            ["description"] = "Activa un plan de energía específico, desbloquea el modo 'Máximo Rendimiento' (Ultimate Performance) o activa el temporizador de 0.5ms para gaming.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["scheme_guid"] = new JsonObject { ["type"] = "string", ["description"] = "GUID del plan a activar" },
                    ["unlock_ultimate"] = new JsonObject { ["type"] = "boolean", ["description"] = "Desbloquear el plan oculto Ultimate Performance" },
                    ["enable_05ms_timer"] = new JsonObject { ["type"] = "boolean", ["description"] = "Ajustar la resolución del temporizador a 0.5ms" }
                }
            }
        });

        // 14. win_list_processes
        tools.Add(new JsonObject
        {
            ["name"] = "win_list_processes",
            ["description"] = "Lista los procesos en ejecución ordenados por consumo de memoria RAM o CPU.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["limit"] = new JsonObject { ["type"] = "integer" },
                    ["sort_by"] = new JsonObject { ["type"] = "string" }
                }
            }
        });

        // 15. win_kill_process
        tools.Add(new JsonObject
        {
            ["name"] = "win_kill_process",
            ["description"] = "Termina un proceso en ejecución por su PID.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pid"] = new JsonObject { ["type"] = "integer" } },
                ["required"] = new JsonArray { "pid" }
            }
        });

        // 16. win_list_startup_items
        tools.Add(new JsonObject
        {
            ["name"] = "win_list_startup_items",
            ["description"] = "Lista todos los programas configurados para iniciar con Windows.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 17. win_toggle_startup_item
        tools.Add(new JsonObject
        {
            ["name"] = "win_toggle_startup_item",
            ["description"] = "Habilita o deshabilita un programa de inicio de Windows.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["scope"] = new JsonObject { ["type"] = "string" },
                    ["name"] = new JsonObject { ["type"] = "string" },
                    ["enable"] = new JsonObject { ["type"] = "boolean" }
                },
                ["required"] = new JsonArray { "scope", "name", "enable" }
            }
        });

        // 18. win_list_tweaks
        tools.Add(new JsonObject
        {
            ["name"] = "win_list_tweaks",
            ["description"] = "Lista las optimizaciones disponibles y su estado.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 19. win_apply_tweak
        tools.Add(new JsonObject
        {
            ["name"] = "win_apply_tweak",
            ["description"] = "Aplica una optimización específica con opción de Punto de Restauración.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["tweak_id"] = new JsonObject { ["type"] = "string" },
                    ["create_restore_point"] = new JsonObject { ["type"] = "boolean" }
                },
                ["required"] = new JsonArray { "tweak_id" }
            }
        });

        // 20. win_rollback_tweak
        tools.Add(new JsonObject
        {
            ["name"] = "win_rollback_tweak",
            ["description"] = "Revierte una optimización a sus valores originales.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["tweak_id"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray { "tweak_id" }
            }
        });

        // 21. win_heal_network
        tools.Add(new JsonObject
        {
            ["name"] = "win_heal_network",
            ["description"] = "Repara la pila de red: purga la caché DNS (/flushdns), reinicia el catálogo Winsock y vacía la tabla ARP.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 22. win_find_file_locks
        tools.Add(new JsonObject
        {
            ["name"] = "win_find_file_locks",
            ["description"] = "Identifica los procesos (PID, nombre, tipo de app) que tienen bloqueado un archivo o carpeta mediante el Restart Manager nativo de Windows.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["file_path"] = new JsonObject { ["type"] = "string", ["description"] = "Ruta absoluta del archivo o carpeta" } },
                ["required"] = new JsonArray { "file_path" }
            }
        });

        // 23. win_unlock_file
        tools.Add(new JsonObject
        {
            ["name"] = "win_unlock_file",
            ["description"] = "Libera un archivo bloqueado terminando los procesos que lo retienen para permitir su eliminación, edición o movimiento.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["file_path"] = new JsonObject { ["type"] = "string", ["description"] = "Ruta absoluta del archivo bloqueado" },
                    ["kill_processes"] = new JsonObject { ["type"] = "boolean", ["description"] = "Si es true, termina los procesos bloqueadores" }
                },
                ["required"] = new JsonArray { "file_path" }
            }
        });

        // 24. win_list_windows_services
        tools.Add(new JsonObject
        {
            ["name"] = "win_list_windows_services",
            ["description"] = "Lista servicios de Windows con su estado, tipo de inicio y recomendaciones de desactivación para servicios de telemetría y bloatware.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["bloat_only"] = new JsonObject { ["type"] = "boolean", ["description"] = "Filtrar solo servicios candidatos a optimización" } }
            }
        });

        // 25. win_set_service_state
        tools.Add(new JsonObject
        {
            ["name"] = "win_set_service_state",
            ["description"] = "Configura el modo de inicio de un servicio de Windows (disabled, demand, auto) y opcionalmente lo detiene.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["service_name"] = new JsonObject { ["type"] = "string" },
                    ["start_mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray { "disabled", "manual", "auto" } },
                    ["stop_now"] = new JsonObject { ["type"] = "boolean" }
                },
                ["required"] = new JsonArray { "service_name", "start_mode" }
            }
        });

        // 26. win_optimize_services
        tools.Add(new JsonObject
        {
            ["name"] = "win_optimize_services",
            ["description"] = "Desactiva y detiene en 1 clic los servicios de telemetría y diagnósticos pesados (DiagTrack, dmwappushservice, MapsBroker, RetailDemo).",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 27. win_list_context_menus
        tools.Add(new JsonObject
        {
            ["name"] = "win_list_context_menus",
            ["description"] = "Lista las extensiones registradas en los menús contextuales de Windows (clic derecho en escritorio, carpetas y archivos).",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 28. win_toggle_context_menu
        tools.Add(new JsonObject
        {
            ["name"] = "win_toggle_context_menu",
            ["description"] = "Habilita o deshabilita un elemento del menú contextual de forma no destructiva.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["parent_path"] = new JsonObject { ["type"] = "string" },
                    ["key_name"] = new JsonObject { ["type"] = "string" },
                    ["enable"] = new JsonObject { ["type"] = "boolean" }
                },
                ["required"] = new JsonArray { "parent_path", "key_name", "enable" }
            }
        });

        // 29. win_pcie_doctor
        tools.Add(new JsonObject
        {
            ["name"] = "win_pcie_doctor",
            ["description"] = "Diagnóstico profundo de ancho de carril y velocidad de enlace PCIe (GPU y unidades de almacenamiento NVMe) detectando degradación o estrangulamiento de hardware.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        // 30. win_bypassio_doctor
        tools.Add(new JsonObject
        {
            ["name"] = "win_bypassio_doctor",
            ["description"] = "Inspecciona la compatibilidad de DirectStorage BypassIO en los volúmenes del sistema e identifica controladores o filtros minifilter que bloquean la carga ultrarrápida.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["volume"] = new JsonObject { ["type"] = "string", ["description"] = "Letra de unidad o ruta a inspeccionar (opcional, por defecto todos los discos)" }
                }
            }
        });

        // 31. win_stutter_investigate
        tools.Add(new JsonObject
        {
            ["name"] = "win_stutter_investigate",
            ["description"] = "Analiza los últimos 15 a 30 segundos de telemetría del sistema para diagnosticar causas de tirones (throttling térmico, presión de RAM/pagefile, DPC latency o GPU stall).",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["lookback_seconds"] = new JsonObject { ["type"] = "integer", ["description"] = "Segundos a analizar hacia atrás (por defecto 15)" }
                }
            }
        });

        // 32. win_cpu_topology
        tools.Add(new JsonObject
        {
            ["name"] = "win_cpu_topology",
            ["description"] = "Obtiene la topología física y lógica de núcleos del procesador mediante Win32 CPU Sets nativo, clasificando P-Cores vs E-Cores y máscaras de afinidad sin adivinar por SKU.",
            ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
        });

        return tools;
    }

    private async Task<JsonObject> ExecuteToolAsync(string toolName, JsonObject args)
    {
        var content = new JsonArray();
        bool isError = false;

        try
        {
            switch (toolName)
            {
                case "win_get_system_health":
                    string profile = args["profile"]?.GetValue<string>()?.ToLowerInvariant() ?? "quick";
                    object healthReport = await GenerateHealthReportAsync(profile);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(healthReport, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_test_network":
                    string? target = args["target"]?.GetValue<string>();
                    var netReport = await _networkService.RunDiagnosticsAsync(target);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(netReport, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_get_security_audit":
                    var secReport = _securityAuditService.GetSecurityAudit();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(secReport, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_get_software_updates":
                    var updates = await _softwareService.GetUpgradableAppsAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(updates, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_upgrade_all_software":
                    var upgResult = await _softwareService.UpgradeAllAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = upgResult });
                    break;

                case "win_clean_dism_store":
                    bool resetBase = args["reset_base"]?.GetValue<bool>() ?? false;
                    var dismRes = await _dismService.CleanComponentStoreAsync(resetBase);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(dismRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_run_sfc_scan":
                    var sfcRes = await _dismService.RunSfcScanAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(sfcRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_list_drivers":
                    var drivers = await _driverService.GetOemDriversAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(drivers, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_get_power_schemes":
                    var schemes = await _powerService.GetPowerSchemesAsync();
                    var (min, max, cur) = _powerService.GetTimerResolution();
                    content.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = JsonSerializer.Serialize(new
                        {
                            schemes = schemes,
                            timer_resolution_ms = new { min, max, current = cur }
                        }, new JsonSerializerOptions { WriteIndented = true })
                    });
                    break;

                case "win_set_power_scheme":
                    string? guid = args["scheme_guid"]?.GetValue<string>();
                    bool unlock = args["unlock_ultimate"]?.GetValue<bool>() ?? false;
                    bool timer05 = args["enable_05ms_timer"]?.GetValue<bool>() ?? false;

                    string msg = "";
                    if (unlock)
                    {
                        msg += await _powerService.UnlockUltimatePerformanceSchemeAsync() + " ";
                    }
                    if (!string.IsNullOrEmpty(guid))
                    {
                        bool ok = await _powerService.SetActiveSchemeAsync(guid);
                        msg += ok ? $"Plan {guid} activado. " : "Error activando plan. ";
                    }
                    if (timer05)
                    {
                        var (ok, newRes) = _powerService.SetHighPrecisionTimer(true);
                        msg += ok ? $"Temporizador ajustado a {newRes} ms. " : "Fallo al ajustar temporizador. ";
                    }

                    content.Add(new JsonObject { ["type"] = "text", ["text"] = msg.Trim() });
                    break;

                case "win_purge_ram":
                    bool purgeStandby = args["purge_standby"]?.GetValue<bool>() ?? true;
                    bool purgeWs = args["purge_workingsets"]?.GetValue<bool>() ?? true;
                    var purgeRes = _memoryService.PurgeMemory(purgeStandby, purgeWs);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(purgeRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_analyze_disk_bloat":
                    var bloat = await _diskService.AnalyzeBloatAsync();
                    var bloatSummary = new
                    {
                        total_bloat_mb = Math.Round(bloat.TotalBloatBytes / (1024.0 * 1024.0), 1),
                        total_files = bloat.TotalBloatFiles,
                        categories = bloat.Categories.Select(c => new
                        {
                            id = c.Id,
                            name = c.Name,
                            size_mb = Math.Round(c.SizeBytes / (1024.0 * 1024.0), 1),
                            files = c.FileCount,
                            requires_admin = c.RequiresAdmin
                        })
                    };
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(bloatSummary, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_clean_disk":
                    var catIds = new List<string>();
                    if (args["categories"] is JsonArray catArr)
                    {
                        foreach (var item in catArr) if (item != null) catIds.Add(item.GetValue<string>());
                    }
                    bool emptyBin = args["empty_recycle_bin"]?.GetValue<bool>() ?? true;
                    var cleanRes = await _diskService.CleanBloatAsync(new DiskCleanOptions { CategoryIdsToClean = catIds, EmptyRecycleBin = emptyBin });
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(cleanRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_list_processes":
                    int limit = args["limit"]?.GetValue<int>() ?? 30;
                    string sortBy = args["sort_by"]?.GetValue<string>() ?? "memory";
                    var procs = _processService.GetRunningProcesses(limit, sortBy);
                    var procList = procs.Select(p => new { pid = p.Pid, name = p.Name, title = p.Title, memory_mb = Math.Round(p.MemoryMB, 1), threads = p.ThreadCount, path = p.FilePath });
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(procList, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_kill_process":
                    int killPid = args["pid"]?.GetValue<int>() ?? 0;
                    var killRes = _processService.KillProcess(killPid);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(killRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_list_startup_items":
                    var startItems = _startupService.GetStartupItems();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(startItems, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_toggle_startup_item":
                    string scope = args["scope"]?.GetValue<string>() ?? "User";
                    string name = args["name"]?.GetValue<string>() ?? "";
                    bool enable = args["enable"]?.GetValue<bool>() ?? false;
                    var toggleRes = _startupService.ToggleStartupItem(scope, name, enable);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(toggleRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_list_tweaks":
                    var tweaks = _tweakService.GetTweaks();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(tweaks, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_apply_tweak":
                    string twId = args["tweak_id"]?.GetValue<string>() ?? "";
                    bool restore = args["create_restore_point"]?.GetValue<bool>() ?? false;
                    var twRes = _tweakService.ApplyTweak(twId, restore);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(twRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_rollback_tweak":
                    string rbId = args["tweak_id"]?.GetValue<string>() ?? "";
                    var rbRes = _tweakService.RollbackTweak(rbId);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(rbRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_heal_network":
                    var healRes = await _networkService.HealNetworkAsync();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(healRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_find_file_locks":
                    string lockPath = args["file_path"]?.GetValue<string>() ?? "";
                    var locks = _fileLockService.GetLockingProcesses(lockPath);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(locks, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_unlock_file":
                    string unlockPath = args["file_path"]?.GetValue<string>() ?? "";
                    bool killProc = args["kill_processes"]?.GetValue<bool>() ?? true;
                    var unlockRes = _fileLockService.UnlockFile(unlockPath, killProc);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(unlockRes, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_list_windows_services":
                    bool bloatOnly = args["bloat_only"]?.GetValue<bool>() ?? false;
                    var svcList = _windowsServiceService.GetServices(bloatOnly);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(svcList, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_set_service_state":
                    string sName = args["service_name"]?.GetValue<string>() ?? "";
                    string sMode = args["start_mode"]?.GetValue<string>() ?? "demand";
                    bool sStop = args["stop_now"]?.GetValue<bool>() ?? false;
                    bool sOk = _windowsServiceService.SetServiceState(sName, sMode, sStop);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { service = sName, start_mode = sMode, stopped = sStop, success = sOk }) });
                    break;

                case "win_optimize_services":
                    var optSvcs = _windowsServiceService.OptimizeTelemetryServices();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { optimized_services = optSvcs, message = $"Se optimizaron {optSvcs.Count} servicios de telemetría." }, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_list_context_menus":
                    var ctxItems = _contextMenuService.GetContextMenuItems();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(ctxItems, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_toggle_context_menu":
                    string pPath = args["parent_path"]?.GetValue<string>() ?? "";
                    string kName = args["key_name"]?.GetValue<string>() ?? "";
                    bool cEnable = args["enable"]?.GetValue<bool>() ?? true;
                    bool ctxOk = _contextMenuService.ToggleContextMenuItem(pPath, kName, cEnable);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(new { parent = pPath, key = kName, enabled = cEnable, success = ctxOk }) });
                    break;

                case "win_pcie_doctor":
                    var pcieReport = PcieLinkInspector.Instance.RunDoctorCheck();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(pcieReport, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_bypassio_doctor":
                    string? vol = args["volume"]?.GetValue<string>();
                    object bypassResult = string.IsNullOrWhiteSpace(vol)
                        ? BypassIoService.Instance.CheckSystemBypassIoState()
                        : BypassIoService.Instance.CheckVolumeBypassIo(vol);
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(bypassResult, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_stutter_investigate":
                    int seconds = args["lookback_seconds"]?.GetValue<int>() ?? 15;
                    var stutterReport = StutterInvestigatorService.Instance.AnalyzeRecentStutter(TimeSpan.FromSeconds(seconds));
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(stutterReport, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                case "win_cpu_topology":
                    var topo = CpuTopologyService.Instance.GetTopology();
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(topo, new JsonSerializerOptions { WriteIndented = true }) });
                    break;

                default:
                    isError = true;
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = $"Herramienta no implementada: {toolName}" });
                    break;
            }
        }
        catch (Exception ex)
        {
            isError = true;
            content.Add(new JsonObject { ["type"] = "text", ["text"] = $"Error ejecutando {toolName}: {ex.Message}" });
        }

        return new JsonObject { ["content"] = content, ["isError"] = isError };
    }

    private async Task<object> GenerateHealthReportAsync(string profile)
    {
        var mem = _memoryService.GetMemoryStats();
        var drives = _diskService.GetDriveVolumes();

        if (profile == "quick")
        {
            var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
            return new
            {
                profile = "quick",
                ram = new
                {
                    total_gb = Math.Round(mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    used_gb = Math.Round(mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    available_gb = Math.Round(mem.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    usage_percent = Math.Round(mem.UsagePercentage, 1)
                },
                system_drive = cDrive != null ? new
                {
                    total_gb = Math.Round(cDrive.TotalBytes / (1024.0 * 1024.0 * 1024.0), 1),
                    free_gb = Math.Round(cDrive.FreeBytes / (1024.0 * 1024.0 * 1024.0), 1),
                    usage_percent = Math.Round(cDrive.UsagePercent, 1)
                } : null,
                uptime_hours = Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours, 1)
            };
        }

        if (profile == "performance")
        {
            var tele = _hardwareService?.GetTelemetrySnapshot() ?? new HardwareTelemetrySnapshot();
            var topProcs = _processService.GetRunningProcesses(10, "memory")
                .Select(p => new { pid = p.Pid, name = p.Name, memory_mb = Math.Round(p.MemoryMB, 1) });

            return new
            {
                profile = "performance",
                cpu = new { name = tele.CpuName, load_percent = tele.CpuLoadPercent, temp_celsius = tele.CpuTemperatureCelsius, power_watts = tele.CpuPowerWatts },
                gpu = new { name = tele.GpuName, load_percent = tele.GpuLoadPercent, temp_celsius = tele.GpuTemperatureCelsius, vram_used_mb = tele.GpuMemoryUsedMB },
                ram = new
                {
                    total_gb = Math.Round(mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    used_gb = Math.Round(mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    usage_percent = Math.Round(mem.UsagePercentage, 1)
                },
                top_memory_consumers = topProcs
            };
        }

        if (profile == "network")
        {
            var netReport = await _networkService.RunDiagnosticsAsync();
            return new { profile = "network", network = netReport };
        }

        if (profile == "security")
        {
            var secReport = _securityAuditService.GetSecurityAudit();
            return new { profile = "security", security = secReport };
        }

        // Full profile
        var teleFull = _hardwareService?.GetTelemetrySnapshot() ?? new HardwareTelemetrySnapshot();
        var netFull = await _networkService.RunDiagnosticsAsync();
        var secFull = _securityAuditService.GetSecurityAudit();

        return new
        {
            profile = "full",
            cpu = new { name = teleFull.CpuName, load_percent = teleFull.CpuLoadPercent, temp_celsius = teleFull.CpuTemperatureCelsius, power_watts = teleFull.CpuPowerWatts },
            gpu = new { name = teleFull.GpuName, load_percent = teleFull.GpuLoadPercent, temp_celsius = teleFull.GpuTemperatureCelsius, vram_used_mb = teleFull.GpuMemoryUsedMB },
            ram = new
            {
                total_gb = Math.Round(mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                used_gb = Math.Round(mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                available_gb = Math.Round(mem.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                usage_percent = Math.Round(mem.UsagePercentage, 1)
            },
            drives = drives.Select(d => new
            {
                name = d.Name,
                label = d.Label,
                total_gb = Math.Round(d.TotalBytes / (1024.0 * 1024.0), 1),
                free_gb = Math.Round(d.FreeBytes / (1024.0 * 1024.0), 1),
                usage_percent = Math.Round(d.UsagePercent, 1)
            }),
            security = secFull,
            network_summary = new
            {
                has_internet = netFull.HasInternetAccess,
                dns_latency_ms = netFull.DnsResolutionTimeMs,
                active_sockets = netFull.ActiveTcpConnections
            }
        };
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

/// <summary>
/// Acciones posibles para las reglas Attack Surface Reduction (ASR) de Microsoft Defender.
/// </summary>
public enum AsrRuleAction
{
    Disabled = 0,
    Block = 1,
    Audit = 2
}

/// <summary>
/// Definición estática de una regla ASR nativa de Microsoft Defender.
/// </summary>
public class AsrRuleDefinition
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string MitigationType { get; set; } = string.Empty;
    public AsrRuleAction DefaultAction { get; set; } = AsrRuleAction.Block;
}

/// <summary>
/// Estado actual de una regla ASR configurada en el sistema.
/// </summary>
public class AsrRuleItem
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string MitigationType { get; set; } = string.Empty;
    public AsrRuleAction Action { get; set; } = AsrRuleAction.Disabled;

    public string ActionText => Action switch
    {
        AsrRuleAction.Block => "Bloquear",
        AsrRuleAction.Audit => "Auditar",
        _ => "Desactivado"
    };

    public bool IsBlocked => Action == AsrRuleAction.Block;
    public bool IsAudited => Action == AsrRuleAction.Audit;
    public bool IsDisabled => Action == AsrRuleAction.Disabled;
}

/// <summary>
/// Estado general del motor Microsoft Defender y recuento de reglas ASR.
/// </summary>
public class DefenderGeneralStatus
{
    public bool AntivirusEnabled { get; set; } = true;
    public bool RealTimeProtectionEnabled { get; set; } = true;
    public bool IoavProtectionEnabled { get; set; } = true;
    public bool AntispywareEnabled { get; set; } = true;
    public bool AMServiceEnabled { get; set; } = true;
    public int ActiveRulesCount { get; set; }
    public int BlockedRulesCount { get; set; }
    public int AuditedRulesCount { get; set; }
    public int DisabledRulesCount { get; set; }
    public int TotalRulesCount { get; set; } = 16;

    public string OverallStatusText => RealTimeProtectionEnabled ? "Protección Activa" : "Protección Desactivada";
}

/// <summary>
/// Resultado de la ejecución de una acción de configuración ASR.
/// </summary>
public class AsrActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }
    public int RulesUpdatedCount { get; set; }
}

/// <summary>
/// Servicio de gestión y auditoría de reglas Attack Surface Reduction (ASR) de Microsoft Defender.
/// </summary>
public class AsrRulesService
{
    public const string ProfileMaxSecurity = "Máxima Seguridad";
    public const string ProfileDeveloper = "Equilibrado / Developer";
    public const string ProfileDisabled = "Desactivar Todas";

    /// <summary>
    /// Catálogo oficial de las 16 reglas ASR nativas de Microsoft con sus GUIDs canónicos.
    /// </summary>
    public static readonly IReadOnlyList<AsrRuleDefinition> NativeRules = new List<AsrRuleDefinition>
    {
        new AsrRuleDefinition
        {
            Guid = "56a8634f-1139-427a-a85b-0f5a066ab21f",
            Name = "Bloquear creación de procesos hijos por Adobe Reader",
            Category = "Office & Aplicaciones",
            MitigationType = "Exploit Mitigation",
            Description = "Evita que Adobe Acrobat Reader cree procesos secundarios no autorizados, mitigando exploits dirigidos a PDFs maliciosos."
        },
        new AsrRuleDefinition
        {
            Guid = "7674ba52-37eb-46dc-a42b-2e6f00c14f4f",
            Name = "Bloquear inyección de código de aplicaciones de Office en otros procesos",
            Category = "Office & Aplicaciones",
            MitigationType = "Process Injection",
            Description = "Impide que aplicaciones de Office (Word, Excel, PowerPoint) inyecten código malicioso en otros procesos en ejecución para evadir defensas."
        },
        new AsrRuleDefinition
        {
            Guid = "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2",
            Name = "Bloquear robo de credenciales desde el subsistema LSASS de Windows",
            Category = "Credenciales & Sistema",
            MitigationType = "Credential Dumping",
            Description = "Bloquea herramientas como Mimikatz o volcados de memoria no autorizados dirigidos al proceso Local Security Authority Subsystem Service (lsass.exe)."
        },
        new AsrRuleDefinition
        {
            Guid = "d4f940ab-401b-4efc-aadc-ad5f3c50688a",
            Name = "Bloquear creación de procesos secundarios por aplicaciones de Office",
            Category = "Office & Aplicaciones",
            MitigationType = "Exploit Mitigation",
            Description = "Impide que macros y documentos de Office generen procesos hijos arbitrarios (como cmd.exe, powershell.exe o binarios descargados)."
        },
        new AsrRuleDefinition
        {
            Guid = "b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4",
            Name = "Bloquear ejecución de procesos no confiables desde dispositivos USB",
            Category = "Protección Malware",
            MitigationType = "Removable Media",
            Description = "Bloquea ejecutables no firmados o no confiables que intenten iniciarse directamente desde pendrives, discos externos y unidades extraíbles USB."
        },
        new AsrRuleDefinition
        {
            Guid = "26190899-773f-478c-b965-0222241fb74a",
            Name = "Bloquear contenido ejecutable proveniente de clientes de correo",
            Category = "Protección Malware",
            MitigationType = "Email Protection",
            Description = "Detiene la ejecución de archivos adjuntos ejecutables y scripts peligrosos en Microsoft Outlook y servicios de correo web."
        },
        new AsrRuleDefinition
        {
            Guid = "3b576839-7123-495a-8460-362c16e3d019",
            Name = "Bloquear creación de contenido ejecutable por aplicaciones de Office",
            Category = "Office & Aplicaciones",
            MitigationType = "Payload Drop",
            Description = "Bloquea la escritura de ejecutables (.exe, .dll, scripts) en disco por parte de aplicaciones de Office para prevenir la colocación de payloads secundarios."
        },
        new AsrRuleDefinition
        {
            Guid = "756e358d-1488-49e2-86d7-ecbed922f94e",
            Name = "Bloquear ejecución de scripts ofuscados en Office",
            Category = "Scripts & Intérpretes",
            MitigationType = "Obfuscation",
            Description = "Detecta y bloquea scripts VBA y macros ofuscadas diseñadas para eludir el análisis estático y firmas de antivirus tradicionales."
        },
        new AsrRuleDefinition
        {
            Guid = "d3e037e1-3eb8-44c8-a917-57927947596d",
            Name = "Bloquear lanzamiento de ejecutables descargados mediante JS o VBS",
            Category = "Scripts & Intérpretes",
            MitigationType = "Script Downloader",
            Description = "Impide que scripts en JavaScript o VBScript descarguen e inicien ejecutables no reconocidos desde servidores de Internet."
        },
        new AsrRuleDefinition
        {
            Guid = "e6db77e5-33e2-472b-9254-504432d709e1",
            Name = "Bloquear persistencia mediante suscripciones a eventos WMI",
            Category = "Credenciales & Sistema",
            MitigationType = "Persistence",
            Description = "Evita que amenazas avanzadas establezcan mecanismos de persistencia o ejecución encubierta utilizando filtros y consumidores de eventos WMI."
        },
        new AsrRuleDefinition
        {
            Guid = "c1db55ab-c21a-4637-bb3f-a12568109d35",
            Name = "Activar protección avanzada contra Ransomware",
            Category = "Protección Malware",
            MitigationType = "Ransomware Defense",
            Description = "Aplica heurísticas avanzadas y análisis de comportamiento continuo para interceptar y bloquear actividad típica de cifrado ransomware."
        },
        new AsrRuleDefinition
        {
            Guid = "01443614-cd74-433a-b99e-2ecdc07bfc25",
            Name = "Bloquear ejecutables que no cumplan criterios de prevalencia o antigüedad",
            Category = "Protección Malware",
            MitigationType = "SmartScreen Cloud",
            Description = "Bloquea binarios recién descubiertos o con muy baja reputación global según la nube de inteligencia de amenazas de Microsoft Defender."
        },
        new AsrRuleDefinition
        {
            Guid = "c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb",
            Name = "Bloquear abuso de controladores vulnerables firmados (BYOVD)",
            Category = "Credenciales & Sistema",
            MitigationType = "Kernel Protection",
            Description = "Previene ataques Bring Your Own Vulnerable Driver (BYOVD) donde atacantes cargan drivers con fallos conocidos para desactivar protecciones EDR en modo kernel."
        },
        new AsrRuleDefinition
        {
            Guid = "a8f5898e-1dc8-49a9-9878-85004b8a61e6",
            Name = "Bloquear creación de Webshells en Windows Server",
            Category = "Protección Malware",
            MitigationType = "Webshell Protection",
            Description = "Bloquea la generación de scripts y artefactos webshell en directorios de servicios web (IIS / Apache) en entornos cliente y servidor."
        },
        new AsrRuleDefinition
        {
            Guid = "33ddedf1-c6e0-47cb-833e-de6133960387",
            Name = "Bloquear reinicio forzado de la máquina en Modo Seguro",
            Category = "Credenciales & Sistema",
            MitigationType = "Anti-Tampering",
            Description = "Impide que atacantes o ransomware fuercen un reinicio en Modo Seguro de Windows para evadir servicios y controladores de seguridad."
        },
        new AsrRuleDefinition
        {
            Guid = "4f940ab0-1a52-4140-a244-4670c3202575",
            Name = "Bloquear procesos hijos de intérpretes de scripts (PowerShell/cmd)",
            Category = "Scripts & Intérpretes",
            MitigationType = "Lateral Movement",
            Description = "Previene la ejecución y spawn de procesos maliciosos originados desde comandos PowerShell, cmd.exe, PSExec o comandos WMI."
        }
    };

    /// <summary>
    /// Obtiene las 16 reglas ASR con su estado actual configurado en Microsoft Defender.
    /// Consulta el estado actual de las reglas vía PowerShell Get-MpPreference.
    /// </summary>
    public async Task<List<AsrRuleItem>> GetRulesAsync()
    {
        var configuredRules = await QueryConfiguredRulesMapAsync();

        var result = new List<AsrRuleItem>(NativeRules.Count);
        foreach (var def in NativeRules)
        {
            var action = AsrRuleAction.Disabled;
            if (configuredRules.TryGetValue(def.Guid.ToLowerInvariant(), out int actionCode))
            {
                action = actionCode switch
                {
                    1 => AsrRuleAction.Block,
                    2 => AsrRuleAction.Audit,
                    _ => AsrRuleAction.Disabled
                };
            }

            result.Add(new AsrRuleItem
            {
                Guid = def.Guid,
                Name = def.Name,
                Description = def.Description,
                Category = def.Category,
                MitigationType = def.MitigationType,
                Action = action
            });
        }

        return result;
    }

    /// <summary>
    /// Obtiene el estado general de Microsoft Defender y el conteo de reglas ASR activas.
    /// </summary>
    public async Task<DefenderGeneralStatus> GetDefenderStatusAsync()
    {
        var status = new DefenderGeneralStatus();

        string script = @"
$ProgressPreference = 'SilentlyContinue'
$pref = Get-MpPreference -ErrorAction SilentlyContinue
$compStatus = Get-MpComputerStatus -ErrorAction SilentlyContinue
$ids = @($pref.AttackSurfaceReductionRules_Ids)
$actions = @($pref.AttackSurfaceReductionRules_Actions)
$map = @{}
for ($i = 0; $i -lt $ids.Count; $i++) {
    $id = $ids[$i]
    if ($id) {
        $map[$id.ToString().ToLower()] = [int]$actions[$i]
    }
}
[PSCustomObject]@{
    AntivirusEnabled = [bool]($compStatus.AntivirusEnabled -eq $true)
    RealTimeProtectionEnabled = [bool]($compStatus.RealTimeProtectionEnabled -eq $true)
    IoavProtectionEnabled = [bool]($compStatus.IoavProtectionEnabled -eq $true)
    AntispywareEnabled = [bool]($compStatus.AntispywareEnabled -eq $true)
    AMServiceEnabled = [bool]($compStatus.AMServiceEnabled -eq $true)
    Rules = $map
} | ConvertTo-Json -Compress
";

        var (exitCode, output, _) = await RunPowerShellScriptAsync(script);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            try
            {
                var doc = JsonNode.Parse(output);
                if (doc != null)
                {
                    status.AntivirusEnabled = doc["AntivirusEnabled"]?.GetValue<bool>() ?? true;
                    status.RealTimeProtectionEnabled = doc["RealTimeProtectionEnabled"]?.GetValue<bool>() ?? true;
                    status.IoavProtectionEnabled = doc["IoavProtectionEnabled"]?.GetValue<bool>() ?? true;
                    status.AntispywareEnabled = doc["AntispywareEnabled"]?.GetValue<bool>() ?? true;
                    status.AMServiceEnabled = doc["AMServiceEnabled"]?.GetValue<bool>() ?? true;

                    if (doc["Rules"] is JsonObject rulesObj)
                    {
                        int blocked = 0;
                        int audited = 0;
                        foreach (var def in NativeRules)
                        {
                            if (rulesObj.TryGetPropertyValue(def.Guid.ToLowerInvariant(), out var actNode) && actNode != null)
                            {
                                int a = actNode.GetValue<int>();
                                if (a == 1) blocked++;
                                else if (a == 2) audited++;
                            }
                        }
                        status.BlockedRulesCount = blocked;
                        status.AuditedRulesCount = audited;
                        status.ActiveRulesCount = blocked + audited;
                        status.DisabledRulesCount = NativeRules.Count - status.ActiveRulesCount;
                    }
                }
            }
            catch
            {
                // Fallback a conteos predeterminados
            }
        }

        return status;
    }

    /// <summary>
    /// Modifica el estado de una regla ASR específica (0 = Desactivado, 1 = Bloquear, 2 = Auditar) vía Set-MpPreference.
    /// </summary>
    public async Task<AsrActionResult> SetRuleActionAsync(string ruleGuid, AsrRuleAction action)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new AsrActionResult
            {
                Success = false,
                RequiresAdmin = true,
                Message = "Se requieren permisos de Administrador para modificar las reglas ASR de Microsoft Defender.\nPor favor reinicia OmniWin como Administrador."
            };
        }

        var targetDef = NativeRules.FirstOrDefault(r => r.Guid.Equals(ruleGuid, StringComparison.OrdinalIgnoreCase));
        if (targetDef == null)
        {
            return new AsrActionResult
            {
                Success = false,
                Message = $"El GUID '{ruleGuid}' no corresponde a ninguna regla ASR nativa soportada."
            };
        }

        int actionCode = (int)action;

        // Obtenemos el mapa actual de reglas para actualizar la regla específica de manera segura con Set-MpPreference
        var currentMap = await QueryConfiguredRulesMapAsync();
        currentMap[ruleGuid.ToLowerInvariant()] = actionCode;

        var ids = new List<string>();
        var actions = new List<int>();

        foreach (var def in NativeRules)
        {
            string key = def.Guid.ToLowerInvariant();
            int act = currentMap.TryGetValue(key, out int val) ? val : 0;
            ids.Add(def.Guid);
            actions.Add(act);
        }

        return await ApplyRulesBatchAsync(ids, actions, $"Regla '{targetDef.Name}' configurada en modo {action}.");
    }

    /// <summary>
    /// Aplica un perfil preconfigurado de reglas ASR.
    /// 'Máxima Seguridad': todas las 16 reglas en Bloquear (1).
    /// 'Equilibrado / Developer': LSASS y scripts bloqueados, sin bloquear prevalencia de ejecutables locales.
    /// 'Desactivar Todas': todas las 16 reglas en Desactivado (0).
    /// </summary>
    public async Task<AsrActionResult> ApplyProfileAsync(string profileName)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new AsrActionResult
            {
                Success = false,
                RequiresAdmin = true,
                Message = "Se requieren privilegios de Administrador para aplicar perfiles ASR de Microsoft Defender.\nPor favor reinicia OmniWin como Administrador."
            };
        }

        var ids = new List<string>();
        var actions = new List<int>();

        switch (profileName.Trim())
        {
            case ProfileMaxSecurity:
            case "Máxima Protección":
            case "MaxSecurity":
                foreach (var def in NativeRules)
                {
                    ids.Add(def.Guid);
                    actions.Add(1); // 1 = Bloquear todas
                }
                return await ApplyRulesBatchAsync(ids, actions, "Perfil 'Máxima Protección' aplicado: 16 reglas ASR configuradas en Bloquear.");

            case ProfileDeveloper:
            case "Modo Desarrollador/Gamer":
            case "Modo Desarrollador / Gamer":
            case "Developer":
                foreach (var def in NativeRules)
                {
                    ids.Add(def.Guid);

                    // Regla de prevalencia/antigüedad (01443614-cd74-433a-b99e-2ecdc07bfc25):
                    // Desactivada (0) para no bloquear compiladores locales (rustc, dotnet, gcc, cargo) ni juegos.
                    if (def.Guid.Equals("01443614-cd74-433a-b99e-2ecdc07bfc25", StringComparison.OrdinalIgnoreCase))
                    {
                        actions.Add(0); // Desactivado
                    }
                    // Regla de procesos hijos de scripts (4f940ab0-1a52-4140-a244-4670c3202575):
                    // Bloqueada como solicita el requerimiento de scripts protegidos
                    else if (def.Guid.Equals("4f940ab0-1a52-4140-a244-4670c3202575", StringComparison.OrdinalIgnoreCase))
                    {
                        actions.Add(1); // Bloquear
                    }
                    // LSASS, Office, Ransomware, BYOVD, Scripts, USB, WMI: todos bloqueados
                    else
                    {
                        actions.Add(1); // Bloquear
                    }
                }
                return await ApplyRulesBatchAsync(ids, actions, "Perfil 'Modo Desarrollador / Gamer' aplicado: LSASS, scripts y ransomware bloqueados; prevalencia local permitida.");

            case ProfileDisabled:
            case "Disabled":
                foreach (var def in NativeRules)
                {
                    ids.Add(def.Guid);
                    actions.Add(0); // 0 = Desactivado
                }
                return await ApplyRulesBatchAsync(ids, actions, "Perfil 'Desactivar Todas' aplicado: 16 reglas ASR desactivadas.");

            default:
                return new AsrActionResult
                {
                    Success = false,
                    Message = $"Perfil desconocido: '{profileName}'."
                };
        }
    }

    /// <summary>
    /// Configura todas las 16 reglas ASR al estado especificado.
    /// </summary>
    public async Task<AsrActionResult> SetAllRulesActionAsync(AsrRuleAction action)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new AsrActionResult
            {
                Success = false,
                RequiresAdmin = true,
                Message = "Se requieren permisos de Administrador para modificar las reglas ASR."
            };
        }

        var ids = NativeRules.Select(r => r.Guid).ToList();
        var actions = Enumerable.Repeat((int)action, ids.Count).ToList();

        return await ApplyRulesBatchAsync(ids, actions, $"Todas las 16 reglas ASR configuradas en modo {action}.");
    }

    /// <summary>
    /// Aplica una lista de reglas y sus acciones correspondientes en lote utilizando Set-MpPreference.
    /// </summary>
    private async Task<AsrActionResult> ApplyRulesBatchAsync(List<string> ids, List<int> actions, string successMessage)
    {
        try
        {
            string idsArray = string.Join(",", ids.Select(g => $"'{g}'"));
            string actionsArray = string.Join(",", actions);

            string script = $@"
$ProgressPreference = 'SilentlyContinue'
$ids = @({idsArray})
$actions = @({actionsArray})
Set-MpPreference -AttackSurfaceReductionRules_Ids $ids -AttackSurfaceReductionRules_Actions $actions -ErrorAction Stop
";

            var (exitCode, _, error) = await RunPowerShellScriptAsync(script);
            if (exitCode == 0)
            {
                return new AsrActionResult
                {
                    Success = true,
                    Message = successMessage,
                    RulesUpdatedCount = ids.Count
                };
            }
            else
            {
                return new AsrActionResult
                {
                    Success = false,
                    Message = $"Error al configurar reglas ASR con Set-MpPreference: {error}"
                };
            }
        }
        catch (Exception ex)
        {
            return new AsrActionResult
            {
                Success = false,
                Message = $"Excepción al aplicar reglas ASR: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Consulta el diccionario de reglas ASR actualmente configuradas en el sistema mediante Get-MpPreference.
    /// </summary>
    private async Task<Dictionary<string, int>> QueryConfiguredRulesMapAsync()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        string script = @"
$ProgressPreference = 'SilentlyContinue'
$p = Get-MpPreference -ErrorAction SilentlyContinue
$ids = @($p.AttackSurfaceReductionRules_Ids)
$actions = @($p.AttackSurfaceReductionRules_Actions)
$map = @{}
for ($i = 0; $i -lt $ids.Count; $i++) {
    $id = $ids[$i]
    if ($id) {
        $map[$id.ToString().ToLower()] = [int]$actions[$i]
    }
}
$map | ConvertTo-Json -Compress
";

        var (exitCode, output, _) = await RunPowerShellScriptAsync(script);
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            try
            {
                var doc = JsonNode.Parse(output);
                if (doc is JsonObject obj)
                {
                    foreach (var prop in obj)
                    {
                        if (prop.Value != null && prop.Value.AsValue().TryGetValue<int>(out int actionVal))
                        {
                            map[prop.Key.ToLowerInvariant()] = actionVal;
                        }
                    }
                }
            }
            catch
            {
                // Devolver mapa vacío si no hay reglas configuradas o hay error de parseo
            }
        }

        return map;
    }

    /// <summary>
    /// Ejecuta un script de PowerShell codificado en Base64 para evitar errores de sintaxis y comillas en Windows.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellScriptAsync(string script)
    {
        try
        {
            byte[] bytes = Encoding.Unicode.GetBytes(script);
            string encoded = Convert.ToBase64String(bytes);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (-1, string.Empty, "No se pudo iniciar powershell.exe");

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();
            string stdOut = await outTask;
            string stdErr = await errTask;

            return (proc.ExitCode, stdOut.Trim(), stdErr.Trim());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}

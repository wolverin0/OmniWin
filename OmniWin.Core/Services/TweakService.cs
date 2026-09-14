using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class TweakDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresAdmin { get; set; }
    public bool IsApplied { get; set; }
}

public class TweakActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class TweakService
{
    public List<TweakDefinition> GetTweaks()
    {
        return new List<TweakDefinition>
        {
            new TweakDefinition
            {
                Id = "privacy_bing_search",
                Name = "Desactivar Bing en Búsqueda de Windows",
                Category = "Privacidad",
                Description = "Evita que las búsquedas del Menú Inicio envíen consultas a servidores de Bing.",
                RequiresAdmin = false,
                IsApplied = CheckBingSearchDisabled()
            },
            new TweakDefinition
            {
                Id = "gaming_game_dvr",
                Name = "Desactivar GameDVR y Grabación en Fondo",
                Category = "Gaming & Latencia",
                Description = "Reduce el input lag y micro-stuttering en juegos al deshabilitar la captura en segundo plano.",
                RequiresAdmin = false,
                IsApplied = CheckGameDVRDisabled()
            },
            new TweakDefinition
            {
                Id = "gaming_network_throttling",
                Name = "Desactivar Estrangulamiento de Red",
                Category = "Gaming & Latencia",
                Description = "Deshabilita el limitador de paquetes de red que Windows activa al reproducir contenido multimedia.",
                RequiresAdmin = true,
                IsApplied = CheckNetworkThrottlingDisabled()
            },
            new TweakDefinition
            {
                Id = "perf_menu_show_delay",
                Name = "Acelerar Apertura de Menús",
                Category = "Rendimiento Visual",
                Description = "Reduce el retardo artificial de animación de menús en el escritorio de 400ms a 50ms.",
                RequiresAdmin = false,
                IsApplied = CheckMenuDelayReduced()
            },
            new TweakDefinition
            {
                Id = "privacy_telemetry",
                Name = "Minimizar Telemetría de Windows",
                Category = "Privacidad",
                Description = "Configura el nivel de datos de diagnóstico al mínimo permitido por el sistema.",
                RequiresAdmin = true,
                IsApplied = CheckTelemetryMinimized()
            }
        };
    }

    public static bool CreateRestorePoint(string description = "OmniWin Restore Point")
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public TweakActionResult ApplyTweak(string tweakId, bool createRestorePoint = false)
    {
        if (createRestorePoint)
        {
            CreateRestorePoint($"OmniWin - Antes de {tweakId}");
        }

        try
        {
            switch (tweakId)
            {
                case "privacy_bing_search":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Windows\Explorer", true))
                    {
                        key.SetValue("DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord);
                    }
                    using (var key2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                    {
                        key2.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "Búsqueda web de Bing desactivada." };

                case "gaming_game_dvr":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", true))
                    {
                        key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    }
                    using (var key2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", true))
                    {
                        key2.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "GameDVR y captura en fondo desactivados." };

                case "gaming_network_throttling":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true))
                    {
                        key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                        key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "Estrangulamiento de red multimedia desactivado." };

                case "perf_menu_show_delay":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        key.SetValue("MenuShowDelay", "50", RegistryValueKind.String);
                    }
                    return new TweakActionResult { Success = true, Message = "Retardo de menús ajustado a 50ms." };

                case "privacy_telemetry":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true))
                    {
                        key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "Nivel de telemetría minimizado." };

                default:
                    return new TweakActionResult { Success = false, Message = $"Tweak desconocido: {tweakId}" };
            }
        }
        catch (Exception ex)
        {
            return new TweakActionResult { Success = false, Message = $"Error aplicando tweak: {ex.Message}" };
        }
    }

    public TweakActionResult RollbackTweak(string tweakId)
    {
        try
        {
            switch (tweakId)
            {
                case "privacy_bing_search":
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer", true))
                    {
                        key?.DeleteValue("DisableSearchBoxSuggestions", false);
                    }
                    using (var key2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                    {
                        key2?.DeleteValue("BingSearchEnabled", false);
                    }
                    return new TweakActionResult { Success = true, Message = "Búsqueda web de Bing restaurada." };

                case "gaming_game_dvr":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", true))
                    {
                        key.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);
                    }
                    using (var key2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", true))
                    {
                        key2.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "GameDVR restaurado a su valor predeterminado." };

                case "gaming_network_throttling":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true))
                    {
                        key.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                        key.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "Estrangulamiento de red restaurado a valores por defecto." };

                case "perf_menu_show_delay":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        key.SetValue("MenuShowDelay", "400", RegistryValueKind.String);
                    }
                    return new TweakActionResult { Success = true, Message = "Retardo de menús restaurado a 400ms." };

                case "privacy_telemetry":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true))
                    {
                        key.SetValue("AllowTelemetry", 1, RegistryValueKind.DWord);
                    }
                    return new TweakActionResult { Success = true, Message = "Telemetría restaurada." };

                default:
                    return new TweakActionResult { Success = false, Message = $"Tweak desconocido: {tweakId}" };
            }
        }
        catch (Exception ex)
        {
            return new TweakActionResult { Success = false, Message = $"Error revirtiendo tweak: {ex.Message}" };
        }
    }

    private static bool CheckBingSearchDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
            return Convert.ToInt32(key?.GetValue("DisableSearchBoxSuggestions") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckGameDVRDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            return Convert.ToInt32(key?.GetValue("GameDVR_Enabled") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckNetworkThrottlingDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            return Convert.ToInt64(key?.GetValue("NetworkThrottlingIndex") ?? 10) == 0xFFFFFFFF;
        }
        catch { return false; }
    }

    private static bool CheckMenuDelayReduced()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return (key?.GetValue("MenuShowDelay")?.ToString() ?? "400") == "50";
        }
        catch { return false; }
    }

    private static bool CheckTelemetryMinimized()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
            return Convert.ToInt32(key?.GetValue("AllowTelemetry") ?? 1) == 0;
        }
        catch { return false; }
    }
}

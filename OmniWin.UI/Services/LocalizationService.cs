using System;
using System.Collections.Generic;
using OmniWin.Core.Services;

namespace OmniWin.UI.Services;

public class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public string CurrentLanguage { get; private set; } = "es";
    public event Action<string>? LanguageChanged;

    private readonly Dictionary<string, Dictionary<string, string>> _strings = new()
    {
        ["es"] = new Dictionary<string, string>
        {
            ["AppTitle"] = "OmniWin — Windows Control Plane, Optimizer & MCP Agent",
            ["Dashboard"] = "Dashboard Central",
            ["Telemetry"] = "Telemetría & Hardware",
            ["Processes"] = "Forense de Procesos",
            ["Ram"] = "Memoria RAM",
            ["Power"] = "Energía & CPU",
            ["Disk"] = "Limpieza de Disco",
            ["DiskSpace"] = "Espacio en Disco",
            ["Tweaks"] = "Tweaks & Debloat",
            ["Maintenance"] = "Mantenimiento",
            ["RepairConsole"] = "Consola Reparación",
            ["Network"] = "Red & Sockets",
            ["FileUnlock"] = "Desbloqueo Archivos",
            ["Startup"] = "Inicio de Windows",
            ["AudioMixer"] = "Mezclador de Audio",
            ["AsrRules"] = "Reglas Defender ASR",
            ["Bsod"] = "Caja Negra & BSOD",
            ["Software"] = "Software & Drivers",
            ["Mcp"] = "Servidor IA / MCP",
            ["QuickPurge"] = "⚡ Liberar RAM",
            ["AwakeOn"] = "☕ Cafeína: ON",
            ["AwakeOff"] = "☕ Cafeína: Off",
            ["WidgetShow"] = "📌 Widget Flotante",
            ["WidgetHide"] = "📌 Ocultar Widget",
            ["OverlayHud"] = "🎮 Overlay HUD",
            ["Admin"] = "Administrador",
            ["Ready"] = "OmniWin activo • Listo"
        },
        ["en"] = new Dictionary<string, string>
        {
            ["AppTitle"] = "OmniWin — Windows Control Plane, Optimizer & MCP Agent",
            ["Dashboard"] = "Central Dashboard",
            ["Telemetry"] = "Telemetry & Hardware",
            ["Processes"] = "Process Explorer",
            ["Ram"] = "RAM Memory",
            ["Power"] = "Power & CPU Cores",
            ["Disk"] = "Disk Cleaner",
            ["DiskSpace"] = "Disk Space Analyzer",
            ["Tweaks"] = "Tweaks & Debloat",
            ["Maintenance"] = "Maintenance",
            ["RepairConsole"] = "Repair Console",
            ["Network"] = "Network & Sockets",
            ["FileUnlock"] = "File Unlocker",
            ["Startup"] = "Windows Startup",
            ["AudioMixer"] = "Audio Mixer",
            ["AsrRules"] = "Defender ASR Rules",
            ["Bsod"] = "Black Box & BSOD",
            ["Software"] = "Software & Drivers",
            ["Mcp"] = "AI Agent / MCP Server",
            ["QuickPurge"] = "⚡ Free RAM",
            ["AwakeOn"] = "☕ Awake: ON",
            ["AwakeOff"] = "☕ Awake: Off",
            ["WidgetShow"] = "📌 Floating Widget",
            ["WidgetHide"] = "📌 Hide Widget",
            ["OverlayHud"] = "🎮 Overlay HUD",
            ["Admin"] = "Administrator",
            ["Ready"] = "OmniWin active • Ready"
        }
    };

    public LocalizationService()
    {
        CurrentLanguage = AppSettingsService.Instance.Settings.Language ?? "es";
    }

    public void SetLanguage(string lang)
    {
        if (lang != "es" && lang != "en") lang = "es";
        CurrentLanguage = lang;
        AppSettingsService.Instance.SaveSettings(s => s.Language = lang);
        LanguageChanged?.Invoke(CurrentLanguage);
    }

    public string Get(string key, string fallback = "")
    {
        if (_strings.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(key, out var val))
        {
            return val;
        }
        if (_strings["es"].TryGetValue(key, out var esVal))
        {
            return esVal;
        }
        return fallback;
    }
}

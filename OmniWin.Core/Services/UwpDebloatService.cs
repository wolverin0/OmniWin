using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class UwpAppItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PackagePattern { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconEmoji { get; set; } = "📦";
    public bool IsSafeToRemove { get; set; } = true;
    public bool IsInstalled { get; set; }
    public bool IsSelected { get; set; }
    public string Version { get; set; } = string.Empty;
    public string PackageFullName { get; set; } = string.Empty;

    public string StatusText => IsInstalled ? "Instalada ✔" : "No instalada / Eliminada";
    public string SafeBadgeText => IsSafeToRemove ? "Segura para eliminar" : "Opcional / Cuidado";
}

public class DebloatProgressUpdate
{
    public string PackageName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public double Percent => TotalCount > 0 ? (double)CurrentIndex / TotalCount * 100.0 : 0.0;
}

public class DebloatResult
{
    public bool Success { get; set; }
    public int RemovedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> LogMessages { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class UwpDebloatService
{
    public static List<UwpAppItem> GetDefaultCatalog()
    {
        return new List<UwpAppItem>
        {
            // --- BLOATWARE RECOMENDADO / SEGURO ---
            new UwpAppItem
            {
                Id = "bing_weather",
                DisplayName = "MSN El Tiempo (Bing Weather)",
                PackagePattern = "Microsoft.BingWeather",
                Description = "Aplicación de pronóstico de clima y telemetría de ubicación periódica.",
                Category = "Noticias & Clima",
                IconEmoji = "⛅",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "bing_news",
                DisplayName = "Microsoft Noticias (MSN News)",
                PackagePattern = "Microsoft.BingNews",
                Description = "Lector de noticias con feeds patrocinados y publicidad de MSN.",
                Category = "Noticias & Clima",
                IconEmoji = "📰",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "bing_sports",
                DisplayName = "MSN Deportes (Bing Sports)",
                PackagePattern = "Microsoft.BingSports",
                Description = "Marcadores y noticias deportivas de la red Bing de Microsoft.",
                Category = "Noticias & Clima",
                IconEmoji = "⚽",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "bing_finance",
                DisplayName = "MSN Dinero (Bing Finance)",
                PackagePattern = "Microsoft.BingFinance",
                Description = "Cotizaciones bursátiles y portal financiero con telemetría activa.",
                Category = "Noticias & Clima",
                IconEmoji = "📈",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "solitaire",
                DisplayName = "Colección Solitario (Microsoft Solitaire)",
                PackagePattern = "Microsoft.MicrosoftSolitaireCollection",
                Description = "Juegos de cartas clásicos ahora monetizados con anuncios en video.",
                Category = "Juegos & Ocio",
                IconEmoji = "🃏",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "feedback_hub",
                DisplayName = "Centro de Comentarios (Feedback Hub)",
                PackagePattern = "Microsoft.WindowsFeedbackHub",
                Description = "Herramienta de reporte de errores que recopila telemetría extendida de diagnóstico.",
                Category = "Telemetría & Ayuda",
                IconEmoji = "📣",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "tips",
                DisplayName = "Sugerencias y Consejos (Tips / GetStarted)",
                PackagePattern = "Microsoft.Getstarted",
                Description = "Notificaciones promocionales emergentes sobre características de Windows y Edge.",
                Category = "Telemetría & Ayuda",
                IconEmoji = "💡",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "get_help",
                DisplayName = "Obtener Ayuda (Get Help)",
                PackagePattern = "Microsoft.GetHelp",
                Description = "Asistente web de soporte técnico automatizado de Microsoft.",
                Category = "Telemetría & Ayuda",
                IconEmoji = "❓",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "paint_3d",
                DisplayName = "Paint 3D",
                PackagePattern = "Microsoft.MSPaint",
                Description = "Editor 3D discontinuado de Microsoft preinstalado en muchas compilaciones.",
                Category = "Multimedia & Diseño",
                IconEmoji = "🎨",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "people",
                DisplayName = "Contactos (Microsoft People)",
                PackagePattern = "Microsoft.People",
                Description = "Sincronizador de contactos de Outlook y Skype integrado en Windows.",
                Category = "Social & Contactos",
                IconEmoji = "👥",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "skype",
                DisplayName = "Skype UWP",
                PackagePattern = "Microsoft.SkypeApp",
                Description = "Cliente de mensajería y llamadas VoIP preinstalado.",
                Category = "Comunicaciones",
                IconEmoji = "💬",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "cortana",
                DisplayName = "Cortana",
                PackagePattern = "Microsoft.549981C3F5F10",
                Description = "Asistente de voz discontinuado por Microsoft que mantiene servicios de fondo.",
                Category = "Telemetría & Asistente",
                IconEmoji = "🎙️",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "clipchamp",
                DisplayName = "Clipchamp Video Editor",
                PackagePattern = "Clipchamp.Clipchamp",
                Description = "Editor de video en la nube basado en suscripción preinstalado en Win11.",
                Category = "Multimedia & Video",
                IconEmoji = "🎬",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "office_hub",
                DisplayName = "Microsoft 365 / Office Hub",
                PackagePattern = "Microsoft.MicrosoftOfficeHub",
                Description = "Portal de enlace a compras y documentos web de Office 365.",
                Category = "Productividad",
                IconEmoji = "📑",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "maps",
                DisplayName = "Mapas de Windows",
                PackagePattern = "Microsoft.WindowsMaps",
                Description = "Visor de mapas offline y cálculo de rutas de Bing Maps.",
                Category = "Navegación",
                IconEmoji = "🗺️",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "zune_video",
                DisplayName = "Películas y TV (Zune Video)",
                PackagePattern = "Microsoft.ZuneVideo",
                Description = "Reproductor y tienda de alquiler de video de Microsoft Store.",
                Category = "Multimedia & Video",
                IconEmoji = "🍿",
                IsSafeToRemove = true,
                IsSelected = true
            },
            new UwpAppItem
            {
                Id = "xbox_speech",
                DisplayName = "Xbox Speech-To-Text Overlay",
                PackagePattern = "Microsoft.XboxSpeechToTextOverlay",
                Description = "Módulo de conversión de voz a texto de los servicios de Xbox.",
                Category = "Gaming / Xbox",
                IconEmoji = "🗣️",
                IsSafeToRemove = true,
                IsSelected = true
            },

            // --- APLICACIONES OPCIONALES / NO RECOMENDADAS PARA BORRADO AUTOMÁTICO ---
            new UwpAppItem
            {
                Id = "xbox_app",
                DisplayName = "Consola Xbox / Xbox Companion",
                PackagePattern = "Microsoft.XboxApp",
                Description = "Aplicación principal de Xbox para Game Pass y gestión de biblioteca de juegos.",
                Category = "Gaming / Xbox",
                IconEmoji = "🎮",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "xbox_overlay",
                DisplayName = "Xbox Game Bar",
                PackagePattern = "Microsoft.XboxGamingOverlay",
                Description = "Barra de juegos flotante (Win + G) con widgets de rendimiento y chat.",
                Category = "Gaming / Xbox",
                IconEmoji = "🎯",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "xbox_identity",
                DisplayName = "Xbox Identity Provider",
                PackagePattern = "Microsoft.XboxIdentityProvider",
                Description = "Autenticación de cuenta Microsoft requerida para títulos de Xbox y Microsoft Store.",
                Category = "Gaming / Xbox",
                IconEmoji = "🔑",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "zune_music",
                DisplayName = "Reproductor de Medios (Groove / Zune Music)",
                PackagePattern = "Microsoft.ZuneMusic",
                Description = "Reproductor multimedia de audio y video estándar de Windows 11.",
                Category = "Multimedia & Audio",
                IconEmoji = "🎵",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "phone_link",
                DisplayName = "Enlace Móvil (Phone Link / Your Phone)",
                PackagePattern = "Microsoft.YourPhone",
                Description = "Sincronización de notificaciones, SMS y fotos con dispositivos Android e iOS.",
                Category = "Sincronización",
                IconEmoji = "📱",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "teams_chat",
                DisplayName = "Microsoft Teams (Chat de Barra de Tareas)",
                PackagePattern = "MicrosoftTeams",
                Description = "Cliente personal de Teams integrado en la barra de tareas de Windows 11.",
                Category = "Comunicaciones",
                IconEmoji = "🤝",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "sound_recorder",
                DisplayName = "Grabadora de Voz de Windows",
                PackagePattern = "Microsoft.WindowsSoundRecorder",
                Description = "Utilidad ligera para grabar notas de audio mediante micrófono.",
                Category = "Utilidades",
                IconEmoji = "🎙️",
                IsSafeToRemove = false,
                IsSelected = false
            },
            new UwpAppItem
            {
                Id = "ms_todo",
                DisplayName = "Microsoft To Do",
                PackagePattern = "Microsoft.Todos",
                Description = "Gestor de tareas pendientes y listas sincronizado con cuenta de Microsoft.",
                Category = "Productividad",
                IconEmoji = "✅",
                IsSafeToRemove = false,
                IsSelected = false
            }
        };
    }

    public async Task<List<UwpAppItem>> GetDetectedAppsAsync()
    {
        var catalog = GetDefaultCatalog();

        try
        {
            var installedPackages = await Task.Run(GetInstalledAppxPackagesInternal);

            foreach (var item in catalog)
            {
                var matched = installedPackages.FirstOrDefault(p =>
                    p.Name.IndexOf(item.PackagePattern, StringComparison.OrdinalIgnoreCase) >= 0);

                if (matched != null)
                {
                    item.IsInstalled = true;
                    item.Version = matched.Version;
                    item.PackageFullName = matched.PackageFullName;
                }
                else
                {
                    item.IsInstalled = false;
                    item.IsSelected = false;
                }
            }
        }
        catch
        {
            // Fallback: Si la consulta de PowerShell fallase, marcar como detectadas de forma segura
            foreach (var item in catalog)
            {
                item.IsInstalled = true;
            }
        }

        return catalog;
    }

    public async Task<DebloatResult> UninstallSelectedAppsAsync(
        IEnumerable<UwpAppItem> appsToUninstall,
        IProgress<DebloatProgressUpdate>? progress = null)
    {
        var list = appsToUninstall.ToList();
        var result = new DebloatResult();

        if (list.Count == 0)
        {
            result.Success = true;
            result.Summary = "No se seleccionó ninguna aplicación para desinstalar.";
            return result;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var app = list[i];
            progress?.Report(new DebloatProgressUpdate
            {
                PackageName = app.DisplayName,
                Message = $"Desinstalando '{app.DisplayName}' ({app.PackagePattern})...",
                CurrentIndex = i + 1,
                TotalCount = list.Count
            });

            bool success = await Task.Run(() => RemoveAppxPackage(app.PackagePattern));
            if (success)
            {
                result.RemovedCount++;
                result.LogMessages.Add($"✔ Desinstalado con éxito: {app.DisplayName}");
                app.IsInstalled = false;
                app.IsSelected = false;
            }
            else
            {
                result.FailedCount++;
                result.LogMessages.Add($"⚠ No se pudo desinstalar completamente: {app.DisplayName}");
            }
        }

        result.Success = result.FailedCount == 0;
        result.Summary = $"Proceso finalizado: {result.RemovedCount} paquete(s) eliminados, {result.FailedCount} error(es).";
        return result;
    }

    private static bool RemoveAppxPackage(string packagePattern)
    {
        try
        {
            // Elimina para el usuario actual, para todos los usuarios y desaprovisiona de la imagen de Windows
            string psScript = $"Get-AppxPackage -Name '*{packagePattern}*' -AllUsers | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue; " +
                              $"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*{packagePattern}*' | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(30000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private class RawAppxPackage
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string PackageFullName { get; set; } = string.Empty;
    }

    private static List<RawAppxPackage> GetInstalledAppxPackagesInternal()
    {
        var result = new List<RawAppxPackage>();

        try
        {
            string psCommand = "Get-AppxPackage | Select-Object Name, Version, PackageFullName | ConvertTo-Json -Compress";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc == null) return result;

            string json = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15000);

            if (string.IsNullOrWhiteSpace(json)) return result;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    result.Add(new RawAppxPackage
                    {
                        Name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                        Version = el.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "",
                        PackageFullName = el.TryGetProperty("PackageFullName", out var f) ? f.GetString() ?? "" : ""
                    });
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                result.Add(new RawAppxPackage
                {
                    Name = doc.RootElement.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                    Version = doc.RootElement.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "",
                    PackageFullName = doc.RootElement.TryGetProperty("PackageFullName", out var f) ? f.GetString() ?? "" : ""
                });
            }
        }
        catch
        {
            // Ignorar y retornar lista vacía o parcial
        }

        return result;
    }
}

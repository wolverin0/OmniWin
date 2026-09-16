using System;

namespace OmniWin.UI.Services;

public record HubSubItem(int TabIndex, string Label, string Icon);

public record HubDefinition(int HubIndex, string Title, string Icon, HubSubItem[] SubItems);

public static class HubNavigationRegistry
{
    public static readonly HubDefinition[] Hubs = new[]
    {
        // Hub 0: Visión General (4 herramientas de supervisión)
        new HubDefinition(0, "Visión General", "📊", new[]
        {
            new HubSubItem(0, "Dashboard", "📊"),
            new HubSubItem(1, "Telemetría Live", "📈"),
            new HubSubItem(2, "Forense Procesos", "🔍"),
            new HubSubItem(18, "OmniCompanion", "📱")
        }),

        // Hub 1: Rendimiento & Gaming (4 herramientas de optimización activa)
        new HubDefinition(1, "Rendimiento & Gaming", "⚡", new[]
        {
            new HubSubItem(3, "Memoria RAM", "⚡"),
            new HubSubItem(19, "Game Profiler", "🎮"),
            new HubSubItem(15, "Energía & CPU", "🔋"),
            new HubSubItem(13, "Mezclador Audio", "🎧")
        }),

        // Hub 2: Almacenamiento & Archivos (7 herramientas de disco y rescate)
        new HubDefinition(2, "Almacenamiento", "💾", new[]
        {
            new HubSubItem(16, "Espacio en Disco", "💾"),
            new HubSubItem(23, "Deduplicador Zero-Copy", "🗂️"),
            new HubSubItem(26, "Migrador Apps", "🚀"),
            new HubSubItem(4, "Limpieza Disco", "🧹"),
            new HubSubItem(24, "Expulsor USB", "🔌"),
            new HubSubItem(22, "Recuperación", "♻️"),
            new HubSubItem(10, "Desbloqueo", "🔒")
        }),

        // Hub 3: Red & Conectividad (4 herramientas de red y tráfico)
        new HubDefinition(3, "Red & Conectividad", "🌐", new[]
        {
            new HubSubItem(9, "Diagnóstico Red", "🌐"),
            new HubSubItem(20, "Cortafuegos Visual", "🛡️"),
            new HubSubItem(21, "DNS Seguro", "🔒"),
            new HubSubItem(27, "Red QoS & Puertos", "🚦")
        }),

        // Hub 4: Seguridad & Privacidad (3 herramientas de hardening)
        new HubDefinition(4, "Seguridad & Privacidad", "🛡️", new[]
        {
            new HubSubItem(25, "Privacidad Anti-Espía", "🛡️"),
            new HubSubItem(14, "Defender ASR", "🛡️"),
            new HubSubItem(17, "Caja Negra BSOD", "✈️")
        }),

        // Hub 5: Sistema & Herramientas (7 herramientas de mantenimiento y tweaks)
        new HubDefinition(5, "Sistema & Herramientas", "⚙️", new[]
        {
            new HubSubItem(5, "Tweaks & Debloat", "🛠️"),
            new HubSubItem(6, "Software & Drivers", "📦"),
            new HubSubItem(11, "Inicio de Windows", "🚀"),
            new HubSubItem(8, "Consola Reparación", "🩹"),
            new HubSubItem(7, "Mantenimiento", "⚙️"),
            new HubSubItem(28, "Fondos 4K", "📸"),
            new HubSubItem(12, "Servidor MCP", "🤖")
        })
    };

    public static (HubDefinition Hub, HubSubItem SubItem)? FindByTabIndex(int tabIndex)
    {
        foreach (var hub in Hubs)
        {
            foreach (var sub in hub.SubItems)
            {
                if (sub.TabIndex == tabIndex)
                {
                    return (hub, sub);
                }
            }
        }
        return null;
    }
}

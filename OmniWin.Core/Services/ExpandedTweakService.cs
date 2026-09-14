using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class ExpandedTweakItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = "Medio"; // Bajo, Medio, Alto
    public bool RequiresAdmin { get; set; } = true;
    public bool IsRecommendedForGaming { get; set; }
    public bool IsApplied { get; set; }
}

public class TweakExecutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TweakId { get; set; } = string.Empty;
}

public class ExpandedTweakService
{
    private const string HostsBeginMarker = "# BEGIN OMNIWIN TELEMETRY BLOCK";
    private const string HostsEndMarker = "# END OMNIWIN TELEMETRY BLOCK";

    private static readonly string[] TelemetryDomains = new[]
    {
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "telemetry.microsoft.com",
        "telemetry.appex.bing.net",
        "watson.telemetry.microsoft.com",
        "oca.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com",
        "diagtrack-gw.cloudapp.net",
        "activity.windows.com",
        "settings-win.data.microsoft.com",
        "feedback.windows.com",
        "choice.microsoft.com",
        "df.telemetry.microsoft.com",
        "diagnostics.support.microsoft.com"
    };

    public List<ExpandedTweakItem> GetCategorizedTweaks()
    {
        var tweaks = new List<ExpandedTweakItem>
        {
            // ==========================================
            // CATEGORÍA 1: GAMING & LATENCIA (14 Tweaks)
            // ==========================================
            new ExpandedTweakItem
            {
                Id = "gaming_win32_priority",
                Name = "Win32PrioritySeparation (Prioridad a Ventana Activa)",
                Category = "Gaming & Latencia",
                Description = "Ajusta la política de planificación del kernel (0x26/38) para dar máxima prioridad de CPU a la ventana en primer plano sin micro-stuttering.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckWin32Priority()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_nagle_algorithm",
                Name = "Desactivar Algoritmo de Nagle (TcpAckFrequency & TCPNoDelay)",
                Category = "Gaming & Latencia",
                Description = "Elimina la retención artificial de paquetes de red pequeños en TCP. Reduce drásticamente el ping y jitter en juegos online competitivos.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckNagleDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_hags",
                Name = "Programación de GPU Acelerada por Hardware (HAGS)",
                Category = "Gaming & Latencia",
                Description = "Permite que la GPU gestione directamente su propia VRAM y scheduling sin sobrecargar la CPU, mejorando FPS y fluidez.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckHagsEnabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_network_throttling",
                Name = "Desactivar NetworkThrottlingIndex",
                Category = "Gaming & Latencia",
                Description = "Deshabilita el limitador de paquetes de red multimedia que Windows impone cuando hay audio o video en reproducción.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckNetworkThrottlingDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_system_responsiveness",
                Name = "SystemResponsiveness a 0%",
                Category = "Gaming & Latencia",
                Description = "Elimina la reserva del 20% de recursos del sistema para tareas en segundo plano en Multimedia Profile, dedicando el 100% al juego.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckSystemResponsivenessZero()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_game_dvr",
                Name = "Desactivar GameDVR y Grabación en Fondo",
                Category = "Gaming & Latencia",
                Description = "Desactiva la captura en segundo plano de Windows Game Bar que consume ancho de banda de GPU y genera caídas de fotogramas.",
                Impact = "Alto",
                RequiresAdmin = false,
                IsRecommendedForGaming = true,
                IsApplied = CheckGameDvrDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_gpu_priority_games",
                Name = "Prioridad de GPU Máxima en Perfil 'Games'",
                Category = "Gaming & Latencia",
                Description = "Asigna GPU Priority = 8 y Scheduling High en Multimedia\\SystemProfile\\Tasks\\Games para asegurar preferencia en el pipeline de renderizado.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckGpuPriorityGames()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_game_bar_presence",
                Name = "Desactivar GameBarPresenceWriter",
                Category = "Gaming & Latencia",
                Description = "Evita la inyección continua de procesos de telemetría de Xbox Game Bar al detectar el inicio de un ejecutable de juego.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsRecommendedForGaming = true,
                IsApplied = CheckGameBarPresenceDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_mouse_accel",
                Name = "Desactivar Aceleración de Ratón (Raw Input 1:1)",
                Category = "Gaming & Latencia",
                Description = "Configura la curva del puntero para respuesta lineal 1:1, indispensable para memoria muscular y precisión en juegos FPS.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsRecommendedForGaming = true,
                IsApplied = CheckMouseAccelDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_hpet_disable",
                Name = "Desactivar HPET (High Precision Event Timer en BCD)",
                Category = "Gaming & Latencia",
                Description = "Fuerza el uso de TSC nativo de la CPU (useplatformclock false) reduciendo latencia de interrupción de hardware DPC.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckHpetDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_qos_bandwidth",
                Name = "Desactivar Límite de Ancho de Banda QoS (100% de Red)",
                Category = "Gaming & Latencia",
                Description = "Establece NonBestEffortLimit = 0 en el programador de paquetes QoS para que Windows no reserve hasta un 20% para el sistema.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckQosDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_power_throttling",
                Name = "Desactivar Power Throttling en Procesos",
                Category = "Gaming & Latencia",
                Description = "Impide que el subsistema de energía de Windows degrade la frecuencia de núcleos de CPU durante sesiones intensas.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckPowerThrottlingDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_fullscreen_optimizations",
                Name = "Desactivar Optimizaciones de Pantalla Completa Globales",
                Category = "Gaming & Latencia",
                Description = "Permite modo exclusivo real sin la capa de composición intermedia de DWM, reduciendo el retardo de presentación de cuadros.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsRecommendedForGaming = true,
                IsApplied = CheckFseDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "gaming_visual_effects_perf",
                Name = "Efectos Visuales Optimizados para Rendimiento",
                Category = "Gaming & Latencia",
                Description = "Deshabilita animaciones costosas del escritorio DWM (VisualFXSetting = 2) para liberar ciclos de GPU/CPU.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsRecommendedForGaming = false,
                IsApplied = CheckVisualFxPerf()
            },
            new ExpandedTweakItem
            {
                Id = "net_max_connections",
                Name = "Aumentar Conexiones Concurrentes por Servidor (16 sockets)",
                Category = "Gaming & Latencia",
                Description = "Eleva de 6 a 16 el límite de conexiones HTTP/HTTPS simultáneas por servidor, acelerando descargas paralelas y APIs.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsRecommendedForGaming = true,
                IsApplied = CheckMaxConnections()
            },
            new ExpandedTweakItem
            {
                Id = "net_dns_cache_ttl",
                Name = "Optimizar Caché de Resolución DNS (MaxCacheTtl 86400s)",
                Category = "Gaming & Latencia",
                Description = "Mantiene la caché DNS positiva por hasta 24 horas y purga errores en 5 segundos, reduciendo latencia de resolución en juegos y navegación.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsRecommendedForGaming = true,
                IsApplied = CheckDnsCacheTtl()
            },

            // ==========================================
            // CATEGORÍA 2: PRIVACIDAD RADICAL (14 Tweaks)
            // ==========================================
            new ExpandedTweakItem
            {
                Id = "privacy_hosts_telemetry",
                Name = "Bloqueador de Telemetría en Archivo HOSTS",
                Category = "Privacidad Radical",
                Description = "Redirige a 0.0.0.0 a nivel de socket los dominios de rastreo de Microsoft (vortex, telemetry, activity, diagtrack-gw, watson).",
                Impact = "Alto",
                RequiresAdmin = true,
                IsApplied = CheckHostsBlocked()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_diagtrack",
                Name = "Desactivar Servicio DiagTrack (Experiencias del Usuario)",
                Category = "Privacidad Radical",
                Description = "Detiene y deshabilita permanentemente el servicio DiagTrack de recopilación y transmisión periódica de diagnósticos.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsApplied = CheckDiagTrackDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_dmwappushservice",
                Name = "Desactivar Servicio dmwappushservice",
                Category = "Privacidad Radical",
                Description = "Deshabilita el servicio de enrutamiento de telemetría WAP Push utilizado para canalizar telemetría hacia los servidores centrales.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckDmwpDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_smartscreen",
                Name = "Desactivar SmartScreen para Aplicaciones",
                Category = "Privacidad Radical",
                Description = "Evita que Windows envíe un hash y telemetría de cada ejecutable descargado a los servidores de Microsoft antes de iniciarlo.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckSmartScreenDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_advertising_id",
                Name = "Desactivar ID de Publicidad de Microsoft",
                Category = "Privacidad Radical",
                Description = "Impide que aplicaciones de la tienda y Windows construyan un perfil publicitario basado en tus patrones de uso.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckAdvertisingIdDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_start_suggestions",
                Name = "Desactivar Sugerencias y Anuncios en Menú Inicio",
                Category = "Privacidad Radical",
                Description = "Deshabilita la descarga en segundo plano y sugerencias de apps patrocinadas (SilentInstalledApps y SystemPaneSuggestions).",
                Impact = "Medio",
                RequiresAdmin = false,
                IsApplied = CheckStartSuggestionsDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_cortana_telemetry",
                Name = "Desactivar Cortana y Búsqueda Web de Bing",
                Category = "Privacidad Radical",
                Description = "Apaga la indexación en la nube de Cortana y desvincula las consultas de búsqueda local del buscador de Bing.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckCortanaDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_activity_history",
                Name = "Desactivar Historial de Actividad (Timeline en la Nube)",
                Category = "Privacidad Radical",
                Description = "Detiene la recopilación de sitios visitados, documentos y aplicaciones para la sincronización entre dispositivos.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckActivityHistoryDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_ceip",
                Name = "Desactivar Programa de Mejora (CEIP / SQM)",
                Category = "Privacidad Radical",
                Description = "Deshabilita los clientes SQM (Software Quality Metrics) que envían registros de fiabilidad a Microsoft.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckCeipDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_location_access",
                Name = "Desactivar Sensores y Seguimiento de Ubicación",
                Category = "Privacidad Radical",
                Description = "Deshabilita a nivel de política el acceso a geolocalización global para el sistema y aplicaciones en segundo plano.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckLocationDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_typing_personalization",
                Name = "Desactivar Personalización de Entrada y Mecanografía",
                Category = "Privacidad Radical",
                Description = "Previene el keylogger de telemetría que envía pulsaciones de teclas y patrones manuscritos a la nube.",
                Impact = "Alto",
                RequiresAdmin = false,
                IsApplied = CheckTypingPersonalizationDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_feedback_frequency",
                Name = "Desactivar Notificaciones y Frecuencia de Feedback",
                Category = "Privacidad Radical",
                Description = "Configura la frecuencia de recolección de comentarios a 'Nunca' y silencia las encuestas emergentes.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckFeedbackDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_tailored_experiences",
                Name = "Desactivar Experiencias Personalizadas con Diagnósticos",
                Category = "Privacidad Radical",
                Description = "Bloquea el uso de telemetría de fallos para mostrar recomendaciones de productos dentro del sistema operativo.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckTailoredExpDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "privacy_wifi_sense",
                Name = "Desactivar Compartir Redes Wi-Fi (Wi-Fi Sense)",
                Category = "Privacidad Radical",
                Description = "Deshabilita la conexión e informe automático de hotspots y el intercambio de contraseñas de red con contactos.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckWifiSenseDisabled()
            },

            // ==========================================
            // CATEGORÍA 3: WINDOWS 11 UI & EXPLORER (14 Tweaks)
            // ==========================================
            new ExpandedTweakItem
            {
                Id = "win11_classic_context_menu",
                Name = "Menú Contextual Clásico de Windows 10 en Win11",
                Category = "Windows 11 UI & Explorer",
                Description = "Restaura el menú contextual clásico completo sin tener que pulsar 'Mostrar más opciones'.",
                Impact = "Alto",
                RequiresAdmin = false,
                IsApplied = CheckClassicContextMenu()
            },
            new ExpandedTweakItem
            {
                Id = "win11_show_file_extensions",
                Name = "Mostrar Extensiones de Archivo Siempre",
                Category = "Windows 11 UI & Explorer",
                Description = "Muestra extensiones (.exe, .bat, .zip) en el Explorador, evitando engaños con ejecutables camuflados.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsApplied = CheckShowExtensions()
            },
            new ExpandedTweakItem
            {
                Id = "win11_show_hidden_files",
                Name = "Mostrar Archivos y Carpetas Ocultos",
                Category = "Windows 11 UI & Explorer",
                Description = "Hace visibles carpetas ocultas del sistema como AppData y ProgramData para facilitar administración técnica.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsApplied = CheckShowHidden()
            },
            new ExpandedTweakItem
            {
                Id = "win11_hide_taskbar_widgets",
                Name = "Ocultar Widgets / Noticias en Barra de Tareas",
                Category = "Windows 11 UI & Explorer",
                Description = "Oculta el icono de Noticias y Clima que consume procesos de WebView2 en segundo plano de Windows 11.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckHideWidgets()
            },
            new ExpandedTweakItem
            {
                Id = "win11_hide_copilot",
                Name = "Ocultar Botón de Copilot en Barra de Tareas",
                Category = "Windows 11 UI & Explorer",
                Description = "Elimina el icono de acceso directo a Microsoft Copilot en la barra de tareas de Windows 11.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckHideCopilot()
            },
            new ExpandedTweakItem
            {
                Id = "win11_hide_task_view",
                Name = "Ocultar Botón de Vista de Tareas",
                Category = "Windows 11 UI & Explorer",
                Description = "Limpia la barra de tareas ocultando el botón de escritorios virtuales si no se utiliza habitualmente.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckHideTaskView()
            },
            new ExpandedTweakItem
            {
                Id = "win11_align_taskbar_left",
                Name = "Alinear Barra de Tareas a la Izquierda",
                Category = "Windows 11 UI & Explorer",
                Description = "Devuelve el botón de Inicio y las aplicaciones a la esquina inferior izquierda al estilo clásico de Windows.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsApplied = CheckAlignTaskbarLeft()
            },
            new ExpandedTweakItem
            {
                Id = "win11_launch_this_pc",
                Name = "Abrir 'Este Equipo' por Defecto en el Explorador",
                Category = "Windows 11 UI & Explorer",
                Description = "Abre directamente tus discos duros y unidades en lugar de la vista lenta de 'Inicio / Acceso Rápido'.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckLaunchThisPc()
            },
            new ExpandedTweakItem
            {
                Id = "win11_compact_view_explorer",
                Name = "Activar Vista Compacta en el Explorador",
                Category = "Windows 11 UI & Explorer",
                Description = "Reduce el espacio excesivo entre filas y elementos en el Explorador de Windows 11 para ver más archivos.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckCompactExplorer()
            },
            new ExpandedTweakItem
            {
                Id = "win11_disable_lockscreen",
                Name = "Desactivar Pantalla de Bloqueo Previa",
                Category = "Windows 11 UI & Explorer",
                Description = "Salta la imagen de bloqueo inicial al encender el PC, yendo directamente a la casilla de contraseña o PIN.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckDisableLockScreen()
            },
            new ExpandedTweakItem
            {
                Id = "win11_disable_finish_setup",
                Name = "Desactivar Pantalla 'Terminar de Configurar Windows'",
                Category = "Windows 11 UI & Explorer",
                Description = "Bloquea los avisos a pantalla completa tras actualizaciones que intentan forzar Edge, OneDrive y suscripciones.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckDisableFinishSetup()
            },
            new ExpandedTweakItem
            {
                Id = "win11_disable_snap_assist_flyout",
                Name = "Desactivar Sugerencias Automáticas de Snap Assist",
                Category = "Windows 11 UI & Explorer",
                Description = "Deshabilita la ventana emergente con miniaturas de otras apps al anclar una ventana al lateral de la pantalla.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckDisableSnapAssist()
            },
            new ExpandedTweakItem
            {
                Id = "win11_show_seconds_taskbar",
                Name = "Mostrar Segundos en Reloj de Barra de Tareas",
                Category = "Windows 11 UI & Explorer",
                Description = "Muestra los segundos en tiempo real en la bandeja del sistema de Windows 11 (HH:MM:SS).",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckShowSecondsTaskbar()
            },
            new ExpandedTweakItem
            {
                Id = "win11_disable_bing_search",
                Name = "Desactivar Sugerencias de Búsqueda Web de Bing",
                Category = "Windows 11 UI & Explorer",
                Description = "Mantiene las búsquedas del Menú Inicio 100% locales, eliminando resultados web publicitarios.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsApplied = CheckBingSearchDisabled()
            },

            // ==========================================
            // CATEGORÍA 4: RENDIMIENTO DE SISTEMA (13 Tweaks)
            // ==========================================
            new ExpandedTweakItem
            {
                Id = "sys_disable_hibernation",
                Name = "Desactivar Hibernación (Liberar hiberfil.sys)",
                Category = "Rendimiento de Sistema",
                Description = "Ejecuta 'powercfg -h off' eliminando el archivo hiberfil.sys y liberando entre 8 y 64 GB de almacenamiento SSD.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsApplied = CheckHibernationDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "sys_waittokill_service",
                Name = "Acelerar Apagado de Servicios Colgados (2000ms)",
                Category = "Rendimiento de Sistema",
                Description = "Reduce el tiempo de espera WaitToKillServiceTimeout de 5000ms a 2000ms para apagar o reiniciar el sistema al instante.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckWaitToKillService()
            },
            new ExpandedTweakItem
            {
                Id = "sys_hung_app_timeout",
                Name = "Cierre Rápido de Aplicaciones Bloqueadas al Apagar",
                Category = "Rendimiento de Sistema",
                Description = "Ajusta HungAppTimeout a 1000ms y AutoEndTasks a 1 para no trabar el apagado por programas que no responden.",
                Impact = "Medio",
                RequiresAdmin = false,
                IsApplied = CheckHungAppTimeout()
            },
            new ExpandedTweakItem
            {
                Id = "sys_ntfs_disable_8dot3",
                Name = "Optimizar NTFS (Desactivar Nombres Cortos DOS 8.3)",
                Category = "Rendimiento de Sistema",
                Description = "Deshabilita la generación obsoleta de nombres 8.3 en discos NTFS (NtfsDisable8dot3NameCreation), acelerando accesos masivos.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsApplied = CheckNtfsDisable8dot3()
            },
            new ExpandedTweakItem
            {
                Id = "sys_ntfs_disable_last_access",
                Name = "Desactivar Actualización de Último Acceso en NTFS",
                Category = "Rendimiento de Sistema",
                Description = "Evita que Windows escriba metadatos de última lectura en cada archivo visitado, reduciendo desgaste en unidades SSD.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckNtfsDisableLastAccess()
            },
            new ExpandedTweakItem
            {
                Id = "sys_menu_show_delay",
                Name = "Acelerar Apertura de Menús (MenuShowDelay 10ms)",
                Category = "Rendimiento de Sistema",
                Description = "Reduce la demora predeterminada de 400ms a 10ms, haciendo que los submenús y menús contextuales respondan de forma instantánea.",
                Impact = "Bajo",
                RequiresAdmin = false,
                IsApplied = CheckMenuShowDelay()
            },
            new ExpandedTweakItem
            {
                Id = "sys_disable_autoreboot_bsod",
                Name = "Desactivar Reinicio Automático tras Pantalla Azul (BSOD)",
                Category = "Rendimiento de Sistema",
                Description = "Impide que el equipo se reinicie de inmediato al fallar, permitiendo leer el código STOP y guardar el volcado de memoria.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckAutoRebootDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "sys_disable_wer",
                Name = "Desactivar Windows Error Reporting (WER)",
                Category = "Rendimiento de Sistema",
                Description = "Detiene la congelación de programas cuando se cuelgan mientras Windows intenta recopilar y subir volcados de fallo.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckWerDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "sys_clear_pagefile_shutdown",
                Name = "Desactivar Borrado de Paginación en Apagado",
                Category = "Rendimiento de Sistema",
                Description = "Evita que Windows sobrescriba todo el pagefile.sys al cerrar sesión, recortando de 10 a 30 segundos del tiempo de apagado.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckClearPageFileDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "sys_large_system_cache",
                Name = "Activar LargeSystemCache para Caché en RAM",
                Category = "Rendimiento de Sistema",
                Description = "Permite que el sistema de archivos aproveche la memoria RAM disponible para caché de operaciones pesadas de disco.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckLargeSystemCache()
            },
            new ExpandedTweakItem
            {
                Id = "sys_svchost_split",
                Name = "Aislamiento de Procesos Svchost (3.5 GB Threshold)",
                Category = "Rendimiento de Sistema",
                Description = "Configura SvcHostSplitThresholdInKB para evitar que el fallo de un servicio individual arrastre a otros servicios del sistema.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckSvcHostSplit()
            },
            new ExpandedTweakItem
            {
                Id = "sys_disable_remote_assistance",
                Name = "Desactivar Asistencia Remota de Windows",
                Category = "Rendimiento de Sistema",
                Description = "Cierra los puertos y solicitudes de asistencia remota no solicitada, mejorando la superficie de ataque y liberando recursos.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckRemoteAssistanceDisabled()
            },
            new ExpandedTweakItem
            {
                Id = "sys_disk_io_priority",
                Name = "Priorizar E/S de Disco para Aplicaciones Activas",
                Category = "Rendimiento de Sistema",
                Description = "Asigna IoPriorityOverride para asegurar que la lectura de disco de tu aplicación en uso tenga preferencia sobre tareas de fondo.",
                Impact = "Medio",
                RequiresAdmin = true,
                IsApplied = CheckIoPriorityOverride()
            },
            new ExpandedTweakItem
            {
                Id = "sys_startup_delay",
                Name = "Eliminar Retardo de Inicio de Aplicaciones (StartupDelay = 0)",
                Category = "Rendimiento de Sistema",
                Description = "Elimina la demora artificial de 10 segundos tras iniciar sesión, cargando las herramientas de arranque inmediatamente.",
                Impact = "Alto",
                RequiresAdmin = false,
                IsApplied = CheckStartupDelay()
            },
            new ExpandedTweakItem
            {
                Id = "sys_ntfs_memory_usage",
                Name = "Aumentar Búfer de Paginado NTFS (NtfsMemoryUsage = 2)",
                Category = "Rendimiento de Sistema",
                Description = "Incrementa la reserva de memoria RAM para MFT y lectura de metadatos de archivos, optimizando accesos masivos en SSD/NVMe.",
                Impact = "Alto",
                RequiresAdmin = true,
                IsApplied = CheckNtfsMemoryUsage()
            },
            new ExpandedTweakItem
            {
                Id = "sys_autochk_timeout",
                Name = "Acelerar Cuenta Atrás de AutoChk en Arranque (2 segundos)",
                Category = "Rendimiento de Sistema",
                Description = "Reduce la pausa de comprobación de disco chkdsk en el arranque de 8 a 2 segundos.",
                Impact = "Bajo",
                RequiresAdmin = true,
                IsApplied = CheckAutoChkTimeOut()
            }
        };

        return tweaks;
    }

    public bool IsTweakApplied(string id)
    {
        var item = GetCategorizedTweaks().FirstOrDefault(t => t.Id == id);
        return item?.IsApplied ?? false;
    }

    public TweakExecutionResult ApplyTweak(string id)
    {
        try
        {
            TransactionService.Instance.BeginTransaction(id);
            switch (id)
            {
                // --- GAMING & LATENCIA ---
                case "gaming_win32_priority":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26);
                    return Ok(id, "Win32PrioritySeparation ajustado a 0x26 (38 decimal). Prioridad máxima a la ventana activa.");

                case "gaming_nagle_algorithm":
                    ApplyNagle(disable: true);
                    return Ok(id, "Algoritmo de Nagle desactivado en interfaces de red activas (TcpAckFrequency=1, TCPNoDelay=1).");

                case "gaming_hags":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2);
                    return Ok(id, "HAGS (Hardware Accelerated GPU Scheduling) habilitado. Requiere reinicio para entrar en efecto.");

                case "gaming_network_throttling":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
                    return Ok(id, "NetworkThrottlingIndex desactivado (sin estrangulamiento de paquetes de red).");

                case "gaming_system_responsiveness":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0);
                    return Ok(id, "SystemResponsiveness configurado a 0 (100% de CPU para juegos/multimedia).");

                case "gaming_game_dvr":
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                    return Ok(id, "GameDVR y captura en fondo desactivados.");

                case "gaming_gpu_priority_games":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true))
                    {
                        key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                        key.SetValue("Priority", 6, RegistryValueKind.DWord);
                        key.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                        key.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                    }
                    return Ok(id, "Prioridad de GPU y programación en perfil 'Games' elevadas al máximo.");

                case "gaming_game_bar_presence":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0);
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2);
                    return Ok(id, "GameBarPresenceWriter y comportamientos invasivos de Game Bar desactivados.");

                case "gaming_mouse_accel":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse", true))
                    {
                        key.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                        key.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                        key.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                    }
                    return Ok(id, "Aceleración de ratón desactivada (respuesta lineal 1:1).");

                case "gaming_hpet_disable":
                    RunCmd("bcdedit /set useplatformclock false");
                    RunCmd("bcdedit /set disabledynamictick yes");
                    return Ok(id, "HPET desactivado y dynamic tick suprimido en el arranque BCD.");

                case "gaming_qos_bandwidth":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0);
                    return Ok(id, "Reserva de ancho de banda QoS desactivada (100% de red disponible).");

                case "gaming_power_throttling":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1);
                    return Ok(id, "Power Throttling desactivado en los estados de energía.");

                case "gaming_fullscreen_optimizations":
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehavior", 2);
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
                    return Ok(id, "Optimizaciones de pantalla completa de DWM desactivadas.");

                case "gaming_visual_effects_perf":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
                    return Ok(id, "Efectos visuales configurados en modo Rendimiento.");

                case "net_max_connections":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "MaxConnectionsPerServer", 16);
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "MaxConnectionsPer1_0Server", 16);
                    return Ok(id, "Límite de conexiones simultáneas por servidor aumentado a 16.");

                case "net_dns_cache_ttl":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxCacheTtl", 86400);
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl", 5);
                    return Ok(id, "Caché DNS optimizada (MaxCacheTtl = 86400s, MaxNegativeCacheTtl = 5s).");

                // --- PRIVACIDAD RADICAL ---
                case "privacy_hosts_telemetry":
                    ApplyHostsBlock(block: true);
                    return Ok(id, "Dominios de telemetría de Microsoft bloqueados en C:\\Windows\\System32\\drivers\\etc\\hosts.");

                case "privacy_diagtrack":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4);
                    RunCmd("sc stop DiagTrack");
                    return Ok(id, "Servicio DiagTrack detenido y configurado como Deshabilitado.");

                case "privacy_dmwappushservice":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", 4);
                    RunCmd("sc stop dmwappushservice");
                    return Ok(id, "Servicio dmwappushservice detenido y configurado como Deshabilitado.");

                case "privacy_smartscreen":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0);
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", 0);
                    return Ok(id, "SmartScreen desactivado para aplicaciones y web.");

                case "privacy_advertising_id":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
                    return Ok(id, "ID de publicidad de Microsoft desactivado.");

                case "privacy_start_suggestions":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", true))
                    {
                        key.SetValue("SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                        key.SetValue("SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                        key.SetValue("SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord);
                        key.SetValue("SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                    }
                    return Ok(id, "Sugerencias y aplicaciones patrocinadas en Menú Inicio desactivadas.");

                case "privacy_cortana_telemetry":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", true))
                    {
                        key.SetValue("AllowCortana", 0, RegistryValueKind.DWord);
                        key.SetValue("ConnectedSearchUseWeb", 0, RegistryValueKind.DWord);
                        key.SetValue("DisableWebSearch", 1, RegistryValueKind.DWord);
                    }
                    return Ok(id, "Cortana y búsquedas web en nube de Windows Search desactivadas.");

                case "privacy_activity_history":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System", true))
                    {
                        key.SetValue("PublishUserActivities", 0, RegistryValueKind.DWord);
                        key.SetValue("UploadUserActivities", 0, RegistryValueKind.DWord);
                    }
                    return Ok(id, "Historial de actividad y sincronización con nube Timeline desactivados.");

                case "privacy_ceip":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0);
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\SQMClient\ReliabilityAnalysis", "CEIPEnable", 0);
                    return Ok(id, "Programa de Mejora de Experiencia de Usuario (CEIP/SQM) desactivado.");

                case "privacy_location_access":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1);
                    return Ok(id, "Servicios y sensores de ubicación global desactivados.");

                case "privacy_typing_personalization":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\InputPersonalization", true))
                    {
                        key.SetValue("RestrictImplicitInkCollection", 1, RegistryValueKind.DWord);
                        key.SetValue("RestrictImplicitTextCollection", 1, RegistryValueKind.DWord);
                    }
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0);
                    return Ok(id, "Personalización de entrada de texto y mecanografía desactivada.");

                case "privacy_feedback_frequency":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Siuf\Rules", true))
                    {
                        key.SetValue("NumberOfSIFUrlSegments", 0, RegistryValueKind.DWord);
                        key.SetValue("PeriodInNanoSeconds", 0, RegistryValueKind.DWord);
                    }
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1);
                    return Ok(id, "Encuestas y recolección de feedback desactivadas.");

                case "privacy_tailored_experiences":
                    SetRegDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1);
                    return Ok(id, "Experiencias personalizadas con datos de diagnóstico desactivadas.");

                case "privacy_wifi_sense":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowWiFiHotSpotReporting", "value", 0);
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowAutoConnectToWiFiSenseHotspots", "value", 0);
                    return Ok(id, "Wi-Fi Sense y reporte de hotspots desactivados.");

                // --- WINDOWS 11 UI & EXPLORER ---
                case "win11_classic_context_menu":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", true))
                    {
                        key.SetValue("", "");
                    }
                    return Ok(id, "Menú contextual clásico de Windows 10 habilitado. Reinicia explorer.exe para verlo de inmediato.");

                case "win11_show_file_extensions":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0);
                    return Ok(id, "Extensiones de archivo visibles en el Explorador.");

                case "win11_show_hidden_files":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1);
                    return Ok(id, "Archivos y carpetas ocultos visibles.");

                case "win11_hide_taskbar_widgets":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0);
                    return Ok(id, "Icono de Widgets / Noticias oculto en la barra de tareas.");

                case "win11_hide_copilot":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0);
                    return Ok(id, "Botón de Copilot oculto en la barra de tareas.");

                case "win11_hide_task_view":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 0);
                    return Ok(id, "Botón de Vista de Tareas oculto.");

                case "win11_align_taskbar_left":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 0);
                    return Ok(id, "Barra de tareas alineada a la izquierda (estilo clásico).");

                case "win11_launch_this_pc":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1);
                    return Ok(id, "Explorador de archivos configurado para abrir 'Este Equipo'.");

                case "win11_compact_view_explorer":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "UseCompactMode", 1);
                    return Ok(id, "Modo compacto activado en el Explorador de Windows 11.");

                case "win11_disable_lockscreen":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1);
                    return Ok(id, "Pantalla de bloqueo previa al login desactivada.");

                case "win11_disable_finish_setup":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0);
                    return Ok(id, "Avisos de 'Terminar de configurar el equipo' desactivados.");

                case "win11_disable_snap_assist_flyout":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SnapAssist", 0);
                    return Ok(id, "Sugerencias emergentes de Snap Assist desactivadas.");

                case "win11_show_seconds_taskbar":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", 1);
                    return Ok(id, "Segundos habilitados en el reloj de la barra de tareas.");

                case "win11_disable_bing_search":
                    SetRegDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0);
                    return Ok(id, "Sugerencias de Bing en búsqueda del Menú Inicio desactivadas.");

                // --- RENDIMIENTO DE SISTEMA ---
                case "sys_disable_hibernation":
                    RunCmd("powercfg.exe -h off");
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 0);
                    return Ok(id, "Hibernación desactivada. Archivo hiberfil.sys eliminado para liberar almacenamiento.");

                case "sys_waittokill_service":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control", true))
                    {
                        key.SetValue("WaitToKillServiceTimeout", "2000", RegistryValueKind.String);
                    }
                    return Ok(id, "WaitToKillServiceTimeout ajustado a 2000ms (apagado acelerado).");

                case "sys_hung_app_timeout":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        key.SetValue("HungAppTimeout", "1000", RegistryValueKind.String);
                        key.SetValue("WaitToKillAppTimeout", "2000", RegistryValueKind.String);
                        key.SetValue("AutoEndTasks", "1", RegistryValueKind.String);
                    }
                    return Ok(id, "Cierre de aplicaciones colgadas al apagar ajustado a 1-2 segundos.");

                case "sys_ntfs_disable_8dot3":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1);
                    return Ok(id, "Creación de nombres cortos DOS 8.3 desactivada en NTFS.");

                case "sys_ntfs_disable_last_access":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1);
                    return Ok(id, "Marca de último acceso en NTFS desactivada (menos escrituras en SSD).");

                case "sys_menu_show_delay":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        key.SetValue("MenuShowDelay", "10", RegistryValueKind.String);
                    }
                    return Ok(id, "Retardo de apertura de menús ajustado a 10ms.");

                case "sys_disable_autoreboot_bsod":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 0);
                    return Ok(id, "Reinicio automático tras BSOD desactivado.");

                case "sys_disable_wer":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
                    return Ok(id, "Windows Error Reporting (WER) desactivado.");

                case "sys_clear_pagefile_shutdown":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "ClearPageFileAtShutdown", 0);
                    return Ok(id, "Sobrescritura del archivo de paginación al apagar desactivada (apagado rápido).");

                case "sys_large_system_cache":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 1);
                    return Ok(id, "LargeSystemCache habilitado para mayor caché de lectura en RAM.");

                case "sys_svchost_split":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", 0x380000);
                    return Ok(id, "Umbral de separación de procesos Svchost optimizado.");

                case "sys_disable_remote_assistance":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0);
                    return Ok(id, "Asistencia Remota no solicitada desactivada.");

                case "sys_disk_io_priority":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\I/O System", "IoPriorityOverride", 1);
                    return Ok(id, "Prioridad de E/S de disco para programas activos configurada.");

                case "sys_startup_delay":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0);
                    return Ok(id, "Retardo de arranque desactivado (StartupDelayInMSec = 0). Las apps de inicio cargan al instante.");

                case "sys_ntfs_memory_usage":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage", 2);
                    return Ok(id, "Caché de memoria NTFS incrementada (NtfsMemoryUsage = 2). Acelera lectura de directorios y SSDs.");

                case "sys_autochk_timeout":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "AutoChkTimeOut", 2);
                    return Ok(id, "Pausa de comprobación de disco en arranque reducida a 2 segundos (AutoChkTimeOut = 2).");

                default:
                    return Fail(id, $"Tweak desconocido: {id}");
            }
        }
        catch (Exception ex)
        {
            return Fail(id, $"Error al aplicar tweak '{id}': {ex.Message}");
        }
    }

    public TweakExecutionResult RollbackTweak(string id)
    {
        try
        {
            if (TransactionService.Instance.HasActiveTransaction(id))
            {
                if (TransactionService.Instance.RollbackTransaction(id, out var msg))
                {
                    return new TweakExecutionResult { Success = true, TweakId = id, Message = msg };
                }
            }

            switch (id)
            {
                // --- GAMING & LATENCIA ROLLBACK ---
                case "gaming_win32_priority":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2);
                    return Ok(id, "Win32PrioritySeparation restaurado a 2 (valor predeterminado de Windows).");

                case "gaming_nagle_algorithm":
                    ApplyNagle(disable: false);
                    return Ok(id, "Algoritmo de Nagle restaurado a su comportamiento estándar.");

                case "gaming_hags":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1);
                    return Ok(id, "HAGS desactivado (modo estándar).");

                case "gaming_network_throttling":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 10);
                    return Ok(id, "NetworkThrottlingIndex restaurado a 10.");

                case "gaming_system_responsiveness":
                    SetRegDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 20);
                    return Ok(id, "SystemResponsiveness restaurado a 20%.");

                case "gaming_game_dvr":
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1);
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true))
                    {
                        k?.DeleteValue("AllowGameDVR", false);
                    }
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
                    return Ok(id, "GameDVR restaurado a sus valores iniciales.");

                case "gaming_gpu_priority_games":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true))
                    {
                        key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                        key.SetValue("Priority", 2, RegistryValueKind.DWord);
                        key.SetValue("Scheduling Category", "Medium", RegistryValueKind.String);
                        key.SetValue("SFIO Priority", "Normal", RegistryValueKind.String);
                    }
                    return Ok(id, "Prioridades del perfil 'Games' restauradas a valores predeterminados.");

                case "gaming_game_bar_presence":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 1);
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 0);
                    return Ok(id, "Comportamientos de Game Bar restaurados.");

                case "gaming_mouse_accel":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse", true))
                    {
                        key.SetValue("MouseSpeed", "1", RegistryValueKind.String);
                        key.SetValue("MouseThreshold1", "6", RegistryValueKind.String);
                        key.SetValue("MouseThreshold2", "10", RegistryValueKind.String);
                    }
                    return Ok(id, "Curva de aceleración de ratón restaurada a estándar de Windows.");

                case "gaming_hpet_disable":
                    RunCmd("bcdedit /deletevalue useplatformclock");
                    RunCmd("bcdedit /set disabledynamictick no");
                    return Ok(id, "Configuración BCD de reloj y dynamic tick restaurada.");

                case "gaming_qos_bandwidth":
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched", true))
                    {
                        k?.DeleteValue("NonBestEffortLimit", false);
                    }
                    return Ok(id, "Límite de reserva de ancho de banda QoS restaurado.");

                case "gaming_power_throttling":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 0);
                    return Ok(id, "Power Throttling restaurado.");

                case "gaming_fullscreen_optimizations":
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehavior", 0);
                    SetRegDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 0);
                    return Ok(id, "Optimizaciones de pantalla completa restauradas.");

                case "gaming_visual_effects_perf":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 1);
                    return Ok(id, "Efectos visuales restaurados a configuración personalizada/estándar.");

                case "net_max_connections":
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                    {
                        key?.DeleteValue("MaxConnectionsPerServer", false);
                        key?.DeleteValue("MaxConnectionsPer1_0Server", false);
                    }
                    return Ok(id, "Límite de conexiones por servidor restaurado a valores por defecto.");

                case "net_dns_cache_ttl":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", true))
                    {
                        key?.DeleteValue("MaxCacheTtl", false);
                        key?.DeleteValue("MaxNegativeCacheTtl", false);
                    }
                    return Ok(id, "Parámetros de caché DNS restaurados a valores predeterminados.");

                // --- PRIVACIDAD ROLLBACK ---
                case "privacy_hosts_telemetry":
                    ApplyHostsBlock(block: false);
                    return Ok(id, "Bloqueo de dominios de telemetría eliminado del archivo HOSTS.");

                case "privacy_diagtrack":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2);
                    RunCmd("sc start DiagTrack");
                    return Ok(id, "Servicio DiagTrack configurado como Automático y reanudado.");

                case "privacy_dmwappushservice":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", 3);
                    return Ok(id, "Servicio dmwappushservice configurado en inicio Manual.");

                case "privacy_smartscreen":
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System", true))
                    {
                        k?.DeleteValue("EnableSmartScreen", false);
                    }
                    using (var k2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AppHost", true))
                    {
                        k2?.DeleteValue("EnableWebContentEvaluation", false);
                    }
                    return Ok(id, "SmartScreen restaurado.");

                case "privacy_advertising_id":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1);
                    return Ok(id, "ID de publicidad restaurado.");

                case "privacy_start_suggestions":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", true))
                    {
                        key.SetValue("SystemPaneSuggestionsEnabled", 1, RegistryValueKind.DWord);
                        key.SetValue("SubscribedContent-338388Enabled", 1, RegistryValueKind.DWord);
                        key.SetValue("SubscribedContent-338389Enabled", 1, RegistryValueKind.DWord);
                        key.SetValue("SilentInstalledAppsEnabled", 1, RegistryValueKind.DWord);
                    }
                    return Ok(id, "Sugerencias de inicio restauradas.");

                case "privacy_cortana_telemetry":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", true))
                    {
                        key?.DeleteValue("AllowCortana", false);
                        key?.DeleteValue("ConnectedSearchUseWeb", false);
                        key?.DeleteValue("DisableWebSearch", false);
                    }
                    return Ok(id, "Configuración de Cortana y búsqueda web restaurada.");

                case "privacy_activity_history":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System", true))
                    {
                        key?.DeleteValue("PublishUserActivities", false);
                        key?.DeleteValue("UploadUserActivities", false);
                    }
                    return Ok(id, "Historial de actividad restaurado.");

                case "privacy_ceip":
                    using (var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\SQMClient\Windows", true))
                    {
                        k1?.DeleteValue("CEIPEnable", false);
                    }
                    using (var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\SQMClient\ReliabilityAnalysis", true))
                    {
                        k2?.DeleteValue("CEIPEnable", false);
                    }
                    return Ok(id, "CEIP / SQM restaurado.");

                case "privacy_location_access":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", true))
                    {
                        key?.DeleteValue("DisableLocation", false);
                    }
                    return Ok(id, "Acceso a sensores de localización restaurado.");

                case "privacy_typing_personalization":
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\InputPersonalization", true))
                    {
                        key?.DeleteValue("RestrictImplicitInkCollection", false);
                        key?.DeleteValue("RestrictImplicitTextCollection", false);
                    }
                    return Ok(id, "Personalización de entrada de texto restaurada.");

                case "privacy_feedback_frequency":
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Siuf\Rules", true))
                    {
                        key?.DeleteValue("NumberOfSIFUrlSegments", false);
                        key?.DeleteValue("PeriodInNanoSeconds", false);
                    }
                    using (var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true))
                    {
                        k2?.DeleteValue("DoNotShowFeedbackNotifications", false);
                    }
                    return Ok(id, "Notificaciones de feedback restauradas.");

                case "privacy_tailored_experiences":
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\CloudContent", true))
                    {
                        key?.DeleteValue("DisableTailoredExperiencesWithDiagnosticData", false);
                    }
                    return Ok(id, "Experiencias personalizadas restauradas.");

                case "privacy_wifi_sense":
                    using (var k1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowWiFiHotSpotReporting", true))
                    {
                        k1?.SetValue("value", 1, RegistryValueKind.DWord);
                    }
                    using (var k2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowAutoConnectToWiFiSenseHotspots", true))
                    {
                        k2?.SetValue("value", 1, RegistryValueKind.DWord);
                    }
                    return Ok(id, "Opciones de Wi-Fi Sense restauradas.");

                // --- WINDOWS 11 UI ROLLBACK ---
                case "win11_classic_context_menu":
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                    return Ok(id, "Menú contextual restaurado al diseño moderno de Windows 11.");

                case "win11_show_file_extensions":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 1);
                    return Ok(id, "Extensiones de archivo ocultas para tipos conocidos.");

                case "win11_show_hidden_files":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 2);
                    return Ok(id, "Archivos ocultos restaurados a invisibles.");

                case "win11_hide_taskbar_widgets":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 1);
                    return Ok(id, "Icono de Widgets restaurado en barra de tareas.");

                case "win11_hide_copilot":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 1);
                    return Ok(id, "Botón de Copilot restaurado.");

                case "win11_hide_task_view":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 1);
                    return Ok(id, "Botón de Vista de Tareas restaurado.");

                case "win11_align_taskbar_left":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1);
                    return Ok(id, "Barra de tareas centrada (estándar Windows 11).");

                case "win11_launch_this_pc":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 2);
                    return Ok(id, "Explorador restaurado para abrir en 'Inicio / Acceso Rápido'.");

                case "win11_compact_view_explorer":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "UseCompactMode", 0);
                    return Ok(id, "Espaciado táctil estándar de Windows 11 restaurado.");

                case "win11_disable_lockscreen":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", true))
                    {
                        key?.DeleteValue("NoLockScreen", false);
                    }
                    return Ok(id, "Pantalla de bloqueo previa restaurada.");

                case "win11_disable_finish_setup":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 1);
                    return Ok(id, "Avisos de configuración restaurados.");

                case "win11_disable_snap_assist_flyout":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SnapAssist", 1);
                    return Ok(id, "Sugerencias de Snap Assist restauradas.");

                case "win11_show_seconds_taskbar":
                    SetRegDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", 0);
                    return Ok(id, "Segundos ocultos en el reloj del sistema.");

                case "win11_disable_bing_search":
                    using (var k1 = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer", true))
                    {
                        k1?.DeleteValue("DisableSearchBoxSuggestions", false);
                    }
                    using (var k2 = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", true))
                    {
                        k2?.DeleteValue("BingSearchEnabled", false);
                    }
                    return Ok(id, "Búsqueda web de Bing en menú Inicio restaurada.");

                // --- RENDIMIENTO ROLLBACK ---
                case "sys_disable_hibernation":
                    RunCmd("powercfg.exe -h on");
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1);
                    return Ok(id, "Hibernación reanudada (hiberfil.sys regenerado).");

                case "sys_waittokill_service":
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control", true))
                    {
                        key.SetValue("WaitToKillServiceTimeout", "5000", RegistryValueKind.String);
                    }
                    return Ok(id, "WaitToKillServiceTimeout restaurado a 5000ms.");

                case "sys_hung_app_timeout":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        key.SetValue("HungAppTimeout", "5000", RegistryValueKind.String);
                        key.SetValue("WaitToKillAppTimeout", "5000", RegistryValueKind.String);
                        key.SetValue("AutoEndTasks", "0", RegistryValueKind.String);
                    }
                    return Ok(id, "Tiempos de espera de aplicaciones colgadas restaurados a valores por defecto.");

                case "sys_ntfs_disable_8dot3":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 2);
                    return Ok(id, "Generación de nombres 8.3 de NTFS restaurada.");

                case "sys_ntfs_disable_last_access":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 0);
                    return Ok(id, "Registro de último acceso en NTFS restaurado.");

                case "sys_menu_show_delay":
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", true))
                    {
                        key.SetValue("MenuShowDelay", "400", RegistryValueKind.String);
                    }
                    return Ok(id, "Demora de apertura de menús restaurada a 400ms.");

                case "sys_disable_autoreboot_bsod":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 1);
                    return Ok(id, "Reinicio automático tras BSOD restaurado.");

                case "sys_disable_wer":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", true))
                    {
                        key?.DeleteValue("Disabled", false);
                    }
                    return Ok(id, "Windows Error Reporting restaurado.");

                case "sys_clear_pagefile_shutdown":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "ClearPageFileAtShutdown", 1);
                    return Ok(id, "Limpieza de archivo de paginación al apagar restaurada.");

                case "sys_large_system_cache":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 0);
                    return Ok(id, "LargeSystemCache restaurado a 0.");

                case "sys_svchost_split":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control", true))
                    {
                        key?.DeleteValue("SvcHostSplitThresholdInKB", false);
                    }
                    return Ok(id, "Configuración de Svchost restaurada.");

                case "sys_disable_remote_assistance":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 1);
                    return Ok(id, "Asistencia remota restaurada a valores por defecto.");

                case "sys_disk_io_priority":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\I/O System", "IoPriorityOverride", 0);
                    return Ok(id, "Prioridad de E/S de disco restaurada.");

                case "sys_startup_delay":
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", true))
                    {
                        key?.DeleteValue("StartupDelayInMSec", false);
                    }
                    return Ok(id, "Retardo de arranque restaurado a valores por defecto de Windows.");

                case "sys_ntfs_memory_usage":
                    SetRegDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage", 1);
                    return Ok(id, "Caché de memoria NTFS restaurada al valor predeterminado (1).");

                case "sys_autochk_timeout":
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", true))
                    {
                        key?.DeleteValue("AutoChkTimeOut", false);
                    }
                    return Ok(id, "Tiempo de espera de comprobación de disco en arranque restaurado.");

                default:
                    return Fail(id, $"Tweak desconocido: {id}");
            }
        }
        catch (Exception ex)
        {
            return Fail(id, $"Error al revertir tweak '{id}': {ex.Message}");
        }
    }

    public List<TweakExecutionResult> ApplyGamingProfile()
    {
        var gamingIds = GetCategorizedTweaks()
            .Where(t => t.IsRecommendedForGaming)
            .Select(t => t.Id)
            .ToList();

        var results = new List<TweakExecutionResult>();
        foreach (var id in gamingIds)
        {
            results.Add(ApplyTweak(id));
        }
        return results;
    }

    // ==========================================
    // HELPERS DE CONSULTA DE ESTADO (IS APPLIED)
    // ==========================================
    private static bool CheckWin32Priority()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
            return Convert.ToInt32(key?.GetValue("Win32PrioritySeparation") ?? 2) == 0x26;
        }
        catch { return false; }
    }

    private static bool CheckNagleDisabled()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (baseKey == null) return false;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                using var sub = baseKey.OpenSubKey(subName);
                if (Convert.ToInt32(sub?.GetValue("TcpAckFrequency") ?? 0) == 1 &&
                    Convert.ToInt32(sub?.GetValue("TCPNoDelay") ?? 0) == 1)
                {
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static bool CheckHagsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return Convert.ToInt32(key?.GetValue("HwSchMode") ?? 1) == 2;
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

    private static bool CheckSystemResponsivenessZero()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            return Convert.ToInt32(key?.GetValue("SystemResponsiveness") ?? 20) == 0;
        }
        catch { return false; }
    }

    private static bool CheckGameDvrDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            return Convert.ToInt32(key?.GetValue("GameDVR_Enabled") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckGpuPriorityGames()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
            return Convert.ToInt32(key?.GetValue("Priority") ?? 0) == 6 &&
                   string.Equals(key?.GetValue("Scheduling Category")?.ToString(), "High", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool CheckGameBarPresenceDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            return Convert.ToInt32(key?.GetValue("GameDVR_FSEBehaviorMode") ?? 0) == 2;
        }
        catch { return false; }
    }

    private static bool CheckMouseAccelDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            return (key?.GetValue("MouseSpeed")?.ToString() ?? "1") == "0";
        }
        catch { return false; }
    }

    private static bool CheckHpetDisabled()
    {
        try
        {
            var output = RunCmdCapture("bcdedit /enum {current}");
            return output.Contains("useplatformclock        No") || output.Contains("disabledynamictick      Yes");
        }
        catch { return false; }
    }

    private static bool CheckQosDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched");
            return Convert.ToInt32(key?.GetValue("NonBestEffortLimit") ?? -1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckPowerThrottlingDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling");
            return Convert.ToInt32(key?.GetValue("PowerThrottlingOff") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckFseDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            return Convert.ToInt32(key?.GetValue("GameDVR_FSEBehavior") ?? 0) == 2;
        }
        catch { return false; }
    }

    private static bool CheckVisualFxPerf()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
            return Convert.ToInt32(key?.GetValue("VisualFXSetting") ?? 0) == 2;
        }
        catch { return false; }
    }

    private static bool CheckHostsBlocked()
    {
        try
        {
            string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            if (!File.Exists(hostsPath)) return false;
            return File.ReadAllText(hostsPath).Contains(HostsBeginMarker);
        }
        catch { return false; }
    }

    private static bool CheckDiagTrackDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
            return Convert.ToInt32(key?.GetValue("Start") ?? 0) == 4;
        }
        catch { return false; }
    }

    private static bool CheckDmwpDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\dmwappushservice");
            return Convert.ToInt32(key?.GetValue("Start") ?? 0) == 4;
        }
        catch { return false; }
    }

    private static bool CheckSmartScreenDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
            return Convert.ToInt32(key?.GetValue("EnableSmartScreen") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckAdvertisingIdDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
            return Convert.ToInt32(key?.GetValue("Enabled") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckStartSuggestionsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
            return Convert.ToInt32(key?.GetValue("SystemPaneSuggestionsEnabled") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckCortanaDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search");
            return Convert.ToInt32(key?.GetValue("AllowCortana") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckActivityHistoryDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System");
            return Convert.ToInt32(key?.GetValue("PublishUserActivities") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckCeipDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\SQMClient\Windows");
            return Convert.ToInt32(key?.GetValue("CEIPEnable") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckLocationDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors");
            return Convert.ToInt32(key?.GetValue("DisableLocation") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckTypingPersonalizationDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\InputPersonalization");
            return Convert.ToInt32(key?.GetValue("RestrictImplicitInkCollection") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckFeedbackDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
            return Convert.ToInt32(key?.GetValue("DoNotShowFeedbackNotifications") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckTailoredExpDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\CloudContent");
            return Convert.ToInt32(key?.GetValue("DisableTailoredExperiencesWithDiagnosticData") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckWifiSenseDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\PolicyManager\default\WiFi\AllowWiFiHotSpotReporting");
            return Convert.ToInt32(key?.GetValue("value") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckClassicContextMenu()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
            return key != null;
        }
        catch { return false; }
    }

    private static bool CheckShowExtensions()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("HideFileExt") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckShowHidden()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("Hidden") ?? 2) == 1;
        }
        catch { return false; }
    }

    private static bool CheckHideWidgets()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("TaskbarDa") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckHideCopilot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("ShowCopilotButton") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckHideTaskView()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("TaskbarMn") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckAlignTaskbarLeft()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("TaskbarAl") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckLaunchThisPc()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("LaunchTo") ?? 2) == 1;
        }
        catch { return false; }
    }

    private static bool CheckCompactExplorer()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("UseCompactMode") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckDisableLockScreen()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Personalization");
            return Convert.ToInt32(key?.GetValue("NoLockScreen") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckDisableFinishSetup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement");
            return Convert.ToInt32(key?.GetValue("ScoobeSystemSettingEnabled") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckDisableSnapAssist()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("SnapAssist") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckShowSecondsTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return Convert.ToInt32(key?.GetValue("ShowSecondsInSystemClock") ?? 0) == 1;
        }
        catch { return false; }
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

    private static bool CheckHibernationDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return Convert.ToInt32(key?.GetValue("HibernateEnabled") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckWaitToKillService()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            return (key?.GetValue("WaitToKillServiceTimeout")?.ToString() ?? "5000") == "2000";
        }
        catch { return false; }
    }

    private static bool CheckHungAppTimeout()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return (key?.GetValue("HungAppTimeout")?.ToString() ?? "5000") == "1000";
        }
        catch { return false; }
    }

    private static bool CheckNtfsDisable8dot3()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            return Convert.ToInt32(key?.GetValue("NtfsDisable8dot3NameCreation") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckNtfsDisableLastAccess()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            return Convert.ToInt32(key?.GetValue("NtfsDisableLastAccessUpdate") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckMenuShowDelay()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return (key?.GetValue("MenuShowDelay")?.ToString() ?? "400") == "10";
        }
        catch { return false; }
    }

    private static bool CheckAutoRebootDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
            return Convert.ToInt32(key?.GetValue("AutoReboot") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckWerDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting");
            return Convert.ToInt32(key?.GetValue("Disabled") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckClearPageFileDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            return Convert.ToInt32(key?.GetValue("ClearPageFileAtShutdown") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckLargeSystemCache()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            return Convert.ToInt32(key?.GetValue("LargeSystemCache") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckSvcHostSplit()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            return Convert.ToInt32(key?.GetValue("SvcHostSplitThresholdInKB") ?? 0) == 0x380000;
        }
        catch { return false; }
    }

    private static bool CheckRemoteAssistanceDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Remote Assistance");
            return Convert.ToInt32(key?.GetValue("fAllowToGetHelp") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckIoPriorityOverride()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\I/O System");
            return Convert.ToInt32(key?.GetValue("IoPriorityOverride") ?? 0) == 1;
        }
        catch { return false; }
    }

    private static bool CheckStartupDelay()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize");
            return Convert.ToInt32(key?.GetValue("StartupDelayInMSec") ?? -1) == 0;
        }
        catch { return false; }
    }

    private static bool CheckNtfsMemoryUsage()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            return Convert.ToInt32(key?.GetValue("NtfsMemoryUsage") ?? 1) == 2;
        }
        catch { return false; }
    }

    private static bool CheckAutoChkTimeOut()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            var val = key?.GetValue("AutoChkTimeOut");
            return val != null && Convert.ToInt32(val) <= 2;
        }
        catch { return false; }
    }

    private static bool CheckMaxConnections()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            return Convert.ToInt32(key?.GetValue("MaxConnectionsPerServer") ?? 0) == 16;
        }
        catch { return false; }
    }

    private static bool CheckDnsCacheTtl()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters");
            return Convert.ToInt32(key?.GetValue("MaxCacheTtl") ?? 0) == 86400 &&
                   Convert.ToInt32(key?.GetValue("MaxNegativeCacheTtl") ?? 0) == 5;
        }
        catch { return false; }
    }

    // ==========================================
    // MÉTODOS DE SOPORTE (REGISTRY / CMD / HOSTS)
    // ==========================================
    private static void SetRegDword(RegistryKey root, string subPath, string valueName, int value)
    {
        try
        {
            TransactionService.Instance.CaptureRegistryPreState(root, subPath, valueName);
        }
        catch { }

        using var key = root.OpenSubKey(subPath, true) ?? root.CreateSubKey(subPath, true);
        key?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private static void ApplyNagle(bool disable)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
            if (baseKey == null) return;

            // Target only active physical network interfaces with gateway to avoid breaking VPNs, Hyper-V, or local loopbacks
            var activeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                                 n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                                !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                                !n.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
                                !n.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) &&
                                n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

                foreach (var iface in ifaces)
                {
                    activeGuids.Add(iface.Id);
                }
            }
            catch { }

            var subNames = baseKey.GetSubKeyNames();
            var targets = activeGuids.Count > 0 
                ? subNames.Where(s => activeGuids.Contains(s)).ToList()
                : subNames.ToList();

            foreach (var subName in targets)
            {
                using var sub = baseKey.OpenSubKey(subName, true);
                if (sub != null)
                {
                    if (disable)
                    {
                        sub.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                        sub.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                    }
                    else
                    {
                        sub.DeleteValue("TcpAckFrequency", false);
                        sub.DeleteValue("TCPNoDelay", false);
                    }
                }
            }
        }
        catch { }
    }

    private static void ApplyHostsBlock(bool block)
    {
        string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        if (!File.Exists(hostsPath)) return;

        try
        {
            var lines = File.ReadAllLines(hostsPath).ToList();
            var newLines = new List<string>();
            bool insideBlock = false;

            foreach (var line in lines)
            {
                if (line.Trim() == HostsBeginMarker)
                {
                    insideBlock = true;
                    continue;
                }
                if (line.Trim() == HostsEndMarker)
                {
                    insideBlock = false;
                    continue;
                }
                if (!insideBlock)
                {
                    newLines.Add(line);
                }
            }

            if (block)
            {
                newLines.Add(HostsBeginMarker);
                foreach (var dom in TelemetryDomains)
                {
                    newLines.Add($"0.0.0.0 {dom}");
                }
                newLines.Add(HostsEndMarker);
            }

            var attr = File.GetAttributes(hostsPath);
            if ((attr & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(hostsPath, attr & ~FileAttributes.ReadOnly);
            }

            File.WriteAllLines(hostsPath, newLines);
        }
        catch { }
    }

    private static void RunCmd(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(4000);
        }
        catch { }
    }

    private static string RunCmdCapture(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            string output = p?.StandardOutput.ReadToEnd() ?? string.Empty;
            p?.WaitForExit(3000);
            return output;
        }
        catch { return string.Empty; }
    }

    private static TweakExecutionResult Ok(string id, string message)
    {
        try
        {
            TransactionService.Instance.CommitTransaction(id);
        }
        catch { }
        return new() { Success = true, TweakId = id, Message = message };
    }

    private static TweakExecutionResult Fail(string id, string message) =>
        new() { Success = false, TweakId = id, Message = message };
}

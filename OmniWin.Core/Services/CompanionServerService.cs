using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QRCoder;

namespace OmniWin.Core.Services;

public class CompanionStatusModel
{
    public bool IsRunning { get; set; }
    public int Port { get; set; } = 8766;
    public string LocalIp { get; set; } = "127.0.0.1";
    public string PairingToken { get; set; } = string.Empty;
    public string PairingUrl { get; set; } = string.Empty;
    public int ConnectedClientsCount { get; set; } = 0;
}

public class CompanionServerService : IDisposable
{
    private static readonly Lazy<CompanionServerService> _instance = new(() => new CompanionServerService());
    public static CompanionServerService Instance => _instance.Value;

    public const int DEFAULT_PORT = 8766;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, WebSocket> _activeSockets = new();
    private string _pairingToken;
    private readonly int _port;
    private readonly string _localIp;

    public string PairingToken => _pairingToken;
    public int Port => _port;
    public string LocalIp => _localIp;
    public string PairingUrl => $"http://{_localIp}:{_port}/?token={_pairingToken}";
    public bool IsRunning => _listener?.IsListening ?? false;
    public int ActiveClientsCount => _activeSockets.Count;

    public void RegenerateToken()
    {
        _pairingToken = GenerateSecureToken(8);
    }

    public event Action<string>? OnCommandReceived;
    public event Action<string, JsonElement>? OnHudRemoteActionReceived;
    public event Action<int>? OnClientsCountChanged;

    public CompanionServerService(int port = DEFAULT_PORT)
    {
        _port = port;
        _pairingToken = GenerateSecureToken(8);
        _localIp = ResolvePrimaryLocalIp();
    }

    private static string GenerateSecureToken(int length)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ResolvePrimaryLocalIp()
    {
        try
        {
            var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase));

            foreach (var iface in activeInterfaces)
            {
                var props = iface.GetIPProperties();
                var ipInfo = props.UnicastAddresses.FirstOrDefault(u =>
                    u.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(u.Address));

                if (ipInfo != null)
                {
                    return ipInfo.Address.ToString();
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public byte[] GenerateQrCodePngBytes(int pixelsPerModule = 10)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(PairingUrl, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(pixelsPerModule);
    }

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();

            // Register both wildcard and explicit localhost/IP prefixes
            try
            {
                _listener.Prefixes.Add($"http://*:{_port}/");
            }
            catch
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://{_localIp}:{_port}/");
            }

            _listener.Start();
            Task.Run(() => ListenLoopAsync(_cts.Token));
            Task.Run(() => TelemetryBroadcastLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Stop();
            throw new InvalidOperationException($"No se pudo iniciar el servidor OmniCompanion en el puerto {_port}: {ex.Message}", ex);
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            foreach (var kvp in _activeSockets)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _activeSockets.Clear();
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        finally
        {
            _listener = null;
            _cts = null;
            OnClientsCountChanged?.Invoke(0);
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch { }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var req = context.Request;
            var resp = context.Response;

            // CORS headers for seamless companion connectivity
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = 200;
                resp.Close();
                return;
            }

            // WebSocket Upgrade Request
            if (context.Request.IsWebSocketRequest)
            {
                string queryToken = req.QueryString["token"] ?? string.Empty;
                if (!string.Equals(queryToken, _pairingToken, StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = 401;
                    resp.Close();
                    return;
                }

                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                var socketId = Guid.NewGuid().ToString("N");
                _activeSockets[socketId] = wsContext.WebSocket;
                OnClientsCountChanged?.Invoke(_activeSockets.Count);

                _ = HandleWebSocketConnectionAsync(socketId, wsContext.WebSocket, ct);
                return;
            }

            // HTTP API endpoints
            string path = req.Url?.AbsolutePath ?? "/";
            string token = req.QueryString["token"] ?? req.Headers["X-OmniWin-Token"] ?? string.Empty;

            if (path == "/api/status")
            {
                if (!string.Equals(token, _pairingToken, StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = 401;
                    byte[] unauthorized = Encoding.UTF8.GetBytes("{\"error\": \"Unauthorized: Invalid or missing pairing token\"}");
                    resp.ContentType = "application/json";
                    await resp.OutputStream.WriteAsync(unauthorized, ct);
                    resp.Close();
                    return;
                }

                var stats = GetLiveTelemetryJson();
                byte[] data = Encoding.UTF8.GetBytes(stats);
                resp.ContentType = "application/json";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(data, ct);
                resp.Close();
                return;
            }

            if (path == "/api/stutter")
            {
                if (!string.Equals(token, _pairingToken, StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = 401;
                    resp.Close();
                    return;
                }

                var report = StutterInvestigatorService.Instance.AnalyzeRecentStutter(TimeSpan.FromSeconds(15));
                byte[] reportBytes = JsonSerializer.SerializeToUtf8Bytes(report);
                resp.ContentType = "application/json";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(reportBytes, ct);
                resp.Close();
                return;
            }

            if (path == "/api/command" && req.HttpMethod == "POST")
            {
                if (!string.Equals(token, _pairingToken, StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = 401;
                    resp.Close();
                    return;
                }

                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync(ct);
                ExecuteCommand(body);

                resp.StatusCode = 200;
                byte[] ok = Encoding.UTF8.GetBytes("{\"success\": true}");
                resp.ContentType = "application/json";
                await resp.OutputStream.WriteAsync(ok, ct);
                resp.Close();
                return;
            }

            // Serve Embedded Mobile WebApp
            string html = GetEmbeddedHtmlApp();
            byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
            resp.ContentType = "text/html; charset=utf-8";
            resp.StatusCode = 200;
            await resp.OutputStream.WriteAsync(htmlBytes, ct);
            resp.Close();
        }
        catch { }
    }

    private async Task HandleWebSocketConnectionAsync(string socketId, WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[2048];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ExecuteCommand(message);
                }
            }
        }
        catch { }
        finally
        {
            _activeSockets.TryRemove(socketId, out _);
            OnClientsCountChanged?.Invoke(_activeSockets.Count);
            try { socket.Dispose(); } catch { }
        }
    }

    private void ExecuteCommand(string commandPayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(commandPayload);
            string action = doc.RootElement.TryGetProperty("action", out var act) ? act.GetString() ?? string.Empty : string.Empty;

            switch (action.ToLowerInvariant())
            {
                case "purge_ram":
                    var mem = new MemoryService();
                    mem.PurgeMemory(true, false);
                    break;

                case "toggle_awake":
                    var awake = AwakeService.Instance;
                    if (awake.CurrentState.IsActive)
                        awake.Deactivate();
                    else
                        awake.Activate(AwakeMode.KeepAwakeIndefinite, duration: null, keepDisplayOn: true);
                    break;

                case "stutter_trigger":
                case "analyze_stutter":
                    var stutter = StutterInvestigatorService.Instance.AnalyzeRecentStutter(TimeSpan.FromSeconds(15));
                    OnCommandReceived?.Invoke($"stutter:{stutter.ProbableCause}");
                    break;

                case "hibernate_launchers":
                    LauncherHibernatorService.Instance.HibernateBackgroundProcesses();
                    OnCommandReceived?.Invoke("launchers:hibernated");
                    break;

                case "wake_launchers":
                    LauncherHibernatorService.Instance.WakeAllHibernatedProcesses();
                    OnCommandReceived?.Invoke("launchers:woken");
                    break;

                case "set_hud_style":
                case "set_hud_opacity":
                case "set_hud_scale":
                case "set_hud_metric":
                case "set_hud_corner":
                case "toggle_hud_lock":
                case "toggle_hud_visibility":
                    OnHudRemoteActionReceived?.Invoke(action.ToLowerInvariant(), doc.RootElement.Clone());
                    break;

                default:
                    OnCommandReceived?.Invoke(action);
                    break;
            }
        }
        catch { }
    }

    private async Task TelemetryBroadcastLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);

                // Sample live system telemetry via unified TelemetryHub (feeds StutterInvestigatorService with real CPU/GPU metrics)
                var hubSnap = TelemetryHub.Instance.SampleNow();

                if (_activeSockets.IsEmpty) continue;

                string json = GetLiveTelemetryJson();
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                foreach (var kvp in _activeSockets)
                {
                    if (kvp.Value.State == WebSocketState.Open)
                    {
                        try
                        {
                            await kvp.Value.SendAsync(segment, WebSocketMessageType.Text, true, ct);
                        }
                        catch
                        {
                            _activeSockets.TryRemove(kvp.Key, out _);
                            OnClientsCountChanged?.Invoke(_activeSockets.Count);
                        }
                    }
                }
            }
            catch (TaskCanceledException) { break; }
            catch { }
        }
    }

    public string GetLiveTelemetryJson()
    {
        try
        {
            var hub = TelemetryHub.Instance.CurrentSnapshot;
            var net = new NetworkService().GetNetworkThroughput();
            var awake = AwakeService.Instance.CurrentState;
            var hibStatus = LauncherHibernatorService.Instance.GetStatus();
            var settings = AppSettingsService.Instance.Settings;

            var payload = new
            {
                hostname = Environment.MachineName,
                cpuLoad = hub.CpuLoadPercent,
                cpuTemp = hub.CpuTemperatureCelsius,
                cpuPower = hub.CpuPowerWatts,
                gpuLoad = hub.GpuLoadPercent,
                gpuTemp = hub.GpuTemperatureCelsius,
                gpuMemoryMb = hub.GpuMemoryUsedMb,
                ramUsagePercent = hub.RamUsagePercent,
                ramUsedGb = hub.RamUsedBytes / (1024.0 * 1024.0 * 1024.0),
                ramTotalGb = hub.RamTotalBytes / (1024.0 * 1024.0 * 1024.0),
                netDownloadSpeed = net.downloadBytesPerSec,
                netUploadSpeed = net.uploadBytesPerSec,
                kernelJitterUs = hub.KernelJitterUs,
                isAwakeActive = awake.IsActive,
                isHibernating = hibStatus.IsHibernating,
                hibernatedCount = hibStatus.HibernatedProcessCount,
                freedMemoryMb = hibStatus.TotalMemoryFreedMb,
                hudStyle = settings.HudStyleIndex,
                hudOpacity = settings.HudBackgroundOpacity,
                hudScale = settings.HudScale,
                hudShowCpu = settings.HudShowCpu,
                hudShowCpuTemp = settings.HudShowCpuTemp,
                hudShowGpu = settings.HudShowGpu,
                hudShowGpuTemp = settings.HudShowGpuTemp,
                hudShowRam = settings.HudShowRam,
                hudShowPing = settings.HudShowPing,
                hudShowTimer = settings.HudShowSessionTimer,
                timestamp = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(payload);
        }
        catch
        {
            return "{}";
        }
    }

    private string GetEmbeddedHtmlApp()
    {
        return """
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
  <meta name="theme-color" content="#07090E" />
  <meta name="apple-mobile-web-app-capable" content="yes" />
  <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent" />
  <title>OmniCompanion 2.0 — Control Móvil</title>
  <style>
    :root {
      --bg: #07090E; --card: #0D1322; --border: #162035; --accent: #0284C7;
      --emerald: #10B981; --crimson: #EF4444; --indigo: #6366F1; --text: #F8FAFC; --muted: #94A3B8;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; user-select: none; -webkit-tap-highlight-color: transparent; }
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI Variable', Roboto, sans-serif; background: var(--bg); color: var(--text); padding: 16px 14px 80px 14px; min-height: 100vh; }
    
    .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: 12px; margin-bottom: 14px; }
    .brand { display: flex; align-items: center; gap: 8px; font-size: 18px; font-weight: 800; }
    .badge { background: #0A2644; color: var(--accent); font-size: 10px; font-weight: bold; padding: 2px 7px; border-radius: 4px; border: 1px solid #144272; }
    .status-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--emerald); display: inline-block; margin-right: 5px; box-shadow: 0 0 8px var(--emerald); }
    
    /* Tabs Navigation */
    .tabs-nav { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 6px; background: #090D18; padding: 4px; border-radius: 10px; border: 1px solid var(--border); margin-bottom: 16px; }
    .tab-btn { background: transparent; border: none; color: var(--muted); font-size: 11.5px; font-weight: 700; padding: 8px 4px; border-radius: 7px; cursor: pointer; text-align: center; transition: all 0.2s ease; }
    .tab-btn.active { background: #1E293B; color: #38BDF8; box-shadow: 0 2px 8px rgba(0,0,0,0.4); }
    
    .tab-content { display: none; }
    .tab-content.active { display: block; animation: fadeIn 0.15s ease; }
    @keyframes fadeIn { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: translateY(0); } }

    /* Telemetry Cards */
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 16px; }
    .card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 12px; display: flex; flex-direction: column; justify-content: space-between; }
    .card-title { font-size: 10.5px; font-weight: 700; color: var(--muted); text-transform: uppercase; margin-bottom: 3px; }
    .card-value { font-size: 22px; font-weight: 800; color: var(--text); }
    .card-sub { font-size: 10.5px; color: var(--muted); margin-top: 2px; }

    /* Section Headings */
    .sec-title { font-size: 11.5px; font-weight: 800; color: var(--muted); text-transform: uppercase; margin: 14px 0 8px 0; letter-spacing: 0.5px; }

    /* Button Grids */
    .btn-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 12px; }
    .btn { background: #111A2E; border: 1px solid var(--border); border-radius: 10px; padding: 12px 10px; color: var(--text); font-size: 12px; font-weight: 700; text-align: center; cursor: pointer; transition: all 0.15s ease; display: flex; flex-direction: column; align-items: center; gap: 4px; }
    .btn:active { transform: scale(0.96); background: var(--accent); border-color: var(--accent); }
    .btn-primary { background: #064E3B; border-color: #047857; color: #34D399; }
    .btn-indigo { background: #1E1B4B; border-color: #4F46E5; color: #A5B4FC; }

    /* Segmented Style Buttons */
    .style-selector { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 6px; margin-bottom: 14px; }
    .style-btn { background: #0B1120; border: 1px solid var(--border); border-radius: 8px; padding: 9px 4px; font-size: 11px; font-weight: 700; color: var(--muted); text-align: center; cursor: pointer; }
    .style-btn.active { background: #0C2A4D; border-color: var(--accent); color: #38BDF8; }

    /* Sliders */
    .slider-box { background: var(--card); border: 1px solid var(--border); border-radius: 10px; padding: 12px; margin-bottom: 10px; }
    .slider-header { display: flex; justify-content: space-between; font-size: 11.5px; font-weight: 700; color: var(--muted); margin-bottom: 6px; }
    .slider-header span:last-child { color: #38BDF8; }
    input[type=range] { width: 100%; -webkit-appearance: none; background: #1E293B; height: 6px; border-radius: 3px; outline: none; }
    input[type=range]::-webkit-slider-thumb { -webkit-appearance: none; width: 18px; height: 18px; border-radius: 50%; background: var(--accent); cursor: pointer; }

    /* Corner Buttons */
    .corner-grid { display: grid; grid-template-columns: 1fr 1fr 1fr 1fr; gap: 6px; margin-bottom: 12px; }
    .corner-btn { background: #111A2E; border: 1px solid var(--border); border-radius: 8px; padding: 8px 0; font-size: 11px; font-weight: 800; color: var(--muted); text-align: center; cursor: pointer; }
    .corner-btn:active { background: var(--accent); color: #FFF; }

    /* Metrics Chips */
    .chips-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-bottom: 12px; }
    .chip { background: #0F172A; border: 1px solid var(--border); border-radius: 8px; padding: 9px 10px; display: flex; justify-content: space-between; align-items: center; font-size: 11.5px; font-weight: 700; cursor: pointer; }
    .chip.active { border-color: var(--emerald); color: #34D399; }

    .toast { position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); background: #0F172A; border: 1px solid var(--accent); color: var(--text); padding: 9px 16px; border-radius: 30px; font-size: 11.5px; font-weight: bold; opacity: 0; transition: opacity 0.2s ease; pointer-events: none; z-index: 100; box-shadow: 0 4px 14px rgba(0,0,0,0.6); }
    .toast.show { opacity: 1; }
  </style>
</head>
<body>
  <div class="header">
    <div class="brand">⚡ OmniWin <span class="badge">PWA 2.0</span></div>
    <div style="font-size: 11.5px; color: var(--muted);"><span class="status-dot"></span><span id="txtHost">Conectando...</span></div>
  </div>

  <!-- TABS NAV -->
  <div class="tabs-nav">
    <button class="tab-btn active" onclick="switchTab('telemetry')">📊 Telemetría</button>
    <button class="tab-btn" onclick="switchTab('hud')">🎮 Control HUD</button>
    <button class="tab-btn" onclick="switchTab('boost')">⚡ Boost PC</button>
  </div>

  <!-- ================= TAB 1: TELEMETRY ================= -->
  <div id="tab-telemetry" class="tab-content active">
    <div class="grid">
      <div class="card">
        <div class="card-title">Memoria RAM</div>
        <div class="card-value" id="valRam">--%</div>
        <div class="card-sub" id="subRam">-- / -- GB</div>
      </div>
      <div class="card">
        <div class="card-title">Temp CPU</div>
        <div class="card-value" id="valCpuTemp">--°C</div>
        <div class="card-sub">Sensor Paquete</div>
      </div>
      <div class="card">
        <div class="card-title">Descarga Red</div>
        <div class="card-value" id="valNetDown">--</div>
        <div class="card-sub">Throughput vivo</div>
      </div>
      <div class="card">
        <div class="card-title">Subida Red</div>
        <div class="card-value" id="valNetUp">--</div>
        <div class="card-sub">Throughput vivo</div>
      </div>
    </div>

    <div class="card" style="margin-bottom: 12px;">
      <div class="card-title">Estado de Optimización</div>
      <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 4px;">
        <span style="font-size: 12.5px; font-weight: 700;" id="txtHibStatus">Hibernador: Inactivo</span>
        <span style="font-size: 11px; color: var(--emerald);" id="txtHibFreed">0 MB Libres</span>
      </div>
    </div>
  </div>

  <!-- ================= TAB 2: HUD REMOTE ================= -->
  <div id="tab-hud" class="tab-content">
    <div class="sec-title">Estilo Visual del HUD</div>
    <div class="style-selector">
      <div id="btnStyle0" class="style-btn active" onclick="setHudStyle(0)">Riva OSD<br><span style="font-size: 9px; color: var(--muted);">(Solo Texto)</span></div>
      <div id="btnStyle1" class="style-btn" onclick="setHudStyle(1)">Card<br><span style="font-size: 9px; color: var(--muted);">(Glassmorphism)</span></div>
      <div id="btnStyle2" class="style-btn" onclick="setHudStyle(2)">Barra<br><span style="font-size: 9px; color: var(--muted);">(Compacta)</span></div>
    </div>

    <div class="sec-title">Transparencia y Escala en Monitor</div>
    <div class="slider-box">
      <div class="slider-header">
        <span>Transparencia de Fondo</span>
        <span id="lblOpacity">0% (Puro)</span>
      </div>
      <input type="range" id="rngOpacity" min="0" max="1" step="0.05" value="0" oninput="changeOpacity(this.value)" />
    </div>

    <div class="slider-box">
      <div class="slider-header">
        <span>Tamaño / Escala</span>
        <span id="lblScale">100%</span>
      </div>
      <input type="range" id="rngScale" min="0.8" max="1.6" step="0.05" value="1.0" oninput="changeScale(this.value)" />
    </div>

    <div class="sec-title">Anclaje a Esquinas</div>
    <div class="corner-grid">
      <button class="corner-btn" onclick="setCorner('TL')">↖ Sup. Izq</button>
      <button class="corner-btn" onclick="setCorner('TR')">↗ Sup. Der</button>
      <button class="corner-btn" onclick="setCorner('BL')">↙ Inf. Izq</button>
      <button class="corner-btn" onclick="setCorner('BR')">↘ Inf. Der</button>
    </div>

    <div class="sec-title">Acciones de Overlay</div>
    <div class="btn-grid">
      <div class="btn" onclick="sendCmd('toggle_hud_lock')">
        <span style="font-size: 18px;">🔒</span>
        <span>Alternar Bloqueo</span>
      </div>
      <div class="btn" onclick="sendCmd('toggle_hud_visibility')">
        <span style="font-size: 18px;">👁️</span>
        <span>Mostrar/Ocultar</span>
      </div>
    </div>
  </div>

  <!-- ================= TAB 3: GAMING BOOST ================= -->
  <div id="tab-boost" class="tab-content">
    <div class="sec-title">Optimizaciones In-Game de 1 Toque</div>
    <div class="btn-grid">
      <div class="btn btn-primary" onclick="sendCmd('purge_ram')">
        <span style="font-size: 20px;">⚡</span>
        <span>Liberar Memoria</span>
      </div>
      <div class="btn btn-indigo" onclick="sendCmd('hibernate_launchers')">
        <span style="font-size: 20px;">🧊</span>
        <span>Hibernar Launchers</span>
      </div>
    </div>

    <div class="btn-grid">
      <div class="btn" onclick="sendCmd('wake_launchers')">
        <span style="font-size: 20px;">☀️</span>
        <span>Restaurar Apps</span>
      </div>
      <div class="btn" onclick="sendCmd('toggle_awake')">
        <span style="font-size: 20px;">☕</span>
        <span id="lblAwake">Modo Cafeína</span>
      </div>
    </div>

    <div style="margin-top: 14px;">
      <div class="btn" style="background: linear-gradient(135deg, #1E1B4B, #312E81); border: 1px solid #6366F1; display: flex; align-items: center; justify-content: flex-start; gap: 14px; padding: 14px 18px; border-radius: 12px; cursor: pointer;" onclick="triggerStutter()">
        <span style="font-size: 22px;">⚠️</span>
        <div style="text-align: left;">
          <div style="font-weight: 800; font-size: 14px; color: #E0E7FF;">¡Sentí un Stutter!</div>
          <div style="font-size: 11px; color: #A5B4FC;">Diagnosticar tirones de los últimos 15s</div>
        </div>
      </div>
    </div>

    <div id="stutterBox" style="display: none; margin-top: 12px; background: #0F172A; border: 1px solid #6366F1; border-radius: 12px; padding: 14px;">
      <div style="font-size: 13px; font-weight: 800; color: #F59E0B; margin-bottom: 6px;" id="stutterCause">Diagnóstico</div>
      <div style="font-size: 12px; color: var(--text); line-height: 1.4;" id="stutterDetails">...</div>
    </div>
  </div>

  <div id="toast" class="toast">Comando enviado</div>

  <script>
    const urlParams = new URLSearchParams(window.location.search);
    const token = urlParams.get('token') || '';
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${proto}//${window.location.host}/ws?token=${token}`;
    let socket;

    function switchTab(tabId) {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
      event.currentTarget.classList.add('active');
      document.getElementById('tab-' + tabId).classList.add('active');
    }

    function formatSpeed(bytes) {
      if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB/s';
      if (bytes >= 1024) return (bytes / 1024).toFixed(0) + ' KB/s';
      return bytes.toFixed(0) + ' B/s';
    }

    function showToast(msg) {
      const t = document.getElementById('toast');
      t.innerText = msg;
      t.classList.add('show');
      setTimeout(() => t.classList.remove('show'), 1600);
    }

    function setHudStyle(styleIdx) {
      [0, 1, 2].forEach(i => {
        const el = document.getElementById('btnStyle' + i);
        if (el) el.classList.toggle('active', i === styleIdx);
      });
      sendCmdAction('set_hud_style', { style: styleIdx });
    }

    function changeOpacity(val) {
      const pct = Math.round(val * 100);
      document.getElementById('lblOpacity').innerText = pct <= 2 ? '0% (Puro)' : pct + '%';
      sendCmdAction('set_hud_opacity', { opacity: parseFloat(val) });
    }

    function changeScale(val) {
      const pct = Math.round(val * 100);
      document.getElementById('lblScale').innerText = pct + '%';
      sendCmdAction('set_hud_scale', { scale: parseFloat(val) });
    }

    function setCorner(corner) {
      sendCmdAction('set_hud_corner', { corner: corner });
    }

    function triggerStutter() {
      if (navigator.vibrate) navigator.vibrate([60, 40, 60]);
      showToast('Analizando telemetría reciente...');
      fetch('/api/stutter?token=' + token)
        .then(r => r.json())
        .then(data => {
          document.getElementById('stutterBox').style.display = 'block';
          document.getElementById('stutterCause').innerText = data.ProbableCause || data.probableCause || 'Stutter Analizado';
          document.getElementById('stutterDetails').innerText = data.Summary || data.summary || 'Telemetría dentro de rangos normales.';
        })
        .catch(() => {
          sendCmd('stutter_trigger');
        });
    }

    function connect() {
      socket = new WebSocket(wsUrl);
      socket.onopen = () => { document.getElementById('txtHost').innerText = 'En vivo'; };
      socket.onmessage = (e) => {
        try {
          const d = JSON.parse(e.data);
          if (d.hostname) document.getElementById('txtHost').innerText = d.hostname;
          if (d.ramUsagePercent !== undefined) {
            document.getElementById('valRam').innerText = d.ramUsagePercent.toFixed(0) + '%';
            document.getElementById('subRam').innerText = `${d.ramUsedGb.toFixed(1)} / ${d.ramTotalGb.toFixed(1)} GB`;
          }
          if (d.cpuTemp !== undefined && d.cpuTemp !== null) {
            document.getElementById('valCpuTemp').innerText = d.cpuTemp.toFixed(0) + '°C';
          }
          if (d.netDownloadSpeed !== undefined) {
            document.getElementById('valNetDown').innerText = formatSpeed(d.netDownloadSpeed);
          }
          if (d.netUploadSpeed !== undefined) {
            document.getElementById('valNetUp').innerText = formatSpeed(d.netUploadSpeed);
          }
          if (d.isAwakeActive !== undefined) {
            document.getElementById('lblAwake').innerText = d.isAwakeActive ? '☕ Cafeína: ON' : '☕ Cafeína: Off';
          }
          if (d.isHibernating !== undefined) {
            document.getElementById('txtHibStatus').innerText = d.isHibernating ? `Hibernando ${d.hibernatedCount} apps` : 'Hibernador: Inactivo';
            document.getElementById('txtHibFreed').innerText = d.freedMemoryMb ? `~${Math.round(d.freedMemoryMb)} MB Libres` : '0 MB Libres';
          }
        } catch {}
      };
      socket.onclose = () => {
        document.getElementById('txtHost').innerText = 'Desconectado (Reintentando...)';
        setTimeout(connect, 2000);
      };
    }

    function sendCmd(act) {
      sendCmdAction(act, {});
    }

    function sendCmdAction(act, extra) {
      const payload = Object.assign({ action: act }, extra);
      if (socket && socket.readyState === WebSocket.OPEN) {
        socket.send(JSON.stringify(payload));
        showToast('✔ ' + act.replace(/_/g, ' ').toUpperCase());
      } else {
        fetch('/api/command?token=' + token, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        }).then(() => showToast('✔ ' + act));
      }
    }

    connect();
  </script>
</body>
</html>
""";
    }

    public void Dispose()
    {
        Stop();
    }
}

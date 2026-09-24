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

public class NetworkAdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public NetworkInterfaceType InterfaceType { get; set; }
    public OperationalStatus Status { get; set; }
    public bool IsVirtual { get; set; }

    public string DisplayName => $"{Name} — {IpAddress} ({(IsVirtual ? "Virtual / " : "")}{InterfaceType})";
    public override string ToString() => DisplayName;
}

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
    private readonly NetworkService _networkService = new();
    private string _pairingToken;
    private int _port;
    private string _localIp;

    public string PairingToken => _pairingToken;
    public int Port => _port;
    public string LocalIp => _localIp;
    public string PairingUrl => $"http://{_localIp}:{_port}/?token={_pairingToken}";
    public bool IsRunning => _listener?.IsListening ?? false;
    public int ActiveClientsCount => _activeSockets.Count;

    public void RegenerateToken()
    {
        _pairingToken = GenerateSecureToken(8);
        try
        {
            AppSettingsService.Instance.SaveSettings(s => s.CompanionAuthToken = _pairingToken);
        }
        catch { }
    }

    public event Action<string>? OnCommandReceived;
    public event Action<string, JsonElement>? OnHudRemoteActionReceived;
    public event Action<int>? OnClientsCountChanged;
    public event Action<string>? OnNetworkChanged;

    public CompanionServerService(int port = DEFAULT_PORT)
    {
        _port = port;
        string? savedToken = null;
        try
        {
            savedToken = AppSettingsService.Instance.Settings.CompanionAuthToken;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(savedToken))
        {
            _pairingToken = GenerateSecureToken(8);
            try
            {
                AppSettingsService.Instance.SaveSettings(s => s.CompanionAuthToken = _pairingToken);
            }
            catch { }
        }
        else
        {
            _pairingToken = savedToken;
        }

        _localIp = ResolvePrimaryLocalIp();
    }

    private static string GenerateSecureToken(int length)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static List<NetworkAdapterInfo> GetAvailableNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var iface in interfaces)
            {
                var props = iface.GetIPProperties();
                var ipv4Addrs = props.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(u.Address) &&
                                u.Address.ToString() != "0.0.0.0");

                bool isVirtual = iface.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                 iface.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                                 iface.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                                 iface.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                                 iface.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                                 iface.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                                 iface.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase);

                foreach (var addr in ipv4Addrs)
                {
                    string ip = addr.Address.ToString();
                    list.Add(new NetworkAdapterInfo
                    {
                        Id = iface.Id,
                        Name = iface.Name,
                        Description = iface.Description,
                        IpAddress = ip,
                        InterfaceType = iface.NetworkInterfaceType,
                        Status = iface.OperationalStatus,
                        IsVirtual = isVirtual
                    });
                }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            list.Add(new NetworkAdapterInfo
            {
                Id = "loopback",
                Name = "Localhost",
                Description = "Loopback Adapter",
                IpAddress = "127.0.0.1",
                InterfaceType = NetworkInterfaceType.Loopback,
                Status = OperationalStatus.Up,
                IsVirtual = false
            });
        }

        // Order: physical non-APIPA (169.254) first (Ethernet, Wi-Fi), then virtual, then APIPA
        return list.OrderBy(a => a.IpAddress.StartsWith("169.254") ? 3 : (a.IsVirtual ? 2 : 1))
                   .ThenBy(a => a.InterfaceType == NetworkInterfaceType.Ethernet || a.InterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                   .ThenBy(a => a.Name)
                   .ToList();
    }

    public static string ResolvePrimaryLocalIp()
    {
        try
        {
            var adapters = GetAvailableNetworkAdapters();

            // Check if user has a previously configured preferred IP that is currently active
            string? preferredIp = AppSettingsService.Instance.Settings.PreferredCompanionIp;
            if (!string.IsNullOrWhiteSpace(preferredIp))
            {
                var match = adapters.FirstOrDefault(a => a.IpAddress.Equals(preferredIp, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match.IpAddress;
                }
            }

            // Otherwise pick the highest-priority non-virtual, non-APIPA adapter
            var best = adapters.FirstOrDefault(a => !a.IsVirtual && !a.IpAddress.StartsWith("169.254") && a.IpAddress != "127.0.0.1")
                      ?? adapters.FirstOrDefault(a => a.IpAddress != "127.0.0.1")
                      ?? adapters.FirstOrDefault();

            if (best != null)
            {
                return best.IpAddress;
            }
        }
        catch { }

        return "127.0.0.1";
    }

    public void SetSelectedIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out _))
            return;

        _localIp = ipAddress;
        try
        {
            AppSettingsService.Instance.SaveSettings(s => s.PreferredCompanionIp = ipAddress);
        }
        catch { }

        OnNetworkChanged?.Invoke(_localIp);
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

        // Try preferred/current port first, followed by fallbacks 8766-8770
        int[] candidatePorts = new[] { _port, 8766, 8767, 8768, 8769, 8770 }
            .Distinct()
            .ToArray();

        Exception? lastEx = null;
        bool started = false;

        foreach (int testPort in candidatePorts)
        {
            try
            {
                Stop();
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();

                // 1. Try wildcard prefix
                try
                {
                    _listener.Prefixes.Add($"http://*:{testPort}/");
                    _listener.Start();
                    _port = testPort;
                    started = true;
                    break;
                }
                catch (HttpListenerException)
                {
                    // 2. Wildcard failed (e.g. prefix conflict or permission), try localhost + local IP prefixes
                    try
                    {
                        _listener.Close();
                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://localhost:{testPort}/");
                        _listener.Prefixes.Add($"http://127.0.0.1:{testPort}/");
                        _listener.Prefixes.Add($"http://{_localIp}:{testPort}/");

                        foreach (var adapter in GetAvailableNetworkAdapters())
                        {
                            try
                            {
                                string prefix = $"http://{adapter.IpAddress}:{testPort}/";
                                if (!_listener.Prefixes.Contains(prefix))
                                {
                                    _listener.Prefixes.Add(prefix);
                                }
                            }
                            catch { }
                        }

                        _listener.Start();
                        _port = testPort;
                        started = true;
                        break;
                    }
                    catch (Exception ipEx)
                    {
                        lastEx = ipEx;
                        try { _listener.Close(); } catch { }
                        _listener = null;

                        // 3. Fallback to loopback only (allowed for standard non-elevated user)
                        try
                        {
                            _listener = new HttpListener();
                            _listener.Prefixes.Add($"http://localhost:{testPort}/");
                            _listener.Prefixes.Add($"http://127.0.0.1:{testPort}/");
                            _listener.Start();
                            _port = testPort;
                            started = true;
                            break;
                        }
                        catch (Exception loopEx)
                        {
                            lastEx = loopEx;
                            try { _listener?.Close(); } catch { }
                            _listener = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Stop();
            }
        }

        if (started && _listener != null && _cts != null)
        {
            Task.Run(() => ListenLoopAsync(_cts.Token));
            Task.Run(() => TelemetryBroadcastLoopAsync(_cts.Token));
            OnNetworkChanged?.Invoke(_localIp);
        }
        else
        {
            Stop();
            throw new InvalidOperationException($"No se pudo iniciar el servidor OmniCompanion en ningún puerto (8766-8770): {lastEx?.Message}", lastEx);
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

                // Decoupled telemetry observation: TelemetryHub samples continuously at 1 Hz in its own background loop.
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
            var net = _networkService.GetNetworkThroughput();
            var awake = AwakeService.Instance.CurrentState;
            var hibStatus = LauncherHibernatorService.Instance.GetStatus();
            var settings = AppSettingsService.Instance.Settings;
            var thermalSnap = ThermalSensorService.Instance.GetSnapshot();
            double? cpuTemp = hub.CpuTemperatureCelsius ?? thermalSnap.CpuPackageTemp;

            var payload = new
            {
                hostname = Environment.MachineName,
                cpuLoad = hub.CpuLoadPercent,
                cpuTemp = cpuTemp,
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
  <meta name="theme-color" content="#0B0F19" />
  <meta name="apple-mobile-web-app-capable" content="yes" />
  <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent" />
  <title>OmniCompanion 2.0 — WinUI 3 Mobile</title>
  <style>
    :root {
      --bg: #0B0E17;
      --card-acrylic: rgba(22, 30, 49, 0.76);
      --card-border: rgba(255, 255, 255, 0.09);
      --card-highlight: rgba(255, 255, 255, 0.12);
      --accent: #38BDF8;
      --accent-grad: linear-gradient(135deg, #38BDF8, #0284C7);
      --emerald: #10B981;
      --emerald-grad: linear-gradient(135deg, #10B981, #059669);
      --purple: #818CF8;
      --amber: #F59E0B;
      --crimson: #EF4444;
      --text: #F8FAFC;
      --text-muted: #94A3B8;
      --text-dim: #64748B;
    }
    * {
      box-sizing: border-box;
      margin: 0;
      padding: 0;
      user-select: none;
      -webkit-tap-highlight-color: transparent;
    }
    body {
      font-family: 'Segoe UI Variable Display', 'Segoe UI Variable Text', -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
      background: var(--bg);
      background-image: 
        radial-gradient(circle at 10% 0%, rgba(30, 58, 138, 0.32) 0%, transparent 45%),
        radial-gradient(circle at 90% 85%, rgba(16, 185, 129, 0.2) 0%, transparent 45%),
        radial-gradient(circle at 50% 50%, rgba(15, 23, 42, 0.6) 0%, transparent 100%);
      color: var(--text);
      padding: 16px 16px 90px 16px;
      min-height: 100vh;
      overflow-x: hidden;
    }

    /* WinUI Header */
    .app-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding-bottom: 14px;
      margin-bottom: 16px;
      border-bottom: 1px solid var(--card-border);
    }
    .brand-group {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .brand-icon {
      width: 32px;
      height: 32px;
      border-radius: 10px;
      background: var(--accent-grad);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 17px;
      box-shadow: 0 4px 14px rgba(56, 189, 248, 0.4);
    }
    .brand-title {
      font-size: 19px;
      font-weight: 800;
      letter-spacing: -0.02em;
      background: linear-gradient(135deg, #FFFFFF 40%, #93C5FD 100%);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
    }
    .host-badge {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      padding: 6px 14px;
      background: rgba(15, 23, 42, 0.85);
      border: 1px solid var(--card-border);
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      color: #93C5FD;
      box-shadow: 0 4px 12px rgba(0,0,0,0.3);
      backdrop-filter: blur(12px);
    }
    .status-pulse-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--emerald);
      box-shadow: 0 0 10px var(--emerald);
      animation: pulseGlow 2s infinite ease-in-out;
    }
    @keyframes pulseGlow {
      0%, 100% { transform: scale(1); opacity: 1; }
      50% { transform: scale(1.3); opacity: 0.7; }
    }

    /* WinUI 3 Acrylic Card */
    .acrylic-card {
      background: var(--card-acrylic);
      border: 1px solid var(--card-border);
      border-radius: 20px;
      padding: 18px;
      backdrop-filter: blur(28px) saturate(190%);
      -webkit-backdrop-filter: blur(28px) saturate(190%);
      box-shadow: 0 12px 30px -8px rgba(0, 0, 0, 0.6), inset 0 1px 0 var(--card-highlight);
      position: relative;
      overflow: hidden;
      margin-bottom: 14px;
    }

    /* Fluent Segmented Tabs */
    .segmented-tabs {
      display: grid;
      grid-template-columns: 1fr 1fr 1fr;
      gap: 6px;
      background: rgba(15, 23, 42, 0.75);
      border: 1px solid var(--card-border);
      border-radius: 14px;
      padding: 5px;
      margin-bottom: 16px;
      backdrop-filter: blur(18px);
      box-shadow: 0 4px 16px rgba(0,0,0,0.4);
    }
    .seg-tab-btn {
      background: transparent;
      border: none;
      color: var(--text-muted);
      font-size: 12.5px;
      font-weight: 700;
      padding: 10px 4px;
      border-radius: 10px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      transition: all 0.2s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .seg-tab-btn.active {
      background: linear-gradient(135deg, rgba(56, 189, 248, 0.25), rgba(2, 132, 199, 0.25));
      border: 1px solid rgba(56, 189, 248, 0.5);
      color: #FFFFFF;
      box-shadow: 0 4px 16px rgba(56, 189, 248, 0.25);
    }

    .tab-pane { display: none; }
    .tab-pane.active { display: block; animation: paneReveal 0.2s cubic-bezier(0.16, 1, 0.3, 1); }
    @keyframes paneReveal {
      from { opacity: 0; transform: translateY(6px); }
      to { opacity: 1; transform: translateY(0); }
    }

    /* Hero Dual Radial Gauges */
    .hero-gauges {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-bottom: 14px;
    }
    .gauge-card {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 18px 12px;
    }
    .radial-box {
      position: relative;
      width: 110px;
      height: 110px;
      margin-bottom: 10px;
    }
    .radial-svg {
      width: 100%;
      height: 100%;
      transform: rotate(-90deg);
    }
    .radial-bg {
      stroke: rgba(255, 255, 255, 0.08);
      stroke-width: 9;
      fill: none;
    }
    .radial-bar {
      stroke-width: 9;
      stroke-linecap: round;
      fill: none;
      stroke-dasharray: 289;
      stroke-dashoffset: 289;
      transition: stroke-dashoffset 0.8s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .radial-content {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      text-align: center;
    }
    .radial-val {
      font-size: 23px;
      font-weight: 900;
      letter-spacing: -0.02em;
      color: #FFFFFF;
    }
    .radial-sub {
      font-size: 10px;
      font-weight: 700;
      color: var(--text-muted);
      text-transform: uppercase;
      margin-top: 1px;
    }
    .gauge-footer-label {
      font-size: 13px;
      font-weight: 800;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    /* Network Bar Indicators */
    .net-stats-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }
    .net-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .net-item-header {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      font-weight: 800;
      color: var(--text-muted);
      text-transform: uppercase;
    }
    .net-item-val {
      font-size: 20px;
      font-weight: 800;
      color: #38BDF8;
    }

    /* WinUI Tactile Action Tiles */
    .actions-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-bottom: 14px;
    }
    .action-tile {
      background: rgba(30, 41, 59, 0.6);
      border: 1px solid var(--card-border);
      border-radius: 18px;
      padding: 16px 14px;
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 8px;
      cursor: pointer;
      position: relative;
      overflow: hidden;
      box-shadow: 0 6px 18px rgba(0,0,0,0.35);
      transition: all 0.15s cubic-bezier(0.16, 1, 0.3, 1);
    }
    .action-tile:active {
      transform: scale(0.96);
    }
    .action-tile-icon {
      width: 38px;
      height: 38px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 19px;
    }
    .action-tile-title {
      font-size: 14px;
      font-weight: 800;
      color: #FFFFFF;
    }
    .action-tile-desc {
      font-size: 10.5px;
      color: var(--text-muted);
      line-height: 1.3;
    }

    /* Tile Variants */
    .tile-emerald {
      background: linear-gradient(135deg, rgba(16, 185, 129, 0.22), rgba(6, 78, 59, 0.3));
      border: 1px solid rgba(16, 185, 129, 0.45);
    }
    .tile-emerald .action-tile-icon {
      background: rgba(16, 185, 129, 0.3);
      color: #34D399;
      box-shadow: 0 0 16px rgba(16, 185, 129, 0.4);
    }
    .tile-blue {
      background: linear-gradient(135deg, rgba(56, 189, 248, 0.22), rgba(2, 132, 199, 0.28));
      border: 1px solid rgba(56, 189, 248, 0.45);
    }
    .tile-blue .action-tile-icon {
      background: rgba(56, 189, 248, 0.3);
      color: #38BDF8;
      box-shadow: 0 0 16px rgba(56, 189, 248, 0.4);
    }
    .tile-amber {
      background: linear-gradient(135deg, rgba(245, 158, 11, 0.22), rgba(180, 83, 9, 0.28));
      border: 1px solid rgba(245, 158, 11, 0.45);
    }
    .tile-amber .action-tile-icon {
      background: rgba(245, 158, 11, 0.3);
      color: #FBBF24;
      box-shadow: 0 0 16px rgba(245, 158, 11, 0.4);
    }
    .tile-purple {
      background: linear-gradient(135deg, rgba(129, 140, 248, 0.22), rgba(67, 56, 202, 0.28));
      border: 1px solid rgba(129, 140, 248, 0.45);
    }
    .tile-purple .action-tile-icon {
      background: rgba(129, 140, 248, 0.3);
      color: #A5B4FC;
      box-shadow: 0 0 16px rgba(129, 140, 248, 0.4);
    }

    /* Stutter Emergency Button */
    .stutter-emergency-btn {
      background: linear-gradient(135deg, #1E1B4B, #312E81);
      border: 1px solid #6366F1;
      border-radius: 18px;
      padding: 16px 20px;
      display: flex;
      align-items: center;
      gap: 16px;
      cursor: pointer;
      box-shadow: 0 8px 25px rgba(99, 102, 241, 0.3);
      transition: all 0.15s ease;
    }
    .stutter-emergency-btn:active {
      transform: scale(0.97);
    }
    .stutter-icon-box {
      width: 44px;
      height: 44px;
      border-radius: 12px;
      background: rgba(99, 102, 241, 0.3);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 22px;
      box-shadow: 0 0 15px rgba(99, 102, 241, 0.5);
    }

    /* WinUI Sliders & Controls */
    .control-label-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 13px;
      font-weight: 700;
      color: var(--text-muted);
      margin-bottom: 8px;
    }
    .control-val-tag {
      font-weight: 800;
      color: var(--accent);
      background: rgba(56, 189, 248, 0.15);
      padding: 2px 8px;
      border-radius: 6px;
      font-size: 12px;
    }
    .winui-slider {
      width: 100%;
      height: 6px;
      -webkit-appearance: none;
      background: rgba(255, 255, 255, 0.12);
      border-radius: 4px;
      outline: none;
      margin: 6px 0 16px 0;
    }
    .winui-slider::-webkit-slider-thumb {
      -webkit-appearance: none;
      width: 22px;
      height: 22px;
      border-radius: 50%;
      background: #FFFFFF;
      border: 3px solid #0284C7;
      box-shadow: 0 2px 10px rgba(0,0,0,0.5);
      cursor: pointer;
    }

    /* Style Selector 3-Pills */
    .style-pill-grid {
      display: grid;
      grid-template-columns: 1fr 1fr 1fr;
      gap: 8px;
      margin-bottom: 16px;
    }
    .style-pill {
      background: rgba(30, 41, 59, 0.6);
      border: 1px solid var(--card-border);
      border-radius: 12px;
      padding: 12px 6px;
      text-align: center;
      cursor: pointer;
      transition: all 0.2s ease;
    }
    .style-pill.active {
      background: rgba(56, 189, 248, 0.2);
      border-color: var(--accent);
      box-shadow: 0 4px 14px rgba(56, 189, 248, 0.25);
    }
    .style-pill-title {
      font-size: 13px;
      font-weight: 800;
      color: #FFFFFF;
    }
    .style-pill-sub {
      font-size: 10px;
      color: var(--text-muted);
      margin-top: 2px;
    }

    /* Interactive Monitor 4-Corner Pad */
    .monitor-wireframe {
      width: 100%;
      height: 140px;
      background: #060911;
      border: 2px solid rgba(255, 255, 255, 0.12);
      border-radius: 14px;
      position: relative;
      margin: 10px 0 16px 0;
      box-shadow: inset 0 0 20px rgba(0,0,0,0.8);
      display: grid;
      grid-template-columns: 1fr 1fr;
      grid-template-rows: 1fr 1fr;
      gap: 6px;
      padding: 6px;
    }
    .monitor-corner-btn {
      background: rgba(30, 41, 59, 0.4);
      border: 1px dashed rgba(255, 255, 255, 0.1);
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      font-weight: 800;
      color: var(--text-muted);
      cursor: pointer;
      transition: all 0.15s ease;
    }
    .monitor-corner-btn:active, .monitor-corner-btn.active {
      background: rgba(56, 189, 248, 0.25);
      border-color: var(--accent);
      color: #FFFFFF;
    }

    /* Floating Toast Notification */
    .fluent-toast {
      position: fixed;
      bottom: 24px;
      left: 50%;
      transform: translateX(-50%) translateY(50px);
      background: rgba(15, 23, 42, 0.95);
      border: 1px solid var(--accent);
      color: #FFFFFF;
      padding: 10px 22px;
      border-radius: 999px;
      font-size: 12.5px;
      font-weight: 700;
      opacity: 0;
      transition: all 0.25s cubic-bezier(0.16, 1, 0.3, 1);
      pointer-events: none;
      z-index: 1000;
      box-shadow: 0 10px 30px rgba(0,0,0,0.8);
      display: flex;
      align-items: center;
      gap: 8px;
      backdrop-filter: blur(16px);
    }
    .fluent-toast.visible {
      opacity: 1;
      transform: translateX(-50%) translateY(0);
    }
  </style>
</head>
<body>

  <!-- WinUI 3 App Header -->
  <div class="app-header">
    <div class="brand-group">
      <div class="brand-icon">⚡</div>
      <div class="brand-title">OmniWin</div>
    </div>
    <div class="host-badge">
      <div class="status-pulse-dot" id="dotStatus"></div>
      <span id="txtHost">Conectando...</span>
    </div>
  </div>

  <!-- Unauthorized Warning Banner -->
  <div id="authBanner" style="display: none; background: rgba(239, 68, 68, 0.15); border: 1px solid #EF4444; color: #FCA5A5; padding: 14px 16px; border-radius: 16px; margin-bottom: 14px; font-size: 12px; line-height: 1.4; text-align: center;">
    <strong>⚠️ Sesión no autorizada o token desactualizado</strong><br/>
    Escanea nuevamente el código QR desde la pantalla de la PC para vincular este dispositivo.
  </div>

  <!-- Segmented Pivot Navigation (Tabs) -->
  <div class="segmented-tabs">
    <button class="seg-tab-btn active" onclick="switchTab('telemetry', this)">📊 Telemetría</button>
    <button class="seg-tab-btn" onclick="switchTab('hud', this)">🎮 Control HUD</button>
    <button class="seg-tab-btn" onclick="switchTab('boost', this)">⚡ Boost PC</button>
  </div>

  <!-- ================= TAB 1: TELEMETRÍA ================= -->
  <div id="tab-telemetry" class="tab-pane active">
    <!-- Hero Dual Radial Gauges -->
    <div class="hero-gauges">
      <!-- RAM Gauge -->
      <div class="acrylic-card gauge-card">
        <div class="radial-box">
          <svg class="radial-svg" viewBox="0 0 110 110">
            <defs>
              <linearGradient id="ramGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#38BDF8" />
                <stop offset="100%" stop-color="#0284C7" />
              </linearGradient>
            </defs>
            <circle class="radial-bg" cx="55" cy="55" r="46" />
            <circle id="ringRam" class="radial-bar" cx="55" cy="55" r="46" stroke="url(#ramGrad)" />
          </svg>
          <div class="radial-content">
            <div class="radial-val" id="valRam">--%</div>
            <div class="radial-sub" id="subRam">-- GB</div>
          </div>
        </div>
        <div class="gauge-footer-label">Memoria RAM</div>
      </div>

      <!-- CPU Gauge -->
      <div class="acrylic-card gauge-card">
        <div class="radial-box">
          <svg class="radial-svg" viewBox="0 0 110 110">
            <defs>
              <linearGradient id="cpuGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#10B981" />
                <stop offset="100%" stop-color="#059669" />
              </linearGradient>
            </defs>
            <circle class="radial-bg" cx="55" cy="55" r="46" />
            <circle id="ringCpu" class="radial-bar" cx="55" cy="55" r="46" stroke="url(#cpuGrad)" />
          </svg>
          <div class="radial-content">
            <div class="radial-val" id="valCpuTemp">--°C</div>
            <div class="radial-sub" id="valCpuLoad">--% Carga</div>
          </div>
        </div>
        <div class="gauge-footer-label">Procesador</div>
      </div>
    </div>

    <!-- Network Throughput Card -->
    <div class="acrylic-card">
      <div style="font-size: 11px; font-weight: 800; color: var(--text-muted); text-transform: uppercase; margin-bottom: 12px; letter-spacing: 0.06em;">Throughput de Red en Vivo</div>
      <div class="net-stats-grid">
        <div class="net-item">
          <div class="net-item-header"><span>↓ Descarga</span></div>
          <div class="net-item-val" id="valNetDown">0 B/s</div>
        </div>
        <div class="net-item">
          <div class="net-item-header"><span>↑ Subida</span></div>
          <div class="net-item-val" id="valNetUp" style="color: #10B981;">0 B/s</div>
        </div>
      </div>
    </div>

    <!-- Optimization Status Card -->
    <div class="acrylic-card">
      <div style="display: flex; justify-content: space-between; align-items: center;">
        <div>
          <div style="font-size: 11px; font-weight: 800; color: var(--text-muted); text-transform: uppercase; margin-bottom: 4px;">Optimizador de Procesos</div>
          <div style="font-size: 14px; font-weight: 800; color: #FFFFFF;" id="txtHibStatus">Hibernador: Inactivo</div>
        </div>
        <div style="text-align: right;">
          <div style="font-size: 11px; color: var(--text-muted); margin-bottom: 4px;">Memoria Liberada</div>
          <div style="font-size: 14px; font-weight: 800; color: var(--emerald);" id="txtHibFreed">0 MB Libres</div>
        </div>
      </div>
    </div>
  </div>

  <!-- ================= TAB 2: CONTROL HUD ================= -->
  <div id="tab-hud" class="tab-pane">
    <div class="acrylic-card">
      <div style="font-size: 12px; font-weight: 800; color: var(--text-muted); text-transform: uppercase; margin-bottom: 12px; letter-spacing: 0.05em;">Estilo Visual del Overlay</div>
      <div class="style-pill-grid">
        <div id="btnStyle0" class="style-pill active" onclick="setHudStyle(0)">
          <div class="style-pill-title">Riva OSD</div>
          <div class="style-pill-sub">Solo Texto</div>
        </div>
        <div id="btnStyle1" class="style-pill" onclick="setHudStyle(1)">
          <div class="style-pill-title">Card</div>
          <div class="style-pill-sub">Glassmorphism</div>
        </div>
        <div id="btnStyle2" class="style-pill" onclick="setHudStyle(2)">
          <div class="style-pill-title">Barra</div>
          <div class="style-pill-sub">Ultra Compacta</div>
        </div>
      </div>

      <div class="control-label-row">
        <span>Opacidad de Fondo</span>
        <span class="control-val-tag" id="lblOpacity">0% (Puro)</span>
      </div>
      <input type="range" class="winui-slider" id="rngOpacity" min="0" max="1" step="0.05" value="0" oninput="changeOpacity(this.value)" />

      <div class="control-label-row">
        <span>Escala del HUD</span>
        <span class="control-val-tag" id="lblScale">100%</span>
      </div>
      <input type="range" class="winui-slider" id="rngScale" min="0.8" max="1.6" step="0.05" value="1.0" oninput="changeScale(this.value)" />
    </div>

    <!-- Monitor Corner Wireframe -->
    <div class="acrylic-card">
      <div style="font-size: 12px; font-weight: 800; color: var(--text-muted); text-transform: uppercase; margin-bottom: 6px;">Posición en Pantalla</div>
      <div class="monitor-wireframe">
        <div class="monitor-corner-btn" onclick="setCorner('TL')">↖ Sup. Izq</div>
        <div class="monitor-corner-btn" onclick="setCorner('TR')">↗ Sup. Der</div>
        <div class="monitor-corner-btn" onclick="setCorner('BL')">↙ Inf. Izq</div>
        <div class="monitor-corner-btn" onclick="setCorner('BR')">↘ Inf. Der</div>
      </div>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px;">
        <button class="action-tile" style="align-items: center; text-align: center; padding: 14px 8px;" onclick="sendCmd('toggle_hud_lock')">
          <span style="font-size: 20px;">🔒</span>
          <span style="font-size: 12px; font-weight: 800;">Alternar Bloqueo</span>
        </button>
        <button class="action-tile" style="align-items: center; text-align: center; padding: 14px 8px;" onclick="sendCmd('toggle_hud_visibility')">
          <span style="font-size: 20px;">👁️</span>
          <span style="font-size: 12px; font-weight: 800;">Mostrar / Ocultar</span>
        </button>
      </div>
    </div>
  </div>

  <!-- ================= TAB 3: BOOST PC ================= -->
  <div id="tab-boost" class="tab-pane">
    <div class="actions-grid">
      <div class="action-tile tile-emerald" onclick="sendCmd('purge_ram')">
        <div class="action-tile-icon">⚡</div>
        <div class="action-tile-title">Liberar RAM</div>
        <div class="action-tile-desc">Purga de Working Sets en 12ms</div>
      </div>

      <div class="action-tile tile-blue" onclick="sendCmd('hibernate_launchers')">
        <div class="action-tile-icon">🧊</div>
        <div class="action-tile-title">Hibernar Apps</div>
        <div class="action-tile-desc">Suspende Steam / Epic / Discord</div>
      </div>

      <div class="action-tile tile-amber" onclick="sendCmd('wake_launchers')">
        <div class="action-tile-icon">🌟</div>
        <div class="action-tile-title">Restaurar Apps</div>
        <div class="action-tile-desc">Reanuda launchers en segundo plano</div>
      </div>

      <div class="action-tile tile-purple" onclick="sendCmd('toggle_awake')">
        <div class="action-tile-icon">☕</div>
        <div class="action-tile-title" id="lblAwake">Modo Cafeína</div>
        <div class="action-tile-desc">Evita que la PC suspenda la sesión</div>
      </div>
    </div>

    <!-- Emergency Stutter Diagnostic Button -->
    <div class="stutter-emergency-btn" onclick="triggerStutter()">
      <div class="stutter-icon-box">⚠️</div>
      <div style="flex: 1;">
        <div style="font-size: 15px; font-weight: 800; color: #FFFFFF; letter-spacing: -0.01em;">¡Sentí un Stutter!</div>
        <div style="font-size: 11px; color: #A5B4FC; margin-top: 2px;">Diagnóstico de tirones de los últimos 15 segundos</div>
      </div>
    </div>

    <!-- Diagnostic Details Drawer -->
    <div id="stutterBox" style="display: none; margin-top: 14px;" class="acrylic-card">
      <div style="font-size: 13px; font-weight: 800; color: #F59E0B; margin-bottom: 6px;" id="stutterCause">Diagnóstico de Stutter</div>
      <div style="font-size: 12px; color: var(--text); line-height: 1.5;" id="stutterDetails">...</div>
    </div>
  </div>

  <!-- Acrylic Floating Toast -->
  <div id="toast" class="fluent-toast">
    <span>✔</span>
    <span id="toastMsg">Comando ejecutado</span>
  </div>

  <script>
    const urlParams = new URLSearchParams(window.location.search);
    const token = urlParams.get('token') || '';
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${proto}//${window.location.host}/ws?token=${token}`;
    let socket;
    let isUnauthorized = false;

    function vibrate() {
      if (navigator.vibrate) {
        try { navigator.vibrate(15); } catch {}
      }
    }

    function switchTab(tabId, btn) {
      vibrate();
      document.querySelectorAll('.seg-tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-pane').forEach(c => c.classList.remove('active'));
      btn.classList.add('active');
      document.getElementById('tab-' + tabId).classList.add('active');
    }

    function formatSpeed(bytes) {
      if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB/s';
      if (bytes >= 1024) return (bytes / 1024).toFixed(0) + ' KB/s';
      return bytes.toFixed(0) + ' B/s';
    }

    function showToast(msg) {
      vibrate();
      const t = document.getElementById('toast');
      document.getElementById('toastMsg').innerText = msg;
      t.classList.add('visible');
      setTimeout(() => t.classList.remove('visible'), 1600);
    }

    function updateRadialRing(ringId, pct) {
      const ring = document.getElementById(ringId);
      if (!ring) return;
      const circumference = 289; // 2 * pi * 46
      const clamped = Math.max(0, Math.min(100, pct));
      const offset = circumference - (clamped / 100) * circumference;
      ring.style.strokeDashoffset = offset;
    }

    function setHudStyle(styleIdx) {
      vibrate();
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
      vibrate();
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

    async function checkAuth() {
      if (!token) {
        showAuthError();
        return false;
      }
      try {
        const res = await fetch('/api/status?token=' + token);
        if (res.status === 401) {
          showAuthError();
          return false;
        }
        hideAuthError();
        return true;
      } catch {
        return true;
      }
    }

    function showAuthError() {
      isUnauthorized = true;
      document.getElementById('authBanner').style.display = 'block';
      const hostEl = document.getElementById('txtHost');
      if (hostEl) {
        hostEl.innerText = 'Token Inválido';
        hostEl.style.color = '#EF4444';
      }
      const dot = document.getElementById('dotStatus');
      if (dot) dot.style.background = '#EF4444';
    }

    function hideAuthError() {
      isUnauthorized = false;
      document.getElementById('authBanner').style.display = 'none';
      const hostEl = document.getElementById('txtHost');
      if (hostEl) {
        hostEl.style.color = '';
      }
      const dot = document.getElementById('dotStatus');
      if (dot) dot.style.background = '';
    }

    async function connect() {
      const ok = await checkAuth();
      if (!ok) return;

      socket = new WebSocket(wsUrl);
      socket.onopen = () => {
        hideAuthError();
        document.getElementById('txtHost').innerText = 'En vivo';
      };
      socket.onmessage = (e) => {
        try {
          const d = JSON.parse(e.data);
          if (d.hostname) document.getElementById('txtHost').innerText = d.hostname;
          if (d.ramUsagePercent !== undefined) {
            document.getElementById('valRam').innerText = d.ramUsagePercent.toFixed(0) + '%';
            document.getElementById('subRam').innerText = `${d.ramUsedGb.toFixed(1)} / ${d.ramTotalGb.toFixed(1)} GB`;
            updateRadialRing('ringRam', d.ramUsagePercent);
          }
          if (d.cpuTemp !== undefined && d.cpuTemp !== null) {
            document.getElementById('valCpuTemp').innerText = d.cpuTemp.toFixed(0) + '°C';
          }
          if (d.cpuLoad !== undefined) {
            document.getElementById('valCpuLoad').innerText = d.cpuLoad.toFixed(0) + '% Carga';
            updateRadialRing('ringCpu', d.cpuLoad);
          }
          if (d.netDownloadSpeed !== undefined) {
            document.getElementById('valNetDown').innerText = formatSpeed(d.netDownloadSpeed);
          }
          if (d.netUploadSpeed !== undefined) {
            document.getElementById('valNetUp').innerText = formatSpeed(d.netUploadSpeed);
          }
          if (d.isAwakeActive !== undefined) {
            document.getElementById('lblAwake').innerText = d.isAwakeActive ? 'Cafeína: ON' : 'Cafeína: OFF';
          }
          if (d.isHibernating !== undefined) {
            document.getElementById('txtHibStatus').innerText = d.isHibernating ? `Hibernando ${d.hibernatedCount} apps` : 'Hibernador: Inactivo';
            document.getElementById('txtHibFreed').innerText = d.freedMemoryMb ? `~${Math.round(d.freedMemoryMb)} MB Libres` : '0 MB Libres';
          }
        } catch {}
      };
      socket.onclose = () => {
        if (!isUnauthorized) {
          document.getElementById('txtHost').innerText = 'Reconectando...';
          setTimeout(connect, 2000);
        }
      };
    }

    function sendCmd(act) {
      sendCmdAction(act, {});
    }

    function sendCmdAction(act, extra) {
      vibrate();
      if (isUnauthorized) {
        showToast('Token no autorizado');
        return;
      }
      const payload = Object.assign({ action: act }, extra);
      if (socket && socket.readyState === WebSocket.OPEN) {
        socket.send(JSON.stringify(payload));
        showToast(act.replace(/_/g, ' ').toUpperCase());
      } else {
        fetch('/api/command?token=' + token, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        }).then(res => {
          if (res.status === 401) {
            showAuthError();
            showToast('401: Token inválido');
          } else {
            showToast(act.replace(/_/g, ' ').toUpperCase());
          }
        }).catch(() => showToast('⚠️ Sin conexión con PC'));
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

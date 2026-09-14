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
                    mem.PurgeMemory(true, true);
                    break;

                case "toggle_awake":
                    var awake = AwakeService.Instance;
                    if (awake.CurrentState.IsActive)
                        awake.Deactivate();
                    else
                        awake.Activate(AwakeMode.KeepAwakeIndefinite, duration: null, keepDisplayOn: true);
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
            var mem = new MemoryService().GetMemoryStats();
            var net = new NetworkService().GetNetworkThroughput();
            var thermal = ThermalSensorService.Instance.GetSnapshot();
            var awake = AwakeService.Instance.CurrentState;

            var payload = new
            {
                hostname = Environment.MachineName,
                ramUsagePercent = mem.UsagePercentage,
                ramUsedGb = mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0),
                ramTotalGb = mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0),
                netDownloadSpeed = net.downloadBytesPerSec,
                netUploadSpeed = net.uploadBytesPerSec,
                cpuTemp = thermal.CpuPackageTemp,
                gpuTemp = thermal.GpuCoreTemp,
                isAwakeActive = awake.IsActive,
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
  <title>OmniCompanion — Control Móvil</title>
  <style>
    :root {
      --bg: #07090E; --card: #0D1322; --border: #162035; --accent: #0284C7;
      --emerald: #10B981; --crimson: #EF4444; --text: #F8FAFC; --muted: #94A3B8;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; user-select: none; -webkit-tap-highlight-color: transparent; }
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI Variable', Roboto, sans-serif; background: var(--bg); color: var(--text); padding: 20px 16px; min-height: 100vh; }
    .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: 14px; margin-bottom: 18px; }
    .brand { display: flex; align-items: center; gap: 8px; font-size: 19px; font-weight: 800; }
    .badge { background: #0A2644; color: var(--accent); font-size: 10px; font-weight: bold; padding: 2px 7px; border-radius: 4px; border: 1px solid #144272; }
    .status-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--emerald); display: inline-block; margin-right: 5px; box-shadow: 0 0 8px var(--emerald); }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 20px; }
    .card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 14px; display: flex; flex-direction: column; justify-content: space-between; }
    .card-title { font-size: 11px; font-weight: 700; color: var(--muted); text-transform: uppercase; margin-bottom: 4px; }
    .card-value { font-size: 24px; font-weight: 800; color: var(--text); }
    .card-sub { font-size: 11px; color: var(--muted); margin-top: 2px; }
    .actions-title { font-size: 13px; font-weight: 700; color: var(--muted); text-transform: uppercase; margin-bottom: 12px; letter-spacing: 0.5px; }
    .btn-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .btn { background: #111A2E; border: 1px solid var(--border); border-radius: 12px; padding: 16px 12px; color: var(--text); font-size: 13px; font-weight: 700; text-align: center; cursor: pointer; transition: all 0.15s ease; display: flex; flex-direction: column; align-items: center; gap: 6px; }
    .btn:active { transform: scale(0.96); background: var(--accent); border-color: var(--accent); }
    .btn-icon { font-size: 22px; }
    .btn-primary { background: #064E3B; border-color: #047857; color: #34D399; }
    .toast { position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); background: #0F172A; border: 1px solid var(--accent); color: var(--text); padding: 10px 18px; border-radius: 30px; font-size: 12px; font-weight: bold; opacity: 0; transition: opacity 0.2s ease; pointer-events: none; z-index: 100; box-shadow: 0 4px 14px rgba(0,0,0,0.5); }
    .toast.show { opacity: 1; }
  </style>
</head>
<body>
  <div class="header">
    <div class="brand">⚡ OmniWin <span class="badge">COMPANION</span></div>
    <div style="font-size: 12px; color: var(--muted);"><span class="status-dot"></span><span id="txtHost">Conectando...</span></div>
  </div>

  <div class="grid">
    <div class="card">
      <div class="card-title">Memoria RAM</div>
      <div class="card-value" id="valRam">--%</div>
      <div class="card-sub" id="subRam">-- / -- GB</div>
    </div>
    <div class="card">
      <div class="card-title">Temperatura CPU</div>
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

  <div class="actions-title">Controles Remotos Rápidos</div>
  <div class="btn-grid">
    <div class="btn btn-primary" onclick="sendCmd('purge_ram')">
      <span class="btn-icon">⚡</span>
      <span>Liberar RAM</span>
    </div>
    <div class="btn" onclick="sendCmd('toggle_awake')">
      <span class="btn-icon">☕</span>
      <span id="lblAwake">Modo Cafeína</span>
    </div>
  </div>

  <div id="toast" class="toast">Comando enviado</div>

  <script>
    const urlParams = new URLSearchParams(window.location.search);
    const token = urlParams.get('token') || '';
    const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${proto}//${window.location.host}/ws?token=${token}`;
    let socket;

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
        } catch {}
      };
      socket.onclose = () => {
        document.getElementById('txtHost').innerText = 'Desconectado (Reintentando...)';
        setTimeout(connect, 2000);
      };
    }

    function sendCmd(act) {
      if (socket && socket.readyState === WebSocket.OPEN) {
        socket.send(JSON.stringify({ action: act }));
        showToast('✔ ' + act.replace('_', ' ').toUpperCase());
      } else {
        fetch('/api/command?token=' + token, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ action: act })
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

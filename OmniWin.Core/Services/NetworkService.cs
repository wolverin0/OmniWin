using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class PingTargetResult
{
    public string Host { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public bool Success { get; set; }
    public long RoundtripTimeMs { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class NetworkInterfaceItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string OperationalStatus { get; set; } = string.Empty;
    public long SpeedMbps { get; set; }
    public List<string> Ipv4Addresses { get; set; } = new();
    public string Gateway { get; set; } = string.Empty;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

public class NetworkDiagnosticReport
{
    public List<NetworkInterfaceItem> ActiveInterfaces { get; set; } = new();
    public List<PingTargetResult> PingTests { get; set; } = new();
    public double DnsResolutionTimeMs { get; set; }
    public bool HasInternetAccess { get; set; }
    public int ActiveTcpConnections { get; set; }
}

public class NetworkService
{
    public async Task<NetworkDiagnosticReport> RunDiagnosticsAsync(string? customTarget = null)
    {
        var report = new NetworkDiagnosticReport();

        // 1. Interfaces
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up && 
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var ipProps = ni.GetIPProperties();
                    var ipv4s = ipProps.UnicastAddresses
                        .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(u => u.Address.ToString())
                        .ToList();

                    if (ipv4s.Count == 0) continue;

                    string gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address?.ToString() ?? "N/D";
                    var stats = ni.GetIPv4Statistics();

                    report.ActiveInterfaces.Add(new NetworkInterfaceItem
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        Type = ni.NetworkInterfaceType.ToString(),
                        OperationalStatus = ni.OperationalStatus.ToString(),
                        SpeedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0,
                        Ipv4Addresses = ipv4s,
                        Gateway = gateway,
                        BytesSent = stats.BytesSent,
                        BytesReceived = stats.BytesReceived
                    });
                }
            }
        }
        catch { }

        // 2. DNS check
        try
        {
            var sw = Stopwatch.StartNew();
            await Dns.GetHostAddressesAsync("one.one.one.one");
            sw.Stop();
            report.DnsResolutionTimeMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        }
        catch
        {
            report.DnsResolutionTimeMs = -1;
        }

        // 3. Ping tests
        var targets = new List<string> { "1.1.1.1", "8.8.8.8" };
        if (!string.IsNullOrEmpty(customTarget) && !targets.Contains(customTarget))
        {
            targets.Insert(0, customTarget);
        }

        using var ping = new Ping();
        foreach (var t in targets)
        {
            try
            {
                var reply = await ping.SendPingAsync(t, 1200);
                bool ok = reply.Status == IPStatus.Success;
                if (ok) report.HasInternetAccess = true;

                report.PingTests.Add(new PingTargetResult
                {
                    Host = t,
                    IpAddress = reply.Address?.ToString() ?? t,
                    Success = ok,
                    RoundtripTimeMs = ok ? reply.RoundtripTime : -1,
                    Status = reply.Status.ToString()
                });
            }
            catch (Exception ex)
            {
                report.PingTests.Add(new PingTargetResult
                {
                    Host = t,
                    Success = false,
                    RoundtripTimeMs = -1,
                    Status = ex.Message
                });
            }
        }

        // 4. TCP Connections Count
        try
        {
            var tcpProps = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConns = tcpProps.GetActiveTcpConnections();
            report.ActiveTcpConnections = tcpConns.Length;
        }
        catch { }

        return report;
    }

    public async Task<NetworkHealResult> HealNetworkAsync()
    {
        var steps = new List<string>();
        bool allOk = true;

        // 1. Flush DNS
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                await p.WaitForExitAsync();
                steps.Add("✔ Caché de resolución DNS purgada con éxito (/flushdns)");
            }
        }
        catch (Exception ex)
        {
            allOk = false;
            steps.Add($"✖ Fallo al purgar DNS: {ex.Message}");
        }

        // 2. Reset Winsock
        try
        {
            var psi = new ProcessStartInfo("netsh", "winsock reset")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                await p.WaitForExitAsync();
                steps.Add("✔ Catálogo Winsock reiniciado a valores limpios (netsh winsock reset)");
            }
        }
        catch (Exception ex)
        {
            allOk = false;
            steps.Add($"✖ Fallo en reinicio Winsock: {ex.Message}");
        }

        // 3. Clear ARP Cache
        try
        {
            var psi = new ProcessStartInfo("arp", "-d *")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                await p.WaitForExitAsync();
                steps.Add("✔ Tabla de enrutamiento ARP limpiada");
            }
        }
        catch (Exception ex)
        {
            steps.Add($"⚠ No se pudo limpiar ARP: {ex.Message}");
        }

        string summary = allOk 
            ? "Pila de red reparada: DNS purgado, Winsock restablecido y ARP limpio." 
            : "Reparación de red completada con advertencias.";

        return new NetworkHealResult(allOk, summary, steps);
    }

    private long _lastBytesRecv;
    private long _lastBytesSent;
    private DateTime _lastBandwidthTime = DateTime.UtcNow;

    public (double downloadBytesPerSec, double uploadBytesPerSec) GetNetworkThroughput()
    {
        long currentRecv = 0;
        long currentSent = 0;

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = ni.GetIPv4Statistics();
                    currentRecv += stats.BytesReceived;
                    currentSent += stats.BytesSent;
                }
            }
        }
        catch { }

        DateTime now = DateTime.UtcNow;
        double seconds = (now - _lastBandwidthTime).TotalSeconds;
        if (seconds <= 0.1) seconds = 1.0;

        double downRate = _lastBytesRecv > 0 && currentRecv >= _lastBytesRecv 
            ? (currentRecv - _lastBytesRecv) / seconds 
            : 0;
        double upRate = _lastBytesSent > 0 && currentSent >= _lastBytesSent 
            ? (currentSent - _lastBytesSent) / seconds 
            : 0;

        _lastBytesRecv = currentRecv;
        _lastBytesSent = currentSent;
        _lastBandwidthTime = now;

        return (downRate, upRate);
    }
}

public record NetworkHealResult(
    bool Success,
    string Summary,
    List<string> StepsExecuted
);

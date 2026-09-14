using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class DnsProviderItem
{
    public string Name { get; set; } = string.Empty;
    public string PrimaryIp { get; set; } = string.Empty;
    public string SecondaryIp { get; set; } = string.Empty;
    public string PrimaryIpv6 { get; set; } = string.Empty;
    public string SecondaryIpv6 { get; set; } = string.Empty;
    public string DohUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long LatencyMs { get; set; } = -1;
    public string LatencyText => LatencyMs < 0 ? "-- ms" : $"{LatencyMs} ms";
    public bool IsActive { get; set; } = false;
}

public class DnsSecurityService
{
    private static readonly Lazy<DnsSecurityService> _instance = new(() => new DnsSecurityService());
    public static DnsSecurityService Instance => _instance.Value;

    private readonly List<DnsProviderItem> _providers = new()
    {
        new DnsProviderItem
        {
            Name = "Cloudflare (1.1.1.1)",
            PrimaryIp = "1.1.1.1",
            SecondaryIp = "1.0.0.1",
            PrimaryIpv6 = "2606:4700:4700::1111",
            SecondaryIpv6 = "2606:4700:4700::1001",
            DohUrl = "https://cloudflare-dns.com/dns-query",
            Description = "Enfocado en velocidad máxima y cero registro de actividad."
        },
        new DnsProviderItem
        {
            Name = "Quad9 (9.9.9.9)",
            PrimaryIp = "9.9.9.9",
            SecondaryIp = "149.112.112.112",
            PrimaryIpv6 = "2620:fe::fe",
            SecondaryIpv6 = "2620:fe::9",
            DohUrl = "https://dns.quad9.net/dns-query",
            Description = "Seguridad suiza y bloqueo automático de dominios maliciosos / phishing."
        },
        new DnsProviderItem
        {
            Name = "AdGuard DNS",
            PrimaryIp = "94.140.14.14",
            SecondaryIp = "94.140.15.15",
            PrimaryIpv6 = "2a10:50c0::ad1:ff",
            SecondaryIpv6 = "2a10:50c0::ad2:ff",
            DohUrl = "https://dns.adguard.com/dns-query",
            Description = "Bloqueo directo de anuncios, telemetría y rastreadores web a nivel de DNS."
        },
        new DnsProviderItem
        {
            Name = "Google Public DNS",
            PrimaryIp = "8.8.8.8",
            SecondaryIp = "8.8.4.4",
            PrimaryIpv6 = "2001:4860:4860::8888",
            SecondaryIpv6 = "2001:4860:4860::8844",
            DohUrl = "https://dns.google/dns-query",
            Description = "Infraestructura global ultra-confiable y alta disponibilidad."
        }
    };

    public List<DnsProviderItem> GetProviders() => _providers.ToList();

    public async Task<List<DnsProviderItem>> BenchmarkAllProvidersAsync()
    {
        var tasks = _providers.Select(async p =>
        {
            p.LatencyMs = await MeasureLatencyAsync(p.PrimaryIp);
            return p;
        });

        var results = await Task.WhenAll(tasks);
        return results.OrderBy(r => r.LatencyMs < 0 ? 9999 : r.LatencyMs).ToList();
    }

    private static async Task<long> MeasureLatencyAsync(string ip)
    {
        try
        {
            using var tcp = new TcpClient();
            var sw = Stopwatch.StartNew();
            var connectTask = tcp.ConnectAsync(IPAddress.Parse(ip), 53);
            if (await Task.WhenAny(connectTask, Task.Delay(1200)) == connectTask)
            {
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }
        }
        catch { }

        return -1;
    }

    public bool ApplyDnsConfiguration(DnsProviderItem provider)
    {
        try
        {
            var activeIfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase));

            bool anySuccess = false;

            foreach (var iface in activeIfaces)
            {
                string ifaceName = iface.Name;

                // 1. Primary IPv4 DNS
                var psi1 = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface ipv4 set dns name=\"{ifaceName}\" static {provider.PrimaryIp} primary",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p1 = Process.Start(psi1);
                p1?.WaitForExit(3000);

                // 2. Secondary IPv4 DNS
                if (!string.IsNullOrEmpty(provider.SecondaryIp))
                {
                    var psi2 = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = $"interface ipv4 add dns name=\"{ifaceName}\" {provider.SecondaryIp} index=2",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p2 = Process.Start(psi2);
                    p2?.WaitForExit(3000);
                }

                // 3. Primary IPv6 DNS (prevents IPv6 DNS leaks)
                if (!string.IsNullOrEmpty(provider.PrimaryIpv6))
                {
                    var psi6a = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = $"interface ipv6 set dns name=\"{ifaceName}\" static {provider.PrimaryIpv6} primary",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p6a = Process.Start(psi6a);
                    p6a?.WaitForExit(3000);
                }

                // 4. Secondary IPv6 DNS
                if (!string.IsNullOrEmpty(provider.SecondaryIpv6))
                {
                    var psi6b = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = $"interface ipv6 add dns name=\"{ifaceName}\" {provider.SecondaryIpv6} index=2",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p6b = Process.Start(psi6b);
                    p6b?.WaitForExit(3000);
                }

                if (p1?.ExitCode == 0) anySuccess = true;
            }

            // Flush DNS Cache
            DnsFlush();

            return anySuccess;
        }
        catch
        {
            return false;
        }
    }

    public static void DnsFlush()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(2000);
        }
        catch { }
    }

    public async Task<(bool Success, int BlockedDomainsCount, string Message)> SyncCommunityHostsBlocklistAsync()
    {
        string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        if (!File.Exists(hostsPath))
        {
            return (false, 0, "No se encontró el archivo HOSTS del sistema.");
        }

        try
        {
            // Download StevenBlack curated unified hosts list
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/fakenews-gambling/hosts";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return (false, 0, $"Error HTTP al descargar lista: {(int)response.StatusCode}");
            }

            string rawHosts = await response.Content.ReadAsStringAsync();
            var validLines = rawHosts.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("0.0.0.0 ") && !l.EndsWith("0.0.0.0"))
                .Take(2000) // Safe limit to prevent Windows Dnscache svchost CPU spikes
                .ToList();

            if (validLines.Count == 0)
            {
                return (false, 0, "La lista descargada no contiene reglas válidas.");
            }

            // Remove ReadOnly attribute if present
            var attr = File.GetAttributes(hostsPath);
            if ((attr & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(hostsPath, attr & ~FileAttributes.ReadOnly);
            }

            string existing = File.ReadAllText(hostsPath);
            const string markerStart = "# BEGIN OMNISUITE COMMUNITY BLOCKLIST";
            const string markerEnd = "# END OMNISUITE COMMUNITY BLOCKLIST";

            var sb = new StringBuilder();
            if (existing.Contains(markerStart) && existing.Contains(markerEnd))
            {
                int startIndex = existing.IndexOf(markerStart, StringComparison.Ordinal);
                int endIndex = existing.IndexOf(markerEnd, StringComparison.Ordinal) + markerEnd.Length;
                sb.Append(existing.Substring(0, startIndex));
                sb.Append(existing.Substring(endIndex));
            }
            else
            {
                sb.Append(existing);
            }

            sb.AppendLine();
            sb.AppendLine(markerStart);
            foreach (var line in validLines)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine(markerEnd);

            File.WriteAllText(hostsPath, sb.ToString(), Encoding.UTF8);
            DnsFlush();

            return (true, validLines.Count, $"Se aplicaron {validLines.Count:N0} dominios bloqueados en HOSTS (límite de seguridad para evitar sobrecarga del servicio Dnscache).");
        }
        catch (Exception ex)
        {
            return (false, 0, $"Error al actualizar archivo HOSTS: {ex.Message}");
        }
    }

    public Task<(bool Success, int BlockedDomainsCount, string Message)> SyncStevenBlackHostsAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Descargando reglas comunitarias unificadas de StevenBlack...");
        return SyncCommunityHostsBlocklistAsync();
    }

    public bool RestoreDefaultDns()
    {
        try
        {
            var activeIfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                            !n.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase));

            bool anySuccess = false;
            foreach (var iface in activeIfaces)
            {
                // Restore IPv4 DHCP
                var psi4 = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface ipv4 set dns name=\"{iface.Name}\" source=dhcp",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p4 = Process.Start(psi4);
                p4?.WaitForExit(3000);

                // Restore IPv6 DHCP
                var psi6 = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface ipv6 set dns name=\"{iface.Name}\" source=dhcp",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p6 = Process.Start(psi6);
                p6?.WaitForExit(3000);

                if (p4?.ExitCode == 0 || p6?.ExitCode == 0) anySuccess = true;
            }
            DnsFlush();
            return anySuccess;
        }
        catch { return false; }
    }

    public (bool IsApplied, int BlockedCount, DateTime? LastModified) GetHostsBlockerStatus()
    {
        try
        {
            string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (!File.Exists(hostsPath)) return (false, 0, null);

            string content = File.ReadAllText(hostsPath);
            bool isApplied = content.Contains("# BEGIN OMNISUITE COMMUNITY BLOCKLIST");
            int count = isApplied ? content.Split('\n').Count(l => l.Trim().StartsWith("0.0.0.0 ")) : 0;
            DateTime lastMod = File.GetLastWriteTime(hostsPath);
            return (isApplied, count, lastMod);
        }
        catch
        {
            return (false, 0, null);
        }
    }

    public bool RestoreOriginalHosts()
    {
        try
        {
            string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (!File.Exists(hostsPath)) return false;

            string content = File.ReadAllText(hostsPath);
            const string markerStart = "# BEGIN OMNISUITE COMMUNITY BLOCKLIST";
            const string markerEnd = "# END OMNISUITE COMMUNITY BLOCKLIST";

            if (content.Contains(markerStart) && content.Contains(markerEnd))
            {
                int startIndex = content.IndexOf(markerStart, StringComparison.Ordinal);
                int endIndex = content.IndexOf(markerEnd, StringComparison.Ordinal) + markerEnd.Length;
                string clean = content.Substring(0, startIndex) + content.Substring(endIndex);
                File.WriteAllText(hostsPath, clean.Trim(), Encoding.UTF8);
                DnsFlush();
                return true;
            }
            return true;
        }
        catch { return false; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class OpenRgbDevice
{
    public int DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DeviceType { get; set; } = "Motherboard";
    public int LedCount { get; set; } = 1;
    public string CurrentHexColor { get; set; } = "#00E5FF";
}

/// <summary>
/// Cliente nativo asíncrono para el protocolo SDK de OpenRGB (TCP 127.0.0.1:6742).
/// Permite sincronizar iluminación RGB multimarcas (ASUS, MSI, Gigabyte, Corsair, Razer, G.Skill)
/// sin instalar software bloatware propietario de los fabricantes.
/// </summary>
public class OpenRgbClientService : IDisposable
{
    private static readonly Lazy<OpenRgbClientService> _instance = new(() => new OpenRgbClientService());
    public static OpenRgbClientService Instance => _instance.Value;

    private const string MagicHeader = "ORGB";
    private const uint CmdRequestControllerCount = 0;
    private const uint CmdRequestControllerData = 1;
    private const uint CmdSetClientName = 1000;
    private const uint CmdUpdateLeds = 1050;
    private const uint CmdSetCustomMode = 1052;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _socketLock = new(1, 1);

    public bool IsConnected { get; private set; }
    public bool IsSimulationMode { get; private set; }
    public List<OpenRgbDevice> Devices { get; private set; } = new();
    public string CurrentHexColor { get; private set; } = "#00E5FF";
    public string ActivePreset { get; private set; } = "Cian Ártico";

    public event Action<string>? OnColorChanged;
    public event Action<bool>? OnConnectionStatusChanged;

    /// <summary>
    /// Intenta conectar al daemon de OpenRGB en localhost:6742.
    /// Si no está disponible, inicializa en modo simulación para permitir previsualización visual.
    /// </summary>
    public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = 6742, int timeoutMs = 800)
    {
        await _socketLock.WaitAsync();
        try
        {
            DisconnectInternal();

            _client = new TcpClient();
            var connectTask = _client.ConnectAsync(host, port);
            var delayTask = Task.Delay(timeoutMs);

            if (await Task.WhenAny(connectTask, delayTask) == connectTask && _client.Connected)
            {
                _stream = _client.GetStream();
                IsConnected = true;
                IsSimulationMode = false;

                // Enviar handshake con el nombre de cliente
                await SendPacketAsync(0, CmdSetClientName, Encoding.ASCII.GetBytes("OmniWin Control Plane\0"));

                // Consultar dispositivos reales
                await RefreshDevicesInternalAsync();
                OnConnectionStatusChanged?.Invoke(true);
                return true;
            }
            else
            {
                // Modo simulación con hardware detectado localmente
                EnableSimulationMode();
                OnConnectionStatusChanged?.Invoke(false);
                return false;
            }
        }
        catch
        {
            EnableSimulationMode();
            OnConnectionStatusChanged?.Invoke(false);
            return false;
        }
        finally
        {
            _socketLock.Release();
        }
    }

    private void EnableSimulationMode()
    {
        DisconnectInternal();
        IsConnected = false;
        IsSimulationMode = true;

        // Dispositivos simulados coherentes con el hardware gamer típico
        Devices = new List<OpenRgbDevice>
        {
            new() { DeviceId = 0, Name = "Motherboard ARGB Header", Vendor = "AORUS / Gigabyte", DeviceType = "Motherboard", LedCount = 8, CurrentHexColor = CurrentHexColor },
            new() { DeviceId = 1, Name = "DDR5 Memory Modules (4x)", Vendor = "Corsair / G.Skill", DeviceType = "DRAM", LedCount = 16, CurrentHexColor = CurrentHexColor },
            new() { DeviceId = 2, Name = "GeForce RTX 3080 RGB", Vendor = "NVIDIA / Partner", DeviceType = "GPU", LedCount = 6, CurrentHexColor = CurrentHexColor },
            new() { DeviceId = 3, Name = "AIO Pump & ARGB Fans (6x)", Vendor = "Universal 3-Pin ARGB", DeviceType = "Cooler", LedCount = 24, CurrentHexColor = CurrentHexColor }
        };
    }

    private async Task RefreshDevicesInternalAsync()
    {
        if (_stream == null) return;

        Devices.Clear();
        await SendPacketAsync(0, CmdRequestControllerCount, Array.Empty<byte>());
        var header = await ReadHeaderAsync();
        if (header.HasValue && header.Value.DataLength >= 4)
        {
            byte[] countBuf = new byte[4];
            await _stream.ReadExactlyAsync(countBuf, 0, 4);
            uint count = BitConverter.ToUInt32(countBuf, 0);

            for (uint i = 0; i < count; i++)
            {
                await SendPacketAsync(i, CmdRequestControllerData, BitConverter.GetBytes((uint)2)); // Protocol v2
                var dataHeader = await ReadHeaderAsync();
                if (dataHeader.HasValue && dataHeader.Value.DataLength > 0)
                {
                    byte[] data = new byte[dataHeader.Value.DataLength];
                    await _stream.ReadExactlyAsync(data, 0, data.Length);

                    string name = ExtractString(data, 4);
                    Devices.Add(new OpenRgbDevice
                    {
                        DeviceId = (int)i,
                        Name = string.IsNullOrWhiteSpace(name) ? $"Controlador #{i}" : name,
                        DeviceType = GuessDeviceType(name),
                        LedCount = 8,
                        CurrentHexColor = CurrentHexColor
                    });
                }
            }
        }
    }

    public async Task SetAllColorsAsync(byte r, byte g, byte b, string presetName = "Personalizado")
    {
        string hex = $"#{r:X2}{g:X2}{b:X2}";
        CurrentHexColor = hex;
        ActivePreset = presetName;

        foreach (var dev in Devices)
        {
            dev.CurrentHexColor = hex;
        }

        if (IsConnected && _stream != null)
        {
            await _socketLock.WaitAsync();
            try
            {
                for (uint devId = 0; devId < Devices.Count; devId++)
                {
                    int ledCount = Devices[(int)devId].LedCount;
                    byte[] payload = BuildColorPayload(ledCount, r, g, b);
                    await SendPacketAsync(devId, CmdSetCustomMode, Array.Empty<byte>());
                    await SendPacketAsync(devId, CmdUpdateLeds, payload);
                }
            }
            catch
            {
                EnableSimulationMode();
            }
            finally
            {
                _socketLock.Release();
            }
        }

        OnColorChanged?.Invoke(hex);
    }

    /// <summary>
    /// Calcula el color de iluminación reactivo en base a la temperatura del procesador.
    /// </summary>
    public async Task SetThermalReactiveColorAsync(double cpuTempCelsius)
    {
        byte r, g, b;
        string preset;

        if (cpuTempCelsius < 45.0)
        {
            r = 0; g = 229; b = 255; // Cian Hielo
            preset = $"Térmico Frío ({cpuTempCelsius:N0}°C)";
        }
        else if (cpuTempCelsius < 65.0)
        {
            r = 16; g = 185; b = 129; // Verde Esmeralda Óptimo
            preset = $"Térmico Normal ({cpuTempCelsius:N0}°C)";
        }
        else if (cpuTempCelsius < 80.0)
        {
            r = 245; g = 158; b = 11; // Ámbar Cálido
            preset = $"Térmico Alto ({cpuTempCelsius:N0}°C)";
        }
        else
        {
            r = 239; g = 68; b = 68; // Rojo Carmesí Crítico
            preset = $"Térmico Crítico ({cpuTempCelsius:N0}°C)";
        }

        await SetAllColorsAsync(r, g, b, preset);
    }

    private static byte[] BuildColorPayload(int ledCount, byte r, byte g, byte b)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        uint dataSize = (uint)(2 + (ledCount * 4));
        bw.Write(dataSize);
        bw.Write((ushort)ledCount);

        for (int i = 0; i < ledCount; i++)
        {
            bw.Write(r);
            bw.Write(g);
            bw.Write(b);
            bw.Write((byte)0); // Alpha / reserved
        }

        return ms.ToArray();
    }

    private async Task SendPacketAsync(uint deviceId, uint command, byte[] data)
    {
        if (_stream == null) return;

        byte[] header = new byte[16];
        Encoding.ASCII.GetBytes(MagicHeader, 0, 4, header, 0);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), deviceId);
        BitConverter.TryWriteBytes(header.AsSpan(8, 4), command);
        BitConverter.TryWriteBytes(header.AsSpan(12, 4), (uint)data.Length);

        await _stream.WriteAsync(header, 0, 16);
        if (data.Length > 0)
        {
            await _stream.WriteAsync(data, 0, data.Length);
        }
        await _stream.FlushAsync();
    }

    private async Task<(uint DeviceId, uint Command, uint DataLength)?> ReadHeaderAsync()
    {
        if (_stream == null) return null;
        byte[] buf = new byte[16];
        int read = await _stream.ReadAsync(buf, 0, 16);
        if (read < 16) return null;

        string magic = Encoding.ASCII.GetString(buf, 0, 4);
        if (magic != MagicHeader) return null;

        uint devId = BitConverter.ToUInt32(buf, 4);
        uint cmd = BitConverter.ToUInt32(buf, 8);
        uint len = BitConverter.ToUInt32(buf, 12);
        return (devId, cmd, len);
    }

    private static string ExtractString(byte[] data, int offset)
    {
        if (offset >= data.Length) return string.Empty;
        int end = Array.IndexOf(data, (byte)0, offset);
        int len = (end >= 0 ? end : data.Length) - offset;
        return len > 0 ? Encoding.ASCII.GetString(data, offset, len) : string.Empty;
    }

    private static string GuessDeviceType(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("dram") || n.Contains("ram") || n.Contains("memory")) return "DRAM";
        if (n.Contains("gpu") || n.Contains("vga") || n.Contains("rtx") || n.Contains("geforce") || n.Contains("radeon")) return "GPU";
        if (n.Contains("cooler") || n.Contains("pump") || n.Contains("aio") || n.Contains("kraken")) return "Cooler";
        if (n.Contains("keyboard")) return "Keyboard";
        if (n.Contains("mouse")) return "Mouse";
        return "Motherboard";
    }

    private void DisconnectInternal()
    {
        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
        IsConnected = false;
    }

    public void Dispose()
    {
        DisconnectInternal();
        _socketLock.Dispose();
    }
}

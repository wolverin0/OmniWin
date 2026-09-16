using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class SpotlightWallpaperItem
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(OriginalFilePath);
    public long FileSizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Resolution => $"{Width} x {Height}";
    public bool IsLandscape => Width > Height;
    public bool Is4KOrFhd => Width >= 1920 && Height >= 1080;
    public DateTime DateCreated { get; set; }
}

public class SpotlightWallpaperService
{
    public static SpotlightWallpaperService Instance { get; } = new();

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    public string GetSpotlightAssetsPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData,
            "Packages", "Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy", "LocalState", "Assets");
    }

    public List<SpotlightWallpaperItem> ScanSpotlightAssets(int minWidth = 1920, bool onlyLandscape = true)
    {
        var list = new List<SpotlightWallpaperItem>();
        string assetsDir = GetSpotlightAssetsPath();
        if (!Directory.Exists(assetsDir)) return list;

        try
        {
            var files = Directory.GetFiles(assetsDir);
            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                // Wallpapers are at least 150KB
                if (fi.Length < 150 * 1024) continue;

                if (TryGetImageResolution(file, out int width, out int height))
                {
                    if (width < minWidth) continue;
                    if (onlyLandscape && width <= height) continue;

                    list.Add(new SpotlightWallpaperItem
                    {
                        OriginalFilePath = file,
                        FileSizeBytes = fi.Length,
                        Width = width,
                        Height = height,
                        DateCreated = fi.CreationTime
                    });
                }
            }
        }
        catch { }

        return list.OrderByDescending(x => x.DateCreated).ToList();
    }

    public async Task<int> ExportWallpapersAsync(IEnumerable<string> filePaths, string destinationFolder)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            int exported = 0;

            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    string baseName = Path.GetFileName(path);
                    string target = Path.Combine(destinationFolder, $"Spotlight_{baseName}.jpg");
                    File.Copy(path, target, overwrite: true);
                    exported++;
                }
                catch { }
            }
            return exported;
        });
    }

    public bool SetAsDesktopWallpaper(string imagePath)
    {
        if (!File.Exists(imagePath)) return false;

        // If it doesn't have an extension, create a temporary .jpg copy
        string wallpaperToSet = imagePath;
        if (string.IsNullOrEmpty(Path.GetExtension(imagePath)))
        {
            string tempJpg = Path.Combine(Path.GetTempPath(), $"omni_wallpaper_{Path.GetFileName(imagePath)}.jpg");
            try
            {
                File.Copy(imagePath, tempJpg, overwrite: true);
                wallpaperToSet = tempJpg;
            }
            catch
            {
                wallpaperToSet = imagePath;
            }
        }

        return SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperToSet, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    private static bool TryGetImageResolution(string filePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 32) return false;

            byte[] header = new byte[32];
            fs.ReadExactly(header, 0, header.Length);

            // Check JPEG (0xFF 0xD8 0xFF)
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return TryParseJpegDimensions(fs, out width, out height);
            }

            // Check PNG (0x89 'P' 'N' 'G' 0x0D 0x0A 0x1A 0x0A)
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
        }
        catch { }

        return false;
    }

    private static bool TryParseJpegDimensions(FileStream fs, out int width, out int height)
    {
        width = 0;
        height = 0;
        fs.Seek(2, SeekOrigin.Begin);

        byte[] markerBuf = new byte[4];
        while (fs.Position < fs.Length - 10)
        {
            int b = fs.ReadByte();
            if (b != 0xFF) continue;

            int marker = fs.ReadByte();
            // Standalone markers
            if (marker == 0xD8 || marker == 0xD9 || marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            int lenHigh = fs.ReadByte();
            int lenLow = fs.ReadByte();
            int length = (lenHigh << 8) | lenLow;
            if (length < 2) break;

            // SOF0 to SOF3, SOF5 to SOF7, SOF9 to SOF11, SOF13 to SOF15 (exclude DHT 0xC4, JPG 0xC8, DAC 0xCC)
            if ((marker >= 0xC0 && marker <= 0xC3) || (marker >= 0xC5 && marker <= 0xC7) ||
                (marker >= 0xC9 && marker <= 0xCB) || (marker >= 0xCD && marker <= 0xCF))
            {
                fs.ReadByte(); // precision
                int hHigh = fs.ReadByte();
                int hLow = fs.ReadByte();
                int wHigh = fs.ReadByte();
                int wLow = fs.ReadByte();

                height = (hHigh << 8) | hLow;
                width = (wHigh << 8) | wLow;
                return width > 0 && height > 0;
            }

            fs.Seek(length - 2, SeekOrigin.Current);
        }

        return false;
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public class TrayMonitorService : IDisposable
{
    public static TrayMonitorService Instance { get; } = new();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public bool ShowCpuInTray { get; set; } = true;
    public bool ShowGpuInTray { get; set; } = true;

    public static Color GetTemperatureColor(double temp, double warn = 75.0, double crit = 85.0)
    {
        if (temp >= crit) return Color.FromArgb(239, 68, 68);     // Red
        if (temp >= warn) return Color.FromArgb(245, 158, 11);    // Orange/Amber
        if (temp >= 50.0) return Color.FromArgb(56, 189, 248);    // Cyan
        return Color.FromArgb(16, 185, 129);                     // Green
    }

    public static Bitmap CreateTemperatureBitmap(int tempCelsius, bool isCpu, int size = 16)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        g.Clear(Color.Transparent);

        Color textColor = GetTemperatureColor(tempCelsius);
        Color bgColor = Color.FromArgb(220, 10, 15, 29); // Dark navy slate pill

        // Background rounded pill
        using var bgBrush = new SolidBrush(bgColor);
        g.FillRectangle(bgBrush, 0, 0, size, size);

        // Subdued top accent line indicating CPU (Cian/Green) vs GPU (Purple/Blue)
        using var accentPen = new Pen(isCpu ? Color.FromArgb(16, 185, 129) : Color.FromArgb(168, 85, 247), 1f);
        g.DrawLine(accentPen, 0, 0, size, 0);

        string text = Math.Clamp(tempCelsius, 0, 99).ToString();
        using var font = new Font("Tahoma", size >= 32 ? 16 : 8.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(textColor);

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(text, font, textBrush, new RectangleF(0, 1, size, size - 1), sf);
        return bmp;
    }

    public static Icon CreateTemperatureIcon(int tempCelsius, bool isCpu, int size = 16)
    {
        using var bmp = CreateTemperatureBitmap(tempCelsius, isCpu, size);
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            // Clone into a managed copy so the native HICON can be safely destroyed immediately
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

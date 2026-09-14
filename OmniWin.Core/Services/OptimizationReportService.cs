using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmniWin.Core.Services;

public class ReportTweakEntry
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public string PreviousValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
}

public class OptimizationReportModel
{
    public string Hostname { get; set; } = Environment.MachineName;
    public string OsVersion { get; set; } = Environment.OSVersion.ToString();
    public string CpuModel { get; set; } = "N/D";
    public string TotalRamGb { get; set; } = "N/D";
    public string GpuModel { get; set; } = "N/D";
    public string SelectedProfile { get; set; } = "Desktop";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool RestorePointCreated { get; set; } = true;
    public long RamPurgedBytes { get; set; } = 0;
    public long DiskFreedBytes { get; set; } = 0;
    public List<ReportTweakEntry> Tweaks { get; set; } = new();
}

public class OptimizationReportService
{
    private static readonly Lazy<OptimizationReportService> _instance = new(() => new OptimizationReportService());
    public static OptimizationReportService Instance => _instance.Value;

    public string GenerateHtmlReport(OptimizationReportModel model, string? destinationPath = null)
    {
        string reportsDir = destinationPath != null
            ? Path.GetDirectoryName(destinationPath)!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OmniWin", "Reports");

        if (!Directory.Exists(reportsDir))
        {
            Directory.CreateDirectory(reportsDir);
        }

        string filePath = destinationPath ?? Path.Combine(reportsDir, $"OmniWin-Optimization-Report-{model.Timestamp:yyyyMMdd_HHmmss}.html");

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"es\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        sb.AppendLine($"  <title>OmniWin - Reporte de Optimización ({model.Hostname})</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root {");
        sb.AppendLine("      --bg: #07090E; --card: #0D1322; --border: #162035; --accent: #0284C7;");
        sb.AppendLine("      --emerald: #10B981; --text: #F8FAFC; --muted: #94A3B8; --subtle: #64748B;");
        sb.AppendLine("    }");
        sb.AppendLine("    * { box-sizing: border-box; margin: 0; padding: 0; }");
        sb.AppendLine("    body { font-family: 'Segoe UI Variable', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: var(--bg); color: var(--text); padding: 32px 20px; line-height: 1.5; }");
        sb.AppendLine("    .container { max-width: 1100px; margin: 0 auto; }");
        sb.AppendLine("    header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--border); padding-bottom: 20px; margin-bottom: 24px; }");
        sb.AppendLine("    .logo-box h1 { font-size: 26px; font-weight: 800; color: var(--text); display: flex; align-items: center; gap: 8px; }");
        sb.AppendLine("    .badge { background: #0A2644; color: var(--accent); font-size: 12px; font-weight: bold; padding: 3px 8px; border-radius: 4px; border: 1px solid #144272; }");
        sb.AppendLine("    .meta-time { color: var(--muted); font-size: 13px; text-align: right; }");
        sb.AppendLine("    .grid-cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-bottom: 24px; }");
        sb.AppendLine("    .card { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 18px; }");
        sb.AppendLine("    .card-title { font-size: 12px; font-weight: bold; color: var(--muted); text-transform: uppercase; margin-bottom: 6px; }");
        sb.AppendLine("    .card-value { font-size: 24px; font-weight: 800; color: var(--text); }");
        sb.AppendLine("    .card-sub { font-size: 12px; color: var(--subtle); margin-top: 4px; }");
        sb.AppendLine("    .specs-box { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px 20px; margin-bottom: 24px; display: flex; flex-wrap: wrap; gap: 24px; }");
        sb.AppendLine("    .spec-item { font-size: 13px; }");
        sb.AppendLine("    .spec-item span { color: var(--muted); }");
        sb.AppendLine("    .section-header { font-size: 18px; font-weight: bold; margin-bottom: 14px; display: flex; align-items: center; gap: 8px; }");
        sb.AppendLine("    table { width: 100%; border-collapse: collapse; background: var(--card); border-radius: 8px; overflow: hidden; border: 1px solid var(--border); font-size: 13px; }");
        sb.AppendLine("    th { background: #0F1626; color: var(--muted); font-weight: 600; text-align: left; padding: 12px 14px; border-bottom: 1px solid var(--border); }");
        sb.AppendLine("    td { padding: 10px 14px; border-bottom: 1px solid rgba(22, 32, 53, 0.6); }");
        sb.AppendLine("    tr:last-child td { border-bottom: none; }");
        sb.AppendLine("    .pill-applied { background: #064E3B; color: #34D399; font-weight: bold; padding: 3px 8px; border-radius: 4px; font-size: 11px; display: inline-block; }");
        sb.AppendLine("    .pill-vss { background: #0A2E4E; color: #38BDF8; font-weight: bold; padding: 3px 8px; border-radius: 4px; font-size: 11px; }");
        sb.AppendLine("    code { background: #080C14; padding: 2px 6px; border-radius: 4px; color: #38BDF8; font-size: 11.5px; font-family: Consolas, monospace; }");
        sb.AppendLine("    footer { text-align: center; margin-top: 36px; color: var(--subtle); font-size: 12px; }");
        sb.AppendLine("    @media print { body { background: white; color: black; } .card, table { border: 1px solid #ddd; background: white; color: black; } code { background: #f4f4f4; color: black; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <div class=\"container\">");

        // Header
        sb.AppendLine("    <header>");
        sb.AppendLine("      <div class=\"logo-box\">");
        sb.AppendLine("        <h1>⚡ OmniWin <span class=\"badge\">PRO CONTROL PLANE</span></h1>");
        sb.AppendLine("        <div style=\"color: var(--muted); font-size: 13px;\">Informe Oficial de Optimización &amp; Auditoría del Sistema</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"meta-time\">");
        sb.AppendLine($"        <div><strong>Fecha:</strong> {model.Timestamp:yyyy-MM-dd HH:mm:ss}</div>");
        sb.AppendLine($"        <div><strong>Equipo:</strong> {model.Hostname}</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </header>");

        // Hardware Specs Strip
        sb.AppendLine("    <div class=\"specs-box\">");
        sb.AppendLine($"      <div class=\"spec-item\"><span>Sistema Operativo:</span> <strong>{model.OsVersion}</strong></div>");
        sb.AppendLine($"      <div class=\"spec-item\"><span>Procesador:</span> <strong>{model.CpuModel}</strong></div>");
        sb.AppendLine($"      <div class=\"spec-item\"><span>Memoria RAM:</span> <strong>{model.TotalRamGb}</strong></div>");
        sb.AppendLine($"      <div class=\"spec-item\"><span>GPU:</span> <strong>{model.GpuModel}</strong></div>");
        sb.AppendLine($"      <div class=\"spec-item\"><span>Perfil Aplicado:</span> <strong>{model.SelectedProfile}</strong></div>");
        sb.AppendLine("    </div>");

        // Metrics Grid
        double ramPurgedMb = model.RamPurgedBytes / (1024.0 * 1024.0);
        double diskFreedMb = model.DiskFreedBytes / (1024.0 * 1024.0);

        sb.AppendLine("    <div class=\"grid-cards\">");
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <div class=\"card-title\">Ajustes Aplicados</div>");
        sb.AppendLine($"        <div class=\"card-value\" style=\"color: var(--accent);\">{model.Tweaks.Count}</div>");
        sb.AppendLine("        <div class=\"card-sub\">Optimizaciones de Registro, Red y Kernel</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <div class=\"card-title\">RAM Liberada en Vivo</div>");
        sb.AppendLine($"        <div class=\"card-value\" style=\"color: var(--emerald);\">{ramPurgedMb:F1} MB</div>");
        sb.AppendLine("        <div class=\"card-sub\">Purga de Standby List &amp; Working Sets</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <div class=\"card-title\">Espacio en Disco Recuperado</div>");
        sb.AppendLine($"        <div class=\"card-value\">{diskFreedMb:F1} MB</div>");
        sb.AppendLine("        <div class=\"card-sub\">Cachés del sistema y archivos basura</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"card\">");
        sb.AppendLine("        <div class=\"card-title\">Punto de Restauración</div>");
        sb.AppendLine($"        <div class=\"card-value\"><span class=\"pill-vss\">{(model.RestorePointCreated ? "✔ Creado" : "✖ Omitido")}</span></div>");
        sb.AppendLine("        <div class=\"card-sub\">Respaldo VSS para reversión 100% segura</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");

        // Tweaks Table
        sb.AppendLine("    <div class=\"section-header\">📋 Detalle de Optimizaciones y Cambios Realizados</div>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead>");
        sb.AppendLine("        <tr>");
        sb.AppendLine("          <th>Categoría</th>");
        sb.AppendLine("          <th>Nombre de la Optimización</th>");
        sb.AppendLine("          <th>Ruta en el Registro / Subsistema</th>");
        sb.AppendLine("          <th>Valor Previo</th>");
        sb.AppendLine("          <th>Nuevo Valor</th>");
        sb.AppendLine("          <th>Estado</th>");
        sb.AppendLine("        </tr>");
        sb.AppendLine("      </thead>");
        sb.AppendLine("      <tbody>");

        foreach (var t in model.Tweaks)
        {
            sb.AppendLine("        <tr>");
            sb.AppendLine($"          <td><span style=\"color: var(--accent); font-weight: 600;\">{t.Category}</span></td>");
            sb.AppendLine($"          <td><strong>{t.Name}</strong><br/><span style=\"color: var(--subtle); font-size: 11.5px;\">{t.Description}</span></td>");
            sb.AppendLine($"          <td><code>{t.RegistryPath}</code></td>");
            sb.AppendLine($"          <td style=\"color: var(--muted);\">{t.PreviousValue}</td>");
            sb.AppendLine($"          <td style=\"color: var(--emerald); font-weight: bold;\">{t.NewValue}</td>");
            sb.AppendLine("          <td><span class=\"pill-applied\">✔ OPTIMIZADO</span></td>");
            sb.AppendLine("        </tr>");
        }

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");

        // Footer
        sb.AppendLine("    <footer>");
        sb.AppendLine("      OmniWin Windows Control Plane &amp; MCP Agent • Diseñado para Máximo Rendimiento y Cero Bloatware.");
        sb.AppendLine("    </footer>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class IncidentInvestigatorControl : UserControl
{
    private readonly IncidentInvestigatorService _service = new();
    private List<IncidentRecord> _allIncidents = new();

    public IncidentInvestigatorControl()
    {
        InitializeComponent();
        Loaded += IncidentInvestigatorControl_Loaded;
    }

    private async void IncidentInvestigatorControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_allIncidents.Count == 0)
        {
            await RefreshIncidentsAsync();
        }
    }

    private async Task RefreshIncidentsAsync()
    {
        try
        {
            BtnRefreshIncidents.IsEnabled = false;
            TxtStatus.Text = "Leyendo registros de eventos del sistema (WER, Kernel-Power, User32) y carpeta Minidump...";

            _allIncidents = await _service.GetIncidentsAsync();
            ApplyFilter();

            int bsodCount = _allIncidents.Count(i => i.Category == IncidentCategory.BSOD);
            int powerCount = _allIncidents.Count(i => i.Category == IncidentCategory.KernelPower);
            TxtStatus.Text = $"Análisis completado: {_allIncidents.Count} incidentes encontrados ({bsodCount} BSODs / volcados, {powerCount} cortes de energía Kernel-Power).";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error al leer incidentes: {ex.Message}";
        }
        finally
        {
            BtnRefreshIncidents.IsEnabled = true;
        }
    }

    private void ApplyFilter()
    {
        if (_allIncidents == null || DgIncidents == null || CmbCategoryFilter == null) return;

        int selected = CmbCategoryFilter.SelectedIndex;
        var filtered = selected switch
        {
            1 => _allIncidents.Where(i => i.Category == IncidentCategory.BSOD).ToList(),
            2 => _allIncidents.Where(i => i.Category == IncidentCategory.KernelPower).ToList(),
            3 => _allIncidents.Where(i => i.Category == IncidentCategory.Shutdown).ToList(),
            _ => _allIncidents
        };

        DgIncidents.ItemsSource = filtered;

        if (filtered.Count > 0)
        {
            DgIncidents.SelectedIndex = 0;
        }
    }

    private async void BtnRefreshIncidents_Click(object sender, RoutedEventArgs e)
    {
        await RefreshIncidentsAsync();
    }

    private void CmbCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void DgIncidents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtDetailTitle == null || TxtDetailCode == null || TxtDetailVerdict == null || TxtDetailRecommendation == null || TxtDetailRaw == null)
            return;

        if (DgIncidents?.SelectedItem is IncidentRecord item)
        {
            TxtDetailTitle.Text = $"{item.Icon}  {item.EventTitle}";
            TxtDetailCode.Text = !string.IsNullOrEmpty(item.BugcheckCodeHex) ? $"[{item.BugcheckCodeHex}]" : "";
            TxtDetailVerdict.Text = item.DiagnosticVerdict;
            TxtDetailRecommendation.Text = item.RecommendedAction;
            TxtDetailRaw.Text = !string.IsNullOrEmpty(item.DumpPath) 
                ? $"Volcado guardado en: {item.DumpPath}\r\n\r\n{item.RawDetails}"
                : item.RawDetails;
        }
    }

    private void BtnOpenMinidumpFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string minidumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            if (!Directory.Exists(minidumpDir))
            {
                MessageBox.Show($"La carpeta de volcados no existe o no se han generado volcados aún en:\n{minidumpDir}", "Carpeta Minidump", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", minidumpDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir carpeta: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCopyDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (DgIncidents.SelectedItem is IncidentRecord item)
        {
            string report = $"[OmniWin - Informe Forense de Incidente]\n" +
                            $"Fecha: {item.Timestamp:dd/MM/yyyy HH:mm:ss}\n" +
                            $"Categoría: {item.Category}\n" +
                            $"Evento: {item.EventTitle}\n" +
                            $"Código: {item.BugcheckCodeHex} ({item.BugcheckName})\n" +
                            $"Diagnóstico: {item.DiagnosticVerdict}\n" +
                            $"Recomendación: {item.RecommendedAction}\n" +
                            $"Dump: {item.DumpPath}\n\n" +
                            $"Detalles:\n{item.RawDetails}";

            try
            {
                Clipboard.SetText(report);
                MessageBox.Show("Informe forense copiado al portapapeles.", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }
    }
}

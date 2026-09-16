using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

/// <summary>
/// Lógica de interacción para ProcessExplorerControl.xaml.
/// Proporciona exploración de jerarquía de procesos, inspección de módulos DLL,
/// sockets TCP en tiempo real (GetExtendedTcpTable) y métricas de memoria virtual.
/// </summary>
public partial class ProcessExplorerControl : UserControl
{
    private readonly ProcessDeepDiagService _diagService = new();
    private readonly ProcessIntelligenceService _intelService = new();
    private string _currentVirusTotalUrl = "";

    private readonly ObservableCollection<ProcessTreeNode> _displayedProcesses = new();
    private List<ProcessTreeNode> _allFlattenedTree = new();
    private List<ProcessTreeNode> _allFlatList = new();

    private readonly ObservableCollection<ProcessModuleItem> _displayedModules = new();
    private List<ProcessModuleItem> _allModules = new();

    private readonly ObservableCollection<ProcessTcpConnectionItem> _tcpConnections = new();
    private readonly ObservableCollection<ProcessThreadItem> _threads = new();

    private ProcessTreeNode? _currentSelectedProc;
    private bool _isLoadingProcesses;

    public ProcessExplorerControl()
    {
        InitializeComponent();

        DgProcessList.ItemsSource = _displayedProcesses;
        DgModules.ItemsSource = _displayedModules;
        DgTcpConnections.ItemsSource = _tcpConnections;
        DgThreads.ItemsSource = _threads;

        Loaded += ProcessExplorerControl_Loaded;
    }

    private async void ProcessExplorerControl_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadProcessesAsync();
    }

    #region Carga y Filtrado de Procesos

    public async Task LoadProcessesAsync()
    {
        if (_isLoadingProcesses) return;
        _isLoadingProcesses = true;

        TxtProcListStatus.Text = "Enumerando árbol y jerarquía de procesos del sistema...";

        try
        {
            var (tree, flat) = await Task.Run(() =>
            {
                var hierarchy = _diagService.GetFlattenedProcessTree();
                var flatList = hierarchy
                    .Select(n => new ProcessTreeNode
                    {
                        Pid = n.Pid,
                        ParentPid = n.ParentPid,
                        Name = n.Name,
                        Title = n.Title,
                        FilePath = n.FilePath,
                        WorkingSet64 = n.WorkingSet64,
                        PrivateBytes64 = n.PrivateBytes64,
                        HandleCount = n.HandleCount,
                        ThreadCount = n.ThreadCount,
                        Depth = 0
                    })
                    .OrderBy(n => n.Name)
                    .ToList();

                return (hierarchy, flatList);
            });

            _allFlattenedTree = tree;
            _allFlatList = flat;

            ApplyProcessFilter();

            TxtProcCountBadge.Text = $"{_allFlattenedTree.Count} procesos";
            TxtProcListStatus.Text = $"Árbol actualizado: {_allFlattenedTree.Count} procesos activos enumerados.";
        }
        catch (Exception ex)
        {
            TxtProcListStatus.Text = $"Error al enumerar procesos: {ex.Message}";
        }
        finally
        {
            _isLoadingProcesses = false;
        }
    }

    private void ApplyProcessFilter()
    {
        string query = TxtSearchProc.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        bool isTree = ChkTreeMode.IsChecked == true;

        var source = isTree && string.IsNullOrEmpty(query) ? _allFlattenedTree : _allFlatList;

        var filtered = source.Where(p =>
            string.IsNullOrEmpty(query) ||
            p.Name.ToLowerInvariant().Contains(query) ||
            p.Pid.ToString().Contains(query) ||
            p.ParentPid.ToString().Contains(query) ||
            (!string.IsNullOrEmpty(p.Title) && p.Title.ToLowerInvariant().Contains(query))
        ).ToList();

        _displayedProcesses.Clear();
        foreach (var proc in filtered)
        {
            _displayedProcesses.Add(proc);
        }

        // Mantener selección si sigue existiendo
        if (_currentSelectedProc != null)
        {
            var match = _displayedProcesses.FirstOrDefault(p => p.Pid == _currentSelectedProc.Pid);
            if (match != null)
            {
                DgProcessList.SelectedItem = match;
            }
        }
    }

    private async void BtnRefreshProcs_Click(object sender, RoutedEventArgs e)
    {
        await LoadProcessesAsync();
    }

    private void TxtSearchProc_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyProcessFilter();
    }

    private void ChkTreeMode_Changed(object sender, RoutedEventArgs e)
    {
        ApplyProcessFilter();
    }

    #endregion

    #region Selección y Carga Forense

    private async void DgProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DgProcessList.SelectedItem is not ProcessTreeNode selected)
        {
            return;
        }

        _currentSelectedProc = selected;
        ShowProcessHeader(selected);
        await LoadProcessForensicsAsync(selected.Pid);
    }

    private void ShowProcessHeader(ProcessTreeNode node)
    {
        PnlNoSelection.Visibility = Visibility.Collapsed;
        PnlSelectedHeader.Visibility = Visibility.Visible;
        TcProcessDetails.Visibility = Visibility.Visible;

        TxtSelectedProcName.Text = node.Name;
        TxtSelectedProcPid.Text = $"PID: {node.Pid}";
        TxtSelectedProcPpid.Text = $"PPID: {node.ParentPid}";
        TxtSelectedProcPath.Text = !string.IsNullOrEmpty(node.FilePath) ? node.FilePath : "(Ruta no disponible / Acceso restringido de sistema)";
    }

    private async Task LoadProcessForensicsAsync(int pid)
    {
        TxtDiagFooterStatus.Text = $"Cargando módulos DLL, sockets TCP y métricas para PID {pid}...";

        try
        {
            // Ejecutar consultas forenses en hilo en segundo plano
            var result = await Task.Run(() =>
            {
                var modules = _diagService.GetProcessModules(pid);
                var tcp = _diagService.GetTcpConnections(pid);
                var metrics = _diagService.GetProcessForensics(pid);
                return (modules, tcp, metrics);
            });

            // 1. Módulos DLL
            _allModules = result.modules;
            ApplyModuleFilter();
            TxtModulesSummary.Text = $"{_allModules.Count} módulos DLL cargados en memoria";

            // 2. Conexiones TCP
            _tcpConnections.Clear();
            foreach (var conn in result.tcp)
            {
                _tcpConnections.Add(conn);
            }
            int listenCount = result.tcp.Count(c => c.IsListening);
            int estCount = result.tcp.Count(c => c.IsEstablished);
            TxtTcpSummary.Text = $"{result.tcp.Count} sockets TCP ({listenCount} en escucha, {estCount} establecidos)";

            // 3. Métricas de Memoria y Rendimiento
            var m = result.metrics;
            TxtWorkingSet.Text = $"{m.WorkingSetMB:N1} MB";
            TxtPeakWorkingSet.Text = $"Pico: {m.PeakWorkingSetMB:N1} MB";

            TxtPrivateBytes.Text = $"{m.PrivateMB:N1} MB";
            TxtPagedMemory.Text = $"Paginado: {m.PagedMemoryMB:N1} MB";

            TxtHandlesCount.Text = $"{m.HandleCount:N0}";
            TxtPageFaults.Text = $"Fallas pág: {m.PageFaultCount:N0}";

            TxtThreadsCount.Text = $"{m.ThreadCount} hilos";
            TxtCpuTime.Text = m.TotalCpuTime.HasValue ? $"CPU: {m.TotalCpuTime.Value:hh\\:mm\\:ss}" : "CPU: N/D";

            // 4. Hilos
            _threads.Clear();
            foreach (var th in m.Threads.OrderByDescending(t => t.TotalProcessorTimeMs))
            {
                _threads.Add(th);
            }

            // 5. Inteligencia y Seguridad de Procesos
            string procName = _currentSelectedProc?.Name ?? m.Name;
            string procPath = _currentSelectedProc?.FilePath ?? string.Empty;
            var intel = _intelService.EvaluateProcessIntelligence(pid, procName, procPath, m.WorkingSetMB);
            TxtIntelCategory.Text = intel.Category.ToString();
            TxtIntelVendor.Text = intel.Company;
            TxtIntelDescription.Text = intel.Description;
            TxtIntelRole.Text = intel.RoleAndInfluence;
            TxtIntelImpact.Text = intel.ImpactDetails;
            TxtIntelSecurityVerdict.Text = intel.SecurityVerdict;
            TxtIntelSigner.Text = $"Firmante: {intel.SignerName ?? "No disponible"} | Hash SHA-256: {(string.IsNullOrEmpty(intel.Sha256Hash) ? "N/D" : intel.Sha256Hash)}";
            _currentVirusTotalUrl = intel.OnlineLookupUrl;

            if (intel.SafetyImpact == ProcessSafetyImpact.Never)
            {
                BrdSafetyBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));
                TxtIntelSafety.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
                TxtIntelSafety.Text = "¡NUNCA CERRAR (BSOD)!";
            }
            else if (intel.SafetyImpact == ProcessSafetyImpact.Careful)
            {
                BrdSafetyBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 53, 15));
                TxtIntelSafety.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                TxtIntelSafety.Text = "Cerrar con precaución";
            }
            else
            {
                BrdSafetyBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
                TxtIntelSafety.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                TxtIntelSafety.Text = "Seguro de cerrar";
            }

            BrdThreatBadge.Visibility = intel.ThreatLevel == ProcessThreatLevel.CriticalMasquerading ? Visibility.Visible : Visibility.Collapsed;

            TxtDiagFooterStatus.Text = $"Diagnóstico completo para {result.metrics.Name} (PID {pid}): {_allModules.Count} DLLs, {result.tcp.Count} sockets, {m.ThreadCount} hilos.";
        }
        catch (Exception ex)
        {
            TxtDiagFooterStatus.Text = $"Error al inspeccionar PID {pid}: {ex.Message}";
        }
    }

    private void BtnVirusTotalLookup_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentVirusTotalUrl))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_currentVirusTotalUrl) { UseShellExecute = true }); } catch { }
        }
    }

    private void ApplyModuleFilter()
    {
        string query = TxtFilterModules.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = _allModules.Where(m =>
            string.IsNullOrEmpty(query) ||
            m.ModuleName.ToLowerInvariant().Contains(query) ||
            m.FilePath.ToLowerInvariant().Contains(query) ||
            m.Description.ToLowerInvariant().Contains(query) ||
            m.Company.ToLowerInvariant().Contains(query)
        ).ToList();

        _displayedModules.Clear();
        foreach (var m in filtered)
        {
            _displayedModules.Add(m);
        }
    }

    private void TxtFilterModules_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyModuleFilter();
    }

    private async void BtnRefreshDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSelectedProc != null)
        {
            await LoadProcessForensicsAsync(_currentSelectedProc.Pid);
        }
    }

    private async void BtnRefreshSockets_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSelectedProc == null) return;
        int pid = _currentSelectedProc.Pid;

        try
        {
            var sockets = await Task.Run(() => _diagService.GetTcpConnections(pid));
            _tcpConnections.Clear();
            foreach (var s in sockets)
            {
                _tcpConnections.Add(s);
            }
            int listenCount = sockets.Count(c => c.IsListening);
            int estCount = sockets.Count(c => c.IsEstablished);
            TxtTcpSummary.Text = $"{sockets.Count} sockets TCP ({listenCount} en escucha, {estCount} establecidos)";
            TxtDiagFooterStatus.Text = $"Sockets TCP actualizados para PID {pid} a las {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            TxtDiagFooterStatus.Text = $"Error al actualizar sockets: {ex.Message}";
        }
    }

    #endregion

    #region Terminación de Proceso con Confirmación

    private async void BtnKillSelectedProc_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSelectedProc == null) return;
        await TerminateProcessWithConfirmation(_currentSelectedProc, killTree: false);
    }

    private async void MenuKillProc_Click(object sender, RoutedEventArgs e)
    {
        if (DgProcessList.SelectedItem is ProcessTreeNode node)
        {
            await TerminateProcessWithConfirmation(node, killTree: false);
        }
    }

    private async void MenuKillTree_Click(object sender, RoutedEventArgs e)
    {
        if (DgProcessList.SelectedItem is ProcessTreeNode node)
        {
            await TerminateProcessWithConfirmation(node, killTree: true);
        }
    }

    private async Task TerminateProcessWithConfirmation(ProcessTreeNode node, bool killTree)
    {
        string title = killTree ? "Terminar Árbol de Procesos" : "Terminar Proceso";
        string msg = killTree
            ? $"¿Estás seguro de que deseas terminar el proceso '{node.Name}' (PID: {node.Pid}) y TODOS sus procesos secundarios?\n\nEsta acción cerrará de forma forzada toda la jerarquía de procesos."
            : $"¿Estás seguro de que deseas terminar el proceso '{node.Name}' (PID: {node.Pid})?\n\nEsta acción forzará el cierre inmediato del proceso.";

        var confirm = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        TxtDiagFooterStatus.Text = $"Terminando proceso {node.Name} (PID {node.Pid})...";

        var result = await Task.Run(() => _diagService.KillProcess(node.Pid, killTree));

        if (result.Success)
        {
            TxtDiagFooterStatus.Text = $"✔ {result.Message}";
            MessageBox.Show(result.Message, "Proceso finalizado", MessageBoxButton.OK, MessageBoxImage.Information);

            PnlSelectedHeader.Visibility = Visibility.Collapsed;
            TcProcessDetails.Visibility = Visibility.Collapsed;
            PnlNoSelection.Visibility = Visibility.Visible;
            _currentSelectedProc = null;

            await LoadProcessesAsync();
        }
        else
        {
            TxtDiagFooterStatus.Text = $"✖ {result.Message}";
            MessageBox.Show(result.Message, "Error al finalizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuCopyPid_Click(object sender, RoutedEventArgs e)
    {
        if (DgProcessList.SelectedItem is ProcessTreeNode node)
        {
            Clipboard.SetText(node.Pid.ToString());
            TxtProcListStatus.Text = $"PID {node.Pid} copiado al portapapeles.";
        }
    }

    #endregion
}

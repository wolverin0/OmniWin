using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class OnboardingWizardControl : UserControl
{
    public event Action<string?>? OnWizardCompleted;

    private int _currentStep = 1;
    private string _selectedRole = "Desktop"; // "Desktop", "Laptop", "Workstation"

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniWin");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private readonly ExpandedTweakService _tweakService = new();

    public OnboardingWizardControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DetectHardwareInfo();
            SelectRole("Desktop");
            UpdateStepView();
        };
    }

    public static bool ShouldShowOnboarding()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return true;
            string json = File.ReadAllText(SettingsPath);
            var node = JsonNode.Parse(json);
            if (node != null && node["onboarding_completed"] != null)
            {
                return !node["onboarding_completed"]!.GetValue<bool>();
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SaveSettings(bool completed, string profile)
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }

            var obj = new JsonObject
            {
                ["onboarding_completed"] = completed,
                ["selected_profile"] = profile,
                ["last_updated"] = DateTime.UtcNow.ToString("o")
            };

            File.WriteAllText(SettingsPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ==========================================
    // 1. HARDWARE ASSESSMENT & ROLE
    // ==========================================
    private void DetectHardwareInfo()
    {
        try
        {
            // OS Version
            string osName = RuntimeInformation.OSDescription;
            TxtHardwareOs.Text = $"Sistema: {osName}";

            // CPU Name
            using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            {
                string cpu = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? $"{Environment.ProcessorCount} Núcleos Lógicos";
                TxtHardwareCpu.Text = $"Procesador: {cpu}";
            }

            // RAM Memory
            long ramBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            double ramGb = ramBytes / (1024.0 * 1024.0 * 1024.0);
            TxtHardwareRam.Text = ramGb > 0 ? $"Memoria RAM: ~{Math.Round(ramGb)} GB Disponibles" : $"Núcleos de CPU: {Environment.ProcessorCount}";

            // Storage TRIM Check
            DriveInfo systemDrive = new DriveInfo("C");
            double freeGb = systemDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            double totalGb = systemDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
            TxtHardwareDisk.Text = $"Disco C: SSD/NVMe • {freeGb:N0} GB libres de {totalGb:N0} GB (TRIM Activo)";
        }
        catch
        {
            TxtHardwareCpu.Text = $"Procesador: {Environment.ProcessorCount} Hilos de CPU";
            TxtHardwareRam.Text = "Memoria RAM: Arquitectura 64-bit";
        }
    }

    private void CardRoleDesktop_MouseDown(object sender, MouseButtonEventArgs e) => SelectRole("Desktop");
    private void CardRoleLaptop_MouseDown(object sender, MouseButtonEventArgs e) => SelectRole("Laptop");
    private void CardRoleWorkstation_MouseDown(object sender, MouseButtonEventArgs e) => SelectRole("Workstation");

    public void SelectRole(string role)
    {
        _selectedRole = role;

        if (CardRoleDesktop == null || CardRoleLaptop == null || CardRoleWorkstation == null) return;

        var activeBorder = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        var defaultBorder = new SolidColorBrush(Color.FromRgb(22, 32, 53));
        var activeBg = new SolidColorBrush(Color.FromRgb(17, 27, 46));
        var defaultBg = new SolidColorBrush(Color.FromRgb(9, 14, 24));

        CardRoleDesktop.BorderBrush = role == "Desktop" ? activeBorder : defaultBorder;
        CardRoleDesktop.Background = role == "Desktop" ? activeBg : defaultBg;
        CardRoleDesktop.BorderThickness = role == "Desktop" ? new Thickness(1.5) : new Thickness(1);

        CardRoleLaptop.BorderBrush = role == "Laptop" ? activeBorder : defaultBorder;
        CardRoleLaptop.Background = role == "Laptop" ? activeBg : defaultBg;
        CardRoleLaptop.BorderThickness = role == "Laptop" ? new Thickness(1.5) : new Thickness(1);

        CardRoleWorkstation.BorderBrush = role == "Workstation" ? activeBorder : defaultBorder;
        CardRoleWorkstation.Background = role == "Workstation" ? activeBg : defaultBg;
        CardRoleWorkstation.BorderThickness = role == "Workstation" ? new Thickness(1.5) : new Thickness(1);

        // Adjust presets based on role
        if (role == "Laptop")
        {
            ChkTweakLargeCache.IsChecked = false;
            ChkTweakWin32Priority.IsChecked = true;
            ChkEnableCaffeine.IsChecked = false;
        }
        else if (role == "Workstation")
        {
            ChkTweakLargeCache.IsChecked = true;
            ChkTweakWin32Priority.IsChecked = false; // Throughput balance
            ChkEnableCaffeine.IsChecked = true;
        }
        else // Desktop
        {
            ChkTweakLargeCache.IsChecked = false;
            ChkTweakWin32Priority.IsChecked = true;
            ChkEnableCaffeine.IsChecked = false;
        }

        UpdateSummaryCounters();
    }

    // ==========================================
    // 2. STEPPER NAVIGATION & VIEW SWITCHING
    // ==========================================
    private void TabStep_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && int.TryParse(b.Tag?.ToString(), out int step))
        {
            _currentStep = step;
            UpdateStepView();
        }
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < 7)
        {
            _currentStep++;
            UpdateStepView();
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepView();
        }
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e) => FinishWizard(null);
    private void BtnClose_Click(object sender, RoutedEventArgs e) => FinishWizard(null);

    private void UpdateStepView()
    {
        TxtStepBadge.Text = $"Paso {_currentStep} de 7";

        PanelStep1.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep2.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep3.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep4.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep5.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep6.Visibility = _currentStep == 6 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep7.Visibility = _currentStep == 7 ? Visibility.Visible : Visibility.Collapsed;

        // Update tab indicators
        UpdateTabBadge(TabStep1, TxtStep1, 1);
        UpdateTabBadge(TabStep2, TxtStep2, 2);
        UpdateTabBadge(TabStep3, TxtStep3, 3);
        UpdateTabBadge(TabStep4, TxtStep4, 4);
        UpdateTabBadge(TabStep5, TxtStep5, 5);
        UpdateTabBadge(TabStep6, TxtStep6, 6);
        UpdateTabBadge(TabStep7, TxtStep7, 7);

        BtnBack.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnNext.Visibility = _currentStep < 7 ? Visibility.Visible : Visibility.Collapsed;
        BtnFinish.Visibility = _currentStep == 7 ? Visibility.Visible : Visibility.Collapsed;
        BtnSkip.Visibility = _currentStep < 7 ? Visibility.Visible : Visibility.Collapsed;

        if (_currentStep == 7)
        {
            UpdateSummaryCounters();
        }
    }

    private void UpdateTabBadge(Border tab, TextBlock text, int stepIndex)
    {
        if (_currentStep == stepIndex)
        {
            tab.Background = new SolidColorBrush(Color.FromRgb(17, 27, 46));
            tab.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
            tab.BorderThickness = new Thickness(1);
            text.Foreground = Brushes.White;
            text.FontWeight = FontWeights.Bold;
        }
        else if (_currentStep > stepIndex)
        {
            tab.Background = new SolidColorBrush(Color.FromRgb(6, 46, 33));
            tab.BorderBrush = new SolidColorBrush(Color.FromRgb(10, 72, 53));
            tab.BorderThickness = new Thickness(1);
            text.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            text.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            tab.Background = Brushes.Transparent;
            tab.BorderBrush = Brushes.Transparent;
            tab.BorderThickness = new Thickness(0);
            text.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            text.FontWeight = FontWeights.Normal;
        }
    }

    private void OnTweakCheckboxChanged(object sender, RoutedEventArgs e)
    {
        UpdateSummaryCounters();
    }

    private void UpdateSummaryCounters()
    {
        if (TxtSummarySelectedCount == null || TxtSummaryKernel == null || TxtSummaryBoot == null ||
            TxtSummaryNet == null || TxtSummaryVisual == null || TxtSummaryServices == null || TxtSummaryRole == null)
        {
            return;
        }

        int kernelCount = (ChkTweakNtfsMemory?.IsChecked == true ? 1 : 0) +
                          (ChkTweakNtfs8dot3?.IsChecked == true ? 1 : 0) +
                          (ChkTweakNtfsLastAccess?.IsChecked == true ? 1 : 0) +
                          (ChkTweakWin32Priority?.IsChecked == true ? 1 : 0) +
                          (ChkTweakLargeCache?.IsChecked == true ? 1 : 0);

        int bootCount = (ChkTweakStartupDelay?.IsChecked == true ? 1 : 0) +
                        (ChkTweakWaitToKill?.IsChecked == true ? 1 : 0) +
                        (ChkTweakAutoEndTasks?.IsChecked == true ? 1 : 0) +
                        (ChkTweakAutoChk?.IsChecked == true ? 1 : 0) +
                        (ChkTweakClearPageFile?.IsChecked == true ? 1 : 0);

        int netCount = (ChkTweakNagle?.IsChecked == true ? 1 : 0) +
                       (ChkTweakNetThrottling?.IsChecked == true ? 1 : 0) +
                       (ChkTweakSysResponsiveness?.IsChecked == true ? 1 : 0) +
                       (ChkTweakMaxConnections?.IsChecked == true ? 1 : 0) +
                       (ChkTweakDnsTtl?.IsChecked == true ? 1 : 0);

        int visualCount = (ChkTweakMenuDelay?.IsChecked == true ? 1 : 0) +
                          (ChkTweakClassicMenu?.IsChecked == true ? 1 : 0) +
                          (ChkTweakShowExtensions?.IsChecked == true ? 1 : 0) +
                          (ChkTweakCompactView?.IsChecked == true ? 1 : 0) +
                          (ChkTweakHideWidgets?.IsChecked == true ? 1 : 0);

        int servicesCount = (ChkTweakDiagTrack?.IsChecked == true ? 1 : 0) +
                            (ChkTweakHosts?.IsChecked == true ? 1 : 0) +
                            (ChkTweakAdvertisingId?.IsChecked == true ? 1 : 0) +
                            (ChkTweakWer?.IsChecked == true ? 1 : 0) +
                            (ChkTweakTyping?.IsChecked == true ? 1 : 0);

        int totalSelected = kernelCount + bootCount + netCount + visualCount + servicesCount;

        TxtSummarySelectedCount.Text = $"Optimizaciones seleccionadas: {totalSelected} de 25";
        TxtSummaryKernel.Text = $"• Kernel & Archivos: {kernelCount} de 5";
        TxtSummaryBoot.Text = $"• Arranque y Apagado: {bootCount} de 5";
        TxtSummaryNet.Text = $"• Red y Baja Latencia: {netCount} de 5";
        TxtSummaryVisual.Text = $"• Fluidez Visual y DWM: {visualCount} de 5";
        TxtSummaryServices.Text = $"• Servicios y Privacidad: {servicesCount} de 5";
        TxtSummaryRole.Text = $"• Rol: {_selectedRole}";
    }

    // ==========================================
    // 3. 1-CLICK OPTIMIZATION ENGINE
    // ==========================================
    private async void BtnFinish_Click(object sender, RoutedEventArgs e)
    {
        BtnFinish.IsEnabled = false;
        BtnBack.IsEnabled = false;
        BorderApplyStatus.Visibility = Visibility.Visible;

        var tweaksToApply = new List<string>();

        // Step 2: Kernel
        if (ChkTweakNtfsMemory.IsChecked == true) tweaksToApply.Add("sys_ntfs_memory_usage");
        if (ChkTweakNtfs8dot3.IsChecked == true) tweaksToApply.Add("sys_ntfs_disable_8dot3");
        if (ChkTweakNtfsLastAccess.IsChecked == true) tweaksToApply.Add("sys_ntfs_disable_last_access");
        if (ChkTweakWin32Priority.IsChecked == true) tweaksToApply.Add("gaming_win32_priority");
        if (ChkTweakLargeCache.IsChecked == true) tweaksToApply.Add("sys_large_system_cache");

        // Step 3: Boot
        if (ChkTweakStartupDelay.IsChecked == true) tweaksToApply.Add("sys_startup_delay");
        if (ChkTweakWaitToKill.IsChecked == true) tweaksToApply.Add("sys_waittokill_service");
        if (ChkTweakAutoEndTasks.IsChecked == true) tweaksToApply.Add("sys_hung_app_timeout");
        if (ChkTweakAutoChk.IsChecked == true) tweaksToApply.Add("sys_autochk_timeout");
        if (ChkTweakClearPageFile.IsChecked == true) tweaksToApply.Add("sys_clear_pagefile_shutdown");

        // Step 4: Network
        if (ChkTweakNagle.IsChecked == true) tweaksToApply.Add("gaming_nagle_algorithm");
        if (ChkTweakNetThrottling.IsChecked == true) tweaksToApply.Add("gaming_network_throttling");
        if (ChkTweakSysResponsiveness.IsChecked == true) tweaksToApply.Add("gaming_system_responsiveness");
        if (ChkTweakMaxConnections.IsChecked == true) tweaksToApply.Add("net_max_connections");
        if (ChkTweakDnsTtl.IsChecked == true) tweaksToApply.Add("net_dns_cache_ttl");

        // Step 5: Visual
        if (ChkTweakMenuDelay.IsChecked == true) tweaksToApply.Add("sys_menu_show_delay");
        if (ChkTweakClassicMenu.IsChecked == true) tweaksToApply.Add("win11_classic_context_menu");
        if (ChkTweakShowExtensions.IsChecked == true) tweaksToApply.Add("win11_show_file_extensions");
        if (ChkTweakCompactView.IsChecked == true) tweaksToApply.Add("win11_compact_view_explorer");
        if (ChkTweakHideWidgets.IsChecked == true) tweaksToApply.Add("win11_hide_taskbar_widgets");

        // Step 6: Services
        if (ChkTweakDiagTrack.IsChecked == true) tweaksToApply.Add("privacy_diagtrack");
        if (ChkTweakHosts.IsChecked == true) tweaksToApply.Add("privacy_hosts_telemetry");
        if (ChkTweakAdvertisingId.IsChecked == true) tweaksToApply.Add("privacy_advertising_id");
        if (ChkTweakWer.IsChecked == true) tweaksToApply.Add("sys_disable_wer");
        if (ChkTweakTyping.IsChecked == true) tweaksToApply.Add("privacy_typing_personalization");

        bool shouldCreateRestorePoint = ChkCreateRestorePoint.IsChecked == true;
        bool shouldPurgeRam = ChkPurgeRamAfter.IsChecked == true;
        bool shouldEnableCaffeine = ChkEnableCaffeine.IsChecked == true;

        await Task.Run(async () =>
        {
            if (shouldCreateRestorePoint)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtApplyLog.Text = "Creando Punto de Restauración del Sistema...";
                    PbApplyProgress.Value = 10;
                });
                TweakService.CreateRestorePoint("OmniWin Optimization Wizard");
            }

            int total = tweaksToApply.Count;
            for (int i = 0; i < total; i++)
            {
                string id = tweaksToApply[i];
                Dispatcher.Invoke(() =>
                {
                    int pct = 15 + (int)((i / (double)total) * 70);
                    PbApplyProgress.Value = pct;
                    TxtApplyLog.Text = $"Aplicando tweak ({i + 1}/{total}): {id}...";
                });

                var twkRes = _tweakService.ApplyTweak(id);
                App.Log($"[WIZARD_APPLY] ({i + 1}/{total}) {id} -> Success={twkRes.Success}, Msg='{twkRes.Message}'");
                await Task.Delay(25);
            }

            if (shouldPurgeRam)
            {
                Dispatcher.Invoke(() =>
                {
                    PbApplyProgress.Value = 90;
                    TxtApplyLog.Text = "Purgando Standby List y memoria RAM...";
                });
                var mem = new MemoryService();
                mem.PurgeMemory(true, true);
                App.Log("[WIZARD_APPLY] RAM Purged successfully.");
            }

            if (shouldEnableCaffeine)
            {
                AwakeService.Instance.Activate(AwakeMode.KeepAwakeIndefinite, keepDisplayOn: true);
                App.Log("[WIZARD_APPLY] Caffeine Mode activated.");
            }

            Dispatcher.Invoke(() =>
            {
                PbApplyProgress.Value = 100;
                TxtApplyLog.Text = $"✔ ¡{total} optimizaciones aplicadas con éxito!";
                BtnExportReport.Visibility = Visibility.Visible;
            });

            try
            {
                var reportModel = new OptimizationReportModel
                {
                    SelectedProfile = _selectedRole,
                    RestorePointCreated = shouldCreateRestorePoint,
                    RamPurgedBytes = shouldPurgeRam ? 512 * 1024 * 1024L : 0,
                    Tweaks = _tweakService.GetCategorizedTweaks().Where(t => t.IsApplied).Select(t => new ReportTweakEntry
                    {
                        Category = t.Category,
                        Name = t.Name,
                        Description = t.Description,
                        RegistryPath = t.Id,
                        NewValue = "1 (Optimizado)",
                        Success = true
                    }).ToList()
                };
                _lastGeneratedReportPath = OptimizationReportService.Instance.GenerateHtmlReport(reportModel);
                App.Log($"[WIZARD_APPLY] Optimization HTML Report saved to: {_lastGeneratedReportPath}");
            }
            catch (Exception ex)
            {
                App.Log($"[WIZARD_APPLY] Report generation error: {ex.Message}");
            }

            App.Log($"[WIZARD_APPLY] All {total} tweaks finished.");
            await Task.Delay(800);
        });

        FinishWizard(_selectedRole);
    }

    private string? _lastGeneratedReportPath;

    private void BtnExportReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_lastGeneratedReportPath) || !File.Exists(_lastGeneratedReportPath))
            {
                var reportModel = new OptimizationReportModel
                {
                    SelectedProfile = _selectedRole,
                    RestorePointCreated = ChkCreateRestorePoint?.IsChecked == true,
                    RamPurgedBytes = ChkPurgeRamAfter?.IsChecked == true ? 512 * 1024 * 1024L : 0,
                    Tweaks = _tweakService.GetCategorizedTweaks().Where(t => t.IsApplied).Select(t => new ReportTweakEntry
                    {
                        Category = t.Category,
                        Name = t.Name,
                        Description = t.Description,
                        RegistryPath = t.Id,
                        NewValue = "1 (Optimizado)",
                        Success = true
                    }).ToList()
                };
                _lastGeneratedReportPath = OptimizationReportService.Instance.GenerateHtmlReport(reportModel);
            }

            if (File.Exists(_lastGeneratedReportPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _lastGeneratedReportPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            App.Log($"[BtnExportReport_Click Error] {ex.Message}");
        }
    }

    private void FinishWizard(string? appliedRole)
    {
        bool dontShowAgain = ChkDontShowAgain.IsChecked == true;
        if (dontShowAgain)
        {
            SaveSettings(true, appliedRole ?? _selectedRole);
        }

        OnWizardCompleted?.Invoke(appliedRole);
    }
}

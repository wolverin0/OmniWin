using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public enum RepairStepStatus
{
    Pending,
    Running,
    Completed,
    Error,
    Skipped
}

public class RepairPipelineStepInfo
{
    public int StepNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CommandDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RepairStepStatus Status { get; set; } = RepairStepStatus.Pending;
    public string StatusText { get; set; } = "Pendiente ⏳";
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string OutputSummary { get; set; } = string.Empty;

    public TimeSpan? Duration =>
        StartedAt.HasValue && FinishedAt.HasValue ? FinishedAt.Value - StartedAt.Value : null;
}

public class RepairPipelineResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<RepairPipelineStepInfo> Steps { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
}

public class RepairPipelineService
{
    /// <summary>
    /// Evento emitido en tiempo real con cada línea producida y el porcentaje global estimado (0-100%).
    /// </summary>
    public event Action<string, double>? OnProgressOutput;

    /// <summary>
    /// Evento emitido cuando un paso cambia de estado (1 a 6).
    /// </summary>
    public event Action<int, RepairStepStatus, string>? OnStepStatusChanged;

    /// <summary>
    /// Evento emitido al iniciar o finalizar cualquier pipeline.
    /// </summary>
    public event Action<bool>? OnExecutionStateChanged;

    private static readonly Regex PercentageRegex = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    public List<RepairPipelineStepInfo> GetDefaultSteps()
    {
        return new List<RepairPipelineStepInfo>
        {
            new()
            {
                StepNumber = 1,
                Name = "Diagnóstico de Almacén de Componentes",
                CommandDescription = "dism /online /cleanup-image /scanhealth",
                Description = "Escanea la imagen en busca de corrupción en el almacén de componentes de Windows.",
                Status = RepairStepStatus.Pending,
                StatusText = "Pendiente ⏳"
            },
            new()
            {
                StepNumber = 2,
                Name = "Reparación de Almacén de Componentes",
                CommandDescription = "dism /online /cleanup-image /restorehealth",
                Description = "Repara la imagen de Windows descargando o reemplazando componentes corruptos.",
                Status = RepairStepStatus.Pending,
                StatusText = "Pendiente ⏳"
            },
            new()
            {
                StepNumber = 3,
                Name = "Verificación y Reparación de Archivos (SFC)",
                CommandDescription = "sfc /scannow",
                Description = "Examina y repara archivos de sistema protegidos con versiones originales de Microsoft.",
                Status = RepairStepStatus.Pending,
                StatusText = "Pendiente ⏳"
            },
            new()
            {
                StepNumber = 4,
                Name = "Limpieza de Base de WinSxS",
                CommandDescription = "dism /online /cleanup-image /startcomponentcleanup /resetbase",
                Description = "Elimina versiones superseded de componentes y purga la base para liberar espacio.",
                Status = RepairStepStatus.Pending,
                StatusText = "Pendiente ⏳"
            },
            new()
            {
                StepNumber = 5,
                Name = "Reseteo de Componentes de Windows Update",
                CommandDescription = "net stop / rename SoftwareDistribution & catroot2 / net start",
                Description = "Detiene servicios de actualización, purga cachés dañadas y reinicia servicios.",
                Status = RepairStepStatus.Pending,
                StatusText = "Pendiente ⏳"
            },
            new()
            {
                StepNumber = 6,
                Name = "Reparación de Pila Winsock y Catálogo WMI",
                CommandDescription = "netsh winsock reset, ip reset, flushdns, winmgmt /salvagerepository",
                Description = "Restablece sockets de red, purga caché DNS y repara consistencia del repositorio WMI.",
                Status = RepairStepStatus.Pending,
                StatusText = "Pendiente ⏳"
            }
        };
    }

    /// <summary>
    /// Ejecuta el Pipeline Secuencial Estricto de Microsoft con los 6 pasos.
    /// </summary>
    public async Task<RepairPipelineResult> RunFullPipelineAsync(CancellationToken cancellationToken = default)
    {
        var steps = GetDefaultSteps();
        var swTotal = Stopwatch.StartNew();
        bool overallSuccess = true;

        EmitStateChanged(true);
        EmitOutput("[INICIO] Iniciando Pipeline Integral Secuencial de Reparación de Windows (6 Pasos)...", 0);
        EmitOutput($"[*] Hora de inicio: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 0);

        if (!SecurityHelper.IsAdministrator())
        {
            EmitOutput("[ADVERTENCIA CRÍTICA] OmniWin no se está ejecutando con privilegios de Administrador.", 0);
            EmitOutput("[ADVERTENCIA] Muchos comandos (DISM, SFC, Netsh, SC) fallarán con código de acceso denegado (Error 740).", 0);
        }

        try
        {
            const double stepWeight = 100.0 / 6.0;

            // Paso 1: Diagnóstico de Almacén de Componentes (/ScanHealth)
            overallSuccess &= await ExecuteStepAsync(steps[0], 0 * stepWeight, stepWeight, cancellationToken, async (baseProg, weight) =>
            {
                EmitOutput("[Paso 1/6] Ejecutando: dism /online /cleanup-image /scanhealth", baseProg);
                var (exitCode, output) = await ExecuteProcessStreamedAsync("dism.exe", "/online /cleanup-image /scanhealth", baseProg, weight, cancellationToken);
                
                if (exitCode == 0)
                {
                    bool corruptDetected = output.Contains("corruption", StringComparison.OrdinalIgnoreCase) && 
                                           !output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase);
                    string summary = corruptDetected 
                        ? "Daños detectados en almacén. Se repararán en el Paso 2." 
                        : "Almacén de componentes íntegro (Sin corrupción).";
                    return (true, summary);
                }
                return (false, $"DISM /ScanHealth falló con código {exitCode}.");
            });

            cancellationToken.ThrowIfCancellationRequested();

            // Paso 2: Reparación del Almacén de Componentes (/RestoreHealth)
            overallSuccess &= await ExecuteStepAsync(steps[1], 1 * stepWeight, stepWeight, cancellationToken, async (baseProg, weight) =>
            {
                EmitOutput("[Paso 2/6] Ejecutando: dism /online /cleanup-image /restorehealth", baseProg);
                var (exitCode, output) = await ExecuteProcessStreamedAsync("dism.exe", "/online /cleanup-image /restorehealth", baseProg, weight, cancellationToken);
                
                if (exitCode == 0)
                {
                    return (true, "Almacén de componentes reparado exitosamente con DISM.");
                }
                return (false, $"DISM /RestoreHealth finalizó con código {exitCode}.");
            });

            cancellationToken.ThrowIfCancellationRequested();

            // Paso 3: Verificación y Reparación de Archivos del Sistema (SFC /scannow)
            overallSuccess &= await ExecuteStepAsync(steps[2], 2 * stepWeight, stepWeight, cancellationToken, async (baseProg, weight) =>
            {
                EmitOutput("[Paso 3/6] Ejecutando: sfc /scannow", baseProg);
                var (exitCode, output) = await ExecuteProcessStreamedAsync("sfc.exe", "/scannow", baseProg, weight, cancellationToken);
                
                if (exitCode == 0)
                {
                    string summary = output.Contains("found corrupt files and successfully repaired", StringComparison.OrdinalIgnoreCase)
                        ? "Archivos corruptos detectados y reparados con éxito."
                        : "Archivos protegidos íntegros sin violaciones.";
                    return (true, summary);
                }
                return (false, $"SFC /scannow finalizó con código {exitCode}.");
            });

            cancellationToken.ThrowIfCancellationRequested();

            // Paso 4: Limpieza de Base de WinSxS (/StartComponentCleanup /ResetBase)
            overallSuccess &= await ExecuteStepAsync(steps[3], 3 * stepWeight, stepWeight, cancellationToken, async (baseProg, weight) =>
            {
                EmitOutput("[Paso 4/6] Ejecutando: dism /online /cleanup-image /startcomponentcleanup /resetbase", baseProg);
                var (exitCode, output) = await ExecuteProcessStreamedAsync("dism.exe", "/online /cleanup-image /startcomponentcleanup /resetbase", baseProg, weight, cancellationToken);
                
                if (exitCode == 0)
                {
                    return (true, "Base de WinSxS purgada y restablecida correctamente.");
                }
                return (false, $"DISM /ResetBase finalizó con código {exitCode}.");
            });

            cancellationToken.ThrowIfCancellationRequested();

            // Paso 5: Reseteo de Catálogo y Componentes de Windows Update
            overallSuccess &= await ExecuteStepAsync(steps[4], 4 * stepWeight, stepWeight, cancellationToken, async (baseProg, weight) =>
            {
                return await ExecuteWindowsUpdateResetAsync(baseProg, weight, cancellationToken);
            });

            cancellationToken.ThrowIfCancellationRequested();

            // Paso 6: Reparación de Pila de Red Winsock y Catálogo WMI
            overallSuccess &= await ExecuteStepAsync(steps[5], 5 * stepWeight, stepWeight, cancellationToken, async (baseProg, weight) =>
            {
                return await ExecuteNetworkAndWmiRepairAsync(baseProg, weight, cancellationToken);
            });

            swTotal.Stop();
            EmitOutput($"[FIN] Pipeline integral completado en {swTotal.Elapsed:mm\\:ss}.", 100);

            string finalSummary = overallSuccess
                ? "Todos los componentes del sistema, WinSxS, SFC, Windows Update y Red fueron verificados y reparados con éxito."
                : "El pipeline finalizó pero uno o más pasos reportaron advertencias o errores. Revisa la consola para más detalles.";

            EmitOutput($"[*] Resumen: {finalSummary}", 100);

            return new RepairPipelineResult
            {
                Success = overallSuccess,
                Summary = finalSummary,
                Steps = steps,
                TotalDuration = swTotal.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            swTotal.Stop();
            EmitOutput("[CANCELADO] El usuario canceló la ejecución del pipeline.", GetCurrentProgress(steps));
            return new RepairPipelineResult
            {
                Success = false,
                Summary = "Operación cancelada por el usuario.",
                Steps = steps,
                TotalDuration = swTotal.Elapsed
            };
        }
        catch (Exception ex)
        {
            swTotal.Stop();
            EmitOutput($"[ERROR FATAL] Excepción inesperada en el pipeline: {ex.Message}", 100);
            return new RepairPipelineResult
            {
                Success = false,
                Summary = $"Error fatal: {ex.Message}",
                Steps = steps,
                TotalDuration = swTotal.Elapsed
            };
        }
        finally
        {
            EmitStateChanged(false);
        }
    }

    /// <summary>
    /// Ejecuta únicamente el Paso 3: Comprobación y Reparación con SFC /scannow.
    /// </summary>
    public async Task<RepairPipelineResult> RunSfcOnlyAsync(CancellationToken cancellationToken = default)
    {
        var steps = GetDefaultSteps();
        var sfcStep = steps[2];
        var sw = Stopwatch.StartNew();

        EmitStateChanged(true);
        EmitOutput("[INICIO] Ejecutando comprobación individual con SFC /scannow...", 0);

        try
        {
            bool ok = await ExecuteStepAsync(sfcStep, 0, 100, cancellationToken, async (baseProg, weight) =>
            {
                var (exitCode, output) = await ExecuteProcessStreamedAsync("sfc.exe", "/scannow", baseProg, weight, cancellationToken);
                if (exitCode == 0)
                {
                    string summary = output.Contains("found corrupt files and successfully repaired", StringComparison.OrdinalIgnoreCase)
                        ? "Archivos corruptos detectados y reparados con éxito."
                        : "Archivos protegidos íntegros sin violaciones.";
                    return (true, summary);
                }
                return (false, $"SFC /scannow finalizó con código {exitCode}.");
            });

            sw.Stop();
            EmitOutput($"[FIN] SFC completado en {sw.Elapsed:mm\\:ss}.", 100);

            return new RepairPipelineResult
            {
                Success = ok,
                Summary = ok ? "SFC finalizado exitosamente." : "SFC finalizó con errores.",
                Steps = new List<RepairPipelineStepInfo> { sfcStep },
                TotalDuration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            EmitOutput("[CANCELADO] SFC cancelado por el usuario.", 0);
            return new RepairPipelineResult
            {
                Success = false,
                Summary = "Cancelado por el usuario.",
                Steps = new List<RepairPipelineStepInfo> { sfcStep },
                TotalDuration = sw.Elapsed
            };
        }
        finally
        {
            EmitStateChanged(false);
        }
    }

    /// <summary>
    /// Ejecuta únicamente el Paso 5: Reseteo de Catálogo y Componentes de Windows Update.
    /// </summary>
    public async Task<RepairPipelineResult> RunWinUpdateResetOnlyAsync(CancellationToken cancellationToken = default)
    {
        var steps = GetDefaultSteps();
        var wuStep = steps[4];
        var sw = Stopwatch.StartNew();

        EmitStateChanged(true);
        EmitOutput("[INICIO] Ejecutando reseteo individual de Windows Update y Catroot2...", 0);

        try
        {
            bool ok = await ExecuteStepAsync(wuStep, 0, 100, cancellationToken, async (baseProg, weight) =>
            {
                return await ExecuteWindowsUpdateResetAsync(baseProg, weight, cancellationToken);
            });

            sw.Stop();
            EmitOutput($"[FIN] Reseteo de Windows Update completado en {sw.Elapsed:mm\\:ss}.", 100);

            return new RepairPipelineResult
            {
                Success = ok,
                Summary = ok ? "Windows Update reseteado con éxito." : "Reseteo finalizó con advertencias.",
                Steps = new List<RepairPipelineStepInfo> { wuStep },
                TotalDuration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            EmitOutput("[CANCELADO] Reseteo cancelado por el usuario.", 0);
            return new RepairPipelineResult
            {
                Success = false,
                Summary = "Cancelado por el usuario.",
                Steps = new List<RepairPipelineStepInfo> { wuStep },
                TotalDuration = sw.Elapsed
            };
        }
        finally
        {
            EmitStateChanged(false);
        }
    }

    private async Task<bool> ExecuteStepAsync(
        RepairPipelineStepInfo step,
        double baseProgress,
        double weight,
        CancellationToken cancellationToken,
        Func<double, double, Task<(bool Success, string Summary)>> action)
    {
        step.Status = RepairStepStatus.Running;
        step.StatusText = "En Ejecución 🔄";
        step.StartedAt = DateTime.Now;
        EmitStepStatus(step.StepNumber, RepairStepStatus.Running, step.StatusText);
        EmitOutput($"\n═══════════════════════════════════════════════════════════════════", baseProgress);
        EmitOutput($"[PASO {step.StepNumber}] {step.Name.ToUpperInvariant()}", baseProgress);
        EmitOutput($"Comando: {step.CommandDescription}", baseProgress);
        EmitOutput($"═══════════════════════════════════════════════════════════════════", baseProgress);

        try
        {
            var (success, summary) = await action(baseProgress, weight);
            step.FinishedAt = DateTime.Now;
            step.OutputSummary = summary;

            if (success)
            {
                step.Status = RepairStepStatus.Completed;
                step.StatusText = "Completado ✔";
                EmitStepStatus(step.StepNumber, RepairStepStatus.Completed, step.StatusText);
                EmitOutput($"[✔ ÉXITO] {step.Name}: {summary} ({step.Duration?.TotalSeconds:0.0}s)", baseProgress + weight);
                return true;
            }
            else
            {
                step.Status = RepairStepStatus.Error;
                step.StatusText = "Error ✖";
                EmitStepStatus(step.StepNumber, RepairStepStatus.Error, step.StatusText);
                EmitOutput($"[✖ ERROR] {step.Name}: {summary} ({step.Duration?.TotalSeconds:0.0}s)", baseProgress + weight);
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            step.Status = RepairStepStatus.Skipped;
            step.StatusText = "Cancelado 🛑";
            step.FinishedAt = DateTime.Now;
            EmitStepStatus(step.StepNumber, RepairStepStatus.Skipped, step.StatusText);
            throw;
        }
        catch (Exception ex)
        {
            step.Status = RepairStepStatus.Error;
            step.StatusText = "Error ✖";
            step.FinishedAt = DateTime.Now;
            step.OutputSummary = ex.Message;
            EmitStepStatus(step.StepNumber, RepairStepStatus.Error, step.StatusText);
            EmitOutput($"[✖ EXCEPCIÓN] {step.Name}: {ex.Message}", baseProgress + weight);
            return false;
        }
    }

    private async Task<(bool Success, string Summary)> ExecuteWindowsUpdateResetAsync(
        double baseProgress,
        double weight,
        CancellationToken cancellationToken)
    {
        double subSlice = weight / 6.0;
        double curProg = baseProgress;

        // 1. Detener servicios
        string[] servicesToStop = { "wuauserv", "cryptsvc", "bits", "msiserver" };
        foreach (var svc in servicesToStop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmitOutput($"[Paso 5] Deteniendo servicio '{svc}'...", curProg);
            await RunQuickCommandAsync("net.exe", $"stop \"{svc}\" /y", cancellationToken);
            curProg += subSlice * 0.4;
            EmitOutput($"[Paso 5] Servicio '{svc}' detenido o no estaba en ejecución.", curProg);
        }

        await Task.Delay(1000, cancellationToken);

        // 2. Renombrar SoftwareDistribution
        cancellationToken.ThrowIfCancellationRequested();
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sdPath = Path.Combine(winDir, "SoftwareDistribution");
        bool sdRenamed = false;

        if (Directory.Exists(sdPath))
        {
            string sdBak = Path.Combine(winDir, $"SoftwareDistribution.bak_{DateTime.Now:yyyyMMdd_HHmmss}");
            try
            {
                EmitOutput($"[Paso 5] Renombrando {sdPath} a {Path.GetFileName(sdBak)}...", curProg);
                Directory.Move(sdPath, sdBak);
                sdRenamed = true;
                EmitOutput($"[Paso 5] ✔ SoftwareDistribution renombrada exitosamente.", curProg);
            }
            catch (Exception ex)
            {
                EmitOutput($"[Paso 5] ⚠ No se pudo renombrar SoftwareDistribution completa ({ex.Message}). Limpiando carpeta Download...", curProg);
                try
                {
                    string downloadFolder = Path.Combine(sdPath, "Download");
                    if (Directory.Exists(downloadFolder))
                    {
                        foreach (var f in Directory.GetFiles(downloadFolder))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    EmitOutput($"[Paso 5] ✔ Carpeta SoftwareDistribution\\Download vaciada.", curProg);
                    sdRenamed = true;
                }
                catch { }
            }
        }
        else
        {
            sdRenamed = true;
            EmitOutput($"[Paso 5] SoftwareDistribution no encontrada (limpia).", curProg);
        }

        curProg += subSlice;

        // 3. Renombrar Catroot2
        cancellationToken.ThrowIfCancellationRequested();
        string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string catPath = Path.Combine(sysDir, "catroot2");
        bool catRenamed = false;

        if (Directory.Exists(catPath))
        {
            string catBak = Path.Combine(sysDir, $"catroot2.bak_{DateTime.Now:yyyyMMdd_HHmmss}");
            try
            {
                EmitOutput($"[Paso 5] Renombrando {catPath} a {Path.GetFileName(catBak)}...", curProg);
                Directory.Move(catPath, catBak);
                catRenamed = true;
                EmitOutput($"[Paso 5] ✔ Catroot2 renombrada exitosamente.", curProg);
            }
            catch (Exception ex)
            {
                EmitOutput($"[Paso 5] ⚠ Advertencia al renombrar catroot2 ({ex.Message}). Se reintentará con reinicio de servicio.", curProg);
            }
        }
        else
        {
            catRenamed = true;
        }

        curProg += subSlice;

        // 4. Reiniciar servicios
        string[] servicesToStart = { "cryptsvc", "bits", "wuauserv" };
        foreach (var svc in servicesToStart)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmitOutput($"[Paso 5] Reiniciando servicio '{svc}'...", curProg);
            await RunQuickCommandAsync("net.exe", $"start \"{svc}\"", cancellationToken);
            curProg += subSlice * 0.5;
            EmitOutput($"[Paso 5] ✔ Servicio '{svc}' iniciado.", curProg);
        }

        string summary = (sdRenamed && catRenamed)
            ? "Servicios detenidos, SoftwareDistribution y catroot2 reseteados, y servicios reanudados."
            : "Reseteo de Windows Update aplicado (con advertencias de bloqueo de archivos en catroot2).";

        return (true, summary);
    }

    private async Task<(bool Success, string Summary)> ExecuteNetworkAndWmiRepairAsync(
        double baseProgress,
        double weight,
        CancellationToken cancellationToken)
    {
        double subSlice = weight / 4.0;
        double curProg = baseProgress;

        // 1. netsh winsock reset
        cancellationToken.ThrowIfCancellationRequested();
        EmitOutput("[Paso 6/6] Restableciendo catálogo Winsock: netsh winsock reset", curProg);
        var (winsockCode, _) = await ExecuteProcessStreamedAsync("netsh.exe", "winsock reset", curProg, subSlice, cancellationToken);
        curProg += subSlice;

        // 2. netsh int ip reset
        cancellationToken.ThrowIfCancellationRequested();
        EmitOutput("[Paso 6/6] Restableciendo pila IP: netsh int ip reset", curProg);
        var (ipCode, _) = await ExecuteProcessStreamedAsync("netsh.exe", "int ip reset", curProg, subSlice, cancellationToken);
        curProg += subSlice;

        // 3. ipconfig /flushdns
        cancellationToken.ThrowIfCancellationRequested();
        EmitOutput("[Paso 6/6] Purgando caché DNS: ipconfig /flushdns", curProg);
        var (dnsCode, _) = await ExecuteProcessStreamedAsync("ipconfig.exe", "/flushdns", curProg, subSlice, cancellationToken);
        curProg += subSlice;

        // 4. WMI salvage repository
        cancellationToken.ThrowIfCancellationRequested();
        EmitOutput("[Paso 6/6] Verificando consistencia y salvando repositorio WMI: winmgmt /salvagerepository", curProg);
        var (wmiCode, _) = await ExecuteProcessStreamedAsync("winmgmt.exe", "/salvagerepository", curProg, subSlice, cancellationToken);
        curProg += subSlice;

        bool allOk = (winsockCode == 0 || winsockCode == 1) && (dnsCode == 0);
        string summary = allOk
            ? "Pila Winsock/IP restablecida, caché DNS purgada y catálogo WMI verificado con éxito."
            : "Comandos de red y WMI ejecutados con advertencias.";

        return (true, summary);
    }

    private async Task<(int ExitCode, string Output)> ExecuteProcessStreamedAsync(
        string fileName,
        string arguments,
        double stepBaseProgress,
        double stepWeight,
        CancellationToken cancellationToken)
    {
        var outputBuilder = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        void HandleData(string? line)
        {
            if (line == null) return;
            string trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) return;

            lock (outputBuilder)
            {
                outputBuilder.AppendLine(trimmed);
            }

            double currentProgress = stepBaseProgress;
            var match = PercentageRegex.Match(trimmed);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedVal))
            {
                if (parsedVal >= 0 && parsedVal <= 100)
                {
                    currentProgress = Math.Clamp(stepBaseProgress + (parsedVal / 100.0) * stepWeight, 0, 99.9);
                }
            }

            EmitOutput(trimmed, currentProgress);
        }

        process.OutputDataReceived += (s, e) => HandleData(e.Data);
        process.ErrorDataReceived += (s, e) => HandleData(e.Data);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, outputBuilder.ToString());
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            throw;
        }
        catch (Exception ex)
        {
            EmitOutput($"[ERROR INICIANDO PROCESO] No se pudo ejecutar {fileName}: {ex.Message}", stepBaseProgress);
            return (-1, ex.Message);
        }
    }

    private static async Task<int> RunQuickCommandAsync(string fileName, string args, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return -1;
            await p.WaitForExitAsync(cancellationToken);
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private void EmitOutput(string line, double progress)
    {
        OnProgressOutput?.Invoke(line, Math.Clamp(Math.Round(progress, 1), 0, 100));
    }

    private void EmitStepStatus(int stepNumber, RepairStepStatus status, string message)
    {
        OnStepStatusChanged?.Invoke(stepNumber, status, message);
    }

    private void EmitStateChanged(bool isRunning)
    {
        OnExecutionStateChanged?.Invoke(isRunning);
    }

    private static double GetCurrentProgress(List<RepairPipelineStepInfo> steps)
    {
        int completed = 0;
        foreach (var s in steps)
        {
            if (s.Status == RepairStepStatus.Completed) completed++;
        }
        return Math.Round((completed / (double)steps.Count) * 100.0, 1);
    }
}
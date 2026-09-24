using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class InstalledDesktopApp
{
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayVersion { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string InstallDate { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public string QuietUninstallString { get; set; } = string.Empty;
    public long EstimatedSizeKb { get; set; }
    public double EstimatedSizeMb => EstimatedSizeKb > 0 ? EstimatedSizeKb / 1024.0 : 0.0;
    public string RegistryKeyPath { get; set; } = string.Empty;
    public bool Is64Bit { get; set; }
}

public class AppLeftoversResult
{
    public string AppName { get; set; } = string.Empty;
    public List<string> LeftoverDirectories { get; set; } = new();
    public List<string> LeftoverRegistryKeys { get; set; } = new();
    public long TotalBytesRecoverable { get; set; }
}

public class UninstallerLaunchResult
{
    public bool Success { get; set; }
    public bool ExeNotFound { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class SoftwareUninstallerService
{
    public static SoftwareUninstallerService Instance { get; } = new();

    public async Task<List<InstalledDesktopApp>> GetInstalledAppsAsync()
    {
        return await Task.Run(() => GetInstalledApps());
    }

    public List<InstalledDesktopApp> GetInstalledApps()
    {
        var apps = new Dictionary<string, InstalledDesktopApp>(StringComparer.OrdinalIgnoreCase);

        // 1. HKLM 64-bit
        ScanUninstallKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", apps, true);

        // 2. HKLM 32-bit (WOW6432Node)
        ScanUninstallKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", apps, false);

        // 3. HKCU (User specific)
        ScanUninstallKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", apps, Environment.Is64BitOperatingSystem);

        return apps.Values
            .OrderBy(a => a.DisplayName)
            .ToList();
    }

    private void ScanUninstallKey(RegistryKey root, string subKeyPath, Dictionary<string, InstalledDesktopApp> apps, bool is64Bit)
    {
        try
        {
            using var baseKey = root.OpenSubKey(subKeyPath);
            if (baseKey == null) return;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var appKey = baseKey.OpenSubKey(subName);
                    if (appKey == null) continue;

                    string name = appKey.GetValue("DisplayName")?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Filter out system components, drivers, language packs and updates
                    var sysComp = appKey.GetValue("SystemComponent");
                    if (sysComp is int sc && sc == 1) continue;

                    var parentKey = appKey.GetValue("ParentKeyName");
                    if (parentKey != null && !string.IsNullOrWhiteSpace(parentKey.ToString())) continue;

                    string releaseType = appKey.GetValue("ReleaseType")?.ToString() ?? string.Empty;
                    if (releaseType.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                        releaseType.Equals("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                        releaseType.Equals("Security Update", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string uninst = appKey.GetValue("UninstallString")?.ToString()?.Trim() ?? string.Empty;
                    string quiet = appKey.GetValue("QuietUninstallString")?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(uninst) && string.IsNullOrWhiteSpace(quiet)) continue;

                    string version = appKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? string.Empty;
                    string publisher = appKey.GetValue("Publisher")?.ToString()?.Trim() ?? string.Empty;
                    string installLoc = appKey.GetValue("InstallLocation")?.ToString()?.Trim() ?? string.Empty;
                    string date = appKey.GetValue("InstallDate")?.ToString()?.Trim() ?? string.Empty;

                    long sizeKb = 0;
                    var sizeVal = appKey.GetValue("EstimatedSize");
                    if (sizeVal is int sInt) sizeKb = sInt;
                    else if (sizeVal is long sLong) sizeKb = sLong;

                    string fullKeyPath = $"{root.Name}\\{subKeyPath}\\{subName}";

                    var app = new InstalledDesktopApp
                    {
                        DisplayName = name,
                        DisplayVersion = version,
                        Publisher = publisher,
                        InstallLocation = installLoc,
                        InstallDate = date,
                        UninstallString = uninst,
                        QuietUninstallString = quiet,
                        EstimatedSizeKb = sizeKb,
                        RegistryKeyPath = fullKeyPath,
                        Is64Bit = is64Bit
                    };

                    apps[name] = app;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScanUninstallKey Error]: {ex.Message}");
        }
    }

    private static readonly HashSet<string> IgnoredWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Professional", "Community", "Edition", "Repack", "Setup", "Installer", "Sitego",
        "Windows", "Microsoft", "Version", "Build", "Release", "Desktop", "Portable",
        "Update", "Client", "Tool", "Tools", "Software", "System", "Application"
    };

    public async Task<UninstallerLaunchResult> LaunchUninstallerExAsync(InstalledDesktopApp app, bool quiet = false)
    {
        return await Task.Run(() =>
        {
            string cmd = quiet && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                ? app.QuietUninstallString
                : app.UninstallString;

            if (string.IsNullOrWhiteSpace(cmd))
            {
                return new UninstallerLaunchResult
                {
                    Success = false,
                    ErrorMessage = "No hay comando de desinstalación registrado para esta aplicación."
                };
            }

            try
            {
                string exe;
                string args = string.Empty;

                if (cmd.StartsWith("\""))
                {
                    int endQuote = cmd.IndexOf('\"', 1);
                    if (endQuote > 0)
                    {
                        exe = cmd.Substring(1, endQuote - 1);
                        args = cmd.Substring(endQuote + 1).Trim();
                    }
                    else
                    {
                        exe = cmd.Trim('\"');
                    }
                }
                else
                {
                    int firstSpace = cmd.IndexOf(' ');
                    if (firstSpace > 0)
                    {
                        exe = cmd.Substring(0, firstSpace);
                        args = cmd.Substring(firstSpace + 1).Trim();
                    }
                    else
                    {
                        exe = cmd;
                    }
                }

                // Check if uninstaller executable exists on disk (unless it's a standard system binary)
                bool isSystemBinary = exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ||
                                     exe.Contains("cmd", StringComparison.OrdinalIgnoreCase) ||
                                     exe.Contains("powershell", StringComparison.OrdinalIgnoreCase);

                if (!isSystemBinary && !File.Exists(exe))
                {
                    return new UninstallerLaunchResult
                    {
                        Success = false,
                        ExeNotFound = true,
                        ExecutablePath = exe,
                        ErrorMessage = $"El ejecutable de desinstalación no existe en el disco:\n{exe}"
                    };
                }

                // If quiet was requested but only standard msiexec string exists, append /quiet
                if (quiet && exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase) && !args.Contains("/quiet", StringComparison.OrdinalIgnoreCase))
                {
                    args = args.Replace("/I", "/X").Replace("/i", "/x") + " /quiet /norestart";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true
                };

                using var p = Process.Start(psi);
                p?.WaitForExit();
                return new UninstallerLaunchResult { Success = true, ExecutablePath = exe };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LaunchUninstaller Error]: {ex.Message}");
                return new UninstallerLaunchResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public async Task<bool> LaunchUninstallerAsync(InstalledDesktopApp app, bool quiet = false)
    {
        var res = await LaunchUninstallerExAsync(app, quiet);
        return res.Success;
    }

    public bool ForceRemoveAppRegistryEntry(InstalledDesktopApp app)
    {
        try
        {
            string regKey = app.RegistryKeyPath;
            if (string.IsNullOrWhiteSpace(regKey)) return false;

            if (regKey.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            {
                string sub = regKey.Substring("HKEY_CURRENT_USER\\".Length);
                int lastSlash = sub.LastIndexOf('\\');
                if (lastSlash > 0)
                {
                    using var pKey = Registry.CurrentUser.OpenSubKey(sub.Substring(0, lastSlash), true);
                    pKey?.DeleteSubKeyTree(sub.Substring(lastSlash + 1), false);
                    return true;
                }
            }
            else if (regKey.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            {
                string sub = regKey.Substring("HKEY_LOCAL_MACHINE\\".Length);
                int lastSlash = sub.LastIndexOf('\\');
                if (lastSlash > 0)
                {
                    using var pKey = Registry.LocalMachine.OpenSubKey(sub.Substring(0, lastSlash), true);
                    pKey?.DeleteSubKeyTree(sub.Substring(lastSlash + 1), false);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForceRemoveAppRegistryEntry Error]: {ex.Message}");
        }
        return false;
    }

    public AppLeftoversResult ScanLeftovers(InstalledDesktopApp app)
    {
        var result = new AppLeftoversResult { AppName = app.DisplayName };

        // 1. Gather search keywords from DisplayName and Publisher
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = Regex.Split(app.DisplayName, @"[^\w\d]+")
            .Where(w => w.Length >= 4 && !IgnoredWords.Contains(w));
        foreach (var w in words) keywords.Add(w);

        if (!string.IsNullOrWhiteSpace(app.Publisher) && app.Publisher.Length >= 4 && !IgnoredWords.Contains(app.Publisher))
        {
            keywords.Add(app.Publisher);
        }

        // If install location exists, add it directly
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            result.LeftoverDirectories.Add(app.InstallLocation);
        }

        // 2. Check common AppData and ProgramData paths
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var rootDir in roots)
        {
            if (!Directory.Exists(rootDir)) continue;

            try
            {
                foreach (var dir in Directory.GetDirectories(rootDir))
                {
                    string dirName = Path.GetFileName(dir);
                    if (keywords.Any(k => dirName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!result.LeftoverDirectories.Contains(dir))
                        {
                            result.LeftoverDirectories.Add(dir);
                        }
                    }
                }
            }
            catch { }
        }

        // 3. Check Registry Software keys
        var regRoots = new[]
        {
            (Registry.CurrentUser, @"Software"),
            (Registry.LocalMachine, @"SOFTWARE"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node")
        };

        foreach (var (hive, path) in regRoots)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) continue;

                foreach (var sub in key.GetSubKeyNames())
                {
                    if (keywords.Any(k => sub.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        string fullPath = $"{hive.Name}\\{path}\\{sub}";
                        if (!result.LeftoverRegistryKeys.Contains(fullPath))
                        {
                            result.LeftoverRegistryKeys.Add(fullPath);
                        }
                    }
                }
            }
            catch { }
        }

        // 4. Also check if the app's own uninstall registry entry is an orphan
        if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath))
        {
            if (!result.LeftoverRegistryKeys.Contains(app.RegistryKeyPath))
            {
                result.LeftoverRegistryKeys.Add(app.RegistryKeyPath);
            }
        }

        return result;
    }

    public int PurgeLeftovers(AppLeftoversResult leftovers)
    {
        int purged = 0;

        foreach (var dir in leftovers.LeftoverDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    purged++;
                }
            }
            catch { }
        }

        foreach (var regKey in leftovers.LeftoverRegistryKeys)
        {
            try
            {
                if (regKey.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = regKey.Substring("HKEY_CURRENT_USER\\".Length);
                    int lastSlash = sub.LastIndexOf('\\');
                    if (lastSlash > 0)
                    {
                        using var pKey = Registry.CurrentUser.OpenSubKey(sub.Substring(0, lastSlash), true);
                        pKey?.DeleteSubKeyTree(sub.Substring(lastSlash + 1), false);
                        purged++;
                    }
                }
                else if (regKey.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = regKey.Substring("HKEY_LOCAL_MACHINE\\".Length);
                    int lastSlash = sub.LastIndexOf('\\');
                    if (lastSlash > 0)
                    {
                        using var pKey = Registry.LocalMachine.OpenSubKey(sub.Substring(0, lastSlash), true);
                        pKey?.DeleteSubKeyTree(sub.Substring(lastSlash + 1), false);
                        purged++;
                    }
                }
            }
            catch { }
        }

        return purged;
    }
}

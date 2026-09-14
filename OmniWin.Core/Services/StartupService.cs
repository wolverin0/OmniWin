using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class StartupItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Scope { get; set; } = "User"; // "User" or "Machine"
    public bool IsEnabled { get; set; } = true;
    public bool CanToggle { get; set; } = true;
}

public class StartupActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunDisabledKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run_OmniDisabled";

    public List<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();

        // 1. CurrentUser Run (Active)
        ReadRegistryRun(Registry.CurrentUser, RunKeyPath, "User", true, items);

        // 2. CurrentUser Run (Disabled by OmniWin)
        ReadRegistryRun(Registry.CurrentUser, RunDisabledKeyPath, "User", false, items);

        // 3. LocalMachine Run (Active)
        ReadRegistryRun(Registry.LocalMachine, RunKeyPath, "Machine", true, items);

        // 4. LocalMachine Run (Disabled by OmniWin)
        ReadRegistryRun(Registry.LocalMachine, RunDisabledKeyPath, "Machine", false, items);

        // 5. User Startup Folder
        string userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        ReadFolderStartup(userStartup, "User", items);

        // 6. Common Startup Folder
        string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        ReadFolderStartup(commonStartup, "Machine", items);

        return items;
    }

    private static void ReadRegistryRun(RegistryKey root, string subKeyPath, string scope, bool isEnabled, List<StartupItem> list)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath, false);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var val = key.GetValue(valueName)?.ToString() ?? string.Empty;
                list.Add(new StartupItem
                {
                    Id = $"{root.Name}\\{subKeyPath}\\{valueName}",
                    Name = valueName,
                    Command = val,
                    Location = $"{root.Name}\\{subKeyPath}",
                    Scope = scope,
                    IsEnabled = isEnabled,
                    CanToggle = true
                });
            }
        }
        catch
        {
            // Key doesn't exist or access denied
        }
    }

    private static void ReadFolderStartup(string folderPath, string scope, List<StartupItem> list)
    {
        if (!Directory.Exists(folderPath)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".lnk" || ext == ".url" || ext == ".bat" || ext == ".cmd" || ext == ".exe")
                {
                    list.Add(new StartupItem
                    {
                        Id = file,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = folderPath,
                        Scope = scope,
                        IsEnabled = true,
                        CanToggle = false // Folder shortcuts
                    });
                }
            }
        }
        catch
        {
            // Inaccessible
        }
    }

    public StartupActionResult ToggleStartupItem(string scope, string name, bool enable)
    {
        var root = scope.Equals("Machine", StringComparison.OrdinalIgnoreCase) 
            ? Registry.LocalMachine 
            : Registry.CurrentUser;

        string sourceKeyPath = enable ? RunDisabledKeyPath : RunKeyPath;
        string targetKeyPath = enable ? RunKeyPath : RunDisabledKeyPath;

        try
        {
            using var srcKey = root.OpenSubKey(sourceKeyPath, true);
            if (srcKey == null)
                return new StartupActionResult { Success = false, Message = $"Clave de origen no encontrada: {sourceKeyPath}" };

            var value = srcKey.GetValue(name);
            if (value == null)
                return new StartupActionResult { Success = false, Message = $"Elemento de inicio '{name}' no encontrado." };

            using (var dstKey = root.CreateSubKey(targetKeyPath, true))
            {
                dstKey.SetValue(name, value);
            }

            srcKey.DeleteValue(name);

            return new StartupActionResult
            {
                Success = true,
                Message = $"Elemento '{name}' {(enable ? "habilitado" : "deshabilitado")} correctamente."
            };
        }
        catch (Exception ex)
        {
            return new StartupActionResult
            {
                Success = false,
                Message = $"Error modificando registro (¿Faltan permisos de Administrador?): {ex.Message}"
            };
        }
    }
}

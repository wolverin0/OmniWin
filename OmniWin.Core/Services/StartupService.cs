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
                bool isDisabled = file.EndsWith(".omni_disabled", StringComparison.OrdinalIgnoreCase);
                if (ext == ".lnk" || ext == ".url" || ext == ".bat" || ext == ".cmd" || ext == ".exe" || isDisabled)
                {
                    string cleanName = Path.GetFileNameWithoutExtension(file);
                    if (isDisabled) cleanName = Path.GetFileNameWithoutExtension(cleanName);

                    list.Add(new StartupItem
                    {
                        Id = file,
                        Name = cleanName,
                        Command = file,
                        Location = folderPath,
                        Scope = scope,
                        IsEnabled = !isDisabled,
                        CanToggle = true
                    });
                }
            }
        }
        catch
        {
            // Inaccessible
        }
    }

    public StartupActionResult ToggleStartupItem(StartupItem item)
    {
        if (File.Exists(item.Command) || item.Location.Contains("Startup", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string path = item.Command;
                if (item.IsEnabled)
                {
                    string target = path + ".omni_disabled";
                    if (File.Exists(path))
                    {
                        File.Move(path, target, true);
                        item.IsEnabled = false;
                        item.Command = target;
                        item.Id = target;
                    }
                    return new StartupActionResult { Success = true, Message = $"Acceso directo '{item.Name}' desactivado." };
                }
                else
                {
                    string target = path.EndsWith(".omni_disabled", StringComparison.OrdinalIgnoreCase)
                        ? path.Substring(0, path.Length - ".omni_disabled".Length)
                        : path;
                    if (File.Exists(path))
                    {
                        File.Move(path, target, true);
                        item.IsEnabled = true;
                        item.Command = target;
                        item.Id = target;
                    }
                    return new StartupActionResult { Success = true, Message = $"Acceso directo '{item.Name}' activado." };
                }
            }
            catch (Exception ex)
            {
                return new StartupActionResult { Success = false, Message = $"Error al modificar archivo de inicio: {ex.Message}" };
            }
        }
        else
        {
            var res = ToggleStartupItem(item.Scope, item.Name, !item.IsEnabled);
            if (res.Success)
            {
                item.IsEnabled = !item.IsEnabled;
            }
            return res;
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

    public StartupActionResult DeleteStartupItem(StartupItem item)
    {
        try
        {
            if (File.Exists(item.Command))
            {
                File.Delete(item.Command);
                return new StartupActionResult { Success = true, Message = $"Acceso directo '{item.Name}' eliminado de la carpeta de inicio." };
            }

            var root = item.Scope.Equals("Machine", StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine
                : Registry.CurrentUser;

            string keyPath = item.IsEnabled ? RunKeyPath : RunDisabledKeyPath;
            using (var key = root.OpenSubKey(keyPath, true))
            {
                if (key != null && key.GetValue(item.Name) != null)
                {
                    key.DeleteValue(item.Name, false);
                    return new StartupActionResult { Success = true, Message = $"Entrada '{item.Name}' eliminada permanentemente del registro." };
                }
            }

            string fallbackPath = item.IsEnabled ? RunDisabledKeyPath : RunKeyPath;
            using (var fbKey = root.OpenSubKey(fallbackPath, true))
            {
                if (fbKey != null && fbKey.GetValue(item.Name) != null)
                {
                    fbKey.DeleteValue(item.Name, false);
                    return new StartupActionResult { Success = true, Message = $"Entrada '{item.Name}' eliminada permanentemente del registro." };
                }
            }

            return new StartupActionResult { Success = false, Message = $"No se encontró la clave de registro para '{item.Name}'." };
        }
        catch (Exception ex)
        {
            return new StartupActionResult { Success = false, Message = $"Error eliminando entrada (¿Faltan permisos de Administrador?): {ex.Message}" };
        }
    }
}

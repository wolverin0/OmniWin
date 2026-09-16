using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public record ContextMenuItem(
    string Name,
    string Location,
    string RegistryPath,
    string ClsidOrCommand,
    bool IsEnabled
);

public class ContextMenuService
{
    private static readonly (string Name, string Path)[] HandlersLocations = [
        ("Fondo de Escritorio / Carpeta", @"Directory\Background\shellex\ContextMenuHandlers"),
        ("Directorios y Carpetas", @"Directory\shellex\ContextMenuHandlers"),
        ("Todos los Archivos (*)", @"*\shellex\ContextMenuHandlers"),
        ("Carpetas del Explorador", @"Folder\shellex\ContextMenuHandlers"),
        ("Unidades de Disco", @"Drive\shellex\ContextMenuHandlers"),
        ("Todos los Objetos del Sistema", @"AllFilesystemObjects\shellex\ContextMenuHandlers")
    ];

    public List<ContextMenuItem> GetContextMenuItems()
    {
        var items = new List<ContextMenuItem>();

        foreach (var (locName, subKeyPath) in HandlersLocations)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(subKeyPath, false);
                if (key == null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    bool isEnabled = !subName.StartsWith("-");
                    string effectiveName = isEnabled ? subName : subName[1..];
                    string fullSubPath = $@"{subKeyPath}\{subName}";

                    using var childKey = key.OpenSubKey(subName);
                    string val = childKey?.GetValue("")?.ToString() ?? string.Empty;

                    items.Add(new ContextMenuItem(
                        Name: effectiveName,
                        Location: locName,
                        RegistryPath: fullSubPath,
                        ClsidOrCommand: val,
                        IsEnabled: isEnabled
                    ));
                }
            }
            catch { }
        }

        return items;
    }

    public bool ToggleContextMenuItem(string parentSubKeyPath, string keyName, bool enable)
    {
        try
        {
            using var parentKey = Registry.ClassesRoot.OpenSubKey(parentSubKeyPath, true);
            if (parentKey == null) return false;

            string currentKeyName = enable ? $"-{keyName}" : keyName;
            string targetKeyName = enable ? keyName : $"-{keyName}";

            // To rename a subkey in Registry, copy subkey data and delete old
            using (var srcKey = parentKey.OpenSubKey(currentKeyName))
            {
                if (srcKey == null) return false;

                using var dstKey = parentKey.CreateSubKey(targetKeyName);
                if (dstKey == null) return false;

                foreach (var valName in srcKey.GetValueNames())
                {
                    dstKey.SetValue(valName, srcKey.GetValue(valName)!, srcKey.GetValueKind(valName));
                }
            }

            parentKey.DeleteSubKeyTree(currentKeyName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const string Win11ClassicClsidKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    /// <summary>
    /// Comprueba si el menú contextual clásico de Windows 10 está forzado en Windows 11.
    /// </summary>
    public bool IsWindows11ClassicContextMenuEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Win11ClassicClsidKey);
            if (key == null) return false;
            var val = key.GetValue("");
            return val != null && string.IsNullOrEmpty(val.ToString());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Activa o desactiva el menú contextual clásico de Windows 10 en Windows 11 (eliminando el menú moderno lento con 'Mostrar más opciones').
    /// </summary>
    public bool ToggleWindows11ClassicContextMenu(bool enableClassic)
    {
        try
        {
            if (enableClassic)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Win11ClassicClsidKey);
                key?.SetValue("", string.Empty, RegistryValueKind.String);
                return true;
            }
            else
            {
                using var clsid = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID", true);
                clsid?.DeleteSubKeyTree("{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reinicia el proceso explorer.exe para aplicar cambios en la barra de tareas y menús contextuales de forma inmediata.
    /// </summary>
    public bool RestartWindowsExplorer()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); p.WaitForExit(1500); } catch { }
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}

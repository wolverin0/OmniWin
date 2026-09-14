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
        ("Carpetas del Explorador", @"Folder\shellex\ContextMenuHandlers")
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
}

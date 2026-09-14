using System;
using System.Diagnostics;
using System.Security.Principal;

namespace OmniWin.Core.Services;

public static class SecurityHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartAsAdministrator(string? arguments = null)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            string appDir = AppContext.BaseDirectory;
            string candidateExe = System.IO.Path.Combine(appDir, "OmniWin.exe");
            string legacyExe = System.IO.Path.Combine(appDir, "OmniWin.UI.exe");

            string finalPath;
            string finalArgs = arguments ?? string.Empty;

            if (System.IO.File.Exists(candidateExe))
            {
                finalPath = candidateExe;
            }
            else if (System.IO.File.Exists(legacyExe))
            {
                finalPath = legacyExe;
            }
            else if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                if (System.IO.Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    string dllPath = System.IO.Path.Combine(appDir, "OmniWin.UI.dll");
                    finalPath = exePath;
                    finalArgs = $"\"{dllPath}\" {finalArgs}".Trim();
                }
                else
                {
                    finalPath = exePath;
                }
            }
            else
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = finalPath,
                Arguments = finalArgs,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Environment.Exit(0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

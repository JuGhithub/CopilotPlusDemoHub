using System;
using System.IO;

namespace CopilotPlusDemoHub.Core;

public static class PythonPathHelper
{
    private static string? _cachedPythonExe;

    public static string GetPythonExe()
    {
        if (!string.IsNullOrEmpty(_cachedPythonExe) && File.Exists(_cachedPythonExe))
        {
            return _cachedPythonExe;
        }

        // 1. Verifier les dossiers d'installation officiels de Python
        string[] candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python312", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python311", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python310", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "python.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                try
                {
                    if (new FileInfo(c).Length > 1000)
                    {
                        _cachedPythonExe = c;
                        return c;
                    }
                }
                catch { }
            }
        }

        // 2. Parcourir les entrees du PATH en ignorant l'alias vide Microsoft Store (WindowsApps)
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (var p in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (p.Contains("Microsoft\\WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string exe = Path.Combine(p.Trim(), "python.exe");
                    if (File.Exists(exe) && new FileInfo(exe).Length > 1000)
                    {
                        _cachedPythonExe = exe;
                        return exe;
                    }
                }
                catch { }
            }
        }

        return "python";
    }
}

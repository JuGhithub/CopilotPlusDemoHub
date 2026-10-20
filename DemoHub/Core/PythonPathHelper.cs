using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CopilotPlusDemoHub.Core;

public static class PythonPathHelper
{
    private static string? _cachedPythonExe;

    // Versions supportées par les dépendances d'IA (OpenVINO, ONNX Runtime, PyTorch), par ordre de préférence.
    private static readonly string[] SupportedVersions = { "312", "311", "313", "310" };

    public static string GetPythonExe()
    {
        if (!string.IsNullOrEmpty(_cachedPythonExe) && File.Exists(_cachedPythonExe))
        {
            return _cachedPythonExe;
        }

        // 0. Environnement virtuel du projet (.venv) s'il existe
        foreach (string root in CandidateRoots())
        {
            string venvPy = Path.Combine(root, ".venv", "Scripts", "python.exe");
            if (IsRealPython(venvPy))
            {
                return Cache(venvPy);
            }
        }

        // 1. Dossiers d'installation officiels de Python (all-users puis current-user)
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string v in SupportedVersions)
        {
            string[] candidates =
            {
                Path.Combine(programFiles, "Python" + v, "python.exe"),
                Path.Combine(localAppData, "Programs", "Python", "Python" + v, "python.exe"),
                Path.Combine(localAppData, "Programs", "Python", "Python" + v + "-arm64", "python.exe"),
                Path.Combine(localAppData, "Programs", "Python", "Python" + v + "-64", "python.exe")
            };
            foreach (string c in candidates)
            {
                if (IsRealPython(c))
                {
                    return Cache(c);
                }
            }
        }

        // 2. Entrées du PATH en ignorant l'alias vide du Microsoft Store (WindowsApps)
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (string p in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (p.Contains("Microsoft\\WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    string exe = Path.Combine(p.Trim(), "python.exe");
                    if (IsRealPython(exe))
                    {
                        return Cache(exe);
                    }
                }
                catch { }
            }
        }

        // 3. Python Launcher officiel (py.exe) : interroge l'interpréteur réel
        string? viaLauncher = ResolveViaLauncher();
        if (viaLauncher != null)
        {
            return Cache(viaLauncher);
        }

        return "python";
    }

    private static IEnumerable<string> CandidateRoots()
    {
        string?[] starts = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (string? start in starts)
        {
            if (string.IsNullOrEmpty(start)) continue;
            string? cur = start;
            for (int i = 0; i < 7 && !string.IsNullOrEmpty(cur); i++)
            {
                yield return cur;
                cur = Path.GetDirectoryName(cur);
            }
        }
    }

    private static bool IsRealPython(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 1000;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveViaLauncher()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "py.exe",
                Arguments = "-3 -c \"import sys;print(sys.executable)\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(4000);
            if (proc.ExitCode == 0 && IsRealPython(output))
            {
                return output;
            }
        }
        catch { }
        return null;
    }

    private static string Cache(string path)
    {
        _cachedPythonExe = path;
        return path;
    }
}

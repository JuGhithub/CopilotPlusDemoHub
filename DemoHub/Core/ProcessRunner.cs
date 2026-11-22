using System.Diagnostics;

namespace CopilotPlusDemoHub.Core;

/// <summary>Résultat d'un processus enfant exécuté jusqu'à son terme (ou interrompu par délai).</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Success => !TimedOut && ExitCode == 0;

    /// <summary>Texte d'erreur le plus utile : stderr s'il existe, sinon stdout.</summary>
    public string ErrorText => !string.IsNullOrWhiteSpace(StdErr) ? StdErr : StdOut;
}

/// <summary>
/// Lancement de processus enfants sans interblocage : stdout et stderr sont lus en parallèle
/// (lire l'un après l'autre bloque dès que l'autre tampon de 4 Ko est plein), les arguments sont
/// transmis via <see cref="ProcessStartInfo.ArgumentList"/> (aucun problème de guillemets avec des
/// noms saisis par l'utilisateur) et un délai maximal optionnel tue l'arbre de processus.
/// </summary>
public static class ProcessRunner
{
    /// <summary>Prépare un appel Python avec script + arguments passés sans interpolation de chaîne.</summary>
    public static ProcessStartInfo CreatePythonStartInfo(string scriptPath, string? workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PythonPathHelper.GetPythonExe(),
            WorkingDirectory = workingDir ?? AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        return psi;
    }

    public static async Task<ProcessResult> RunAsync(
        ProcessStartInfo psi,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        bool timedOut = false;
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            timedOut = true;
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(timedOut ? -1 : proc.ExitCode, stdout, stderr, timedOut);
    }

    /// <summary>Version synchrone, à n'utiliser que depuis un thread de fond (Task.Run), jamais depuis le thread UI.</summary>
    public static ProcessResult Run(ProcessStartInfo psi, TimeSpan? timeout = null)
        => RunAsync(psi, timeout).GetAwaiter().GetResult();
}

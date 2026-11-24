using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.Infrastructure.Python;

/// <summary>
/// Résultat de l'exécution d'un script Python géré par PythonProcessManager.
/// </summary>
public sealed record PythonExecutionResult
{
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Success => !TimedOut && ExitCode == 0;
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public IReadOnlyList<JsonElement> JsonMessages { get; init; } = Array.Empty<JsonElement>();
    public ReadyMessage? ReadyMessage { get; init; }
    public ErrorMessage? LastErrorMessage { get; init; }
    public string ErrorSummary => LastErrorMessage?.Error ?? (string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr);
}

/// <summary>
/// Gestionnaire centralisé du cycle de vie des processus Python.
/// Assure la séparation stricte stdout (JSON Lines) / stderr (logs),
/// le passage d'arguments par ArgumentList, le timeout et l'annulation sécurisée.
/// </summary>
public static class PythonProcessManager
{
    public static async Task<PythonExecutionResult> RunAsync(
        string scriptPath,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        string? pythonExecutable = null,
        Action<ProgressMessage>? onProgress = null,
        Action<string>? onStderrLog = null,
        Action<ReadyMessage>? onReady = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        string pythonExe = pythonExecutable ?? PythonPathHelper.GetPythonExe();
        if (!File.Exists(pythonExe))
        {
            throw new FileNotFoundException($"Interpréteur Python introuvable : {pythonExe}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            WorkingDirectory = workingDirectory ?? AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add(scriptPath);
        if (arguments != null)
        {
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        using var process = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var jsonMessages = new List<JsonElement>();
        ReadyMessage? readyMsg = null;
        ErrorMessage? lastErrorMsg = null;

        process.Start();

        var stdoutTask = Task.Run(async () =>
        {
            using var reader = process.StandardOutput;
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                stdoutBuilder.AppendLine(line);

                if (JsonLineParser.TryParse(line, out var jsonElement, out var msgType))
                {
                    jsonMessages.Add(jsonElement);

                    switch (msgType.ToLowerInvariant())
                    {
                        case "ready":
                            var ready = JsonLineParser.Deserialize<ReadyMessage>(line);
                            if (ready != null)
                            {
                                readyMsg = ready;
                                onReady?.Invoke(ready);
                            }
                            break;

                        case "progress":
                            var progress = JsonLineParser.Deserialize<ProgressMessage>(line);
                            if (progress != null)
                            {
                                onProgress?.Invoke(progress);
                            }
                            break;

                        case "error":
                            var err = JsonLineParser.Deserialize<ErrorMessage>(line);
                            if (err != null)
                            {
                                lastErrorMsg = err;
                            }
                            break;
                    }
                }
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            using var reader = process.StandardError;
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                stderrBuilder.AppendLine(line);
                onStderrLog?.Invoke(line);
            }
        });

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Processus déjà terminé
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        return new PythonExecutionResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            TimedOut = timedOut,
            StdOut = stdoutBuilder.ToString().TrimEnd(),
            StdErr = stderrBuilder.ToString().TrimEnd(),
            JsonMessages = jsonMessages,
            ReadyMessage = readyMsg,
            LastErrorMessage = lastErrorMsg
        };
    }
}

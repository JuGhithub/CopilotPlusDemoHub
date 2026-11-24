using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.Infrastructure.Python;
using Xunit;

namespace DemoHub.Tests;

public class PythonProcessManagerTests
{
    [Fact]
    public void JsonLineParser_TryParse_ValidMessage_ReturnsTrue()
    {
        string line = "{\"protocol\":\"demohub/1.0\",\"type\":\"progress\",\"progress\":0.75,\"message\":\"Inférence en cours\"}";
        bool success = JsonLineParser.TryParse(line, out var element, out string type);

        Assert.True(success);
        Assert.Equal("progress", type);
        Assert.True(element.TryGetProperty("progress", out var progProp));
        Assert.Equal(0.75, progProp.GetDouble(), 2);
    }

    [Fact]
    public void JsonLineParser_TryParse_MalformedOrNonJson_ReturnsFalse()
    {
        Assert.False(JsonLineParser.TryParse("Info: Normal logging message", out _, out _));
        Assert.False(JsonLineParser.TryParse("{ malformed json ", out _, out _));
        Assert.False(JsonLineParser.TryParse("", out _, out _));
    }

    [Fact]
    public void JsonLineParser_Deserialize_ProgressMessage_MatchesProperties()
    {
        string line = "{\"protocol\":\"demohub/1.0\",\"type\":\"progress\",\"progress\":0.42,\"message\":\"Chargement du modèle\"}";
        var progress = JsonLineParser.Deserialize<ProgressMessage>(line);

        Assert.NotNull(progress);
        Assert.Equal("demohub/1.0", progress.Protocol);
        Assert.Equal("progress", progress.Type);
        Assert.Equal(0.42, progress.Progress, 2);
        Assert.Equal("Chargement du modèle", progress.Message);
    }

    [Fact]
    public void JsonLineParser_Deserialize_ErrorMessage_MatchesProperties()
    {
        string line = "{\"protocol\":\"demohub/1.0\",\"type\":\"error\",\"error\":\"Modèle introuvable\",\"code\":\"ERR_NOT_FOUND\"}";
        var error = JsonLineParser.Deserialize<ErrorMessage>(line);

        Assert.NotNull(error);
        Assert.Equal("demohub/1.0", error.Protocol);
        Assert.Equal("error", error.Type);
        Assert.Equal("Modèle introuvable", error.Error);
        Assert.Equal("ERR_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task PythonProcessManager_RunAsync_SeparatesStdoutAndStderr()
    {
        string pythonExe = PythonPathHelper.GetPythonExe();
        if (!File.Exists(pythonExe))
        {
            // Environnement sans Python configuré : test ignoré
            return;
        }

        // Crée un script temporaire émettant des logs stderr et un JSON Line stdout
        string tempScript = Path.Combine(Path.GetTempPath(), $"demohub_test_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(tempScript, @"
import sys, time
sys.stderr.write('Diagnostic log 1\n')
sys.stderr.flush()
print('{\""protocol\"": \""demohub/1.0\"", \""type\"": \""ready\"", \""service\"": \""test\"", \""status\"": \""ready\""}', flush=True)
print('{\""protocol\"": \""demohub/1.0\"", \""type\"": \""progress\"", \""progress\"": 1.0, \""message\"": \""Terminé\""}', flush=True)
sys.stderr.write('Diagnostic log 2\n')
sys.stderr.flush()
");

        try
        {
            var progressMessages = new List<ProgressMessage>();
            var stderrLogs = new List<string>();
            ReadyMessage? readyReceived = null;

            var result = await PythonProcessManager.RunAsync(
                tempScript,
                pythonExecutable: pythonExe,
                onProgress: p => progressMessages.Add(p),
                onStderrLog: log => stderrLogs.Add(log),
                onReady: r => readyReceived = r,
                timeout: TimeSpan.FromSeconds(10));

            Assert.True(result.Success);
            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);

            // Vérification de la séparation stricte
            Assert.Contains("Diagnostic log 1", result.StdErr);
            Assert.Contains("Diagnostic log 2", result.StdErr);

            Assert.NotNull(readyReceived);
            Assert.Equal("ready", readyReceived.Type);
            Assert.Equal("test", readyReceived.Service);

            Assert.Single(progressMessages);
            Assert.Equal(1.0, progressMessages[0].Progress, 2);
        }
        finally
        {
            if (File.Exists(tempScript))
            {
                File.Delete(tempScript);
            }
        }
    }

    [Fact]
    public async Task PythonProcessManager_RunAsync_TimesOutProperly()
    {
        string pythonExe = PythonPathHelper.GetPythonExe();
        if (!File.Exists(pythonExe)) return;

        string tempScript = Path.Combine(Path.GetTempPath(), $"demohub_timeout_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(tempScript, @"
import time
time.sleep(10)
");

        try
        {
            var result = await PythonProcessManager.RunAsync(
                tempScript,
                pythonExecutable: pythonExe,
                timeout: TimeSpan.FromMilliseconds(500));

            Assert.True(result.TimedOut);
            Assert.False(result.Success);
            Assert.Equal(-1, result.ExitCode);
        }
        finally
        {
            if (File.Exists(tempScript))
            {
                File.Delete(tempScript);
            }
        }
    }
}

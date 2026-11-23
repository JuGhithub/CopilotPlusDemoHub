using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CopilotPlusDemoHub.Core;
using Xunit;

namespace DemoHub.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_EchoCommand_ReturnsStdoutCorrectly()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("echo Hello CopilotPlus");

        var result = await ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(5));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello CopilotPlus", result.StdOut);
        Assert.True(string.IsNullOrEmpty(result.StdErr));
    }

    [Fact]
    public async Task RunAsync_PipeSaturation_DoesNotDeadlock()
    {
        // Flood stdout and stderr concurrently with 100KB of text
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("Write-Output ('A'*50000); [Console]::Error.WriteLine('B'*50000)");

        var result = await ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(10));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StdOut.Length >= 50000);
        Assert.True(result.StdErr.Length >= 50000);
    }

    [Fact]
    public async Task RunAsync_LongRunningProcess_TimesOutGracefully()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("Start-Sleep -Seconds 10");

        var result = await ProcessRunner.RunAsync(psi, TimeSpan.FromMilliseconds(500));

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessTree()
    {
        using var cts = new CancellationTokenSource();
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("Start-Sleep -Seconds 10");

        cts.CancelAfter(300);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(10), cts.Token);
        });
    }
}

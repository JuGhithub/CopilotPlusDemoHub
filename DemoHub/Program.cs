using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.UI;

Console.OutputEncoding = System.Text.Encoding.UTF8;

try
{
    string? targetDevice = null;
    string? targetCommand = null;
    bool forceCli = false;

    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i].ToLowerInvariant().TrimStart('-');

        if (arg is "device" or "d")
        {
            if (i + 1 < args.Length)
            {
                targetDevice = args[++i].ToUpperInvariant();
            }
        }
        else if (arg.StartsWith("device="))
        {
            targetDevice = arg.Substring("device=".Length).ToUpperInvariant();
        }
        else if (arg is "cli")
        {
            forceCli = true;
        }
        else if (arg is "gui")
        {
            forceCli = false;
        }
        else if (arg is "engineering" or "e" or "operations" or "o" or "insurance" or "i" or "benchmark" or "b" or "duel" or "k" or "k2000")
        {
            targetCommand = arg;
        }
        else
        {
            Console.WriteLine($"Unknown argument: {args[i]}");
            Console.WriteLine("Usage: DemoHub [--gui] [--cli] [--engineering | --operations | --insurance | --benchmark | --duel] [--device npu|gpu|cpu|auto]");
            return;
        }
    }

    // Default: Launch modern executive GUI when no direct CLI command is requested
    if (string.IsNullOrWhiteSpace(targetCommand) && !forceCli)
    {
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return;
    }

    var menu = new DemoMenu(targetDevice);

    if (!string.IsNullOrWhiteSpace(targetCommand))
    {
        switch (targetCommand)
        {
            case "engineering":
            case "e":
                await menu.RunEngineeringAsync(targetDevice);
                return;

            case "operations":
            case "o":
                await menu.RunOperationsAsync(targetDevice);
                return;

            case "insurance":
            case "i":
                await menu.RunInsuranceAsync(targetDevice);
                return;

            case "benchmark":
            case "b":
                await menu.RunEnergyBenchmarkAsync(targetDevice);
                return;

            case "duel":
            case "k":
            case "k2000":
                await menu.RunK2000DuelAsync("1");
                return;
        }
    }

    await menu.RunAsync();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;

    Console.WriteLine();
    Console.WriteLine("Fatal error");
    Console.WriteLine("------------------------------------");
    Console.WriteLine(ex.Message);

    Console.ResetColor();
}

Console.WriteLine();
Console.WriteLine("Application closed.");
using CopilotPlusDemoHub.Demos.Insurance;
using CopilotPlusDemoHub.Demos.Engineering;
using CopilotPlusDemoHub.Demos.Operations;
using CopilotPlusDemoHub.Demos.Benchmark;
using CopilotPlusDemoHub.Demos.K2000;

namespace CopilotPlusDemoHub.Core;

public sealed class DemoMenu
{
    private readonly LocalizationService _localizer;
    private readonly FoundryClient _foundryClient;
    private string _selectedDevice = "AUTO";

    public DemoMenu(string? initialDevice = null)
    {
        _localizer =
            new LocalizationService();

        _foundryClient =
            new FoundryClient();

        if (!string.IsNullOrWhiteSpace(initialDevice))
        {
            _selectedDevice =
                initialDevice.ToUpperInvariant();
        }
    }

    public async Task RunAsync()
    {
        bool exitRequested = false;

        try
        {
            while (!exitRequested)
            {
                SafeClear();

                DrawHeader();
                DrawMainMenu();

                Console.WriteLine();

                Console.Write(
                    _localizer.Get("Menu_SelectDemo") +
                    ": ");

                string? choice =
                    Console.ReadLine();

                if (choice == null)
                {
                    exitRequested = true;
                    break;
                }

                Console.WriteLine();

                switch (choice.Trim().ToUpperInvariant())
                {
                    case "1":
                        await RunOperationsAsync();
                        break;

                    case "2":
                        await RunEngineeringAsync();
                        break;

                    case "3":
                        await RunInsuranceAsync();
                        break;

                    case "4":
                        await RunFinancialAsync();
                        break;

                    case "5":
                        await RunEnergyBenchmarkAsync();
                        break;

                    case "6":
                        await RunK2000DuelAsync();
                        break;

                    case "L":
                        ChangeLanguage();
                        break;

                    case "D":
                        ChangeDevice();
                        break;

                    case "0":
                        exitRequested = true;
                        break;

                    default:
                        Console.ForegroundColor =
                            ConsoleColor.Yellow;

                        Console.WriteLine(
                            _localizer.Get(
                                "Menu_InvalidSelection"));

                        Console.ResetColor();

                        Pause();
                        break;
                }
            }
        }
        finally
        {
            _foundryClient.Dispose();
        }
    }

    private void DrawHeader()
    {
        Console.ForegroundColor =
            ConsoleColor.Blue;

        Console.WriteLine(
            "==================================================");

        Console.WriteLine(
            _localizer.Get(
                "Menu_Title"));

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Menu_Subtitle"));

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Menu_CurrentLanguage") +
            " : " +
            GetCurrentLanguageName());

        Console.WriteLine(
            _localizer.Get(
                "Menu_CurrentDevice") +
            " : " +
            GetSelectedDeviceDisplayName());

        Console.WriteLine();
    }

    private void DrawMainMenu()
    {
        Console.WriteLine(
            "1 - " +
            _localizer.Get(
                "Operations_MenuTitle"));

        Console.WriteLine(
            "2 - " +
            _localizer.Get(
                "Engineering_MenuTitle"));

        Console.WriteLine(
            "3 - " +
            _localizer.Get(
                "Insurance_MenuTitle"));

        Console.WriteLine(
            "4 - " +
            _localizer.Get(
                "Financial_MenuTitle"));

        Console.WriteLine(
            "5 - " +
            _localizer.Get(
                "Benchmark_MenuTitle"));

        Console.WriteLine(
            "6 - " +
            _localizer.Get(
                "K2000_MenuTitle"));

        Console.WriteLine();

        Console.WriteLine(
            "L - " +
            _localizer.Get(
                "Menu_ChangeLanguage"));

        Console.WriteLine(
            "D - " +
            _localizer.Get(
                "Menu_ChangeDevice"));

        Console.WriteLine();

        Console.WriteLine(
            "0 - " +
            _localizer.Get(
                "Menu_Exit"));
    }

    private void ChangeLanguage()
    {
        SafeClear();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            "==================================================");

        Console.WriteLine(
            " " +
            _localizer.Get(
                "Menu_LanguageSelection"));

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();

        IReadOnlyList<LanguageOption> languages =
            _localizer.GetSupportedLanguages();

        for (int index = 0;
             index < languages.Count;
             index++)
        {
            LanguageOption language =
                languages[index];

            string currentMarker =
                language.Code.Equals(
                    _localizer.CurrentLanguage,
                    StringComparison.OrdinalIgnoreCase)
                    ? " *"
                    : string.Empty;

            Console.WriteLine(
                (index + 1) +
                " - " +
                language.Name +
                currentMarker);
        }

        Console.WriteLine();

        Console.WriteLine(
            "0 - " +
            _localizer.Get(
                "Common_Back"));

        Console.WriteLine();

        Console.Write(
            _localizer.Get(
                "Menu_SelectLanguage") +
            ": ");

        string? input =
            Console.ReadLine();

        if (input?.Trim() == "0")
        {
            return;
        }

        if (!int.TryParse(
                input,
                out int selection))
        {
            DisplayInvalidSelection();
            return;
        }

        if (selection < 1 ||
            selection > languages.Count)
        {
            DisplayInvalidSelection();
            return;
        }

        LanguageOption selectedLanguage =
            languages[selection - 1];

        try
        {
            _localizer.SetLanguage(
                selectedLanguage.Code);

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                _localizer.Get(
                    "Menu_LanguageChanged") +
                " : " +
                selectedLanguage.Name);

            Console.ResetColor();

            Pause();
        }
        catch (Exception exception)
        {
            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Red;

            Console.WriteLine(
                _localizer.Get(
                    "Common_Error"));

            Console.ResetColor();

            Console.WriteLine(
                "------------------------------------");

            Console.WriteLine(
                exception.Message);

            Console.WriteLine(
                "------------------------------------");

            Pause();
        }
    }

    private void ChangeDevice()
    {
        SafeClear();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            "==================================================");

        Console.WriteLine(
            " " +
            _localizer.Get(
                "Menu_DeviceSelection"));

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();

        var deviceOptions = new List<(string Code, string LabelKey)>
        {
            ("AUTO", "Device_Auto"),
            ("NPU", "Device_NPU"),
            ("GPU", "Device_GPU"),
            ("CPU", "Device_CPU")
        };

        for (int index = 0;
             index < deviceOptions.Count;
             index++)
        {
            var (code, labelKey) = deviceOptions[index];

            string currentMarker =
                code.Equals(
                    _selectedDevice,
                    StringComparison.OrdinalIgnoreCase)
                    ? " *"
                    : string.Empty;

            Console.WriteLine(
                (index + 1) +
                " - " +
                _localizer.Get(labelKey) +
                currentMarker);
        }

        Console.WriteLine();

        Console.WriteLine(
            "0 - " +
            _localizer.Get(
                "Common_Back"));

        Console.WriteLine();

        Console.Write(
            _localizer.Get(
                "Menu_SelectDevice") +
            ": ");

        string? input =
            Console.ReadLine();

        if (input?.Trim() == "0" || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (!int.TryParse(
                input,
                out int selection) ||
            selection < 1 ||
            selection > deviceOptions.Count)
        {
            DisplayInvalidSelection();
            return;
        }

        _selectedDevice =
            deviceOptions[selection - 1].Code;

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Green;

        Console.WriteLine(
            _localizer.Get(
                "Menu_DeviceChanged") +
            " : " +
            GetSelectedDeviceDisplayName());

        Console.ResetColor();

        Pause();
    }

    private string GetSelectedDeviceDisplayName()
    {
        return _selectedDevice.ToUpperInvariant() switch
        {
            "NPU" => _localizer.Get("Device_NPU"),
            "GPU" => _localizer.Get("Device_GPU"),
            "CPU" => _localizer.Get("Device_CPU"),
            _ => _localizer.Get("Device_Auto")
        };
    }

    public async Task RunOperationsAsync(string? preferredDevice = null)
    {
        string device =
            preferredDevice ?? _selectedDevice;

        var demo =
            new OperationsDemo(
                _foundryClient,
                _localizer,
                device);

        await demo.RunAsync();

        Pause();
    }

    public async Task RunEngineeringAsync(string? preferredDevice = null)
    {
        string device =
            preferredDevice ?? _selectedDevice;

        var demo =
            new EngineeringDemo(
                _foundryClient,
                _localizer,
                device);

        await demo.RunAsync();

        Pause();
    }

    public async Task RunInsuranceAsync(string? preferredDevice = null)
    {
        string device =
            preferredDevice ?? _selectedDevice;

        var demo =
            new InsuranceDemo(
                _foundryClient,
                _localizer,
                device);

        await demo.RunAsync();

        Pause();
    }

    private async Task RunFinancialAsync()
    {
        SafeClear();

        DrawSection(
            _localizer.Get(
                "Financial_Title"));

        Console.WriteLine(
            _localizer.Get(
                "Financial_Subtitle"));

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Financial_Placeholder"));

        await Task.CompletedTask;

        Pause();
    }

    public async Task RunEnergyBenchmarkAsync(string? preferredDevice = null)
    {
        string device =
            preferredDevice ?? _selectedDevice;

        var demo =
            new EnergyBenchmarkDemo(
                _foundryClient,
                _localizer,
                device);

        await demo.RunAsync();

        Pause();
    }

    public async Task RunK2000DuelAsync(string? mode = null)
    {
        var demo =
            new K2000DuelDemo(
                _foundryClient,
                _localizer);

        await demo.RunAsync(mode);

        Pause();
    }

    private static void DrawSection(
        string title)
    {
        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            "==================================================");

        Console.WriteLine(
            " " +
            title);

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();
    }

    private string GetCurrentLanguageName()
    {
        LanguageOption? currentLanguage =
            _localizer
                .GetSupportedLanguages()
                .FirstOrDefault(
                    language =>
                        language.Code.Equals(
                            _localizer.CurrentLanguage,
                            StringComparison.OrdinalIgnoreCase));

        return currentLanguage?.Name
            ?? _localizer.CurrentLanguage;
    }

    private void DisplayInvalidSelection()
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Yellow;

        Console.WriteLine(
            _localizer.Get(
                "Menu_InvalidSelection"));

        Console.ResetColor();

        Pause();
    }

    private void Pause()
    {
        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Common_PressEnterContinue"));

        if (!Console.IsInputRedirected)
        {
            Console.ReadLine();
        }
    }
    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
            }
        }
        catch
        {
        }
    }
}
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CopilotPlusDemoHub.Core;

public sealed class LocalizationService
{
    private readonly Dictionary<string, string> _translations;

    public string CurrentLanguage { get; private set; }

    public LocalizationService()
    {
        CurrentLanguage =
            DetectSystemLanguage();

        _translations =
            LoadLanguage(CurrentLanguage);
    }

    public string Get(string key)
    {
        if (_translations.TryGetValue(
                key,
                out string? value))
        {
            return value;
        }

        return $"[{key}]";
    }

    public void SetLanguage(
        string languageCode)
    {
        CurrentLanguage =
            languageCode;

        _translations.Clear();

        foreach (var kvp in LoadLanguage(languageCode))
        {
            _translations[kvp.Key] =
                kvp.Value;
        }
    }

    public IReadOnlyList<LanguageOption> GetSupportedLanguages()
    {
        return new List<LanguageOption>
        {
            new LanguageOption(
                "fr",
                "Français"),

            new LanguageOption(
                "en",
                "English")
        };
    }

    private static string DetectSystemLanguage()
    {
        string language =
            CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName
                .ToLowerInvariant();

        return language switch
        {
            "fr" => "fr",
            "en" => "en",
            _ => "en"
        };
    }

    private static Dictionary<string, string> LoadLanguage(
        string languageCode)
    {
        string filePath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Resources",
                $"{languageCode}.json");

        if (!File.Exists(filePath))
        {
            filePath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Resources",
                    "en.json");
        }

        string json =
            File.ReadAllText(filePath, Encoding.UTF8);

        Dictionary<string, string>? data =
            JsonSerializer.Deserialize<
                Dictionary<string, string>>(
                    json);

        return data ??
               new Dictionary<string, string>();
    }
}

public sealed record LanguageOption(
    string Code,
    string Name);
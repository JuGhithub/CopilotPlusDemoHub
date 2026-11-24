using System;
using System.IO;
using System.Text.Json;

namespace CopilotPlusDemoHub.Core;

/// <summary>
/// Configuration centralisée et typée de Copilot+ Demo Hub.
/// Charge les paramètres depuis Config/appsettings.json avec repli sécurisé.
/// </summary>
public sealed class AppConfig
{
    private static readonly Lazy<AppConfig> _instance = new(() => LoadConfiguration());

    public static AppConfig Current => _instance.Value;

    public bool OfflineModeOnly { get; set; } = true;
    public bool TelemetryEnabled { get; set; } = true;
    public string DefaultLanguage { get; set; } = "fr";
    public string? FoundryBaseUrl { get; set; }

    /// <summary>
    /// Recherche et charge Config/appsettings.json en remontant l'arborescence des dossiers.
    /// </summary>
    public static AppConfig LoadConfiguration()
    {
        var config = new AppConfig();
        string? configPath = FindConfigFile("Config", "appsettings.json");

        if (configPath != null && File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("DemoHub", out var demoHubSection))
                {
                    if (demoHubSection.TryGetProperty("OfflineModeOnly", out var offlineProp))
                    {
                        config.OfflineModeOnly = offlineProp.GetBoolean();
                    }
                    if (demoHubSection.TryGetProperty("TelemetryEnabled", out var telemetryProp))
                    {
                        config.TelemetryEnabled = telemetryProp.GetBoolean();
                    }
                    if (demoHubSection.TryGetProperty("DefaultLanguage", out var langProp))
                    {
                        config.DefaultLanguage = langProp.GetString() ?? "fr";
                    }
                    if (demoHubSection.TryGetProperty("FoundryBaseUrl", out var urlProp))
                    {
                        config.FoundryBaseUrl = urlProp.GetString();
                    }
                }
            }
            catch
            {
                // En cas d'erreur de parsing, les valeurs par défaut sécurisées s'appliquent (OfflineModeOnly = true)
            }
        }

        return config;
    }

    /// <summary>
    /// Valide qu'une URL d'inférence est autorisée selon les règles de confinement local.
    /// </summary>
    public bool ValidateEndpoint(string? url, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "L'adresse d'endpoint est vide.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errorMessage = $"Format d'URL invalide : {url}";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = $"Protocole non autorisé : {uri.Scheme}. Seuls HTTP et HTTPS sont supportés.";
            return false;
        }

        if (OfflineModeOnly)
        {
            bool isLoopback = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            if (!isLoopback)
            {
                errorMessage = $"Confinement local strict actif (OfflineModeOnly=true). Les endpoints distants ({uri.Host}) sont formellement bloqués.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Charge le texte d'un prompt métier depuis le dossier Prompts/.
    /// </summary>
    public static string LoadPrompt(string promptFileName, string fallbackContent = "")
    {
        string? promptPath = FindConfigFile("Prompts", promptFileName);
        if (promptPath != null && File.Exists(promptPath))
        {
            try
            {
                string text = File.ReadAllText(promptPath).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch { }
        }
        return fallbackContent;
    }

    private static string? FindConfigFile(string folder, string fileName)
    {
        string?[] startDirs = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in startDirs)
        {
            if (string.IsNullOrEmpty(start)) continue;
            string? cur = start;
            for (int i = 0; i < 7 && !string.IsNullOrEmpty(cur); i++)
            {
                string candidate = Path.Combine(cur, folder, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                cur = Path.GetDirectoryName(cur);
            }
        }
        return null;
    }
}

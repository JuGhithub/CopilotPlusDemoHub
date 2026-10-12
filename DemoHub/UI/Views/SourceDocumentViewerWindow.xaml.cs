using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class SourceDocumentViewerWindow : Window
{
    private readonly string _fullContent;
    private readonly string? _filePath;
    private readonly string _format;
    private readonly LocalizationService? _localizer;

    public SourceDocumentViewerWindow(
        string title,
        string content,
        string format = "TXT",
        string? filePath = null,
        LocalizationService? localizer = null,
        string? icon = "📄")
    {
        InitializeComponent();

        _fullContent = content ?? string.Empty;
        _filePath = filePath;
        _format = format.ToUpperInvariant();
        _localizer = localizer;

        TxtDocIcon.Text = icon ?? "📄";
        TxtDocTitle.Text = title;
        TxtDocumentBody.Text = _fullContent;

        // Metadata badges
        TxtDocBadgeType.Text = _format switch
        {
            "JSON" => "JSON Structuré",
            "MD" => "Markdown",
            "TXT" => "Texte Brut",
            "CSV" => "Tableur CSV",
            _ => _format
        };

        int lineCount = _fullContent.Split('\n').Length;
        double sizeKb = System.Text.Encoding.UTF8.GetByteCount(_fullContent) / 1024.0;
        TxtDocStats.Text = $"{lineCount} lignes • {sizeKb:F1} Ko";

        if (!string.IsNullOrEmpty(_filePath))
        {
            TxtFilePath.Text = $"📁 Emplacement : {_filePath}";
        }
        else
        {
            TxtFilePath.Text = "🔒 Document chargé en mémoire vive (Zero-Cloud)";
        }

        ApplyLocalization();

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void ApplyLocalization()
    {
        bool isEn = _localizer?.CurrentLanguage == "en";

        TxtBtnCopy.Text = isEn ? "Copy" : "Copier";
        TxtBtnExternal.Text = isEn ? "External Editor" : "Éditeur externe";
        TxtBtnClose.Text = isEn ? "✕ Close" : "✕ Fermer";
        TxtDocBadgePrivacy.Text = isEn ? "🛡️ 100% Local Data" : "🛡️ Donnée 100% Locale";
        TxtFooterTag.Text = isEn ? "Secure inference context • Zero cloud transmission" : "Contexte d'inférence sécurisé • Zéro transfert réseau";
        TxtSearch.ToolTip = isEn ? "Search in document (type to filter)" : "Rechercher dans le document";

        if (string.IsNullOrEmpty(TxtSearch.Text))
        {
            TxtSearchResults.Text = isEn ? "🔍 Press Enter or type to find" : "🔍 Saisir un terme pour chercher";
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtSearch.Text.Trim();
        bool isEn = _localizer?.CurrentLanguage == "en";

        if (string.IsNullOrEmpty(query))
        {
            TxtSearchResults.Text = string.Empty;
            return;
        }

        int count = 0;
        int idx = 0;
        int firstIndex = -1;

        while ((idx = _fullContent.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            if (firstIndex == -1) firstIndex = idx;
            count++;
            idx += query.Length;
        }

        TxtSearchResults.Text = count > 0
            ? (isEn ? $"✅ {count} match(es)" : $"✅ {count} occurrence(s)")
            : (isEn ? "❌ 0 matches" : "❌ Aucune occurrence");

        if (firstIndex >= 0)
        {
            TxtDocumentBody.Focus();
            TxtDocumentBody.Select(firstIndex, query.Length);
        }
    }

    private void BtnCopyContent_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_fullContent);
            bool isEn = _localizer?.CurrentLanguage == "en";
            TxtBtnCopy.Text = isEn ? "✓ Copied!" : "✓ Copié !";

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, args) =>
            {
                TxtBtnCopy.Text = isEn ? "Copy" : "Copier";
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string targetPath = _filePath ?? string.Empty;
            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "CopilotPlusDemoHub");
                Directory.CreateDirectory(tempDir);
                string ext = _format.Equals("JSON", StringComparison.OrdinalIgnoreCase) ? "json" : "txt";
                targetPath = Path.Combine(tempDir, $"source_preview_{DateTime.Now:HHmmss}.{ext}");
                File.WriteAllText(targetPath, _fullContent, System.Text.Encoding.UTF8);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'ouvrir l'éditeur externe : {ex.Message}", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public static void ShowDocument(
        Window? owner,
        string title,
        string content,
        string format = "TXT",
        string? filePath = null,
        LocalizationService? localizer = null,
        string? icon = "📄")
    {
        var win = new SourceDocumentViewerWindow(title, content, format, filePath, localizer, icon);
        if (owner != null)
        {
            win.Owner = owner;
        }
        win.ShowDialog();
    }
}

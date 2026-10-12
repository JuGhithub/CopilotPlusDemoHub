using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class DocumentationWindow : Window
{
    private static DocumentationWindow? _currentInstance;
    private string _currentLanguage = "fr";
    private string _currentTopicKey = "global_overview";
    private readonly Dictionary<string, RadioButton> _navButtons = new();

    public DocumentationWindow()
    {
        InitializeComponent();
        BuildNavigation();
        SetLanguage("fr");
        SelectTopic("global_overview");
    }

    public static void ShowDocumentation(Window? owner, string? topicKey = null, string? lang = null)
    {
        if (_currentInstance == null || !_currentInstance.IsLoaded)
        {
            _currentInstance = new DocumentationWindow();
            if (owner != null && owner.IsVisible)
            {
                _currentInstance.Owner = owner;
            }
        }

        if (!string.IsNullOrEmpty(lang))
        {
            _currentInstance.SetLanguage(lang);
        }

        if (!string.IsNullOrEmpty(topicKey))
        {
            _currentInstance.SelectTopic(topicKey);
        }

        _currentInstance.Show();
        _currentInstance.Activate();
        if (_currentInstance.WindowState == WindowState.Minimized)
        {
            _currentInstance.WindowState = WindowState.Normal;
        }
    }

    private void BuildNavigation()
    {
        PanelGlobalNav.Children.Clear();
        PanelDemoNav.Children.Clear();
        _navButtons.Clear();

        // 1. Global topics
        foreach (var topic in DocumentationService.Instance.GetGlobalTopics())
        {
            var btn = CreateNavButton(topic);
            PanelGlobalNav.Children.Add(btn);
            _navButtons[topic.Key] = btn;
        }

        // 2. Demo topics
        foreach (var topic in DocumentationService.Instance.GetDemoTopics())
        {
            var btn = CreateNavButton(topic);
            PanelDemoNav.Children.Add(btn);
            _navButtons[topic.Key] = btn;
        }
    }

    private RadioButton CreateNavButton(DocTopic topic)
    {
        var rb = new RadioButton
        {
            GroupName = "DocNavGrp",
            Content = $"{topic.Icon}  {topic.GetTitle(_currentLanguage)}",
            Tag = topic.Key,
            Style = (Style)FindResource("NavButtonStyle"),
            Margin = new Thickness(0, 0, 0, 3),
            FontSize = 11.5
        };

        rb.Click += (s, e) =>
        {
            if (rb.Tag is string key)
            {
                SelectTopic(key);
            }
        };

        return rb;
    }

    public void SelectTopic(string topicKey)
    {
        _currentTopicKey = topicKey;

        if (_navButtons.TryGetValue(topicKey, out var rb))
        {
            rb.IsChecked = true;
        }

        var topic = DocumentationService.Instance.GetTopic(topicKey)
                    ?? DocumentationService.Instance.GetTopic("global_overview");

        if (topic == null) return;

        // Render topic content
        TxtTopicIcon.Text = topic.Icon;
        TxtTopicTitle.Text = topic.GetTitle(_currentLanguage);
        TxtTopicSubtitle.Text = topic.GetSubtitle(_currentLanguage);

        TxtBadgeAccelerator.Text = topic.Accelerator;
        TxtBadgeLatencyPower.Text = topic.GetLatencyPower(_currentLanguage);

        TxtSectionPitch.Text = topic.GetPitch(_currentLanguage);
        TxtSectionDesc.Text = topic.GetDescription(_currentLanguage);
        TxtSectionTech.Text = topic.GetTechnicalSpecs(_currentLanguage);
        TxtSectionBinaries.Text = topic.GetBinariesAndModels(_currentLanguage);
        TxtSectionHowTo.Text = topic.GetHowToRun(_currentLanguage);

        ScrollDetails.ScrollToTop();
    }

    public void SetLanguage(string lang)
    {
        _currentLanguage = lang.ToLowerInvariant() == "en" ? "en" : "fr";

        if (_currentLanguage == "en")
        {
            if (RadLangEn.IsChecked != true) RadLangEn.IsChecked = true;
            ApplyWindowLocalizationEn();
        }
        else
        {
            if (RadLangFr.IsChecked != true) RadLangFr.IsChecked = true;
            ApplyWindowLocalizationFr();
        }

        // Update nav buttons text
        foreach (var kvp in _navButtons)
        {
            var topic = DocumentationService.Instance.GetTopic(kvp.Key);
            if (topic != null)
            {
                kvp.Value.Content = $"{topic.Icon}  {topic.GetTitle(_currentLanguage)}";
            }
        }

        // Refresh current topic text
        SelectTopic(_currentTopicKey);
    }

    private void ApplyWindowLocalizationFr()
    {
        Title = "Documentation & Architecture Technique • Copilot+ Demo Hub";
        TxtHeaderTitle.Text = "CENTRE DE DOCUMENTATION & ARCHITECTURE TECHNIQUE";
        TxtHeaderSub.Text = "Spécifications matérielles, guides pas-à-pas et pitchs de démonstration";

        LblNavGlobalHeader.Text = "DOCUMENTATION GLOBALE DU PROJET";
        LblNavDemosHeader.Text = "GUIDES DES 11 DÉMONSTRATIONS";

        LblSectionPitch.Text = "PITCH DE PRÉSENTATION EN DIRECT (2 MINUTES)";
        LblSectionDesc.Text = "DESCRIPTION DU CAS D'USAGE & VALEUR MÉTIER";
        LblSectionTech.Text = "ARCHITECTURE TECHNIQUE & PIPELINE D'INFÉRENCE";
        LblSectionBinaries.Text = "BINAIRES, MODÈLES & FICHIERS CLÉS";
        LblSectionHowTo.Text = "GUIDE PAS-À-PAS : COMMENT EXÉCUTER CETTE DÉMO";

        TxtFooterConfidential.Text = "🔒 Fiches techniques certifiées Microsoft Foundry Local • Confinement 100% sur l'appareil";
        BtnCopyDoc.Content = "📋 Copier la Fiche";
        BtnCloseTop.Content = "✕ Fermer";
        BtnCloseBottom.Content = "Fermer";
    }

    private void ApplyWindowLocalizationEn()
    {
        Title = "Documentation & Technical Architecture • Copilot+ Demo Hub";
        TxtHeaderTitle.Text = "DOCUMENTATION HUB & TECHNICAL ARCHITECTURE";
        TxtHeaderSub.Text = "Hardware specifications, step-by-step guides, and demo pitch scripts";

        LblNavGlobalHeader.Text = "GLOBAL PROJECT DOCUMENTATION";
        LblNavDemosHeader.Text = "11 DEMONSTRATIONS GUIDES";

        LblSectionPitch.Text = "LIVE DEMO PITCH SCRIPT (2 MINUTES)";
        LblSectionDesc.Text = "USE CASE OVERVIEW & ENTERPRISE VALUE";
        LblSectionTech.Text = "TECHNICAL ARCHITECTURE & INFERENCE PIPELINE";
        LblSectionBinaries.Text = "BINARIES, NEURAL MODELS & FILE ASSETS";
        LblSectionHowTo.Text = "STEP-BY-STEP GUIDE: HOW TO RUN THIS DEMO";

        TxtFooterConfidential.Text = "🔒 Microsoft Foundry Local certified technical briefs • 100% on-device containment";
        BtnCopyDoc.Content = "📋 Copy Brief";
        BtnCloseTop.Content = "✕ Close";
        BtnCloseBottom.Content = "Close";
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (RadLangEn.IsChecked == true)
        {
            if (_currentLanguage != "en") SetLanguage("en");
        }
        else
        {
            if (_currentLanguage != "fr") SetLanguage("fr");
        }
    }

    private void BtnCopyDoc_Click(object sender, RoutedEventArgs e)
    {
        var topic = DocumentationService.Instance.GetTopic(_currentTopicKey);
        if (topic == null) return;

        bool isEn = _currentLanguage == "en";
        string content =
            $"# {topic.GetTitle(_currentLanguage)}\n" +
            $"_{topic.GetSubtitle(_currentLanguage)}_\n\n" +
            $"**Silicium / Hardware:** {topic.Accelerator}\n" +
            $"**Performance & Énergie:** {topic.GetLatencyPower(_currentLanguage)}\n\n" +
            $"## 1. PITCH (2 MIN)\n{topic.GetPitch(_currentLanguage)}\n\n" +
            $"## 2. DESCRIPTION\n{topic.GetDescription(_currentLanguage)}\n\n" +
            $"## 3. ARCHITECTURE TECHNIQUE\n{topic.GetTechnicalSpecs(_currentLanguage)}\n\n" +
            $"## 4. BINAIRES & MODÈLES\n{topic.GetBinariesAndModels(_currentLanguage)}\n\n" +
            $"## 5. MODE D'EMPLOI\n{topic.GetHowToRun(_currentLanguage)}\n";

        try
        {
            Clipboard.SetText(content);
            BtnCopyDoc.Content = isEn ? "✅ Copied!" : "✅ Copié !";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, ev) =>
            {
                BtnCopyDoc.Content = isEn ? "📋 Copy Brief" : "📋 Copier la Fiche";
                timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

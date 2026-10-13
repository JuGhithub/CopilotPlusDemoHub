using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;
using CopilotPlusDemoHub.Core;
using Microsoft.Win32;

namespace CopilotPlusDemoHub.UI.Views;

public class DocWordInfo
{
    public string Text { get; set; } = string.Empty;
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double PtX0 { get; set; }
    public double PtY0 { get; set; }
    public double PtX1 { get; set; }
    public double PtY1 { get; set; }

    public bool Intersects(Rect r)
    {
        var wordRect = new Rect(X0, Y0, Math.Max(1, X1 - X0), Math.Max(1, Y1 - Y0));
        return r.IntersectsWith(wordRect);
    }
}

public partial class PdfStudioView : UserControl, IDisposable
{
    private readonly FoundryClient _foundryClient;
    private LocalizationService _localizer;
    private readonly Func<string>? _getSelectedDevice;
    private readonly Func<string>? _getEffectiveDevice;
    private readonly Action<string>? _setSelectedDevice;

    private string _currentDocPath = string.Empty;
    private string _currentPreviewPath = string.Empty;
    private string _currentOverlayPath = string.Empty;
    private string _activeModifiedPdfPath = string.Empty;

    // Interactive mouse selection & direct keyboard editing
    private List<DocWordInfo> _documentWords = new();
    private bool _isSelecting;
    private Point _selectionStartPoint;
    private string _selectedText = string.Empty;
    private string _selectedPointsRect = string.Empty;

    private static string FindRootFile(string relativePath)
    {
        string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (File.Exists(direct)) return direct;

        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        string full = Path.GetFullPath(relativePath);
        if (File.Exists(full)) return full;

        return direct;
    }

    public PdfStudioView(
        FoundryClient foundryClient,
        LocalizationService localizer,
        Func<string>? getSelectedDevice = null,
        Func<string>? getEffectiveDevice = null,
        Action<string>? setSelectedDevice = null)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;
        _getSelectedDevice = getSelectedDevice;
        _getEffectiveDevice = getEffectiveDevice;
        _setSelectedDevice = setSelectedDevice;

        ImgPreview.SizeChanged += (s, e) =>
        {
            CanvasInteractive.Width = ImgPreview.ActualWidth;
            CanvasInteractive.Height = ImgPreview.ActualHeight;
        };

        ApplyLocalization(_localizer);

        // Load default contract PDF
        string defaultContract = FindRootFile(Path.Combine("Data", "Documents", "contrat_cadre_prestation.pdf"));
        if (File.Exists(defaultContract))
        {
            LoadDocument(defaultContract);
        }
    }

    public void RefreshDeviceUI()
    {
        string dev = _getSelectedDevice?.Invoke() ?? "NPU";
        if (dev.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            string eff = _getEffectiveDevice?.Invoke() ?? "NPU";
            TxtActiveDevice.Text = $"🤖 Auto ➔ {eff} ({SiliconDetector.NpuName})";
        }
        else if (dev.Equals("NPU", StringComparison.OrdinalIgnoreCase))
        {
            TxtActiveDevice.Text = $"⚡ NPU ({SiliconDetector.NpuName})";
        }
        else if (dev.Equals("GPU", StringComparison.OrdinalIgnoreCase))
        {
            TxtActiveDevice.Text = $"🎮 GPU ({SiliconDetector.GpuName})";
        }
        else
        {
            TxtActiveDevice.Text = $"💻 CPU ({SiliconDetector.CpuName})";
        }
    }

    private string GetTargetDevice()
    {
        string dev = _getSelectedDevice?.Invoke() ?? "NPU";
        if (dev.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            return _getEffectiveDevice?.Invoke() ?? "NPU";
        }
        return dev;
    }

    private void LoadDocument(string filePath)
    {
        if (!File.Exists(filePath)) return;

        _currentDocPath = filePath;
        string fileName = Path.GetFileName(filePath);
        string ext = Path.GetExtension(filePath).ToUpperInvariant();
        long sizeBytes = new FileInfo(filePath).Length;
        double sizeKb = sizeBytes / 1024.0;

        LblDocName.Text = $"📄 {fileName}";
        LblDocPages.Text = $"({ext} • {sizeKb:F1} Ko • Local)";

        // Enable or disable PDF-specific edit controls
        bool isPdf = ext == ".PDF";
        BtnAutoRedactRgpd.IsEnabled = isPdf;
        BtnRedactKeyword.IsEnabled = isPdf;
        BtnApplyStamp.IsEnabled = isPdf;
        BtnExecuteReplace.IsEnabled = isPdf;

        _currentOverlayPath = string.Empty;
        _activeModifiedPdfPath = string.Empty;
        BtnSavePdf.IsEnabled = false;
        BtnOpenExternal.IsEnabled = File.Exists(filePath);
        if (BtnOpenDocHeader != null) BtnOpenDocHeader.IsEnabled = File.Exists(filePath);
        BadgeSpeed.Visibility = Visibility.Collapsed;
        BadgePower.Visibility = Visibility.Collapsed;

        ClearSelectionAndBar();
        _documentWords.Clear();

        // Render preview image asynchronously
        _ = RenderPreviewAsync(filePath);
    }

    private async Task RenderPreviewAsync(string filePath)
    {
        string scriptPath = FindRootFile("pdf_ocr_service.py");
        string workingDir = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "outputs");
        Directory.CreateDirectory(outDir);

        string previewPath = Path.Combine(outDir, $"preview_{DateTime.Now:HHmmssfff}.jpg");

        OverlayBusy.Visibility = Visibility.Visible;
        TxtBusyMessage.Text = "Rendu du document...";
        TxtBusySub.Text = Path.GetFileName(filePath);

        string? resultImg = null;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action render --file \"{filePath}\" --out \"{previewPath}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var tp) && tp.GetString() == "render_complete")
                            {
                                resultImg = root.GetProperty("output_path").GetString();
                                if (root.TryGetProperty("words", out var wordsElem) && wordsElem.ValueKind == JsonValueKind.Array)
                                {
                                    var list = new List<DocWordInfo>();
                                    foreach (var w in wordsElem.EnumerateArray())
                                    {
                                        list.Add(new DocWordInfo
                                        {
                                            Text = w.GetProperty("text").GetString() ?? "",
                                            X0 = w.GetProperty("x0").GetDouble(),
                                            Y0 = w.GetProperty("y0").GetDouble(),
                                            X1 = w.GetProperty("x1").GetDouble(),
                                            Y1 = w.GetProperty("y1").GetDouble(),
                                            PtX0 = w.TryGetProperty("pt_x0", out var px0) ? px0.GetDouble() : 0,
                                            PtY0 = w.TryGetProperty("pt_y0", out var py0) ? py0.GetDouble() : 0,
                                            PtX1 = w.TryGetProperty("pt_x1", out var px1) ? px1.GetDouble() : 0,
                                            PtY1 = w.TryGetProperty("pt_y1", out var py1) ? py1.GetDouble() : 0
                                        });
                                    }
                                    _documentWords = list;
                                }
                            }
                        }
                        catch { }
                    }
                    proc.WaitForExit();
                }
            }
            catch { }
        });

        OverlayBusy.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(resultImg) && File.Exists(resultImg))
        {
            _currentPreviewPath = resultImg;
            DisplayImage(resultImg);
        }
        else if (File.Exists(filePath) && (filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            _currentPreviewPath = filePath;
            DisplayImage(filePath);
        }
    }

    private void DisplayImage(string imagePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            ImgPreview.Source = bitmap;
        }
        catch { }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelOcrMode == null || PanelEditMode == null) return;

        if (RadModeOcr?.IsChecked == true)
        {
            PanelOcrMode.Visibility = Visibility.Visible;
            PanelEditMode.Visibility = Visibility.Collapsed;
        }
        else
        {
            PanelOcrMode.Visibility = Visibility.Collapsed;
            PanelEditMode.Visibility = Visibility.Visible;
        }
    }

    private void OcrEngine_Checked(object sender, RoutedEventArgs e)
    {
        if (RadEngineOlive == null || RadEngineRaw == null) return;
        bool isOlive = RadEngineOlive.IsChecked == true;
        if (isOlive)
        {
            if (TxtModelBadge != null)
                TxtModelBadge.Text = "Modèle : RapidOCR PP-OCRv4 Optimisé Microsoft Olive (FP16)";
            if (BorderOliveOcrKpi != null)
            {
                BorderOliveOcrKpi.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0C231B"));
                BorderOliveOcrKpi.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                TxtOliveOcrKpiTitle.Text = "⚡ Optimisation Microsoft Olive Active :";
                TxtOliveOcrKpiTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                TxtOliveOcrKpiDesc.Text = "Poids disque : 9.8 Mo (-34%) • RAM : ~33 Mo (-17%) • Énergie : -50% Joules";
            }
        }
        else
        {
            if (TxtModelBadge != null)
                TxtModelBadge.Text = "Modèle : RapidOCR PP-OCRv4 Brut (FP32 Standard)";
            if (BorderOliveOcrKpi != null)
            {
                BorderOliveOcrKpi.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                BorderOliveOcrKpi.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                TxtOliveOcrKpiTitle.Text = "🌐 Modèle Brut Standard (Non optimisé) :";
                TxtOliveOcrKpiTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                TxtOliveOcrKpiDesc.Text = "Poids disque : 14.9 Mo • RAM : ~40 Mo • Précision FP32 sans fusion";
            }
        }
    }

    #region Sample Selection Buttons
    private void BtnSampleContract_Click(object sender, RoutedEventArgs e)
    {
        string path = FindRootFile(Path.Combine("Data", "Documents", "contrat_cadre_prestation.pdf"));
        LoadDocument(path);
        LblStatus.Text = "Contrat PDF chargé. Choisissez 'Lancer l'OCR' ou passez en Mode Édition pour anonymiser les données RGPD.";
    }

    private void BtnSampleInvoice_Click(object sender, RoutedEventArgs e)
    {
        string rawPdf = FindRootFile(Path.Combine("Data", "Documents", "facture_scanned_raw.pdf"));
        string path = File.Exists(rawPdf) ? rawPdf : FindRootFile(Path.Combine("Data", "Documents", "facture_maintenance_scan.png"));
        LoadDocument(path);
        RadModeOcr.IsChecked = true;
        LblStatus.Text = "Facture scannée (PDF image) chargée. Cliquez sur 'Lancer l'Extraction OCR' ou 'Convertir en PDF Rechercheable'.";
    }

    private void BtnImportDoc_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Sélectionner un document pour OCR ou Édition",
            Filter = "Documents (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg|Fichiers PDF (*.pdf)|*.pdf|Images (*.png;*.jpg)|*.png;*.jpg|Tous les fichiers (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            LoadDocument(ofd.FileName);
        }
    }
    #endregion

    #region View Mode
    private void BtnViewOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentDocPath) && File.Exists(_currentDocPath))
        {
            _activeModifiedPdfPath = string.Empty;
            LoadDocument(_currentDocPath);
            LblStatus.Text = "Document réinitialisé à sa version originale brute.";
        }
    }

    private void BtnViewOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentOverlayPath) && File.Exists(_currentOverlayPath))
        {
            DisplayImage(_currentOverlayPath);
        }
        else
        {
            MessageBox.Show("Veuillez d'abord exécuter l'extraction OCR pour générer l'overlay des zones détectées.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    #endregion

    #region OCR Execution
    private async void BtnRunOcr_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentDocPath) || !File.Exists(_currentDocPath))
        {
            MessageBox.Show("Veuillez d'abord charger un document valide.", "Document requis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string device = GetTargetDevice();
        string engine = (RadEngineOlive?.IsChecked == true) ? "olive" : "raw";
        string scriptPath = FindRootFile("pdf_ocr_service.py");
        string workingDir = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "outputs");
        Directory.CreateDirectory(outDir);

        string overlayPath = Path.Combine(outDir, $"ocr_overlay_{DateTime.Now:HHmmssfff}.jpg");

        BtnRunOcr.IsEnabled = false;
        OverlayBusy.Visibility = Visibility.Visible;
        TxtBusyMessage.Text = $"Reconnaissance OCR Neurale (Moteur {engine.ToUpper()})...";
        TxtBusySub.Text = $"Traitement sur {device} (Modèle RapidOCR {(engine == "olive" ? "FP16 Optimisé Olive" : "FP32 Brut")})";

        string extractedText = string.Empty;
        int inferMs = 0;
        int lineCount = 0;
        int wordCount = 0;
        double modelSizeMb = 0.0;
        double ramMb = 0.0;
        string engineLabel = string.Empty;
        string resultOverlay = string.Empty;
        string lastErr = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action ocr --file \"{_currentDocPath}\" --device \"{device}\" --engine \"{engine}\" --out_overlay \"{overlayPath}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var tp))
                            {
                                string type = tp.GetString() ?? "";
                                if (type == "ocr_complete")
                                {
                                    extractedText = root.GetProperty("full_text").GetString() ?? "";
                                    inferMs = root.GetProperty("infer_time_ms").GetInt32();
                                    lineCount = root.GetProperty("line_count").GetInt32();
                                    wordCount = root.GetProperty("word_count").GetInt32();
                                    resultOverlay = root.GetProperty("overlay_path").GetString() ?? overlayPath;
                                    modelSizeMb = root.TryGetProperty("model_size_mb", out var msz) ? msz.GetDouble() : (engine == "olive" ? 9.8 : 14.9);
                                    ramMb = root.TryGetProperty("ram_mb", out var rmb) ? rmb.GetDouble() : 0.0;
                                    engineLabel = root.TryGetProperty("engine_label", out var elbl) ? (elbl.GetString() ?? "") : "";

                                    if (root.TryGetProperty("lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
                                    {
                                        var list = new List<DocWordInfo>();
                                        foreach (var l in linesElem.EnumerateArray())
                                        {
                                            string txt = l.GetProperty("text").GetString() ?? "";
                                            if (l.TryGetProperty("box", out var boxElem) && boxElem.ValueKind == JsonValueKind.Array)
                                            {
                                                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                                                foreach (var pt in boxElem.EnumerateArray())
                                                {
                                                    var ptArr = pt.EnumerateArray().ToList();
                                                    if (ptArr.Count >= 2)
                                                    {
                                                        double x = ptArr[0].GetDouble();
                                                        double y = ptArr[1].GetDouble();
                                                        if (x < minX) minX = x;
                                                        if (x > maxX) maxX = x;
                                                        if (y < minY) minY = y;
                                                        if (y > maxY) maxY = y;
                                                    }
                                                }
                                                list.Add(new DocWordInfo
                                                {
                                                    Text = txt,
                                                    X0 = minX,
                                                    Y0 = minY,
                                                    X1 = maxX,
                                                    Y1 = maxY,
                                                    PtX0 = minX / (150.0 / 72.0),
                                                    PtY0 = minY / (150.0 / 72.0),
                                                    PtX1 = maxX / (150.0 / 72.0),
                                                    PtY1 = maxY / (150.0 / 72.0)
                                                });
                                            }
                                        }
                                        _documentWords = list;
                                    }
                                }
                                else if (type == "error")
                                {
                                    lastErr = root.GetProperty("message").GetString() ?? "Erreur d'inférence OCR";
                                }
                            }
                        }
                        catch { }
                    }
                    proc.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                lastErr = ex.Message;
            }
        });

        OverlayBusy.Visibility = Visibility.Collapsed;
        BtnRunOcr.IsEnabled = true;

        if (!string.IsNullOrEmpty(extractedText))
        {
            TxtOcrResult.Text = extractedText;
            string optBadge = engine == "olive" ? " [⚡ Olive FP16 -34% disque]" : " [🌐 Brut FP32]";
            TxtOcrStats.Text = $"✅ {lineCount} lignes • {wordCount} mots en {inferMs} ms sur {device}{optBadge} ({modelSizeMb:F1} Mo • RAM: {ramMb:F1} Mo)";
            LblStatus.Text = $"✅ OCR {engine.ToUpper()} complété avec succès ! {wordCount} mots extraits localement en {inferMs} ms.";

            if (File.Exists(resultOverlay))
            {
                _currentOverlayPath = resultOverlay;
                DisplayImage(resultOverlay);
            }

            // Silicon Power Metrics
            double watts = device.ToUpperInvariant().Contains("GPU") ? 16.5 : (engine == "olive" ? 2.2 : 4.5);
            double joules = watts * (inferMs / 1000.0);

            TxtInferSpeed.Text = $"⚡ {inferMs} ms • {device} ({engine.ToUpper()})";
            BadgeSpeed.Visibility = Visibility.Visible;
            TxtPower.Text = $"🔋 ~{watts:F1} W • 🌱 {joules:F3} J ({(engine == "olive" ? "Sobriété Olive" : "Standard")})";
            BadgePower.Visibility = Visibility.Visible;

            // Audit Telemetry Logging
            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Studio PDF - Reconnaissance OCR",
                requestedSilicon: device,
                effectiveSilicon: device,
                siliconDeviceName: $"RapidOCR ({engine.ToUpper()} on {device})",
                latencyMs: inferMs,
                customPowerWatts: watts
            );
        }
        else
        {
            LblStatus.Text = $"⚠️ Erreur OCR : {lastErr}";
        }
    }

    private void BtnCopyText_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtOcrResult.Text) && !TxtOcrResult.Text.StartsWith("Le texte extrait"))
        {
            Clipboard.SetText(TxtOcrResult.Text);
            MessageBox.Show("Texte extrait copié dans le presse-papier avec succès !", "Copié", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExportTxt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtOcrResult.Text)) return;

        var sfd = new SaveFileDialog
        {
            Title = "Exporter le texte extrait par OCR",
            FileName = $"ocr_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "Fichier texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*"
        };

        if (sfd.ShowDialog() == true)
        {
            File.WriteAllText(sfd.FileName, TxtOcrResult.Text, System.Text.Encoding.UTF8);
            MessageBox.Show($"Texte exporté dans : {sfd.FileName}", "Export réussi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TxtOcrResult_SelectionChanged(object sender, RoutedEventArgs e)
    {
        string sel = TxtOcrResult.SelectedText.Trim();
        if (!string.IsNullOrEmpty(sel) && sel.Length > 1 && !sel.StartsWith("Le texte extrait"))
        {
            _selectedText = sel;
            TxtSearchTerm.Text = sel;
            TxtKeywordToRedact.Text = sel;
            TxtQuickSelectedDisplay.Text = sel;
            TxtQuickReplaceInput.Text = sel;
            BarQuickAction.Visibility = Visibility.Visible;
        }
    }

    private async void BtnMakeSearchablePdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentDocPath) || !File.Exists(_currentDocPath)) return;

        string device = GetTargetDevice();
        string engine = (RadEngineOlive?.IsChecked == true) ? "olive" : "raw";
        string scriptPath = FindRootFile("pdf_ocr_service.py");
        string workingDir = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "outputs");
        Directory.CreateDirectory(outDir);

        string outPdf = Path.Combine(outDir, $"pdf_searchable_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        string outPreview = Path.Combine(outDir, $"preview_searchable_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

        BtnMakeSearchablePdf.IsEnabled = false;
        OverlayBusy.Visibility = Visibility.Visible;
        TxtBusyMessage.Text = "Génération du PDF Rechercheable (OCR Sandwich)...";
        TxtBusySub.Text = $"Injection de la couche texte invisible via RapidOCR ({engine.ToUpper()}) sur {device} • Zéro Cloud";

        int inferMs = 0;
        int wordCount = 0;
        int lineCount = 0;
        string finalPdf = string.Empty;
        string finalPreview = string.Empty;
        string lastErr = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action make_searchable --file \"{_currentDocPath}\" --out_pdf \"{outPdf}\" --out_preview \"{outPreview}\" --device \"{device}\" --engine \"{engine}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var tp))
                            {
                                string type = tp.GetString() ?? "";
                                if (type == "searchable_complete")
                                {
                                    finalPdf = root.GetProperty("output_pdf").GetString() ?? outPdf;
                                    finalPreview = root.GetProperty("preview_path").GetString() ?? outPreview;
                                    inferMs = root.GetProperty("infer_time_ms").GetInt32();
                                    wordCount = root.GetProperty("word_count").GetInt32();
                                    lineCount = root.GetProperty("line_count").GetInt32();
                                }
                                else if (type == "error")
                                {
                                    lastErr = root.GetProperty("message").GetString() ?? "Erreur lors de la création du PDF rechercheable";
                                }
                            }
                        }
                        catch { }
                    }
                    proc.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                lastErr = ex.Message;
            }
        });

        OverlayBusy.Visibility = Visibility.Collapsed;
        BtnMakeSearchablePdf.IsEnabled = true;

        if (!string.IsNullOrEmpty(finalPdf) && File.Exists(finalPdf))
        {
            // Reload modified document as current to allow editing / mouse selection
            LoadDocument(finalPdf);

            _activeModifiedPdfPath = finalPdf;
            BtnSavePdf.IsEnabled = true;
            BtnOpenExternal.IsEnabled = true;
            if (BtnOpenDocHeader != null) BtnOpenDocHeader.IsEnabled = true;

            // Silicon Power Metrics
            double watts = device.ToUpperInvariant().Contains("GPU") ? 16.5 : 2.2;
            double joules = watts * (inferMs / 1000.0);

            TxtInferSpeed.Text = $"⚡ {inferMs} ms • {device}";
            BadgeSpeed.Visibility = Visibility.Visible;
            TxtPower.Text = $"🔋 ~{watts:F1} W • 🌱 {joules:F3} J";
            BadgePower.Visibility = Visibility.Visible;

            LblStatus.Text = $"✅ PDF Rechercheable généré avec succès ({wordCount} mots indexés en {inferMs} ms). Ouvrez-le pour tester la recherche Ctrl+F !";

            // Telemetry
            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Studio PDF - Numérisation PDF Rechercheable (OCR)",
                requestedSilicon: device,
                effectiveSilicon: device,
                siliconDeviceName: $"RapidOCR DBNet Sandwich ({device})",
                latencyMs: inferMs,
                customPowerWatts: watts
            );

            MessageBox.Show(
                $"Le document scanné a été transformé avec succès en PDF Rechercheable (PDF Sandwich) !\n\n" +
                $"• Mots indexés : {wordCount} (sur {lineCount} lignes)\n" +
                $"• Temps d'exécution : {inferMs} ms sur puce {device}\n" +
                $"• Couche de texte invisible conforme ISO 32000 / PDF-A\n\n" +
                $"Vous pouvez maintenant cliquer sur '👁️ Ouvrir le Fichier' pour tester la recherche Ctrl+F et le copier-coller dans votre lecteur PDF !",
                "PDF Rechercheable Créé",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            LblStatus.Text = $"⚠️ Échec de la transformation : {lastErr}";
        }
    }
    #endregion

    #region PDF Legal Edition (Redact, Stamp, Replace)
    private async void BtnAutoRedactRgpd_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePdfEditAsync(
            action: "redact",
            extraArgs: "--mode auto_rgpd",
            auditName: "Studio PDF - Caviardage RGPD",
            busyMsg: "Caviardage certifié RGPD en cours (Suppression irréversible des métadonnées)..."
        );
    }

    private async void BtnRedactKeyword_Click(object sender, RoutedEventArgs e)
    {
        string kw = TxtKeywordToRedact.Text.Trim();
        if (string.IsNullOrWhiteSpace(kw))
        {
            MessageBox.Show("Veuillez saisir un mot-clé à caviarder.", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ExecutePdfEditAsync(
            action: "redact",
            extraArgs: $"--keyword \"{kw}\"",
            auditName: "Studio PDF - Caviardage Mot-Clé",
            busyMsg: $"Caviardage légal du terme '{kw}'..."
        );
    }

    private async void BtnApplyStamp_Click(object sender, RoutedEventArgs e)
    {
        string stampType = "CONFIDENTIAL";
        if (RadStampApproved.IsChecked == true) stampType = "APPROVED";
        else if (RadStampCertified.IsChecked == true) stampType = "CERTIFIED_COPY";

        await ExecutePdfEditAsync(
            action: "stamp",
            extraArgs: $"--stamp_type {stampType}",
            auditName: "Studio PDF - Tampon Officiel",
            busyMsg: $"Apposition du tampon certifié ({stampType})..."
        );
    }

    private async void BtnExecuteReplace_Click(object sender, RoutedEventArgs e)
    {
        string search = TxtSearchTerm.Text.Trim();
        string replace = TxtReplaceTerm.Text.Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            MessageBox.Show("Veuillez saisir un terme à rechercher.", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ExecutePdfEditAsync(
            action: "replace",
            extraArgs: $"--search \"{search}\" --replace \"{replace}\"",
            auditName: "Studio PDF - Remplacement Texte",
            busyMsg: $"Remplacement vectoriel de '{search}' par '{replace}'..."
        );
    }

    private async Task ExecutePdfEditAsync(string action, string extraArgs, string auditName, string busyMsg)
    {
        string inputDoc = !string.IsNullOrEmpty(_activeModifiedPdfPath) && File.Exists(_activeModifiedPdfPath)
            ? _activeModifiedPdfPath
            : _currentDocPath;

        if (string.IsNullOrEmpty(inputDoc) || !File.Exists(inputDoc)) return;

        string device = GetTargetDevice();
        string scriptPath = FindRootFile("pdf_ocr_service.py");
        string workingDir = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "outputs");
        Directory.CreateDirectory(outDir);

        string outPdf = Path.Combine(outDir, $"pdf_modified_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        string outPreview = Path.Combine(outDir, $"preview_mod_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

        OverlayBusy.Visibility = Visibility.Visible;
        TxtBusyMessage.Text = busyMsg;
        TxtBusySub.Text = "Édition locale en mémoire • Zéro appel cloud";

        int inferMs = 0;
        string finalPdf = string.Empty;
        string finalPreview = string.Empty;
        string lastErr = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action {action} --file \"{inputDoc}\" --out_pdf \"{outPdf}\" --out_preview \"{outPreview}\" --device \"{device}\" {extraArgs}",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var tp))
                            {
                                string type = tp.GetString() ?? "";
                                if (type.EndsWith("_complete"))
                                {
                                    finalPdf = root.GetProperty("output_pdf").GetString() ?? outPdf;
                                    finalPreview = root.GetProperty("preview_path").GetString() ?? outPreview;
                                    inferMs = root.GetProperty("infer_time_ms").GetInt32();

                                    // Update word bounding boxes for subsequent mouse clicks/selection
                                    if (root.TryGetProperty("words", out var wordsElem) && wordsElem.ValueKind == JsonValueKind.Array)
                                    {
                                        var list = new List<DocWordInfo>();
                                        foreach (var w in wordsElem.EnumerateArray())
                                        {
                                            list.Add(new DocWordInfo
                                            {
                                                Text = w.GetProperty("text").GetString() ?? "",
                                                X0 = w.GetProperty("x0").GetDouble(),
                                                Y0 = w.GetProperty("y0").GetDouble(),
                                                X1 = w.GetProperty("x1").GetDouble(),
                                                Y1 = w.GetProperty("y1").GetDouble(),
                                                PtX0 = w.TryGetProperty("pt_x0", out var px0) ? px0.GetDouble() : 0,
                                                PtY0 = w.TryGetProperty("pt_y0", out var py0) ? py0.GetDouble() : 0,
                                                PtX1 = w.TryGetProperty("pt_x1", out var px1) ? px1.GetDouble() : 0,
                                                PtY1 = w.TryGetProperty("pt_y1", out var py1) ? py1.GetDouble() : 0
                                            });
                                        }
                                        _documentWords = list;
                                    }
                                }
                                else if (type == "error")
                                {
                                    lastErr = root.GetProperty("message").GetString() ?? "Erreur lors de la modification";
                                }
                            }
                        }
                        catch { }
                    }
                    proc.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                lastErr = ex.Message;
            }
        });

        OverlayBusy.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(finalPdf) && File.Exists(finalPdf))
        {
            _activeModifiedPdfPath = finalPdf;
            BtnSavePdf.IsEnabled = true;
            BtnOpenExternal.IsEnabled = true;
            if (BtnOpenDocHeader != null) BtnOpenDocHeader.IsEnabled = true;
            LblDocPages.Text = "(Modifié en mémoire • Prêt à sauvegarder)";

            if (File.Exists(finalPreview))
            {
                _currentPreviewPath = finalPreview;
                DisplayImage(finalPreview);
            }

            // Power Metrics
            double watts = device.ToUpperInvariant().Contains("GPU") ? 16.5 : 2.2;
            double joules = watts * (inferMs / 1000.0);

            TxtInferSpeed.Text = $"⚡ {inferMs} ms • {device}";
            BadgeSpeed.Visibility = Visibility.Visible;
            TxtPower.Text = $"🔋 ~{watts:F1} W • 🌱 {joules:F3} J";
            BadgePower.Visibility = Visibility.Visible;

            LblStatus.Text = $"✅ Opération '{auditName}' réalisée avec succès en {inferMs} ms sur {device} !";

            // Telemetry
            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: auditName,
                requestedSilicon: device,
                effectiveSilicon: device,
                siliconDeviceName: $"PyMuPDF Vector Engine ({device})",
                latencyMs: inferMs,
                customPowerWatts: watts
            );
        }
        else
        {
            LblStatus.Text = $"⚠️ Échec de la modification : {lastErr}";
        }
    }

    private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
    {
        string fileToSave = !string.IsNullOrEmpty(_activeModifiedPdfPath) && File.Exists(_activeModifiedPdfPath)
            ? _activeModifiedPdfPath
            : _currentDocPath;

        if (string.IsNullOrEmpty(fileToSave) || !File.Exists(fileToSave)) return;

        var sfd = new SaveFileDialog
        {
            Title = "Enregistrer le document PDF",
            FileName = $"document_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
            Filter = "Document PDF (*.pdf)|*.pdf|Tous les fichiers (*.*)|*.*"
        };

        if (sfd.ShowDialog() == true)
        {
            File.Copy(fileToSave, sfd.FileName, overwrite: true);
            MessageBox.Show($"Document enregistré avec succès dans :\n{sfd.FileName}", "Enregistrement Réussi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        string fileToOpen = !string.IsNullOrEmpty(_activeModifiedPdfPath) && File.Exists(_activeModifiedPdfPath)
            ? _activeModifiedPdfPath
            : _currentDocPath;

        if (!string.IsNullOrEmpty(fileToOpen) && File.Exists(fileToOpen))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileToOpen,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{fileToOpen}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Impossible d'ouvrir le fichier ({fileToOpen}) :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            MessageBox.Show("Aucun document n'est actuellement disponible à l'ouverture.", "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    #endregion

    #region Interactive Mouse Selection & Direct Keyboard Editing
    private Rect GetImageRenderedRect()
    {
        if (ImgPreview.Source is not BitmapSource bmp) return Rect.Empty;
        double srcW = bmp.PixelWidth;
        double srcH = bmp.PixelHeight;
        double ctrlW = ImgPreview.ActualWidth;
        double ctrlH = ImgPreview.ActualHeight;

        if (srcW <= 0 || srcH <= 0 || ctrlW <= 0 || ctrlH <= 0) return Rect.Empty;

        double scale = Math.Min(ctrlW / srcW, ctrlH / srcH);
        double renderW = srcW * scale;
        double renderH = srcH * scale;
        double renderX = (ctrlW - renderW) / 2.0;
        double renderY = (ctrlH - renderH) / 2.0;

        return new Rect(renderX, renderY, renderW, renderH);
    }

    private Point ScreenToPixel(Point screenPt, Rect renderRect, double pixelW, double pixelH)
    {
        if (renderRect.Width <= 0 || renderRect.Height <= 0) return new Point(0, 0);
        double nx = (screenPt.X - renderRect.X) / renderRect.Width;
        double ny = (screenPt.Y - renderRect.Y) / renderRect.Height;
        return new Point(Math.Clamp(nx * pixelW, 0, pixelW), Math.Clamp(ny * pixelH, 0, pixelH));
    }

    private void CanvasInteractive_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (ImgPreview.Source is not BitmapSource) return;

        _isSelecting = true;
        _selectionStartPoint = e.GetPosition(CanvasInteractive);

        Canvas.SetLeft(RectDragSelection, _selectionStartPoint.X);
        Canvas.SetTop(RectDragSelection, _selectionStartPoint.Y);
        RectDragSelection.Width = 0;
        RectDragSelection.Height = 0;
        RectDragSelection.Visibility = Visibility.Visible;

        CanvasInteractive.CaptureMouse();
    }

    private void CanvasInteractive_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        Point current = e.GetPosition(CanvasInteractive);
        double x = Math.Min(_selectionStartPoint.X, current.X);
        double y = Math.Min(_selectionStartPoint.Y, current.Y);
        double w = Math.Abs(current.X - _selectionStartPoint.X);
        double h = Math.Abs(current.Y - _selectionStartPoint.Y);

        Canvas.SetLeft(RectDragSelection, x);
        Canvas.SetTop(RectDragSelection, y);
        RectDragSelection.Width = w;
        RectDragSelection.Height = h;
    }

    private void CanvasInteractive_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;
        CanvasInteractive.ReleaseMouseCapture();

        if (ImgPreview.Source is not BitmapSource bmp) return;

        Point endPoint = e.GetPosition(CanvasInteractive);
        Rect renderRect = GetImageRenderedRect();
        if (renderRect.IsEmpty) return;

        Point p1 = ScreenToPixel(_selectionStartPoint, renderRect, bmp.PixelWidth, bmp.PixelHeight);
        Point p2 = ScreenToPixel(endPoint, renderRect, bmp.PixelWidth, bmp.PixelHeight);

        double selPixX = Math.Min(p1.X, p2.X);
        double selPixY = Math.Min(p1.Y, p2.Y);
        double selPixW = Math.Abs(p2.X - p1.X);
        double selPixH = Math.Abs(p2.Y - p1.Y);

        // Click detection without significant drag -> expand radius around clicked point
        if (selPixW < 8 && selPixH < 8)
        {
            selPixX = Math.Max(0, selPixX - 16);
            selPixY = Math.Max(0, selPixY - 12);
            selPixW = 32;
            selPixH = 24;
        }

        var pixelSelectionRect = new Rect(selPixX, selPixY, selPixW, selPixH);

        // Intersect with known document words or OCR lines
        var matched = _documentWords.Where(w => w.Intersects(pixelSelectionRect)).OrderBy(w => w.Y0).ThenBy(w => w.X0).ToList();

        if (matched.Count > 0)
        {
            string extracted = string.Join(" ", matched.Select(m => m.Text));
            _selectedText = extracted;

            double minPtX = matched.Min(w => w.PtX0);
            double minPtY = matched.Min(w => w.PtY0);
            double maxPtX = matched.Max(w => w.PtX1);
            double maxPtY = matched.Max(w => w.PtY1);
            _selectedPointsRect = $"{minPtX:F1},{minPtY:F1},{maxPtX:F1},{maxPtY:F1}";

            // Snap the visual rectangle around the matched words
            double minPxX = matched.Min(w => w.X0);
            double minPxY = matched.Min(w => w.Y0);
            double maxPxX = matched.Max(w => w.X1);
            double maxPxY = matched.Max(w => w.Y1);

            double scrX0 = renderRect.X + (minPxX / bmp.PixelWidth) * renderRect.Width;
            double scrY0 = renderRect.Y + (minPxY / bmp.PixelHeight) * renderRect.Height;
            double scrX1 = renderRect.X + (maxPxX / bmp.PixelWidth) * renderRect.Width;
            double scrY1 = renderRect.Y + (maxPxY / bmp.PixelHeight) * renderRect.Height;

            Canvas.SetLeft(RectDragSelection, scrX0);
            Canvas.SetTop(RectDragSelection, scrY0);
            RectDragSelection.Width = Math.Max(10, scrX1 - scrX0);
            RectDragSelection.Height = Math.Max(10, scrY1 - scrY0);
            RectDragSelection.Visibility = Visibility.Visible;

            ShowQuickActionBar(_selectedText);
        }
        else if (selPixW > 15 && selPixH > 15)
        {
            // Arbitrary graphic / signature zone without recognized text
            double scale = 150.0 / 72.0;
            double ptX0 = selPixX / scale;
            double ptY0 = selPixY / scale;
            double ptX1 = (selPixX + selPixW) / scale;
            double ptY1 = (selPixY + selPixH) / scale;

            _selectedText = string.Empty;
            _selectedPointsRect = $"{ptX0:F1},{ptY0:F1},{ptX1:F1},{ptY1:F1}";

            ShowQuickActionBar("Zone Graphique / Signature");
        }
        else
        {
            ClearSelectionAndBar();
        }
    }

    private void ShowQuickActionBar(string displayText)
    {
        TxtQuickSelectedDisplay.Text = displayText;
        TxtQuickReplaceInput.Text = displayText;
        BarQuickAction.Visibility = Visibility.Visible;

        // Auto-fill right panel fields
        TxtSearchTerm.Text = displayText;
        TxtKeywordToRedact.Text = displayText;

        // Switch to Edition mode if currently on OCR mode to see changes
        if (RadModeEdit != null && RadModeEdit.IsChecked != true)
        {
            RadModeEdit.IsChecked = true;
        }

        // Direct keyboard focus on replacement input!
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TxtQuickReplaceInput.Focus();
            TxtQuickReplaceInput.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ClearSelectionAndBar()
    {
        _selectedText = string.Empty;
        _selectedPointsRect = string.Empty;
        if (RectDragSelection != null) RectDragSelection.Visibility = Visibility.Collapsed;
        if (BarQuickAction != null) BarQuickAction.Visibility = Visibility.Collapsed;
    }

    private void TxtQuickReplaceInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            BtnQuickExecuteReplace_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ClearSelectionAndBar();
        }
    }

    private async void BtnQuickExecuteReplace_Click(object sender, RoutedEventArgs e)
    {
        string replaceWith = TxtQuickReplaceInput.Text.Trim();
        if (string.IsNullOrEmpty(_selectedText) && string.IsNullOrEmpty(_selectedPointsRect)) return;

        string extraArgs = !string.IsNullOrEmpty(_selectedText)
            ? (!string.IsNullOrEmpty(_selectedPointsRect)
                ? $"--search \"{_selectedText}\" --replace \"{replaceWith}\" --rect \"{_selectedPointsRect}\""
                : $"--search \"{_selectedText}\" --replace \"{replaceWith}\"")
            : $"--rect \"{_selectedPointsRect}\" --replace \"{replaceWith}\"";

        string termLabel = !string.IsNullOrEmpty(_selectedText) ? _selectedText : "Zone";
        ClearSelectionAndBar();

        await ExecutePdfEditAsync(
            action: "replace",
            extraArgs: extraArgs,
            auditName: "Studio PDF - Remplacement Clavier / Souris",
            busyMsg: $"Remplacement de '{termLabel}' par '{replaceWith}'..."
        );
    }

    private async void BtnQuickExecuteRedact_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedText) && string.IsNullOrEmpty(_selectedPointsRect)) return;

        string extraArgs = !string.IsNullOrEmpty(_selectedPointsRect)
            ? $"--rect \"{_selectedPointsRect}\""
            : $"--keyword \"{_selectedText}\"";

        ClearSelectionAndBar();

        await ExecutePdfEditAsync(
            action: "redact",
            extraArgs: extraArgs,
            auditName: "Studio PDF - Caviardage Souris",
            busyMsg: "Caviardage légal de la sélection..."
        );
    }

    private void BtnQuickCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedText))
        {
            Clipboard.SetText(_selectedText);
            LblStatus.Text = $"📋 '{_selectedText}' copié dans le presse-papier.";
        }
    }

    private void BtnQuickClose_Click(object sender, RoutedEventArgs e)
    {
        ClearSelectionAndBar();
    }

    private void TxtReplaceTerm_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            BtnExecuteReplace_Click(sender, e);
        }
    }

    private void TxtKeywordToRedact_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            BtnRedactKeyword_Click(sender, e);
        }
    }
    #endregion

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        bool isEn = _localizer.CurrentLanguage == "en";

        LblPdfTitle.Text = isEn
            ? "📑 SOVEREIGN PDF STUDIO & OCR (LEGAL EDITION & DIGITIZATION)"
            : "📑 STUDIO PDF & OCR SOUVERAIN (ÉDITION LÉGALE & NUMÉRISATION)";

        LblPdfSub.Text = isEn
            ? "Enterprise Proof for CIO & CISO: On-device OCR text recognition & legal PDF edition with 0€ Adobe Pro license and zero cloud leak."
            : "Preuve pour DSI & Juristes : Reconnaissance de texte OCR (ONNX) et modification légale de PDF 100% locales, sans licence payante Adobe Pro ni API Cloud.";

        LblComplianceBadge.Text = isEn
            ? "0€ License • Zero Cloud Leak • GDPR Art. 17"
            : "0€ Licence • Zéro Fuite Cloud • RGPD Art. 17";

        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = isEn
                ? "Model: RapidOCR Text Detection & Recognition (ONNX/OV)"
                : "Modèle : RapidOCR Text Detection & Recognition (ONNX/OV)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = isEn
                    ? "On-device neural OCR pipeline and text segmentation running 100% locally with zero licensing cost."
                    : "Pipeline OCR neuronal et segmentation textuelle exécutés 100% sur appareil sans licence payante.";
            }
        }

        BtnSampleContract.Content = isEn ? "📄 Sample Contract (PDF)" : "📄 Exemple Contrat Cadre (PDF)";
        BtnSampleInvoice.Content = isEn ? "🧾 Sample Scanned Invoice (Scan)" : "🧾 Exemple Facture Numérisée (Scan)";
        BtnImportDoc.Content = isEn ? "📂 Import Document (PDF / Image)" : "📂 Importer un Document (PDF / Image)";

        BtnViewOriginal.Content = isEn ? "Raw Document" : "Document Brut";
        BtnViewOverlay.Content = isEn ? "Cyan OCR Zones" : "Zones OCR Cyan";

        TxtMouseHint.Text = isEn
            ? "Select text with mouse to edit with keyboard"
            : "Sélectionnez du texte à la souris pour modifier au clavier";

        LblQuickReplaceLabel.Text = isEn ? "⌨️ Replace with:" : "⌨️ Remplacer par :";
        BtnQuickExecuteReplace.Content = isEn ? "✏️ Replace (Enter)" : "✏️ Remplacer (Entrée)";
        BtnQuickExecuteRedact.Content = isEn ? "🛡️ Redact" : "🛡️ Caviarder";

        BtnRunOcr.Content = isEn ? "🚀 Run Local Neural OCR (NPU)" : "🚀 Lancer l'Extraction OCR Locale (NPU)";
        BtnMakeSearchablePdf.Content = isEn
            ? "📄 Convert to Searchable PDF (Text Layer Ctrl+F)"
            : "📄 Convertir en PDF Rechercheable (Couche Texte Ctrl+F)";
        BtnCopyText.Content = isEn ? "📋 Copy Text" : "📋 Copier Texte";
        BtnExportTxt.Content = isEn ? "📥 Export TXT" : "📥 Exporter TXT";

        BtnAutoRedactRgpd.Content = isEn
            ? "🛡️ Redact Sensitive GDPR Data (Emails, Phones, IBAN, SSN)"
            : "🛡️ Caviarder Données Sensibles (Emails, Tél, IBAN, NIR)";
        BtnRedactKeyword.Content = isEn ? "Redact this term" : "Cavier ce terme";

        RadStampConfidential.Content = isEn ? "🔒 CONFIDENTIAL" : "🔒 CONFIDENTIEL";
        RadStampApproved.Content = isEn ? "✅ APPROVED BY IT" : "✅ APPROUVÉ DSI";
        RadStampCertified.Content = isEn ? "📄 CERTIFIED COPY" : "📄 COPIE CONFORME";

        BtnApplyStamp.Content = isEn ? "🏷️ Apply Legal Stamp" : "🏷️ Apposer le Tampon Juridique";
        BtnExecuteReplace.Content = isEn ? "✏️ Replace in Document (Enter)" : "✏️ Remplacer dans le Document (Entrée)";

        BtnSavePdf.Content = isEn ? "💾 Save Modified PDF" : "💾 Sauvegarder le PDF Modifié";
        BtnOpenExternal.Content = isEn ? "👁️ Open File" : "👁️ Ouvrir le Fichier";
        if (BtnOpenDocHeader != null)
        {
            BtnOpenDocHeader.Content = isEn ? "👁️ Open File" : "👁️ Ouvrir le Fichier";
            BtnOpenDocHeader.ToolTip = isEn
                ? "Open this document in default PDF reader (Edge / Acrobat)"
                : "Ouvrir ce document dans le lecteur par défaut (Edge / Acrobat)";
        }

        if (TxtDocHelp != null) TxtDocHelp.Text = isEn ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = isEn 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";

        RefreshDeviceUI();
    }

    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_pdf", _localizer?.CurrentLanguage);
    }

    public void Dispose()
    {
    }
}

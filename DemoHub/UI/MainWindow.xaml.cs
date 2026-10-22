using System;
using System.Windows;
using System.Windows.Input;
using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.UI.Views;

namespace CopilotPlusDemoHub.UI;

public partial class MainWindow : Window
{
    private readonly FoundryClient _foundryClient;
    private readonly LocalizationService _localizer;
    private bool _isInitialized;
    private bool _isTrueFullscreen;
    private WindowStyle _previousStyle = WindowStyle.SingleBorderWindow;

    private string _autoResolvedDevice = "NPU";

    private OperationsView? _operationsView;
    private EngineeringView? _engineeringView;
    private InsuranceView? _insuranceView;
    private FinancialView? _financialView;
    private BenchmarkView? _benchmarkView;
    private AudioStudioView? _audioStudioView;
    private AvatarStudioView? _avatarStudioView;
    private MagicEraserStudioView? _magicEraserStudioView;
    private PdfStudioView? _pdfStudioView;
    private SecurityAccessView? _securityAccessView;
    private FleetTelemetryView? _fleetTelemetryView;
    private OliveLabView? _oliveLabView;

    public MainWindow()
    {
        _foundryClient = new FoundryClient();
        _localizer = new LocalizationService();
        InitializeComponent();
        _isInitialized = true;

        if (_localizer.CurrentLanguage == "en")
        {
            RadLangEn.IsChecked = true;
        }
        else
        {
            RadLangFr.IsChecked = true;
        }

        Loaded += (s, e) =>
        {
            _ = EnterpriseTelemetryService.Instance;
            ApplyLocalization();
            ShowOperationsView();
            CheckServerStatusAsync();
            UpdateWindowStateUi();
        };

        StateChanged += (s, e) => UpdateWindowStateUi();

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _isTrueFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
        };
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore transient mouse capture errors
                }
            }
        }
    }

    private void BtnWindowToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized || _isTrueFullscreen)
        {
            if (_isTrueFullscreen)
            {
                WindowStyle = _previousStyle;
                _isTrueFullscreen = false;
            }
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
        UpdateWindowStateUi();
    }

    private void ToggleFullscreen()
    {
        if (!_isTrueFullscreen && WindowState != WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else if (!_isTrueFullscreen && WindowState == WindowState.Maximized)
        {
            // Enter immersive borderless fullscreen
            _previousStyle = WindowStyle;
            WindowStyle = WindowStyle.None;
            _isTrueFullscreen = true;
        }
        else
        {
            // Restore from full screen or maximized to normal
            WindowStyle = _previousStyle;
            WindowState = WindowState.Normal;
            _isTrueFullscreen = false;
        }
        UpdateWindowStateUi();
    }

    private void UpdateWindowStateUi()
    {
        if (TxtWindowToggle == null || IconWindowToggle == null || _localizer == null) return;

        bool isExpanded = (WindowState == WindowState.Maximized) || _isTrueFullscreen;
        if (isExpanded)
        {
            IconWindowToggle.Text = "🗗";
            TxtWindowToggle.Text = _localizer.Get("Header_Restore");
        }
        else
        {
            IconWindowToggle.Text = "🗖";
            TxtWindowToggle.Text = _localizer.Get("Header_Maximize");
        }
    }

    private static bool _modelsWarmedUp;

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _localizer == null) return;

        string target = "fr";
        if (ReferenceEquals(sender, RadLangEn) || (sender is FrameworkElement fe && fe.Name == "RadLangEn"))
        {
            target = "en";
        }
        else if (ReferenceEquals(sender, RadLangFr) || (sender is FrameworkElement fe2 && fe2.Name == "RadLangFr"))
        {
            target = "fr";
        }
        else
        {
            target = (RadLangEn?.IsChecked == true) ? "en" : "fr";
        }

        if (!_localizer.CurrentLanguage.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            _localizer.SetLanguage(target);
            ApplyLocalization();
        }
    }

    private void ApplyLocalization()
    {
        if (!_isInitialized || _localizer == null) return;

        LblTitle.Text = _localizer.Get("Menu_Title");
        LblSubtitle.Text = _localizer.Get("Header_Subtitle");
        LblAccelerator.Text = _localizer.Get("Header_Accelerator");
        LblNavBusiness.Text = _localizer.Get("Nav_BusinessCases");
        NavOperations.Content = _localizer.Get("Nav_Operations");
        NavEngineering.Content = _localizer.Get("Nav_Engineering");
        NavInsurance.Content = _localizer.Get("Nav_Insurance");
        NavFinancial.Content = _localizer.Get("Nav_Financial");
        LblNavSilicon.Text = _localizer.Get("Nav_SiliconDemos");
        NavBenchmark.Content = _localizer.Get("Nav_Benchmark");
        NavOliveLab.Content = _localizer.Get("Nav_OliveLab");
        NavAudio.Content = _localizer.Get("Nav_Audio");
        NavAvatar.Content = _localizer.Get("Nav_Avatar");
        NavEraser.Content = _localizer.Get("Nav_Eraser");
        NavPdfStudio.Content = _localizer.Get("Nav_PdfStudio");
        NavSecurityAccess.Content = _localizer.Get("Nav_SecurityAccess");
        LblNavGovernance.Text = _localizer.Get("Nav_Governance");
        NavFleetTelemetry.Content = _localizer.Get("Nav_FleetTelemetry");
        if (TxtHeaderTelemetry1 != null) TxtHeaderTelemetry1.Text = _localizer.CurrentLanguage == "en" ? "Telemetry" : "Télémétrie";
        if (TxtHeaderTelemetry2 != null) TxtHeaderTelemetry2.Text = "& Audit";
        BtnHeaderTelemetry.ToolTip = _localizer.CurrentLanguage == "en" ? "Open Fleet Governance & Telemetry Console" : "Ouvrir la console de Gouvernance & Télémétrie Flotte";
        if (BtnHeaderDoc != null) BtnHeaderDoc.ToolTip = _localizer.CurrentLanguage == "en" 
            ? "Project Documentation: Prerequisites, Architecture, Binaries & Tech Stack (FR / EN)" 
            : "Documentation globale du projet, prérequis, architecture, binaires & technologies (FR / EN)";
        LblSecurityTitle.Text = _localizer.Get("Security_Title");
        LblSecurityDesc.Text = _localizer.Get("Security_Description");
        LblStatusFooter.Text = _localizer.Get("Status_Footer");
        BtnWindowToggle.ToolTip = _localizer.CurrentLanguage == "en" ? "Toggle Fullscreen / Windowed (F11)" : "Basculer Plein écran / Fenêtré (F11)";
        RadAuto.ToolTip = _localizer.CurrentLanguage == "en" ? "Foundry Local automatically selects the best accelerator (NPU prioritized)" : "Foundry Local sélectionne automatiquement le meilleur accélérateur (priorité NPU)";

        UpdateWindowStateUi();
        UpdateAcceleratorUi();

        _operationsView?.ApplyLocalization(_localizer);
        _engineeringView?.ApplyLocalization(_localizer);
        _insuranceView?.ApplyLocalization(_localizer);
        _financialView?.ApplyLocalization(_localizer);
        _benchmarkView?.ApplyLocalization(_localizer);
        _oliveLabView?.ApplyLocalization(_localizer);
        _audioStudioView?.ApplyLocalization(_localizer);
        _avatarStudioView?.ApplyLocalization(_localizer);
        _magicEraserStudioView?.ApplyLocalization(_localizer);
        _pdfStudioView?.ApplyLocalization(_localizer);
        _securityAccessView?.ApplyLocalization(_localizer);
        _fleetTelemetryView?.ApplyLocalization(_localizer);

        // Keep language selector pills strictly synchronized
        if (RadLangEn != null && RadLangFr != null)
        {
            bool isEn = _localizer.CurrentLanguage == "en";
            if (isEn && RadLangEn.IsChecked != true) RadLangEn.IsChecked = true;
            else if (!isEn && RadLangFr.IsChecked != true) RadLangFr.IsChecked = true;
        }

        CheckServerStatusAsync();
    }

    private async void CheckServerStatusAsync()
    {
        try
        {
            if (!FoundryClient.IsFoundryInstalled())
            {
                Dispatcher.Invoke(() =>
                {
                    LblServerStatus.Text = _localizer?.CurrentLanguage == "en"
                        ? "⚠️ Microsoft Foundry Local not installed (run Installer_Prerequis.ps1)"
                        : "⚠️ Microsoft Foundry Local non installé (lancez Installer_Prerequis.ps1)";
                    UpdateAcceleratorUi();
                });
                return;
            }

            bool available = await _foundryClient.IsServerAvailableAsync();
            if (!available)
            {
                await FoundryClient.TryStartServerAsync();
                available = await _foundryClient.IsServerAvailableAsync();
            }

            if (available)
            {
                try
                {
                    var bestModel = await _foundryClient.GetBestModelAsync("phi-4-mini", "AUTO");
                    if (!string.IsNullOrWhiteSpace(bestModel.TargetDevice))
                    {
                        _autoResolvedDevice = bestModel.TargetDevice.ToUpperInvariant();
                    }
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    LblServerStatus.Text = string.Format(_localizer.Get("Status_Connected"), _foundryClient.BaseUrl, "phi-4-mini");
                    UpdateAcceleratorUi();
                });

                // Warm up default models in background and compile compute graph (once only)
                if (!_modelsWarmedUp)
                {
                    _modelsWarmedUp = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await FoundryClient.TryLoadModelAsync("phi-4-mini-instruct-openvino-npu");
                            await FoundryClient.TryLoadModelAsync("phi-4-mini-instruct-openvino-gpu");
                            var warmModel = await _foundryClient.GetBestModelAsync("phi-4-mini", "AUTO");
                            await _foundryClient.AskAsync("System ping", "Hi", warmModel.Parent, warmModel.TargetDevice, maxTokens: 2);
                        }
                        catch { }
                    });
                }
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    LblServerStatus.Text = _localizer?.CurrentLanguage == "en"
                        ? "⚠️ Foundry Local service stopped (launch via Lancer_DemoHub.ps1)"
                        : "⚠️ Service Foundry Local arrêté (lancez via Lancer_DemoHub.ps1)";
                    UpdateAcceleratorUi();
                });
            }
        }
        catch
        {
            Dispatcher.Invoke(() =>
            {
                LblServerStatus.Text = "⚠️ " + _localizer.Get("Operations_ConnectionError");
                UpdateAcceleratorUi();
            });
        }
    }

    public string GetSelectedDevice()
    {
        if (RadNpu.IsChecked == true) return "NPU";
        if (RadGpu.IsChecked == true) return "GPU";
        if (RadCpu.IsChecked == true) return "CPU";
        return "AUTO";
    }

    public void SetSelectedDevice(string device)
    {
        if (!_isInitialized) return;
        if (string.Equals(device, "GPU", StringComparison.OrdinalIgnoreCase))
        {
            if (RadGpu != null && RadGpu.IsChecked != true) RadGpu.IsChecked = true;
        }
        else if (string.Equals(device, "CPU", StringComparison.OrdinalIgnoreCase))
        {
            if (RadCpu != null && RadCpu.IsChecked != true) RadCpu.IsChecked = true;
        }
        else if (string.Equals(device, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            if (RadAuto != null && RadAuto.IsChecked != true) RadAuto.IsChecked = true;
        }
        else
        {
            if (RadNpu != null && RadNpu.IsChecked != true) RadNpu.IsChecked = true;
        }
    }

    public string GetEffectiveDevice()
    {
        string selected = GetSelectedDevice();
        return selected.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
            ? _autoResolvedDevice
            : selected;
    }

    private void UpdateAcceleratorUi()
    {
        if (!_isInitialized || RadAuto == null || TxtArchitecture == null || DotArchitecture == null) return;

        var npuBrush = (System.Windows.Media.Brush)FindResource("NpuGreenBrush");
        var gpuBrush = (System.Windows.Media.Brush)FindResource("GpuAmberBrush");
        var cpuBrush = (System.Windows.Media.Brush)FindResource("CpuIndigoBrush");

        RadAuto.Content = "🤖 Auto";

        if (RadAuto.IsChecked == true)
        {
            string autoName = _autoResolvedDevice.Equals("GPU", StringComparison.OrdinalIgnoreCase)
                ? SiliconDetector.GpuName
                : _autoResolvedDevice.Equals("CPU", StringComparison.OrdinalIgnoreCase)
                    ? SiliconDetector.CpuName
                    : SiliconDetector.NpuName;

            RadAuto.ToolTip = _localizer.CurrentLanguage == "en"
                ? $"Automatic selection: Foundry Local dynamically chose {_autoResolvedDevice} ({autoName})"
                : $"Sélection automatique : Foundry Local a dynamiquement sélectionné {_autoResolvedDevice} ({autoName})";

            TxtArchitecture.Text = $"Auto ➔ {_autoResolvedDevice} ({autoName})";
            DotArchitecture.Fill = _autoResolvedDevice.Equals("GPU", StringComparison.OrdinalIgnoreCase)
                ? gpuBrush
                : _autoResolvedDevice.Equals("CPU", StringComparison.OrdinalIgnoreCase)
                    ? cpuBrush
                    : npuBrush;
        }
        else if (RadNpu.IsChecked == true)
        {
            TxtArchitecture.Text = $"{SiliconDetector.NpuName}";
            DotArchitecture.Fill = npuBrush;
        }
        else if (RadGpu.IsChecked == true)
        {
            TxtArchitecture.Text = $"{SiliconDetector.GpuName}";
            DotArchitecture.Fill = gpuBrush;
        }
        else if (RadCpu.IsChecked == true)
        {
            TxtArchitecture.Text = $"{SiliconDetector.CpuName}";
            DotArchitecture.Fill = cpuBrush;
        }

        _benchmarkView?.RefreshDeviceUI();
        _operationsView?.RefreshDeviceUI();
        _engineeringView?.RefreshDeviceUI();
        _insuranceView?.RefreshDeviceUI();
        _financialView?.RefreshDeviceUI();
        _oliveLabView?.RefreshDeviceUI();
        _avatarStudioView?.RefreshDeviceUI();
        _magicEraserStudioView?.RefreshDeviceUI();
        _pdfStudioView?.RefreshDeviceUI();
        _securityAccessView?.RefreshDeviceUI();
    }

    private void Accelerator_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        UpdateAcceleratorUi();
    }

    private void NavOperations_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        ShowOperationsView();
    }

    private void NavEngineering_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _engineeringView ??= new EngineeringView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice);
        ViewContainer.Content = _engineeringView;
    }

    private void NavInsurance_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _insuranceView ??= new InsuranceView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice);
        ViewContainer.Content = _insuranceView;
    }

    private void NavFinancial_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _financialView ??= new FinancialView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice);
        ViewContainer.Content = _financialView;
    }

    private void NavBenchmark_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _benchmarkView ??= new BenchmarkView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice);
        ViewContainer.Content = _benchmarkView;
    }

    private void NavOliveLab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _oliveLabView ??= new OliveLabView(GetEffectiveDevice, SetSelectedDevice, _foundryClient);
        _oliveLabView.ApplyLocalization(_localizer);
        _oliveLabView.RefreshDeviceUI();
        ViewContainer.Content = _oliveLabView;
    }

    private void NavAudio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _audioStudioView ??= new AudioStudioView(_foundryClient, _localizer);
        ViewContainer.Content = _audioStudioView;
    }

    private void NavAvatar_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _avatarStudioView ??= new AvatarStudioView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice, SetSelectedDevice);
        _avatarStudioView.RefreshDeviceUI();
        ViewContainer.Content = _avatarStudioView;
    }

    private void NavEraser_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _magicEraserStudioView ??= new MagicEraserStudioView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice, SetSelectedDevice);
        _magicEraserStudioView.RefreshDeviceUI();
        ViewContainer.Content = _magicEraserStudioView;
    }

    private void NavPdfStudio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _pdfStudioView ??= new PdfStudioView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice, SetSelectedDevice);
        _pdfStudioView.RefreshDeviceUI();
        ViewContainer.Content = _pdfStudioView;
    }

    private void NavSecurityAccess_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _securityAccessView ??= new SecurityAccessView(GetEffectiveDevice, SetSelectedDevice);
        _securityAccessView.ApplyLocalization(_localizer);
        _securityAccessView.RefreshDeviceUI();
        ViewContainer.Content = _securityAccessView;
    }

    private void NavFleetTelemetry_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ViewContainer == null) return;
        _fleetTelemetryView ??= new FleetTelemetryView(_localizer);
        _fleetTelemetryView.RefreshData();
        ViewContainer.Content = _fleetTelemetryView;
    }

    private void BtnHeaderTelemetry_Click(object sender, RoutedEventArgs e)
    {
        NavFleetTelemetry.IsChecked = true;
    }

    private void BtnHeaderDoc_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(this, "global_overview", _localizer?.CurrentLanguage);
    }

    private void ShowOperationsView()
    {
        if (ViewContainer == null) return;
        _operationsView ??= new OperationsView(_foundryClient, _localizer, GetSelectedDevice, GetEffectiveDevice);
        ViewContainer.Content = _operationsView;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { _benchmarkView?.Dispose(); } catch { }
        try { _audioStudioView?.Dispose(); } catch { }
        try { _avatarStudioView?.Dispose(); } catch { }
        try { _magicEraserStudioView?.Dispose(); } catch { }
        try { _pdfStudioView?.Dispose(); } catch { }
        try { _securityAccessView?.Dispose(); } catch { }
        try { _fleetTelemetryView?.Dispose(); } catch { }
        try { _foundryClient.Dispose(); } catch { }
    }
}

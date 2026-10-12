using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CopilotPlusDemoHub.Core;
using Microsoft.Win32;

namespace CopilotPlusDemoHub.UI.Views;

public partial class FleetTelemetryView : UserControl, IDisposable
{
    private readonly EnterpriseTelemetryService _telemetry = EnterpriseTelemetryService.Instance;
    private LocalizationService _localizer;

    public FleetTelemetryView(LocalizationService localizer)
    {
        InitializeComponent();
        _localizer = localizer;

        // Initialize hardware labels
        TxtHardwareModel.Text = $"💻 {string.Format(_localizer.Get("Fleet_ModelLabel"), _telemetry.PcManufacturer, _telemetry.PcModel)}";
        TxtHardwareTag.Text = $"🏷️ Tag / S/N : {_telemetry.DeviceSerialNumber}";
        TxtHardwareHost.Text = $"🖥️ Host : {_telemetry.Hostname}";
        TxtHardwareCpu.Text = $"⚙️ {_telemetry.ProcessorName}";
        TxtHardwareNpu.Text = $"⚡ {_telemetry.NpuName}";

        _telemetry.OnRecordAdded += OnRecordAddedHandler;

        ApplyLocalization(_localizer);
        RefreshData();
    }

    private void OnRecordAddedHandler()
    {
        Dispatcher.InvokeAsync(RefreshData);
    }

    public async void RefreshData()
    {
        try
        {
            // 1. Fetch Summary KPIs
            var summary = await _telemetry.GetFleetAnalyticsSummaryAsync();

            TxtKpiNpuAdoptionVal.Text = summary.TotalInferences > 0
                ? $"{summary.NpuAdoptionRatePercent:F1} %"
                : "-- %";
            TxtKpiNpuAdoptionSub.Text = string.Format(
                _localizer.Get("Fleet_KpiNpuSub"),
                summary.NpuInferences,
                summary.TotalInferences
            );

            TxtKpiAnomaliesVal.Text = summary.AnomalyCount > 0
                ? $"{summary.AnomalyCount} " + _localizer.Get("Fleet_AlertWord")
                : "0 " + _localizer.Get("Fleet_AlertWord");
            TxtKpiAnomaliesVal.Foreground = summary.AnomalyCount > 0
                ? (System.Windows.Media.Brush)FindResource("DangerRedBrush")
                : (System.Windows.Media.Brush)FindResource("CopilotCyanBrush");
            TxtKpiAnomaliesSub.Text = summary.AnomalyCount > 0
                ? string.Format(_localizer.Get("Fleet_KpiAnomaliesFound"), summary.AnomalyCount)
                : _localizer.Get("Fleet_KpiAnomaliesZero");

            TxtKpiEsgVal.Text = $"{summary.TotalEnergySavedVsGpuWattHours:F2} Wh";
            TxtKpiEsgSub.Text = string.Format(_localizer.Get("Fleet_KpiEsgSub"), summary.Co2AvoidedGrams);

            TxtKpiTotalVal.Text = summary.TotalInferences.ToString();
            TxtKpiTotalSub.Text = _localizer.Get("Fleet_KpiTotalSub");

            // 2. Fetch Filtered Records
            string? demoFilter = (CmbFilterDemo?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            string? siliconFilter = (CmbFilterSilicon?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            bool anomaliesOnly = ChkAnomaliesOnly?.IsChecked == true;

            var records = await _telemetry.GetRecentRecordsAsync(
                limit: 200,
                demoFilter: demoFilter,
                siliconFilter: siliconFilter,
                anomaliesOnly: anomaliesOnly
            );

            GridAuditLog.ItemsSource = records;
        }
        catch { }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    private async void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = _localizer.Get("Fleet_ExportCsvTitle"),
            FileName = $"telemetrie_flotte_ia_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Filter = "Fichiers CSV (*.csv)|*.csv|Tous les fichiers (*.*)|*.*"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                await _telemetry.ExportToCsvAsync(sfd.FileName);
                MessageBox.Show(
                    string.Format(_localizer.Get("Fleet_ExportCsvSuccess"), sfd.FileName),
                    _localizer.Get("Fleet_ExportSuccessTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'export CSV : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnSimulateSiem_Click(object sender, RoutedEventArgs e)
    {
        BtnSimulateSiem.IsEnabled = false;
        BtnSimulateSiem.Content = "⏳ " + _localizer.Get("Fleet_SimulatingSync");

        bool ok = await _telemetry.SimulateSiemSyncAsync();

        BtnSimulateSiem.IsEnabled = true;
        BtnSimulateSiem.Content = "🌐 " + _localizer.Get("Fleet_BtnSimulateSiem");

        if (ok)
        {
            MessageBox.Show(
                _localizer.Get("Fleet_SiemSyncNotice"),
                _localizer.Get("Fleet_SiemSyncTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    private async void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            _localizer.Get("Fleet_ClearConfirmMsg"),
            _localizer.Get("Fleet_ClearConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (res == MessageBoxResult.Yes)
        {
            await _telemetry.ClearAuditLogAsync();
            RefreshData();
        }
    }

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;

        LblFleetTitle.Text = _localizer.Get("Fleet_Title");
        LblFleetSub.Text = _localizer.Get("Fleet_Subtitle");
        LblDbStatus.Text = _localizer.Get("Fleet_DbStatus");

        LblKpiNpuAdoptionTitle.Text = _localizer.Get("Fleet_KpiNpuTitle");
        LblKpiAnomaliesTitle.Text = _localizer.Get("Fleet_KpiAnomaliesTitle");
        LblKpiEsgTitle.Text = _localizer.Get("Fleet_KpiEsgTitle");
        LblKpiTotalTitle.Text = _localizer.Get("Fleet_KpiTotalTitle");

        LblFilterDemo.Text = _localizer.Get("Fleet_FilterDemo");
        LblFilterSilicon.Text = _localizer.Get("Fleet_FilterSilicon");
        ChkAnomaliesOnly.Content = _localizer.Get("Fleet_ChkAnomaliesOnly");

        BtnRefresh.Content = "🔄 " + _localizer.Get("Fleet_BtnRefresh");
        BtnExportCsv.Content = "📥 " + _localizer.Get("Fleet_BtnExportCsv");
        BtnExportCsvHeader.Content = "📥 " + _localizer.Get("Fleet_BtnExportCsv");
        BtnSimulateSiem.Content = "🌐 " + _localizer.Get("Fleet_BtnSimulateSiem");
        BtnClearLog.Content = "🗑️ " + _localizer.Get("Fleet_BtnClear");

        LblGovTitle.Text = _localizer.Get("Fleet_GovTitle");
        LblGovDesc.Text = _localizer.Get("Fleet_GovDesc");

        if (TxtDocHelp != null) TxtDocHelp.Text = _localizer.CurrentLanguage == "en" ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = _localizer.CurrentLanguage == "en" 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = _localizer.CurrentLanguage == "en"
                ? "Supervision: Multi-Model (Phi-4, YOLO, Whisper, LCM)"
                : "Supervision : Multi-Modèles (Phi-4, YOLO, Whisper, LCM)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = _localizer.CurrentLanguage == "en"
                    ? "Governance and auditability dashboard consolidating telemetry across all local AI models."
                    : "Tableau de bord de gouvernance et auditabilité consolidant la télémétrie de l'ensemble des modèles locaux.";
            }
        }
    }

    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_telemetry", _localizer?.CurrentLanguage);
    }

    public void Dispose()
    {
        _telemetry.OnRecordAdded -= OnRecordAddedHandler;
    }
}

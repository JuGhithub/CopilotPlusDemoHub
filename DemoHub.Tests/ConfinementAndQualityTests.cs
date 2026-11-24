using System;
using System.IO;
using System.Text.Json;
using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.Demos.Insurance;
using Xunit;

namespace DemoHub.Tests;

public class ConfinementAndQualityTests
{
    [Fact]
    public void AppConfig_OfflineModeOnly_BlocksRemoteUrls()
    {
        var config = new AppConfig { OfflineModeOnly = true };

        bool validRemote = config.ValidateEndpoint("https://api.openai.com/v1", out string? errorRemote);
        Assert.False(validRemote);
        Assert.NotNull(errorRemote);
        Assert.Contains("Confinement local strict actif", errorRemote);

        bool validHttp = config.ValidateEndpoint("http://192.168.1.50:8080/v1", out string? errorLan);
        Assert.False(validHttp);
        Assert.NotNull(errorLan);
        Assert.Contains("bloqués", errorLan);
    }

    [Fact]
    public void AppConfig_OfflineModeOnly_AllowsLoopbackAndLocalhost()
    {
        var config = new AppConfig { OfflineModeOnly = true };

        bool valid127 = config.ValidateEndpoint("http://127.0.0.1:52594", out string? err1);
        Assert.True(valid127);
        Assert.Null(err1);

        bool validLocalhost = config.ValidateEndpoint("http://localhost:52594/v1/chat/completions", out string? err2);
        Assert.True(validLocalhost);
        Assert.Null(err2);
    }

    [Fact]
    public void FoundryClient_WhenRemoteUrlGivenInOfflineMode_ThrowsException()
    {
        // En mode OfflineModeOnly (par défaut), toute URL externe doit lever une exception bloquante
        AppConfig.Current.OfflineModeOnly = true;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new FoundryClient("https://external-cloud-inference.org/v1");
        });

        Assert.Contains("Confinement local strict actif", ex.Message);
    }

    [Fact]
    public void DamagePhotoAnalyzer_WhenCustomUpload_SeverityAndAdasAreNullAndFormattedAsNotAssessed()
    {
        // Création d'une image factice temporaire pour le test
        string tempImg = Path.Combine(Path.GetTempPath(), $"test_photo_{Guid.NewGuid():N}.jpg");
        try
        {
            // Écrire un en-tête JPEG minimal 1x1 pixel
            byte[] dummyJpeg = [
                0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
                0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08,
                0x07, 0x07, 0x07, 0x09, 0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
                0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20, 0x24, 0x2E, 0x27, 0x20,
                0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29, 0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27,
                0x39, 0x3D, 0x38, 0x32, 0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
                0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00, 0x01, 0x05, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04,
                0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F,
                0x00, 0x7F, 0x00, 0xFF, 0xD9
            ];
            File.WriteAllBytes(tempImg, dummyJpeg);

            var photoInfo = DamagePhotoAnalyzer.AnalyzeCustomPhoto(tempImg);

            Assert.True(photoInfo.IsCustomUpload);
            Assert.Null(photoInfo.SeverityScore);
            Assert.Null(photoInfo.AdasAffected);

            string promptFr = DamagePhotoAnalyzer.FormatPromptSection(photoInfo, "fr");
            Assert.Contains("Non évalué", promptFr);
            Assert.DoesNotContain("0/5", promptFr);

            string promptEn = DamagePhotoAnalyzer.FormatPromptSection(photoInfo, "en");
            Assert.Contains("Not assessed", promptEn);
            Assert.DoesNotContain("0/5", promptEn);
        }
        finally
        {
            if (File.Exists(tempImg))
            {
                try { File.Delete(tempImg); } catch { }
            }
        }
    }

    [Fact]
    public void RuntimeEvidence_DefaultEvidenceLevel_IsUnknown()
    {
        var evidence = new RuntimeEvidence();
        Assert.Equal("Unknown", evidence.EvidenceLevel);
        Assert.False(evidence.IsNpuActive);
    }

    [Fact]
    public void Localization_JsonResources_DoNotContainMisleadingTpmOrZeroTrustPromises()
    {
        string baseDir = AppContext.BaseDirectory;
        string? root = baseDir;
        string? frPath = null;
        string? enPath = null;

        for (int i = 0; i < 7 && root != null; i++)
        {
            string testFr = Path.Combine(root, "DemoHub", "Resources", "fr.json");
            string testEn = Path.Combine(root, "DemoHub", "Resources", "en.json");
            if (File.Exists(testFr) && File.Exists(testEn))
            {
                frPath = testFr;
                enPath = testEn;
                break;
            }
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(frPath);
        Assert.NotNull(enPath);

        string frJson = File.ReadAllText(frPath!);
        using var frDoc = JsonDocument.Parse(frJson);
        string audioBadgeFr = frDoc.RootElement.GetProperty("Audio_TpmBadge").GetString() ?? "";
        Assert.DoesNotContain("TPM 2.0 Scellé", audioBadgeFr);
        Assert.Contains("DPAPI", audioBadgeFr);

        string enJson = File.ReadAllText(enPath!);
        using var enDoc = JsonDocument.Parse(enJson);
        string audioBadgeEn = enDoc.RootElement.GetProperty("Audio_TpmBadge").GetString() ?? "";
        Assert.DoesNotContain("TPM 2.0 Sealed", audioBadgeEn);
        Assert.Contains("DPAPI", audioBadgeEn);
    }
}

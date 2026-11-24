using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace CopilotPlusDemoHub.Demos.Insurance;

public record DamagePhotoInfo
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string InspectionReportFr { get; init; }
    public required string InspectionReportEn { get; init; }
    public int? SeverityScore { get; init; } = 3;
    public bool? AdasAffected { get; init; } = true;
    public bool IsCustomUpload { get; init; } = false;
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileSizeBytes { get; init; }
}

public static class DamagePhotoAnalyzer
{
    public static List<DamagePhotoInfo> GetDefaultSamplePhotos()
    {
        var list = new List<DamagePhotoInfo>();

        string p1 = ResolveImagePath("choc_arriere_suv.jpg");
        if (File.Exists(p1))
        {
            var (w, h, sz) = ReadImageMetadata(p1);
            list.Add(new DamagePhotoInfo
            {
                FilePath = p1,
                FileName = Path.GetFileName(p1),
                Title = "Photo 1 : Choc Arrière SUV (Vue d'ensemble)",
                Description = "Impact arrière direct droit, enfoncement hayon et pare-chocs",
                Width = w,
                Height = h,
                FileSizeBytes = sz,
                SeverityScore = 3,
                AdasAffected = true,
                IsCustomUpload = false,
                InspectionReportFr =
                    "- Éléments visibles endommagés : Pare-chocs arrière polypropylène fendu et déformé côté droit ; hayon inférieur enfoncé (déformation ~12 cm) ; bloc optique arrière droit fêlé ; capteurs ultrasons de recul arrachés de leur logement.\n" +
                    "- Compatibilité cinématique : Impact direct perpendiculaire par l'arrière à ~35-40 km/h, sans trace de freinage préalable. Parfaitement cohérent avec les déclarations du constat amiable (véhicule assuré à l'arrêt percuté par un fourgon de livraison).\n" +
                    "- Diagnostic ADAS : Désalignement probable du radar anti-collision arrière et faisceau de stationnement endommagé. Étalonnage et géométrie obligatoires en carrosserie agréée.",
                InspectionReportEn =
                    "- Visible damaged parts: Polypropylene rear bumper cracked and deformed on right side; lower tailgate indented (~12 cm depression); right rear light cluster lens broken; ultrasonic parking sensors dislodged from mounting clips.\n" +
                    "- Kinematic consistency: Direct perpendicular rear collision at ~35-40 km/h with zero prior braking marks. Fully consistent with amicable claim statements (insured vehicle stopped at red light struck by delivery van).\n" +
                    "- ADAS assessment: High probability of rear radar anti-collision misalignment and harness pinching. Mandatory recalibration and wheel alignment check in certified body shop."
            });
        }

        string p2 = ResolveImagePath("capteur_radar_adas.jpg");
        if (File.Exists(p2))
        {
            var (w, h, sz) = ReadImageMetadata(p2);
            list.Add(new DamagePhotoInfo
            {
                FilePath = p2,
                FileName = Path.GetFileName(p2),
                Title = "Photo 2 : Capteur Radar & ADAS (Zoom angle)",
                Description = "Rupture bouclier thermoplastique, capteur d'angle délogé",
                Width = w,
                Height = h,
                FileSizeBytes = sz,
                SeverityScore = 3,
                AdasAffected = true,
                IsCustomUpload = false,
                InspectionReportFr =
                    "- Éléments visibles endommagés : Déchirure du bouclier plastique d'angle, support de platine radar anti-angle mort tordu, capteur ultrasonique suspendu par son faisceau électrique dénudé.\n" +
                    "- Compatibilité cinématique : Point d'impact focalisé sur la traverse basse droite correspondant au pare-chocs surélevé d'un véhicule utilitaire léger.\n" +
                    "- Diagnostic ADAS : Remplacement impératif du support radar, contrôle de l'intégrité du faisceau multiplexé et réétalonnage statique (mire) + dynamique (essai sur route).",
                InspectionReportEn =
                    "- Visible damaged parts: Corner plastic bumper torn open, blind-spot radar bracket twisted, ultrasonic transducer dangling from exposed wiring harness.\n" +
                    "- Kinematic consistency: Focal impact point on lower right reinforcement beam matching high-profile bumper height of a light commercial vehicle.\n" +
                    "- ADAS assessment: Imperative bracket replacement, multiplex wiring continuity test, and mandatory static target calibration + dynamic road calibration."
            });
        }

        return list;
    }

    public static DamagePhotoInfo AnalyzeCustomPhoto(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Photo introuvable", filePath);
        }

        var (w, h, sz) = ReadImageMetadata(filePath);
        string name = Path.GetFileName(filePath);

        return new DamagePhotoInfo
        {
            FilePath = filePath,
            FileName = name,
            Title = $"Photo Importée : {name}",
            Description = $"Document photographique externe ({w}x{h} px, {sz / 1024} Ko)",
            Width = w,
            Height = h,
            FileSizeBytes = sz,
            SeverityScore = null,
            AdasAffected = null,
            IsCustomUpload = true,
            InspectionReportFr =
                $"- Métadonnées techniques : Fichier image importé ({w}x{h} px, {sz / 1024} Ko).\n" +
                "- Évaluation visuelle : Document joint au dossier. Aucune conclusion technique inventée à partir des seules métadonnées du fichier.\n" +
                "- Recommandation : Examen visuel contradictoire et chiffrage des dommages requis par un expert d'assurance qualifié.",
            InspectionReportEn =
                $"- Technical metadata: Imported image file ({w}x{h} px, {sz / 1024} KB).\n" +
                "- Visual evaluation: Document attached to claim. No fabricated collision assessment derived from image dimensions alone.\n" +
                "- Recommendation: Physical inspection and damage appraisal by a certified insurance adjuster required."
        };
    }

    public static string FormatPromptSection(DamagePhotoInfo? photo, string language)
    {
        if (photo == null) return string.Empty;

        bool isEn = language.Equals("en", StringComparison.OrdinalIgnoreCase);

        string severityText = photo.SeverityScore.HasValue
            ? $"{photo.SeverityScore.Value}/5"
            : (isEn ? "Not assessed (External photograph)" : "Non évalué (Photographie externe)");

        string adasText = photo.AdasAffected.HasValue
            ? (photo.AdasAffected.Value ? (isEn ? "Yes (Sensors & Radar impacted)" : "Oui (Capteurs & Radar concernés)") : (isEn ? "None" : "Non"))
            : (isEn ? "Not assessed (Requires physical technical inspection)" : "Non évalué (Examen contradictoire requis)");

        if (isEn)
        {
            return $@"
PHOTOGRAPHIC DAMAGE EVIDENCE (INSPECTED LOCALLY)
==================================================
Photo: {photo.Title} ({photo.FileName})
Resolution: {photo.Width}x{photo.Height} px | Size: {photo.FileSizeBytes / 1024} KB | Severity Indicator: {severityText}
ADAS Impact: {adasText}

Visual Inspection Findings:
{photo.InspectionReportEn}
==================================================";
        }

        return $@"
PREUVES PHOTOGRAPHIQUES DES DOMMAGES (EXPERTISE LOCALE)
==================================================
Photo : {photo.Title} ({photo.FileName})
Résolution : {photo.Width}x{photo.Height} px | Taille : {photo.FileSizeBytes / 1024} Ko | Indice de gravité : {severityText}
Impact ADAS : {adasText}

Rapport de constat visuel :
{photo.InspectionReportFr}
==================================================";
    }

    private static (int width, int height, long size) ReadImageMetadata(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            long sz = fi.Length;
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                return (frame.PixelWidth, frame.PixelHeight, sz);
            }
            return (800, 600, sz);
        }
        catch
        {
            return (800, 600, 0);
        }
    }

    public static string ResolveImagePath(string fileName)
    {
        string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Images", "Insurance", fileName);
        if (File.Exists(direct)) return direct;

        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string test = Path.Combine(dir, "Data", "Images", "Insurance", fileName);
            if (File.Exists(test)) return test;
            dir = Path.GetDirectoryName(dir);
        }

        return direct;
    }
}

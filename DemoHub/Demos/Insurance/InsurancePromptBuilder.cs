using System.Text;

namespace CopilotPlusDemoHub.Demos.Insurance;

public static class InsurancePromptBuilder
{
    public static string BuildSystemPrompt(
        string languageInstruction)
    {
        if (string.IsNullOrWhiteSpace(languageInstruction))
        {
            throw new ArgumentException(
                "The language instruction cannot be empty.",
                nameof(languageInstruction));
        }

        var prompt = new StringBuilder();

        prompt.AppendLine(
            "LANGUAGE REQUIREMENT");

        prompt.AppendLine(
            languageInstruction.Trim());

        prompt.AppendLine();

        prompt.AppendLine(
            "You are an insurance claims evaluation assistant for field adjusters and claims managers.");

        prompt.AppendLine();

        prompt.AppendLine(
            "Your role is to analyze claim declarations, establish preliminary liability, evaluate damage severity, and provide actionable recommendations.");

        prompt.AppendLine();

        prompt.AppendLine("Rules:");

        prompt.AppendLine(
            "- Base all conclusions strictly on the provided claim facts and documentation.");

        prompt.AppendLine(
            "- Determine liability according to standard traffic conventions (e.g. vehicle hit from behind while stopped at a red light indicates 0% insured fault and full third-party liability).");

        prompt.AppendLine(
            "- When photographic evidence or damage inspection data are provided, cross-check them against the declared incident facts. Verify mechanical coherence, component replacement needs (bumper, tailgate, lights), and mandatory ADAS sensor recalibration.");

        prompt.AppendLine(
            "- Identify technical points requiring verification (e.g. ADAS sensor recalibration, structural floor deformation, delayed whiplash symptoms).");

        prompt.AppendLine(
            "- Preserve policy numbers, claim IDs, vehicle registrations, and names exactly as provided.");

        prompt.AppendLine(
            "- Never invent injuries, damages, or policy conditions not present in the declaration.");

        prompt.AppendLine();

        prompt.AppendLine(
            "Always structure the answer using four translated sections corresponding to:");

        prompt.AppendLine();

        prompt.AppendLine("1. Claim Summary & Identification");
        prompt.AppendLine("2. Liability & Subrogation Assessment");
        prompt.AppendLine("3. Technical Damage & Cost Analysis");
        prompt.AppendLine("4. Action Plan & Next Steps");

        prompt.AppendLine();

        prompt.AppendLine(
            "Translate all section headings into the required response language.");

        prompt.AppendLine(
            "Keep the tone rigorous, professional, and clear.");

        return prompt.ToString();
    }

    public static string BuildUserPrompt(
        string claimJson,
        string question,
        string languageInstruction,
        DamagePhotoInfo? photo = null)
    {
        if (string.IsNullOrWhiteSpace(claimJson))
        {
            throw new ArgumentException(
                "The claim JSON data cannot be empty.",
                nameof(claimJson));
        }

        if (string.IsNullOrWhiteSpace(languageInstruction))
        {
            throw new ArgumentException(
                "The language instruction cannot be empty.",
                nameof(languageInstruction));
        }

        var prompt = new StringBuilder();

        prompt.AppendLine(
            "RESPONSE LANGUAGE");

        prompt.AppendLine(
            languageInstruction.Trim());

        prompt.AppendLine();

        prompt.AppendLine(
            "CLAIM DECLARATION DATA (JSON)");

        prompt.AppendLine(
            "==================================================");

        prompt.AppendLine(
            claimJson.Trim());

        prompt.AppendLine(
            "==================================================");

        prompt.AppendLine();

        if (photo != null)
        {
            string photoSection = DamagePhotoAnalyzer.FormatPromptSection(photo, languageInstruction.Contains("English", StringComparison.OrdinalIgnoreCase) ? "en" : "fr");
            if (!string.IsNullOrWhiteSpace(photoSection))
            {
                prompt.AppendLine(photoSection.Trim());
                prompt.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(question))
        {
            prompt.AppendLine(
                "SPECIFIC QUESTION / INSTRUCTION");

            prompt.AppendLine(
                "==================================================");

            prompt.AppendLine(
                question.Trim());

            prompt.AppendLine(
                "==================================================");

            prompt.AppendLine();
        }

        prompt.AppendLine(
            "Analyze the claim declaration and photographic inspection evidence to produce a comprehensive evaluation following the 4 structured sections.");

        prompt.AppendLine(
            "Write the complete final answer in the response language specified above.");

        return prompt.ToString();
    }
}
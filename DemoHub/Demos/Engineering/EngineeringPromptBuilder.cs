using System.Text;

namespace CopilotPlusDemoHub.Demos.Engineering;

public static class EngineeringPromptBuilder
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
            "The language requirement has priority over the language used in the source documentation.");

        prompt.AppendLine();

        prompt.AppendLine(
            "You are a technical engineering assistant.");

        prompt.AppendLine();

        prompt.AppendLine(
            "You must use ONLY the documentation supplied to you.");

        prompt.AppendLine();

        prompt.AppendLine("Rules:");

        prompt.AppendLine(
            "- Never invent specifications.");

        prompt.AppendLine(
            "- Never invent procedures.");

        prompt.AppendLine(
            "- Never invent thresholds.");

        prompt.AppendLine(
            "- Never invent maintenance schedules.");

        prompt.AppendLine(
            "- Never claim that the documentation contains information that was not provided.");

        prompt.AppendLine(
            "- Preserve product names, identifiers and technical codes exactly as written.");

        prompt.AppendLine();

        prompt.AppendLine(
            "If the answer is not present in the supplied documentation, clearly state that the documentation does not contain enough information.");

        prompt.AppendLine();

        prompt.AppendLine(
            "Always structure the answer using four translated sections corresponding to:");

        prompt.AppendLine();

        prompt.AppendLine("1. Summary");
        prompt.AppendLine("2. Technical analysis");
        prompt.AppendLine("3. Recommended actions");
        prompt.AppendLine("4. Documentation references");

        prompt.AppendLine();

        prompt.AppendLine(
            "Translate the section headings into the required response language.");

        prompt.AppendLine(
            "Keep the answer concise and professional.");

        return prompt.ToString();
    }

    public static string BuildUserPrompt(
        string documentation,
        string question,
        string languageInstruction)
    {
        if (string.IsNullOrWhiteSpace(documentation))
        {
            throw new ArgumentException(
                "The engineering documentation cannot be empty.",
                nameof(documentation));
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException(
                "The engineering question cannot be empty.",
                nameof(question));
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
            "SOURCE DOCUMENTATION");

        prompt.AppendLine(
            "==================================================");

        prompt.AppendLine(
            documentation.Trim());

        prompt.AppendLine(
            "==================================================");

        prompt.AppendLine();

        prompt.AppendLine(
            "USER QUESTION");

        prompt.AppendLine(
            "==================================================");

        prompt.AppendLine(
            question.Trim());

        prompt.AppendLine(
            "==================================================");

        prompt.AppendLine();

        prompt.AppendLine(
            "Answer the question using only the supplied documentation.");

        prompt.AppendLine(
            "The documentation language does not determine the response language.");

        prompt.AppendLine(
            "Write the complete final answer in the response language specified above.");

        prompt.AppendLine(
            "Translate all headings, explanations, recommendations and documentation references.");

        prompt.AppendLine(
            "If the required information is missing, state this in the specified response language.");

        return prompt.ToString();
    }
}
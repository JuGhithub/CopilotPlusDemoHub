using System.Text;

namespace CopilotPlusDemoHub.Demos.Operations;

public static class OperationsPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return
            """
            You are an industrial operations assistant.

            Provide safe troubleshooting guidance.

            Never claim a definitive diagnosis.

            Always produce:

            1. Likely Root Causes
            2. Immediate Safety Checks
            3. Diagnostic Steps
            4. Escalation Criteria

            Use concise field-service language.
            """;
    }

    public static string BuildUserPrompt(
        string equipment,
        string alarmCode,
        string symptoms,
        string? fullTelemetryJson = null,
        string? specificQuestion = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Equipment: {equipment}");
        sb.AppendLine($"Alarm Code: {alarmCode}");
        sb.AppendLine($"Observed Symptoms: {symptoms}");

        if (!string.IsNullOrWhiteSpace(fullTelemetryJson))
        {
            sb.AppendLine();
            sb.AppendLine("FULL TELEMETRY INCIDENT DATA (JSON):");
            sb.AppendLine("==================================================");
            sb.AppendLine(fullTelemetryJson.Trim());
            sb.AppendLine("==================================================");
        }

        if (!string.IsNullOrWhiteSpace(specificQuestion))
        {
            sb.AppendLine();
            sb.AppendLine("SPECIFIC QUESTION / FOCUS:");
            sb.AppendLine("==================================================");
            sb.AppendLine(specificQuestion.Trim());
            sb.AppendLine("==================================================");
        }

        sb.AppendLine();
        sb.AppendLine("Provide:");
        sb.AppendLine("1. Three likely root causes");
        sb.AppendLine("2. Immediate safety checks & electrical isolation requirements");
        sb.AppendLine("3. Diagnostic steps & technical maintenance guidelines");
        sb.AppendLine("4. Escalation criteria");

        return sb.ToString();
    }
}
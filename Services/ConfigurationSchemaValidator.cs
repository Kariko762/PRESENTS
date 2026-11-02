using System.IO;
using System.Text.Json;
using Json.Schema;

namespace FisPresentationPlatform.Services;

public static class ConfigurationSchemaValidator
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(ConfigurationSchemaValidator).Assembly.GetManifestResourceStream("FIS.presentation.schema.json")
            ?? throw new InvalidOperationException("The embedded configuration schema is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    });

    public static List<string> Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = new List<string>();
        CollectErrors(result, errors);
        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void CollectErrors(EvaluationResults result, List<string> errors)
    {
        if (result.Errors is not null)
            foreach (var error in result.Errors)
                errors.Add($"{(result.InstanceLocation.ToString().Length == 0 ? "$" : result.InstanceLocation.ToString())}: {error.Value}");
        if (result.Details is not null)
            foreach (var child in result.Details) CollectErrors(child, errors);
    }
}
using System.Collections.Immutable;
using System.Text.Json;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelVariableDefinition(
    string Name,
    string? Label,
    string Type,
    bool Required,
    int MaxLength);

public sealed record LabelVariableSchema(int Version, ImmutableArray<LabelVariableDefinition> Variables)
{
    public const int SupportedVersion = 1;
    public const int DefaultMaxLength = 200;

    public static LabelVariableSchema Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            using var document = JsonDocument.Parse(json, StrictJson.DocumentOptions);
            var root = StrictJson.Object(
                document.RootElement,
                "$",
                ["version", "variables"],
                ["version", "variables"]);
            var version = StrictJson.RequiredInt32(root, "version", "$", SupportedVersion, SupportedVersion);
            var variablesElement = root["variables"];
            if (variablesElement.ValueKind != JsonValueKind.Array)
            {
                throw StrictJson.Contract("$.variables must be an array.");
            }

            var variables = ImmutableArray.CreateBuilder<LabelVariableDefinition>(variablesElement.GetArrayLength());
            var names = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var variableElement in variablesElement.EnumerateArray())
            {
                var variable = ParseVariable(variableElement, index++);
                if (!names.Add(variable.Name))
                {
                    throw StrictJson.Contract($"$.variables contains duplicate variable '{variable.Name}'.");
                }

                if (LabelTemplateBinder.IsReservedVariable(variable.Name))
                {
                    throw StrictJson.Contract($"$.variables cannot declare reserved variable '{variable.Name}'.");
                }

                variables.Add(variable);
            }

            return new LabelVariableSchema(version, variables.MoveToImmutable());
        }
        catch (JsonException exception)
        {
            throw StrictJson.Contract("Variable schema JSON is malformed.", exception);
        }
    }

    private static LabelVariableDefinition ParseVariable(JsonElement element, int index)
    {
        var path = $"$.variables[{index}]";
        var properties = StrictJson.Object(
            element,
            path,
            ["name", "label", "type", "required", "maxLength"],
            ["name", "type"]);
        var name = StrictJson.RequiredString(properties, "name", path, allowEmpty: false);
        StrictJson.SafeVariableName(name, $"{path}.name");
        var type = StrictJson.RequiredString(properties, "type", path, allowEmpty: false);
        if (!string.Equals(type, "string", StringComparison.Ordinal))
        {
            throw StrictJson.Contract($"{path}.type must be 'string'.");
        }

        string? label = null;
        if (properties.TryGetValue("label", out var labelElement))
        {
            if (labelElement.ValueKind != JsonValueKind.String)
            {
                throw StrictJson.Contract($"{path}.label must be a string.");
            }

            label = labelElement.GetString();
        }

        var required = true;
        if (properties.TryGetValue("required", out var requiredElement))
        {
            if (requiredElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw StrictJson.Contract($"{path}.required must be a boolean.");
            }

            required = requiredElement.GetBoolean();
        }

        var maxLength = DefaultMaxLength;
        if (properties.TryGetValue("maxLength", out var maxLengthElement))
        {
            if (maxLengthElement.ValueKind != JsonValueKind.Number || !maxLengthElement.TryGetInt32(out maxLength) || maxLength <= 0)
            {
                throw StrictJson.Contract($"{path}.maxLength must be a positive integer.");
            }
        }

        return new LabelVariableDefinition(name, label, type, required, maxLength);
    }
}

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelReservedVariables(
    string LabelValue,
    Gs1BarcodeValue? Gs1Value,
    int SequenceNo,
    string SourceDocumentId);

public sealed record LabelCompilationItem(
    string VariableValuesJson,
    LabelReservedVariables ReservedVariables);

public sealed record BoundLabelField(LabelTemplateField Field, string Value);

public sealed record BoundLabelDocument(
    LabelTemplateDocument Template,
    LabelReservedVariables ReservedVariables,
    ImmutableArray<BoundLabelField> Fields);

public static class LabelTemplateBinder
{
    private static readonly HashSet<string> ReservedVariableNames =
    [
        "label.value",
        "label.gtin",
        "label.lotNo",
        "label.serialNumber",
        "label.epcUri",
        "item.sequenceNo",
        "batch.sourceDocumentId",
    ];

    public static bool IsReservedVariable(string name) => ReservedVariableNames.Contains(name);

    public static ImmutableArray<BoundLabelDocument> BindBatch(
        LabelTemplateDocument template,
        LabelVariableSchema schema,
        IReadOnlyCollection<LabelCompilationItem> items)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw StrictJson.Contract("At least one label compilation item is required.");
        }

        var definitions = schema.Variables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        foreach (var field in template.Fields)
        {
            if (!ReservedVariableNames.Contains(field.Variable) && !definitions.ContainsKey(field.Variable))
            {
                throw StrictJson.Contract($"Template variable '{field.Variable}' is not declared by the variable schema.");
            }
        }

        var boundDocuments = ImmutableArray.CreateBuilder<BoundLabelDocument>(items.Count);
        var itemIndex = 0;
        foreach (var item in items)
        {
            boundDocuments.Add(BindItem(template, definitions, item, itemIndex++));
        }

        return boundDocuments.MoveToImmutable();
    }

    private static BoundLabelDocument BindItem(
        LabelTemplateDocument template,
        IReadOnlyDictionary<string, LabelVariableDefinition> definitions,
        LabelCompilationItem item,
        int itemIndex)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.ReservedVariables);
        var path = $"items[{itemIndex}]";
        var values = ParseBusinessValues(item.VariableValuesJson, definitions, path);
        var reserved = BuildReservedValues(item.ReservedVariables, path);
        var fields = ImmutableArray.CreateBuilder<BoundLabelField>(template.Fields.Length);
        foreach (var field in template.Fields)
        {
            var value = reserved.TryGetValue(field.Variable, out var reservedValue)
                ? reservedValue
                : values.GetValueOrDefault(field.Variable, string.Empty);
            EnsureSafeValue(
                value,
                $"{path}.{field.Variable}",
                allowGs1Separator: field is LabelBarcodeField
                    && field.Variable == "label.value"
                    && item.ReservedVariables.Gs1Value is not null);
            fields.Add(new BoundLabelField(field, value));
        }

        return new BoundLabelDocument(template, item.ReservedVariables, fields.MoveToImmutable());
    }

    private static Dictionary<string, string> ParseBusinessValues(
        string json,
        IReadOnlyDictionary<string, LabelVariableDefinition> definitions,
        string itemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json, StrictJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw StrictJson.Contract($"{itemPath}.variables must be an object.");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (ReservedVariableNames.Contains(property.Name))
                {
                    throw StrictJson.Contract($"{itemPath}.variables cannot override reserved variable '{property.Name}'.");
                }

                if (!definitions.TryGetValue(property.Name, out var definition))
                {
                    throw StrictJson.Contract($"{itemPath}.variables contains undeclared variable '{property.Name}'.");
                }

                if (!values.TryAdd(property.Name, string.Empty))
                {
                    throw StrictJson.Contract($"{itemPath}.variables contains duplicate variable '{property.Name}'.");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw StrictJson.Contract($"{itemPath}.variables.{property.Name} must be a string.");
                }

                var value = property.Value.GetString()!;
                if (value.Length > definition.MaxLength)
                {
                    throw StrictJson.Contract($"{itemPath}.variables.{property.Name} exceeds maxLength {definition.MaxLength}.");
                }

                EnsureSafeValue(value, $"{itemPath}.variables.{property.Name}", allowGs1Separator: false);
                values[property.Name] = value;
            }

            foreach (var definition in definitions.Values)
            {
                if (definition.Required && !values.ContainsKey(definition.Name))
                {
                    throw StrictJson.Contract($"{itemPath}.variables is missing required variable '{definition.Name}'.");
                }
            }

            return values;
        }
        catch (JsonException exception)
        {
            throw StrictJson.Contract($"{itemPath}.variables JSON is malformed.", exception);
        }
    }

    private static Dictionary<string, string> BuildReservedValues(LabelReservedVariables reserved, string itemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reserved.LabelValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(reserved.SourceDocumentId);
        if (reserved.SequenceNo <= 0)
        {
            throw StrictJson.Contract($"{itemPath}.item.sequenceNo must be positive.");
        }

        if (reserved.Gs1Value is not null
            && !string.Equals(reserved.LabelValue, reserved.Gs1Value.ToAiString(), StringComparison.Ordinal))
        {
            throw StrictJson.Contract($"{itemPath}.label.value does not match its GS1 value.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["label.value"] = reserved.LabelValue,
            ["label.gtin"] = reserved.Gs1Value?.Gtin ?? string.Empty,
            ["label.lotNo"] = reserved.Gs1Value?.LotNo ?? string.Empty,
            ["label.serialNumber"] = reserved.Gs1Value?.SerialNumber ?? string.Empty,
            ["label.epcUri"] = reserved.Gs1Value?.EpcUri ?? string.Empty,
            ["item.sequenceNo"] = reserved.SequenceNo.ToString(CultureInfo.InvariantCulture),
            ["batch.sourceDocumentId"] = reserved.SourceDocumentId,
        };

        foreach (var (name, value) in values)
        {
            EnsureSafeValue(
                value,
                $"{itemPath}.{name}",
                allowGs1Separator: name == "label.value" && reserved.Gs1Value is not null);
        }

        return values;
    }

    private static void EnsureSafeValue(string value, string path, bool allowGs1Separator)
    {
        if (value.Any(character => character is '^' or '~'
                || (char.IsControl(character) && (!allowGs1Separator || character != '\u001D'))))
        {
            throw StrictJson.Contract($"{path} contains a control character or ZPL command introducer.");
        }
    }
}

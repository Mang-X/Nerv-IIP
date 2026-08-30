using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public enum PlainLabelBarcodeType
{
    Code128,
    Qr,
    DataMatrix,
}

public enum Gs1LabelBarcodeType
{
    Gs1128,
    DataMatrix,
}

public abstract record LabelBarcodePayload
{
    private protected LabelBarcodePayload()
    {
    }
}

public sealed record PlainLabelBarcodePayload(PlainLabelBarcodeType Type, string Value)
    : LabelBarcodePayload;

public sealed record Gs1LabelBarcodePayload(Gs1LabelBarcodeType Type, Gs1BarcodeValue Value)
    : LabelBarcodePayload;

public sealed record LabelCompilationItem(
    string VariableValuesJson,
    LabelBarcodePayload BarcodePayload,
    int SequenceNo,
    string SourceDocumentId);

public sealed record BoundLabelField(LabelTemplateField Field, string Value);

public sealed record BoundLabelDocument(
    LabelTemplateDocument Template,
    LabelBarcodePayload BarcodePayload,
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
        var path = $"items[{itemIndex}]";
        var values = ParseBusinessValues(item.VariableValuesJson, definitions, path);
        var reserved = BuildReservedValues(item, path);
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
                    && item.BarcodePayload is Gs1LabelBarcodePayload);
            fields.Add(new BoundLabelField(field, value));
        }

        return new BoundLabelDocument(template, item.BarcodePayload, fields.MoveToImmutable());
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

    private static Dictionary<string, string> BuildReservedValues(LabelCompilationItem item, string itemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.SourceDocumentId);
        if (item.SequenceNo <= 0)
        {
            throw StrictJson.Contract($"{itemPath}.item.sequenceNo must be positive.");
        }

        string labelValue;
        Gs1BarcodeValue? gs1Value;
        if (item.BarcodePayload is PlainLabelBarcodePayload plainPayload)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(plainPayload.Value);
            labelValue = plainPayload.Value;
            gs1Value = null;
        }
        else
        {
            var gs1Payload = (Gs1LabelBarcodePayload)item.BarcodePayload;
            labelValue = gs1Payload.Value.ToAiString();
            gs1Value = gs1Payload.Value;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["label.value"] = labelValue,
            ["label.gtin"] = gs1Value?.Gtin ?? string.Empty,
            ["label.lotNo"] = gs1Value?.LotNo ?? string.Empty,
            ["label.serialNumber"] = gs1Value?.SerialNumber ?? string.Empty,
            ["label.epcUri"] = gs1Value?.EpcUri ?? string.Empty,
            ["item.sequenceNo"] = item.SequenceNo.ToString(CultureInfo.InvariantCulture),
            ["batch.sourceDocumentId"] = item.SourceDocumentId,
        };

        foreach (var (name, value) in values)
        {
            EnsureSafeValue(
                value,
                $"{itemPath}.{name}",
                allowGs1Separator: name == "label.value" && item.BarcodePayload is Gs1LabelBarcodePayload);
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

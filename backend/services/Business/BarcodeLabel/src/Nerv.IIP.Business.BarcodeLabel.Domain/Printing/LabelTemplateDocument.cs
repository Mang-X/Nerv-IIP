using System.Collections.Immutable;
using System.Text.Json;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed record LabelTemplateMedia(int Dpi, int WidthDots, int HeightDots);

public abstract record LabelTemplateField(string Kind, int X, int Y, string Variable);

public sealed record LabelTextField(
    int X,
    int Y,
    int FontHeight,
    int FontWidth,
    string Variable)
    : LabelTemplateField("text", X, Y, Variable);

public sealed record LabelBarcodeField(
    int X,
    int Y,
    int ModuleWidth,
    int Height,
    string Variable)
    : LabelTemplateField("barcode", X, Y, Variable);

public sealed record LabelTemplateDocument(
    string Format,
    int Version,
    LabelTemplateMedia Media,
    ImmutableArray<LabelTemplateField> Fields)
{
    public const string SupportedFormat = "nerv-iip.label-template";
    public const int SupportedVersion = 1;
    public const int MaximumFieldCount = 64;

    private static readonly HashSet<int> SupportedDpi = [203, 300, 600];

    public static LabelTemplateDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            using var document = JsonDocument.Parse(json, StrictJson.DocumentOptions);
            var root = StrictJson.Object(
                document.RootElement,
                "$",
                ["format", "version", "media", "fields"],
                ["format", "version", "media", "fields"]);

            var format = StrictJson.RequiredString(root, "format", "$", allowEmpty: false);
            if (!string.Equals(format, SupportedFormat, StringComparison.Ordinal))
            {
                throw StrictJson.Contract("$.format must be 'nerv-iip.label-template'.");
            }

            var version = StrictJson.RequiredInt32(root, "version", "$", SupportedVersion, SupportedVersion);
            var media = ParseMedia(root["media"]);
            var fieldsElement = root["fields"];
            if (fieldsElement.ValueKind != JsonValueKind.Array)
            {
                throw StrictJson.Contract("$.fields must be an array.");
            }

            if (fieldsElement.GetArrayLength() is < 1 or > MaximumFieldCount)
            {
                throw StrictJson.Contract($"$.fields must contain between 1 and {MaximumFieldCount} fields.");
            }

            var fields = ImmutableArray.CreateBuilder<LabelTemplateField>(fieldsElement.GetArrayLength());
            var barcodeCount = 0;
            var index = 0;
            foreach (var fieldElement in fieldsElement.EnumerateArray())
            {
                var field = ParseField(fieldElement, index++);
                fields.Add(field);
                if (field is LabelBarcodeField)
                {
                    barcodeCount++;
                }
            }

            if (barcodeCount != 1)
            {
                throw StrictJson.Contract("$.fields must contain exactly one barcode field.");
            }

            return new LabelTemplateDocument(format, version, media, fields.MoveToImmutable());
        }
        catch (JsonException exception)
        {
            throw StrictJson.Contract("Label template JSON is malformed.", exception);
        }
    }

    private static LabelTemplateMedia ParseMedia(JsonElement element)
    {
        var media = StrictJson.Object(
            element,
            "$.media",
            ["dpi", "widthDots", "heightDots"],
            ["dpi", "widthDots", "heightDots"]);
        var dpi = StrictJson.RequiredInt32(media, "dpi", "$.media", 0, 32767);
        if (!SupportedDpi.Contains(dpi))
        {
            throw StrictJson.Contract("$.media.dpi must be one of 203, 300, or 600.");
        }

        return new LabelTemplateMedia(
            dpi,
            StrictJson.RequiredInt32(media, "widthDots", "$.media", 1, 32767),
            StrictJson.RequiredInt32(media, "heightDots", "$.media", 1, 32767));
    }

    private static LabelTemplateField ParseField(JsonElement element, int index)
    {
        var path = $"$.fields[{index}]";
        var properties = StrictJson.Object(
            element,
            path,
            ["kind", "x", "y", "fontHeight", "fontWidth", "moduleWidth", "height", "variable"],
            ["kind", "x", "y", "variable"]);
        var kind = StrictJson.RequiredString(properties, "kind", path, allowEmpty: false);
        var variable = StrictJson.RequiredString(properties, "variable", path, allowEmpty: false);
        StrictJson.SafeVariableName(variable, $"{path}.variable");
        var x = StrictJson.RequiredInt32(properties, "x", path, 0, 32767);
        var y = StrictJson.RequiredInt32(properties, "y", path, 0, 32767);

        return kind switch
        {
            "text" => ParseTextField(properties, path, x, y, variable),
            "barcode" => ParseBarcodeField(properties, path, x, y, variable),
            _ => throw StrictJson.Contract($"{path}.kind must be 'text' or 'barcode'."),
        };
    }

    private static LabelTextField ParseTextField(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        int x,
        int y,
        string variable)
    {
        StrictJson.RequireExactProperties(properties, path, ["kind", "x", "y", "fontHeight", "fontWidth", "variable"]);
        return new LabelTextField(
            x,
            y,
            StrictJson.RequiredInt32(properties, "fontHeight", path, 1, 32767),
            StrictJson.RequiredInt32(properties, "fontWidth", path, 1, 32767),
            variable);
    }

    private static LabelBarcodeField ParseBarcodeField(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        int x,
        int y,
        string variable)
    {
        StrictJson.RequireExactProperties(properties, path, ["kind", "x", "y", "moduleWidth", "height", "variable"]);
        return new LabelBarcodeField(
            x,
            y,
            StrictJson.RequiredInt32(properties, "moduleWidth", path, 1, 32767),
            StrictJson.RequiredInt32(properties, "height", path, 1, 32767),
            variable);
    }
}

internal static class StrictJson
{
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    public static Dictionary<string, JsonElement> Object(
        JsonElement element,
        string path,
        string[] allowed,
        string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Contract($"{path} must be an object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw Contract($"{path} contains unknown property '{property.Name}'.");
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Contract($"{path} contains duplicate property '{property.Name}'.");
            }
        }

        foreach (var name in required)
        {
            if (!properties.ContainsKey(name))
            {
                throw Contract($"{path} is missing required property '{name}'.");
            }
        }

        return properties;
    }

    public static void RequireExactProperties(
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        string[] expected)
    {
        foreach (var name in properties.Keys)
        {
            if (!expected.Contains(name))
            {
                throw Contract($"{path} contains property '{name}' which is not valid for this field kind.");
            }
        }

        foreach (var name in expected)
        {
            if (!properties.ContainsKey(name))
            {
                throw Contract($"{path} is missing required property '{name}'.");
            }
        }
    }

    public static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string path,
        bool allowEmpty)
    {
        var element = properties[name];
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Contract($"{path}.{name} must be a string.");
        }

        var value = element.GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw Contract($"{path}.{name} must not be empty.");
        }

        return value;
    }

    public static int RequiredInt32(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string path,
        int minimum,
        int maximum)
    {
        var element = properties[name];
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Contract($"{path}.{name} must be an integer.");
        }

        if (value < minimum || value > maximum)
        {
            throw Contract($"{path}.{name} must be between {minimum} and {maximum}.");
        }

        return value;
    }

    public static void SafeVariableName(string value, string path)
    {
        if (value.Any(character => char.IsControl(character) || character is '^' or '~' || char.IsWhiteSpace(character)))
        {
            throw Contract($"{path} contains an invalid variable name.");
        }
    }

    public static ArgumentException Contract(string message, Exception? innerException = null) =>
        new(message, innerException);
}

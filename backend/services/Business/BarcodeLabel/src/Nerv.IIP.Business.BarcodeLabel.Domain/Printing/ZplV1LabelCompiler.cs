using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Printing;

public sealed class CompiledLabelDocument
{
    private readonly byte[] payload;

    internal CompiledLabelDocument(ReadOnlySpan<byte> payload)
    {
        this.payload = payload.ToArray();
    }

    public ReadOnlyMemory<byte> Payload => payload;
}

public static class ZplV1LabelCompiler
{
    public const string ContractVersion = "zpl-v1";
    public const int MaximumPayloadBytes = 262144;

    private static readonly HashSet<string> SupportedBarcodeTypes =
        ["code128", "gs1-128", "qr", "datamatrix", "gs1-datamatrix"];

    public static ImmutableArray<CompiledLabelDocument> CompileBatch(
        LabelTemplateDocument template,
        LabelVariableSchema schema,
        string barcodeType,
        IReadOnlyCollection<LabelCompilationItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcodeType);
        if (!SupportedBarcodeTypes.Contains(barcodeType))
        {
            throw StrictJson.Contract($"Barcode type '{barcodeType}' is not supported by {ContractVersion}.");
        }

        var boundDocuments = LabelTemplateBinder.BindBatch(template, schema, items);
        var payloads = ImmutableArray.CreateBuilder<byte[]>(boundDocuments.Length);
        foreach (var document in boundDocuments)
        {
            var payload = CompileDocument(document, barcodeType);
            if (payload.Length > MaximumPayloadBytes)
            {
                throw StrictJson.Contract($"Compiled ZPL payload exceeds {MaximumPayloadBytes} UTF-8 bytes.");
            }

            payloads.Add(payload);
        }

        var result = ImmutableArray.CreateBuilder<CompiledLabelDocument>(payloads.Count);
        foreach (var payload in payloads)
        {
            result.Add(new CompiledLabelDocument(payload));
        }

        return result.MoveToImmutable();
    }

    private static byte[] CompileDocument(BoundLabelDocument document, string barcodeType)
    {
        var builder = new StringBuilder();
        builder.Append("^XA^PW")
            .Append(document.Template.Media.WidthDots.ToString(CultureInfo.InvariantCulture))
            .Append("^LL")
            .Append(document.Template.Media.HeightDots.ToString(CultureInfo.InvariantCulture));

        foreach (var field in document.Fields)
        {
            builder.Append("^FO")
                .Append(field.Field.X.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(field.Field.Y.ToString(CultureInfo.InvariantCulture));

            switch (field.Field)
            {
                case LabelTextField textField:
                    builder.Append("^A0N,")
                        .Append(textField.FontHeight.ToString(CultureInfo.InvariantCulture))
                        .Append(',')
                        .Append(textField.FontWidth.ToString(CultureInfo.InvariantCulture))
                        .Append("^FD")
                        .Append(field.Value)
                        .Append("^FS");
                    break;
                case LabelBarcodeField barcodeField:
                    AppendBarcode(builder, barcodeField, field.Value, document.ReservedVariables.Gs1Value, barcodeType);
                    break;
                default:
                    throw StrictJson.Contract($"Unsupported template field kind '{field.Field.Kind}'.");
            }
        }

        builder.Append("^XZ");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendBarcode(
        StringBuilder builder,
        LabelBarcodeField field,
        string value,
        Gs1BarcodeValue? gs1Value,
        string barcodeType)
    {
        builder.Append("^BY").Append(field.ModuleWidth.ToString(CultureInfo.InvariantCulture));
        switch (barcodeType)
        {
            case "code128":
                builder.Append("^BCN,")
                    .Append(field.Height.ToString(CultureInfo.InvariantCulture))
                    .Append(",Y,N,N,A^FD>:")
                    .Append(value)
                    .Append("^FS");
                return;
            case "gs1-128":
                builder.Append("^BCN,")
                    .Append(field.Height.ToString(CultureInfo.InvariantCulture))
                    .Append(",Y,N,N,A^FD>;>8")
                    .Append(EncodeGs1(RequiredGs1(gs1Value), ">8"))
                    .Append("^FS");
                return;
            case "qr":
                builder.Append("^BQ,2,6,Q,7^FDQA,")
                    .Append(value)
                    .Append("^FS");
                return;
            case "datamatrix":
                builder.Append("^BXN,6,200^FD")
                    .Append(value)
                    .Append("^FS");
                return;
            case "gs1-datamatrix":
                builder.Append("^BXN,6,200^FH^FD_1")
                    .Append(EncodeGs1(RequiredGs1(gs1Value), "_1"))
                    .Append("^FS");
                return;
            default:
                throw StrictJson.Contract($"Barcode type '{barcodeType}' is not supported by {ContractVersion}.");
        }
    }

    private static Gs1BarcodeValue RequiredGs1(Gs1BarcodeValue? value) =>
        value ?? throw StrictJson.Contract("A GS1 barcode type requires a Gs1BarcodeValue.");

    private static string EncodeGs1(Gs1BarcodeValue value, string separatorEscape)
    {
        if (string.IsNullOrWhiteSpace(value.Gtin) && string.IsNullOrWhiteSpace(value.Sscc))
        {
            throw StrictJson.Contract("A GS1 barcode requires a GTIN or SSCC.");
        }

        ValidateFixedDigits(value.Gtin, 14, "GTIN");
        ValidateFixedDigits(value.Sscc, 18, "SSCC");

        var segments = new List<(string Value, bool VariableLength)>();
        if (!string.IsNullOrWhiteSpace(value.Sscc))
        {
            segments.Add(($"00{value.Sscc}", false));
        }

        if (!string.IsNullOrWhiteSpace(value.Gtin))
        {
            segments.Add(($"01{value.Gtin}", false));
        }

        if (!string.IsNullOrWhiteSpace(value.LotNo))
        {
            segments.Add(($"10{value.LotNo}", true));
        }

        if (!string.IsNullOrWhiteSpace(value.SerialNumber))
        {
            segments.Add(($"21{value.SerialNumber}", true));
        }

        if (value.Quantity is not null)
        {
            segments.Add(($"30{value.Quantity.Value.ToString("0.#############################", CultureInfo.InvariantCulture)}", true));
        }

        var builder = new StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            builder.Append(segments[index].Value);
            if (segments[index].VariableLength && index < segments.Count - 1)
            {
                builder.Append(separatorEscape);
            }
        }

        return builder.ToString();
    }

    private static void ValidateFixedDigits(string? value, int length, string name)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && (value.Length != length || value.Any(character => character is < '0' or > '9')))
        {
            throw StrictJson.Contract($"GS1 {name} must contain exactly {length} digits.");
        }
    }
}

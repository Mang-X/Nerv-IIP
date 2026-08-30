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

    public static ImmutableArray<CompiledLabelDocument> CompileBatch(
        LabelTemplateDocument template,
        LabelVariableSchema schema,
        IReadOnlyCollection<LabelCompilationItem> items)
    {
        var boundDocuments = LabelTemplateBinder.BindBatch(template, schema, items);
        var payloads = ImmutableArray.CreateBuilder<byte[]>(boundDocuments.Length);
        foreach (var document in boundDocuments)
        {
            var payload = CompileDocument(document);
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

    private static byte[] CompileDocument(BoundLabelDocument document)
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
                    AppendBarcode(builder, barcodeField, field.Value, document.BarcodePayload);
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
        LabelBarcodePayload barcodePayload)
    {
        builder.Append("^BY").Append(field.ModuleWidth.ToString(CultureInfo.InvariantCulture));
        if (barcodePayload is PlainLabelBarcodePayload plainPayload)
        {
            AppendPlainBarcode(builder, field, value, plainPayload.Type);
            return;
        }

        var gs1Payload = (Gs1LabelBarcodePayload)barcodePayload;
        AppendGs1Barcode(builder, field, gs1Payload);
    }

    private static void AppendPlainBarcode(
        StringBuilder builder,
        LabelBarcodeField field,
        string value,
        PlainLabelBarcodeType barcodeType)
    {
        switch (barcodeType)
        {
            case PlainLabelBarcodeType.Code128:
                EnsureCode128Data(value, "Code 128");
                builder.Append("^BCN,")
                    .Append(field.Height.ToString(CultureInfo.InvariantCulture))
                    .Append(",Y,N,N,A^FD>:")
                    .Append(value)
                    .Append("^FS");
                return;
            case PlainLabelBarcodeType.Qr:
                builder.Append("^BQ,2,6,Q,7^FDQA,")
                    .Append(value)
                    .Append("^FS");
                return;
            case PlainLabelBarcodeType.DataMatrix:
                builder.Append("^BXN,6,200^FD")
                    .Append(EncodeForDataMatrix(value))
                    .Append("^FS");
                return;
            default:
                throw StrictJson.Contract($"Plain barcode type '{barcodeType}' is not supported by {ContractVersion}.");
        }
    }

    private static void AppendGs1Barcode(
        StringBuilder builder,
        LabelBarcodeField field,
        Gs1LabelBarcodePayload payload)
    {
        switch (payload.Type)
        {
            case Gs1LabelBarcodeType.Gs1128:
                builder.Append("^BCN,")
                    .Append(field.Height.ToString(CultureInfo.InvariantCulture))
                    .Append(",Y,N,N,A^FD>;>8")
                    .Append(EncodeGs1ForCode128(payload.Value, ">8"))
                    .Append("^FS");
                return;
            case Gs1LabelBarcodeType.DataMatrix:
                builder.Append("^BXN,6,200^FD_1")
                    .Append(EncodeGs1ForDataMatrix(payload.Value, "_1"))
                    .Append("^FS");
                return;
            default:
                throw StrictJson.Contract($"GS1 barcode type '{payload.Type}' is not supported by {ContractVersion}.");
        }
    }

    private static string EncodeGs1ForCode128(Gs1BarcodeValue value, string separatorEscape) =>
        EncodeGs1(value, separatorEscape, segment =>
        {
            EnsureCode128Data(segment, "GS1-128");
            return segment;
        });

    private static string EncodeGs1ForDataMatrix(Gs1BarcodeValue value, string separatorEscape) =>
        EncodeGs1(value, separatorEscape, EncodeForDataMatrix);

    private static string EncodeForDataMatrix(string value) =>
        value.Replace("_", "__", StringComparison.Ordinal);

    private static string EncodeGs1(
        Gs1BarcodeValue value,
        string separatorEscape,
        Func<string, string> encodeData)
    {
        if (string.IsNullOrWhiteSpace(value.Gtin) && string.IsNullOrWhiteSpace(value.Sscc))
        {
            throw StrictJson.Contract("A GS1 barcode requires a GTIN or SSCC.");
        }

        ValidateFixedDigits(value.Gtin, 14, "GTIN");
        ValidateFixedDigits(value.Sscc, 18, "SSCC");

        var segments = value.GetApplicationIdentifierSegments();
        var builder = new StringBuilder();
        for (var index = 0; index < segments.Length; index++)
        {
            builder.Append(encodeData(segments[index].Identifier))
                .Append(encodeData(segments[index].Value));
            if (segments[index].VariableLength && index < segments.Length - 1)
            {
                builder.Append(separatorEscape);
            }
        }

        return builder.ToString();
    }

    private static void EnsureCode128Data(string value, string context)
    {
        if (value.Contains('>'))
        {
            throw StrictJson.Contract($"{context} data cannot contain the ZPL Code 128 control introducer '>'.");
        }
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

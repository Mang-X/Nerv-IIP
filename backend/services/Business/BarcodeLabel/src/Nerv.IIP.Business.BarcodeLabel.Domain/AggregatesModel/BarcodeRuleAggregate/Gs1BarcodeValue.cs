using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

public sealed record Gs1BarcodeValue(
    string Gtin,
    string? LotNo,
    string? SerialNumber,
    decimal? Quantity,
    int? CompanyPrefixLength = null,
    string? Sscc = null)
{
    public string EpcUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SerialNumber) || Gtin.Length != 14 || CompanyPrefixLength is null)
            {
                return string.Empty;
            }

            var withoutCheckDigit = Gtin[..^1];
            if (CompanyPrefixLength is < 6 or > 12 || CompanyPrefixLength >= withoutCheckDigit.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(CompanyPrefixLength), "GS1 company prefix length must be between 6 and 12 and shorter than the GTIN root.");
            }

            return $"urn:epc:id:sgtin:{withoutCheckDigit[..CompanyPrefixLength.Value]}.{withoutCheckDigit[CompanyPrefixLength.Value..]}.{SerialNumber}";
        }
    }

    public string ToAiString()
    {
        var segments = new List<(string Text, bool VariableLength)>();
        if (!string.IsNullOrWhiteSpace(Sscc))
        {
            segments.Add(($"(00){Sscc}", false));
        }

        if (!string.IsNullOrWhiteSpace(Gtin))
        {
            segments.Add(($"(01){Gtin}", false));
        }

        if (!string.IsNullOrWhiteSpace(LotNo))
        {
            segments.Add(($"(10){LotNo}", true));
        }

        if (!string.IsNullOrWhiteSpace(SerialNumber))
        {
            segments.Add(($"(21){SerialNumber}", true));
        }

        if (Quantity is not null)
        {
            segments.Add(($"(30){Quantity.Value.ToString("0.#############################", CultureInfo.InvariantCulture)}", true));
        }

        var builder = new StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            builder.Append(segments[index].Text);
            if (segments[index].VariableLength && index < segments.Count - 1)
            {
                builder.Append('\u001D');
            }
        }

        return builder.ToString();
    }

    public static string AppendMod10CheckDigit(string digitsWithoutCheckDigit)
    {
        var digits = BarcodeLabelText.Required(digitsWithoutCheckDigit, nameof(digitsWithoutCheckDigit));
        if (digits.Any(digit => !char.IsDigit(digit)))
        {
            throw new ArgumentException("GTIN must contain only digits.", nameof(digitsWithoutCheckDigit));
        }

        if (digits.Length != 13)
        {
            throw new ArgumentException("GTIN root must be 13 digits before the mod-10 check digit.", nameof(digitsWithoutCheckDigit));
        }

        return $"{digits}{ComputeMod10CheckDigit(digits)}";
    }

    public static string AppendSsccMod10CheckDigit(string digitsWithoutCheckDigit)
    {
        var digits = BarcodeLabelText.Required(digitsWithoutCheckDigit, nameof(digitsWithoutCheckDigit));
        if (digits.Any(digit => !char.IsDigit(digit)))
        {
            throw new ArgumentException("SSCC must contain only digits.", nameof(digitsWithoutCheckDigit));
        }

        if (digits.Length != 17)
        {
            throw new ArgumentException("SSCC root must be 17 digits before the mod-10 check digit.", nameof(digitsWithoutCheckDigit));
        }

        return $"{digits}{ComputeMod10CheckDigit(digits)}";
    }

    public static Gs1BarcodeValue CreateSscc(string ssccWithoutCheckDigit)
    {
        return new Gs1BarcodeValue(string.Empty, null, null, null, null, AppendSsccMod10CheckDigit(ssccWithoutCheckDigit));
    }

    private static int ComputeMod10CheckDigit(string digits)
    {
        var sum = 0;
        var weight = 3;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            sum += (digits[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        return (10 - (sum % 10)) % 10;
    }

    public static Gs1BarcodeValue Create(
        string gtinWithoutCheckDigit,
        string? lotNo,
        string? serialNumber,
        int? companyPrefixLength = null,
        decimal? quantity = null)
    {
        return new Gs1BarcodeValue(
            AppendMod10CheckDigit(gtinWithoutCheckDigit),
            BarcodeLabelText.Optional(lotNo),
            BarcodeLabelText.Optional(serialNumber),
            quantity,
            companyPrefixLength);
    }
}

public static class Gs1ApplicationIdentifierParser
{
    public static Gs1BarcodeValue Parse(string value)
    {
        var text = BarcodeLabelText.Required(value, nameof(value));
        if (text.StartsWith('('))
        {
            return ParseParenthesized(text);
        }

        return ParseRaw(text);
    }

    private static Gs1BarcodeValue ParseParenthesized(string text)
    {
        var index = 0;
        string? sscc = null;
        string? gtin = null;
        string? lotNo = null;
        string? serialNumber = null;
        string? quantityText = null;

        while (index < text.Length)
        {
            var open = text.IndexOf('(', index);
            if (open < 0)
            {
                break;
            }

            var close = text.IndexOf(')', open + 1);
            if (close < 0)
            {
                throw new ArgumentException("GS1 value has a truncated application identifier.", nameof(text));
            }

            var ai = text[(open + 1)..close];
            var valueStart = close + 1;
            var next = text.IndexOf('(', valueStart);
            var rawValue = next < 0 ? text[valueStart..] : text[valueStart..next];
            var aiValue = rawValue.Trim('\u001D');

            switch (ai)
            {
                case "00":
                    sscc = ReadFixedValue(ai, aiValue, 18);
                    break;
                case "01":
                    gtin = ReadFixedValue(ai, aiValue, 14);
                    break;
                case "10":
                    lotNo = aiValue;
                    break;
                case "21":
                    serialNumber = aiValue;
                    break;
                case "30":
                    quantityText = aiValue;
                    break;
            }

            index = next < 0 ? text.Length : next;
        }

        decimal? quantity = decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQuantity)
            ? parsedQuantity
            : null;

        if (string.IsNullOrWhiteSpace(gtin) && string.IsNullOrWhiteSpace(sscc))
        {
            throw new ArgumentException("GS1 value is missing AI 01 or AI 00.", nameof(text));
        }

        return new Gs1BarcodeValue(gtin ?? string.Empty, BarcodeLabelText.Optional(lotNo), BarcodeLabelText.Optional(serialNumber), quantity, null, BarcodeLabelText.Optional(sscc));
    }

    private static string ReadFixedValue(string ai, string value, int length)
    {
        if (value.Length < length)
        {
            throw new ArgumentException($"GS1 AI {ai} is shorter than {length} characters.", nameof(value));
        }

        return value[..length];
    }

    private static Gs1BarcodeValue ParseRaw(string text)
    {
        var index = 0;
        string? sscc = null;
        string? gtin = null;
        string? lotNo = null;
        string? serialNumber = null;
        string? quantityText = null;

        while (index < text.Length)
        {
            if (text[index] == '\u001D')
            {
                index++;
                continue;
            }

            if (index + 2 > text.Length)
            {
                throw new ArgumentException("GS1 raw value has a truncated application identifier.", nameof(text));
            }

            if (index + 4 <= text.Length
                && text[index..].StartsWith("310", StringComparison.Ordinal)
                && char.IsDigit(text[index + 3]))
            {
                index += 4;
                SkipRawFixedValue(text, 6, ref index);
                continue;
            }

            var ai = text.Substring(index, 2);
            index += 2;
            switch (ai)
            {
                case "00":
                    sscc = ReadRawFixedValue(text, ai, 18, ref index);
                    break;
                case "01":
                    gtin = ReadRawFixedValue(text, ai, 14, ref index);
                    break;
                case "10":
                    lotNo = ReadRawVariableValue(text, ref index);
                    break;
                case "11":
                case "17":
                    SkipRawFixedValue(text, 6, ref index);
                    break;
                case "21":
                    serialNumber = ReadRawVariableValue(text, ref index);
                    break;
                case "30":
                    quantityText = ReadRawVariableValue(text, ref index);
                    break;
                default:
                    _ = ReadRawVariableValue(text, ref index);
                    break;
            }
        }

        decimal? quantity = decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQuantity)
            ? parsedQuantity
            : null;

        if (string.IsNullOrWhiteSpace(gtin) && string.IsNullOrWhiteSpace(sscc))
        {
            throw new ArgumentException("GS1 value is missing AI 01 or AI 00.", nameof(text));
        }

        return new Gs1BarcodeValue(gtin ?? string.Empty, BarcodeLabelText.Optional(lotNo), BarcodeLabelText.Optional(serialNumber), quantity, null, BarcodeLabelText.Optional(sscc));
    }

    private static string ReadRawFixedValue(string text, string ai, int length, ref int index)
    {
        if (text.Length < index + length)
        {
            throw new ArgumentException($"GS1 AI {ai} is shorter than {length} characters.", nameof(text));
        }

        var value = text.Substring(index, length);
        index += length;
        return value;
    }

    private static void SkipRawFixedValue(string text, int length, ref int index)
    {
        if (text.Length < index + length)
        {
            throw new ArgumentException("GS1 raw value has a truncated fixed-length value.", nameof(text));
        }

        index += length;
    }

    private static string ReadRawVariableValue(string text, ref int index)
    {
        var end = text.IndexOf('\u001D', index);
        if (end < 0)
        {
            var tail = text[index..];
            index = text.Length;
            return tail;
        }

        var value = text[index..end];
        index = end + 1;
        return value;
    }
}

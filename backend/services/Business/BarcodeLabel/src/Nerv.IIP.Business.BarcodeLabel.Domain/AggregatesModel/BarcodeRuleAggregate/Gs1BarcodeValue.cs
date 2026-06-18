using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

public sealed record Gs1BarcodeValue(
    string Gtin,
    string? LotNo,
    string? SerialNumber,
    decimal? Quantity,
    int? CompanyPrefixLength = null)
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
        var builder = new StringBuilder($"(01){Gtin}");
        if (!string.IsNullOrWhiteSpace(LotNo))
        {
            builder.Append("(10)").Append(LotNo);
        }

        if (!string.IsNullOrWhiteSpace(SerialNumber))
        {
            builder.Append("(21)").Append(SerialNumber);
        }

        if (Quantity is not null)
        {
            builder.Append("(30)").Append(Quantity.Value.ToString("0.#############################", CultureInfo.InvariantCulture));
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

        var sum = 0;
        var weight = 3;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            sum += (digits[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return $"{digits}{checkDigit}";
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
        if (!text.StartsWith("(01)", StringComparison.Ordinal))
        {
            return ParseRaw(text);
        }

        var gtin = ReadRequiredAi(text, "01", 14);
        var lotNo = ReadVariableAi(text, "10");
        var serialNumber = ReadVariableAi(text, "21");
        var quantityText = ReadVariableAi(text, "30");
        decimal? quantity = decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQuantity)
            ? parsedQuantity
            : null;

        return new Gs1BarcodeValue(gtin, lotNo, serialNumber, quantity);
    }

    private static string ReadRequiredAi(string text, string ai, int length)
    {
        var marker = $"({ai})";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new ArgumentException($"GS1 value is missing AI {ai}.", nameof(text));
        }

        start += marker.Length;
        if (text.Length < start + length)
        {
            throw new ArgumentException($"GS1 AI {ai} is shorter than {length} characters.", nameof(text));
        }

        return text.Substring(start, length);
    }

    private static string? ReadVariableAi(string text, string ai)
    {
        var marker = $"({ai})";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var next = text.IndexOf('(', start);
        var value = next < 0 ? text[start..] : text[start..next];
        return BarcodeLabelText.Optional(value);
    }

    private static Gs1BarcodeValue ParseRaw(string text)
    {
        var index = 0;
        var gtin = ReadRequiredRawAi(text, "01", 14, ref index);
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

            var ai = text.Substring(index, 2);
            index += 2;
            switch (ai)
            {
                case "10":
                    lotNo = ReadRawVariableValue(text, ref index);
                    break;
                case "21":
                    serialNumber = ReadRawVariableValue(text, ref index);
                    break;
                case "30":
                    quantityText = ReadRawVariableValue(text, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unsupported GS1 application identifier '{ai}'.", nameof(text));
            }
        }

        decimal? quantity = decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQuantity)
            ? parsedQuantity
            : null;

        return new Gs1BarcodeValue(gtin, BarcodeLabelText.Optional(lotNo), BarcodeLabelText.Optional(serialNumber), quantity);
    }

    private static string ReadRequiredRawAi(string text, string ai, int length, ref int index)
    {
        if (!text[index..].StartsWith(ai, StringComparison.Ordinal))
        {
            throw new ArgumentException($"GS1 value is missing AI {ai}.", nameof(text));
        }

        index += ai.Length;
        if (text.Length < index + length)
        {
            throw new ArgumentException($"GS1 AI {ai} is shorter than {length} characters.", nameof(text));
        }

        var value = text.Substring(index, length);
        index += length;
        return value;
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

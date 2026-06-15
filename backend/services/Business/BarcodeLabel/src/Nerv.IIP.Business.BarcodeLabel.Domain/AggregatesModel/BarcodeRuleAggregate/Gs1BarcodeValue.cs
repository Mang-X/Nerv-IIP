using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

public sealed record Gs1BarcodeValue(
    string Gtin,
    string? LotNo,
    string? SerialNumber,
    decimal? Quantity)
{
    public string EpcUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SerialNumber) || Gtin.Length < 14)
            {
                return string.Empty;
            }

            var withoutCheckDigit = Gtin[..^1];
            return $"urn:epc:id:sgtin:{withoutCheckDigit[..7]}.{withoutCheckDigit[7..]}.{SerialNumber}";
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

    public static Gs1BarcodeValue Create(string gtinWithoutCheckDigit, string? lotNo, string? serialNumber, decimal? quantity = null)
    {
        return new Gs1BarcodeValue(
            AppendMod10CheckDigit(gtinWithoutCheckDigit),
            BarcodeLabelText.Optional(lotNo),
            BarcodeLabelText.Optional(serialNumber),
            quantity);
    }
}

public static class Gs1ApplicationIdentifierParser
{
    public static Gs1BarcodeValue Parse(string value)
    {
        var text = BarcodeLabelText.Required(value, nameof(value));
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
}

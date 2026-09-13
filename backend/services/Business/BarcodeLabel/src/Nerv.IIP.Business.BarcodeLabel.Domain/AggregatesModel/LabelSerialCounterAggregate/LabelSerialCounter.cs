using System.Buffers.Binary;
using System.Security.Cryptography;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

public partial record LabelSerialCounterId : IGuidStronglyTypedId;

public sealed class LabelSerialCounter : Entity<LabelSerialCounterId>, IAggregateRoot
{
    private LabelSerialCounter()
    {
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public BarcodeRuleId BarcodeRuleId { get; private set; } = null!;
    public long CurrentValue { get; private set; }
}

public static class LabelSerialNumber
{
    private const int RuleTokenWidth = 9;
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Format(BarcodeRuleId barcodeRuleId, long value, int width)
    {
        ArgumentNullException.ThrowIfNull(barcodeRuleId);
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Allocated serial width must be at least two characters.");
        }

        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Serial allocation value must be positive.");
        }

        var ruleTokenWidth = Math.Min(RuleTokenWidth, width / 2);
        var counterWidth = width - ruleTokenWidth;
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(barcodeRuleId.Id.ToByteArray(), digest);
        var ruleTokenValue = BinaryPrimitives.ReadUInt64BigEndian(digest) % Pow62(ruleTokenWidth);
        return $"{FormatBase62(ruleTokenValue, ruleTokenWidth)}{FormatBase62((ulong)value, counterWidth)}";
    }

    private static string FormatBase62(ulong value, int width)
    {
        if (width <= 10 && value >= Pow62(width))
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Serial allocation value does not fit in {width} Base62 characters.");
        }

        Span<char> buffer = stackalloc char[width];
        buffer.Fill('0');
        for (var index = width - 1; value > 0; index--)
        {
            buffer[index] = Digits[(int)(value % (ulong)Digits.Length)];
            value /= (ulong)Digits.Length;
        }

        return buffer.ToString();
    }

    private static ulong Pow62(int exponent)
    {
        var result = 1UL;
        for (var index = 0; index < exponent; index++)
        {
            result *= (ulong)Digits.Length;
        }

        return result;
    }
}

public interface ILabelSerialNumberAllocator
{
    Task<IReadOnlyList<string>> AllocateAsync(
        string organizationId,
        string environmentId,
        BarcodeRuleId barcodeRuleId,
        int serialNumberLength,
        int quantity,
        CancellationToken cancellationToken);
}

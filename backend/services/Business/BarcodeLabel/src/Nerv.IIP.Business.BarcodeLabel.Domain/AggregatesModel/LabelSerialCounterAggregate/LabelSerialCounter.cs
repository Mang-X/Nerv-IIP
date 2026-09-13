namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

using System.Buffers.Binary;
using System.Security.Cryptography;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

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
    private const int Width = 11;
    private const int RuleTokenWidth = 9;
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Format(BarcodeRuleId barcodeRuleId, long value)
    {
        ArgumentNullException.ThrowIfNull(barcodeRuleId);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(barcodeRuleId.Id.ToByteArray(), digest);
        var ruleTokenValue = BinaryPrimitives.ReadUInt64BigEndian(digest) % Pow62(RuleTokenWidth);
        return $"{Format(ruleTokenValue, RuleTokenWidth)}{Format(value)}";
    }

    public static string Format(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Serial allocation value must be positive.");
        }

        return Format((ulong)value, Width);
    }

    private static string Format(ulong value, int width)
    {
        Span<char> buffer = stackalloc char[width];
        buffer.Fill('0');
        var remaining = value;
        for (var index = width - 1; remaining > 0; index--)
        {
            buffer[index] = Digits[(int)(remaining % (ulong)Digits.Length)];
            remaining /= (ulong)Digits.Length;
        }

        return buffer.ToString();
    }

    private static ulong Pow62(int exponent)
    {
        var value = 1UL;
        for (var index = 0; index < exponent; index++)
        {
            value *= (ulong)Digits.Length;
        }

        return value;
    }
}

public interface ILabelSerialNumberAllocator
{
    Task<IReadOnlyList<string>> AllocateAsync(
        string organizationId,
        string environmentId,
        BarcodeRuleId barcodeRuleId,
        int quantity,
        CancellationToken cancellationToken);
}

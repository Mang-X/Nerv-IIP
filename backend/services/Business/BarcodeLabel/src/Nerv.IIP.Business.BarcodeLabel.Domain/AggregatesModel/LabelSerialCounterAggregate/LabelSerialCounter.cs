namespace Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

public partial record LabelSerialCounterId : IGuidStronglyTypedId;

public sealed class LabelSerialCounter : Entity<LabelSerialCounterId>, IAggregateRoot
{
    private LabelSerialCounter()
    {
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public long CurrentValue { get; private set; }
}

public static class LabelSerialNumber
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Format(long value, int width)
    {
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Allocated serial width must be at least two characters.");
        }

        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Serial allocation value must be positive.");
        }

        return FormatBase62((ulong)value, width);
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
        int serialNumberLength,
        int quantity,
        CancellationToken cancellationToken);
}

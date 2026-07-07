using System.Globalization;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain;

public sealed record TelemetrySamplingPolicy(int BucketSeconds)
{
    public static TelemetrySamplingPolicy Parse(string samplingPolicy)
    {
        var normalized = IndustrialTelemetryText.RequiredLower(samplingPolicy, nameof(samplingPolicy));
        var bucketPart = normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.StartsWith("bucket=", StringComparison.Ordinal) ? part["bucket=".Length..] : part)
            .FirstOrDefault(part => part.StartsWith("sample-", StringComparison.Ordinal)
                || part.EndsWith("ms", StringComparison.Ordinal)
                || part.EndsWith("s", StringComparison.Ordinal)
                || part.EndsWith("m", StringComparison.Ordinal)
                || part.EndsWith("h", StringComparison.Ordinal))
            ?? normalized;

        if (bucketPart.StartsWith("sample-", StringComparison.Ordinal))
        {
            bucketPart = bucketPart["sample-".Length..];
        }

        var seconds = ParseDurationSeconds(bucketPart);
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingPolicy), "Sampling policy bucket duration must be positive.");
        }

        return new TelemetrySamplingPolicy(seconds);
    }

    private static int ParseDurationSeconds(string value)
    {
        if (value.EndsWith("ms", StringComparison.Ordinal))
        {
            var milliseconds = int.Parse(value[..^2], CultureInfo.InvariantCulture);
            return Math.Max(1, (int)Math.Ceiling(milliseconds / 1000m));
        }

        if (value.EndsWith("s", StringComparison.Ordinal))
        {
            return int.Parse(value[..^1], CultureInfo.InvariantCulture);
        }

        if (value.EndsWith("m", StringComparison.Ordinal))
        {
            return checked(int.Parse(value[..^1], CultureInfo.InvariantCulture) * 60);
        }

        if (value.EndsWith("h", StringComparison.Ordinal))
        {
            return checked(int.Parse(value[..^1], CultureInfo.InvariantCulture) * 60 * 60);
        }

        return int.Parse(value, CultureInfo.InvariantCulture);
    }
}

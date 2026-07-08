using System.Globalization;

namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

// Keep this parser aligned with Business IndustrialTelemetry Domain TelemetrySamplingPolicy.
public sealed record TelemetrySamplingPolicy(
    int BucketSeconds,
    TimeSpan? RawRetention = null,
    TimeSpan? HourlyRetention = null,
    TimeSpan? DailyRetention = null)
{
    public static TelemetrySamplingPolicy Parse(string samplingPolicy)
    {
        try
        {
            return ParseCore(samplingPolicy);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"Telemetry sampling policy is invalid: {ex.Message}", ex);
        }
    }

    private static TelemetrySamplingPolicy ParseCore(string samplingPolicy)
    {
        var normalized = RequiredLower(samplingPolicy, nameof(samplingPolicy));
        var parts = normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Sampling policy bucket duration is required.", nameof(samplingPolicy));
        }

        string? bucketPart = null;
        TimeSpan? rawRetention = null;
        TimeSpan? hourlyRetention = null;
        TimeSpan? dailyRetention = null;
        foreach (var part in parts)
        {
            if (TryReadAssignment(part, "bucket", out var bucketValue))
            {
                bucketPart = bucketValue;
                continue;
            }

            if (TryReadAssignment(part, "raw", out var rawValue))
            {
                rawRetention = ParseRetention(rawValue, nameof(samplingPolicy));
                continue;
            }

            if (TryReadAssignment(part, "hourly", out var hourlyValue))
            {
                hourlyRetention = ParseRetention(hourlyValue, nameof(samplingPolicy));
                continue;
            }

            if (TryReadAssignment(part, "daily", out var dailyValue))
            {
                dailyRetention = ParseRetention(dailyValue, nameof(samplingPolicy));
                continue;
            }

            if (bucketPart is null && IsDurationPart(part))
            {
                bucketPart = part;
                continue;
            }

            throw new ArgumentException($"Unsupported sampling policy segment '{part}'.", nameof(samplingPolicy));
        }

        bucketPart ??= normalized;
        if (bucketPart.StartsWith("sample-", StringComparison.Ordinal))
        {
            bucketPart = bucketPart["sample-".Length..];
        }

        var seconds = ParseDurationSeconds(bucketPart);
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingPolicy), "Sampling policy bucket duration must be positive.");
        }

        return new TelemetrySamplingPolicy(seconds, rawRetention, hourlyRetention, dailyRetention);
    }

    private static bool TryReadAssignment(string part, string name, out string value)
    {
        var prefix = $"{name}=";
        if (part.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = part[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsDurationPart(string part)
    {
        return part.StartsWith("sample-", StringComparison.Ordinal)
            || part.EndsWith("ms", StringComparison.Ordinal)
            || part.EndsWith("s", StringComparison.Ordinal)
            || part.EndsWith("m", StringComparison.Ordinal)
            || part.EndsWith("h", StringComparison.Ordinal);
    }

    private static string RequiredLower(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim().ToLowerInvariant();
    }

    private static TimeSpan ParseRetention(string value, string parameterName)
    {
        var seconds = ParseDurationSeconds(value);
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Sampling policy retention duration must be positive.");
        }

        return TimeSpan.FromSeconds(seconds);
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

        if (value.EndsWith("d", StringComparison.Ordinal))
        {
            return checked(int.Parse(value[..^1], CultureInfo.InvariantCulture) * 24 * 60 * 60);
        }

        return int.Parse(value, CultureInfo.InvariantCulture);
    }
}

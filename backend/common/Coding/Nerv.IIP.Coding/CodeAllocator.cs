using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Coding;

public sealed record CodeAllocation(string Code, bool IsIdempotentReplay);

public sealed record CodeAllocationRequest(
    string OrganizationId,
    string EnvironmentId,
    CodeRuleDefinition Rule,
    IReadOnlyDictionary<string, string>? Fields,
    string? RequestedCode,
    string? IdempotencyKey,
    string PayloadFingerprint,
    string ConflictResourceLabel,
    string SiteCode = "");

public sealed class CodeConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class CodeAllocatorOptions(int MaxConcurrencyAttempts, Func<int, TimeSpan> RetryBackoff)
{
    public static CodeAllocatorOptions Default { get; } = new(5, attempt => TimeSpan.FromMilliseconds(attempt * 10));

    public int MaxConcurrencyAttempts { get; } = MaxConcurrencyAttempts;

    public Func<int, TimeSpan> RetryBackoff { get; } = RetryBackoff;
}

public sealed class CodeAllocator(
    ICodeStore? store = null,
    TimeProvider? timeProvider = null,
    CodeAllocatorOptions? options = null)
{
    private readonly ICodeStore? _store = store;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly CodeAllocatorOptions _options = options ?? CodeAllocatorOptions.Default;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeIdempotencyKey> _idempotency = new(StringComparer.Ordinal);

    public async Task<CodeAllocation> AllocateAsync(CodeAllocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Rule.Validate();
        if (!request.Rule.IsActive)
        {
            throw new KnownException($"Code rule '{request.Rule.RuleKey}' is inactive.");
        }

        var normalizedRequestedCode = Normalize(request.RequestedCode);
        var normalizedIdempotencyKey = Normalize(request.IdempotencyKey);
        if (_store is null)
        {
            return AllocateInMemory(request, normalizedRequestedCode, normalizedIdempotencyKey);
        }

        var idempotencyRecord = normalizedIdempotencyKey is null
            ? null
            : await _store.FindIdempotencyRecordAsync(
                request.OrganizationId,
                request.EnvironmentId,
                request.Rule.RuleKey,
                normalizedIdempotencyKey,
                cancellationToken);
        if (idempotencyRecord is not null)
        {
            if (!string.Equals(idempotencyRecord.PayloadFingerprint, request.PayloadFingerprint, StringComparison.Ordinal))
            {
                throw IdempotencyConflict(normalizedIdempotencyKey!, request.ConflictResourceLabel);
            }

            return new CodeAllocation(idempotencyRecord.Code, true);
        }

        var code = normalizedRequestedCode ?? await NextCodeAsync(request, cancellationToken);
        if (normalizedIdempotencyKey is not null)
        {
            _store.AddIdempotencyRecord(new CodeIdempotencyKey(
                request.OrganizationId,
                request.EnvironmentId,
                request.Rule.RuleKey,
                normalizedIdempotencyKey,
                code,
                request.PayloadFingerprint,
                _timeProvider.GetUtcNow()));
        }

        return new CodeAllocation(code, false);
    }

    public static string Fingerprint(params object?[] parts)
    {
        return string.Join('|', parts.Select(part => part switch
        {
            null => string.Empty,
            IEnumerable<string> values => string.Join(',', values.Order(StringComparer.Ordinal)),
            _ => Convert.ToString(part, CultureInfo.InvariantCulture) ?? string.Empty,
        }));
    }

    private async Task<string> NextCodeAsync(CodeAllocationRequest request, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var sequenceSegment = request.Rule.Segments.First(segment => segment.Type == SegmentType.Sequence);
        var resetKey = ResolveResetKey(sequenceSegment.Reset, now);
        var siteCode = request.Rule.Scope.HasFlag(ScopeDimension.Site) ? Normalize(request.SiteCode) ?? string.Empty : string.Empty;
        long? reservedSequence = null;
        var builder = new StringBuilder();

        foreach (var segment in request.Rule.Segments)
        {
            if (segment.Type == SegmentType.Sequence && reservedSequence is null)
            {
                reservedSequence = await ReserveSequenceAsync(
                    request,
                    siteCode,
                    resetKey,
                    sequenceSegment.Start,
                    cancellationToken);
            }

            builder.Append(segment.Type switch
            {
                SegmentType.Constant => segment.Value,
                SegmentType.Date => now.ToString(segment.Format, CultureInfo.InvariantCulture),
                SegmentType.Field => EvaluateFieldSegment(request, segment),
                SegmentType.Sequence => FormatSequence(segment, reservedSequence!.Value),
                SegmentType.Checksum => EvaluateChecksum(builder.ToString(), segment.Algorithm),
                _ => throw new KnownException($"Unsupported code rule segment type '{segment.Type}'."),
            });
        }

        return builder.ToString();
    }

    private async Task<long> ReserveSequenceAsync(
        CodeAllocationRequest request,
        string siteCode,
        string resetKey,
        long start,
        CancellationToken cancellationToken)
    {
        var scope = new CodeCounterScope(
            request.OrganizationId,
            request.EnvironmentId,
            request.Rule.RuleKey,
            siteCode,
            resetKey,
            start);

        return _store is null
            ? ReserveNextInMemory(scope)
            : await ReserveNextWithRetryAsync(scope, cancellationToken);
    }

    private async Task<long> ReserveNextWithRetryAsync(CodeCounterScope scope, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _store!.ReserveNextCounterValueAsync(scope, cancellationToken);
            }
            catch (CodeConcurrencyException) when (attempt < _options.MaxConcurrencyAttempts)
            {
                await Task.Delay(_options.RetryBackoff(attempt), cancellationToken);
            }
        }
    }

    private long ReserveNextInMemory(CodeCounterScope scope)
    {
        var key = Key(scope.OrganizationId, scope.EnvironmentId, scope.RuleKey, scope.SiteCode, scope.ResetKey);
        lock (_lock)
        {
            _counters.TryGetValue(key, out var current);
            var next = current < scope.Start - 1 ? scope.Start : current + 1;
            _counters[key] = next;
            return next;
        }
    }

    private CodeAllocation AllocateInMemory(
        CodeAllocationRequest request,
        string? normalizedRequestedCode,
        string? normalizedIdempotencyKey)
    {
        lock (_lock)
        {
            var idempotencyRecord = normalizedIdempotencyKey is null
                ? null
                : FindIdempotencyRecordInMemory(request, normalizedIdempotencyKey);
            if (idempotencyRecord is not null)
            {
                if (!string.Equals(idempotencyRecord.PayloadFingerprint, request.PayloadFingerprint, StringComparison.Ordinal))
                {
                    throw IdempotencyConflict(normalizedIdempotencyKey!, request.ConflictResourceLabel);
                }

                return new CodeAllocation(idempotencyRecord.Code, true);
            }
        }

        var code = normalizedRequestedCode ?? NextCodeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        lock (_lock)
        {
            if (normalizedIdempotencyKey is not null)
            {
                var idempotencyKey = new CodeIdempotencyKey(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.Rule.RuleKey,
                    normalizedIdempotencyKey,
                    code,
                    request.PayloadFingerprint,
                    _timeProvider.GetUtcNow());
                _idempotency.Add(
                    Key(idempotencyKey.OrganizationId, idempotencyKey.EnvironmentId, idempotencyKey.RuleKey, idempotencyKey.IdempotencyKey),
                    idempotencyKey);
            }
        }

        return new CodeAllocation(code, false);
    }

    private CodeIdempotencyKey? FindIdempotencyRecordInMemory(CodeAllocationRequest request, string idempotencyKey)
    {
        _idempotency.TryGetValue(Key(request.OrganizationId, request.EnvironmentId, request.Rule.RuleKey, idempotencyKey), out var record);
        return record;
    }

    private static string EvaluateFieldSegment(CodeAllocationRequest request, CodeRuleSegment segment)
    {
        var value = string.Empty;
        var hasValue = request.Fields?.TryGetValue(segment.Source!, out value) == true;
        if (!hasValue || string.IsNullOrWhiteSpace(value))
        {
            if (segment.Required)
            {
                throw new KnownException($"Code rule '{request.Rule.RuleKey}' requires field '{segment.Source}'.");
            }

            return string.Empty;
        }

        var result = segment.Transform switch
        {
            FieldTransform.Upper => value!.ToUpperInvariant(),
            FieldTransform.Lower => value!.ToLowerInvariant(),
            _ => value!,
        };

        return segment.MaxLength is { } maxLength && result.Length > maxLength
            ? result[..maxLength]
            : result;
    }

    private static string FormatSequence(CodeRuleSegment segment, long value)
    {
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(segment.Width, segment.PadChar);
    }

    private static string ResolveResetKey(ResetPeriod reset, DateTimeOffset now)
    {
        return reset switch
        {
            ResetPeriod.None => string.Empty,
            ResetPeriod.Day => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ResetPeriod.Month => now.ToString("yyyyMM", CultureInfo.InvariantCulture),
            ResetPeriod.Year => now.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => throw new KnownException($"Unsupported code reset period '{reset}'."),
        };
    }

    private static string EvaluateChecksum(string prefix, string? algorithm)
    {
        return algorithm switch
        {
            "mod10" => ModChecksum(prefix, 10).ToString(CultureInfo.InvariantCulture),
            "mod11" => ModChecksum(prefix, 11).ToString(CultureInfo.InvariantCulture),
            _ => throw new KnownException($"Unsupported checksum algorithm '{algorithm}'."),
        };
    }

    private static int ModChecksum(string value, int mod)
    {
        var sum = SHA256.HashData(Encoding.UTF8.GetBytes(value)).Sum(b => b);
        return sum % mod;
    }

    private static KnownException IdempotencyConflict(string idempotencyKey, string conflictResourceLabel)
    {
        return new KnownException($"Idempotency key '{idempotencyKey}' conflicts with a different {conflictResourceLabel} create payload.");
    }

    private static string Key(params string[] parts)
    {
        return string.Join('|', parts.Select(part => part.Trim().ToLowerInvariant()));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

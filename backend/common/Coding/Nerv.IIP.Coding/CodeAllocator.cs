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
        EnsureRequestedCodeFitsTheAllocatorColumn(request, normalizedRequestedCode);
        var normalizedIdempotencyKey = Normalize(request.IdempotencyKey);
        if (_store is null)
        {
            return AllocateInMemory(request, normalizedRequestedCode, normalizedIdempotencyKey);
        }

        var replay = await TryPeekReplayCoreAsync(request, normalizedIdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return replay;
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

    public Task<CodeAllocation?> TryPeekReplayAsync(CodeAllocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Rule.Validate();
        if (!request.Rule.IsActive)
        {
            throw new KnownException($"Code rule '{request.Rule.RuleKey}' is inactive.");
        }

        return TryPeekReplayCoreAsync(request, Normalize(request.IdempotencyKey), cancellationToken);
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
            var replay = normalizedIdempotencyKey is null
                ? null
                : ToReplay(request, normalizedIdempotencyKey, idempotencyRecord);
            if (replay is not null)
            {
                return replay;
            }
        }

        var code = normalizedRequestedCode ?? NextCodeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        lock (_lock)
        {
            if (normalizedIdempotencyKey is not null)
            {
                var existingRecord = FindIdempotencyRecordInMemory(request, normalizedIdempotencyKey);
                var replay = ToReplay(request, normalizedIdempotencyKey, existingRecord);
                if (replay is not null)
                {
                    return replay;
                }

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

    private async Task<CodeAllocation?> TryPeekReplayCoreAsync(
        CodeAllocationRequest request,
        string? normalizedIdempotencyKey,
        CancellationToken cancellationToken)
    {
        if (normalizedIdempotencyKey is null)
        {
            return null;
        }

        if (_store is null)
        {
            lock (_lock)
            {
                return ToReplay(
                    request,
                    normalizedIdempotencyKey,
                    FindIdempotencyRecordInMemory(request, normalizedIdempotencyKey));
            }
        }

        var record = await _store.FindIdempotencyRecordAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.Rule.RuleKey,
            normalizedIdempotencyKey,
            cancellationToken);
        return ToReplay(request, normalizedIdempotencyKey, record);
    }

    private static CodeAllocation? ToReplay(
        CodeAllocationRequest request,
        string normalizedIdempotencyKey,
        CodeIdempotencyKey? record)
    {
        if (record is null)
        {
            return null;
        }

        if (!string.Equals(record.PayloadFingerprint, request.PayloadFingerprint, StringComparison.Ordinal))
        {
            throw IdempotencyConflict(normalizedIdempotencyKey, request.ConflictResourceLabel);
        }

        return new CodeAllocation(record.Code, true);
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
            "hash-mod10" => HashModChecksum(prefix, 10).ToString(CultureInfo.InvariantCulture),
            "hash-mod11" => HashModChecksum(prefix, 11).ToString(CultureInfo.InvariantCulture),
            _ => throw new KnownException($"Unsupported checksum algorithm '{algorithm}'."),
        };
    }

    private static int HashModChecksum(string value, int mod)
    {
        var sum = SHA256.HashData(Encoding.UTF8.GetBytes(value)).Sum(b => b);
        return sum % mod;
    }

    /// <summary>
    /// GitHub #3454：调用方给的 <c>RequestedCode</c> 被本类**原样采用**（唯一加工是 <see cref="Normalize"/> 的
    /// <c>Trim()</c>），再写进 <c>code_idempotency_keys.code</c>。改前这里没有任何长度判断，
    /// 超过列宽的码一路走到 <c>SaveChangesAsync</c> 才换来 PostgreSQL <c>22001</c>——
    /// 到了用户那儿是 500，而不是「码太长」这种能自己改的校验提示。
    ///
    /// <para><b>为什么校验放在这里而不是放到各服务的校验器里</b>：<c>RequestedCode</c> 的取值点实读有 50 个
    /// （分布在 7 个业务服务，其中 19 个连 <c>MaximumLength</c> 都没有，spike 见 #3454）。
    /// 逐个补规则是白名单选取，新调用点加进来时会静默漏掉（本仓判例 #3003 / #3135 / #3300）。
    /// 装不下自己这一列是**分配器自己的不变量**，收在这一处对 50 个取值点一次性成立。</para>
    ///
    /// <para><b>抛什么、谁接得住</b>：抛 <see cref="KnownException"/>，与本类既有的业务拒绝
    /// （规则停用、必填字段缺失、幂等键冲突）同型，经各服务统一异常管线转成公开业务错误。
    /// 集成事件消费侧实读三类：<c>ConsumerJournalVoucherNumber.TryAllocateAsync</c> 的 catch filter 是
    /// <c>CodeConcurrencyException or KnownException</c>（收得住，且那 5 个位点本来就传 <c>RequestedCode: null</c>）；
    /// <c>WmsInboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReceipt</c> 的 filter 是
    /// <c>KnownException or ArgumentException or InvalidOperationException</c>（收得住，转死信）；
    /// <c>PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder</c> 的 filter **不含**
    /// <see cref="KnownException"/>，但它传的 <c>DownstreamDocumentId</c> 在 DemandPlanning 侧同时受
    /// <c>AcceptPlanningSuggestionCommandValidator</c> 的 <c>MaximumLength(128)</c> 与
    /// <c>planning_suggestions.accepted_downstream_document_id</c> 的 128 列宽约束，
    /// ⇒ 本守卫在那条路径上**不可达**。⚠️ 失效方向：那两个 128 里任意一个被放宽，
    /// 该消费者就会把本异常逃逸成 poison message（#877 仍 OPEN）。</para>
    ///
    /// <para><b>只管装得下，不管形状</b>：长度合法但不符合规则形状的码（例如 SKU 码写成 <c>!!!</c>）照样放行。
    /// 这是 #3454 有意留的边界，不是漏掉的一条。</para>
    /// </summary>
    private static void EnsureRequestedCodeFitsTheAllocatorColumn(
        CodeAllocationRequest request,
        string? normalizedRequestedCode)
    {
        if (normalizedRequestedCode is null || normalizedRequestedCode.Length <= CodeIdempotencyKey.CodeMaxLength)
        {
            return;
        }

        // ⛔ 不回显 requestedCode 本身：它是调用方可控的任意长字符串，回显等于把攻击者的载荷原样送回错误信封。
        throw new KnownException(
            $"Requested code for rule '{request.Rule.RuleKey}' is {normalizedRequestedCode.Length} characters, "
            + $"which exceeds the {CodeIdempotencyKey.CodeMaxLength} character limit for allocated codes.");
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

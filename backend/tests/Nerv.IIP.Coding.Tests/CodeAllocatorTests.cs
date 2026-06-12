using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.Coding.Tests;

public sealed class CodeAllocatorTests
{
    private static CodeRuleDefinition SkuRule() => new()
    {
        RuleKey = "sku",
        DisplayName = "SKU",
        Segments =
        [
            CodeRuleSegment.ConstantOf("SKU"),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.DateOf("yyyyMMdd"),
            CodeRuleSegment.ConstantOf("-"),
            CodeRuleSegment.SequenceOf(6, ResetPeriod.Day),
        ],
    };

    [Fact]
    public async Task AllocateAsync_generates_date_and_zero_padded_sequence()
    {
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest(
            "org",
            "env",
            SkuRule(),
            Fields: null,
            RequestedCode: null,
            IdempotencyKey: null,
            PayloadFingerprint: "payload",
            ConflictResourceLabel: "sku");

        var first = await allocator.AllocateAsync(request, CancellationToken.None);
        var second = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("SKU-20260612-000001", first.Code);
        Assert.False(first.IsIdempotentReplay);
        Assert.Equal("SKU-20260612-000002", second.Code);
    }

    [Fact]
    public async Task AllocateAsync_resets_sequence_by_reset_bucket()
    {
        var timeProvider = new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero));
        var allocator = new CodeAllocator(timeProvider: timeProvider);
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, null, null, "payload", "sku");

        var first = await allocator.AllocateAsync(request, CancellationToken.None);
        timeProvider.SetUtcNow(new DateTimeOffset(2026, 6, 13, 1, 0, 0, TimeSpan.Zero));
        var second = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("SKU-20260612-000001", first.Code);
        Assert.Equal("SKU-20260613-000001", second.Code);
    }

    [Fact]
    public async Task AllocateAsync_honors_sequence_start()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "site",
            DisplayName = "站点",
            Segments =
            [
                CodeRuleSegment.ConstantOf("ST"),
                CodeRuleSegment.SequenceOf(width: 3, start: 8),
            ],
        };
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));

        var first = await allocator.AllocateAsync(new CodeAllocationRequest("org", "env", rule, null, null, null, "payload", "site"), CancellationToken.None);
        var second = await allocator.AllocateAsync(new CodeAllocationRequest("org", "env", rule, null, null, null, "payload", "site"), CancellationToken.None);

        Assert.Equal("ST008", first.Code);
        Assert.Equal("ST009", second.Code);
    }

    [Fact]
    public async Task AllocateAsync_uppercases_and_truncates_field_segment()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "material",
            DisplayName = "物料",
            Segments =
            [
                CodeRuleSegment.FieldOf("materialType", FieldTransform.Upper, maxLength: 3),
                CodeRuleSegment.ConstantOf("-"),
                CodeRuleSegment.SequenceOf(width: 5),
            ],
        };
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest(
            "org",
            "env",
            rule,
            new Dictionary<string, string> { ["materialType"] = "raw-material" },
            null,
            null,
            "payload",
            "material");

        var result = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("RAW-00001", result.Code);
    }

    [Fact]
    public async Task AllocateAsync_throws_when_required_field_is_missing()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "material",
            DisplayName = "物料",
            Segments =
            [
                CodeRuleSegment.FieldOf("materialType"),
                CodeRuleSegment.SequenceOf(width: 5),
            ],
        };
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<KnownException>(() =>
            allocator.AllocateAsync(new CodeAllocationRequest("org", "env", rule, null, null, null, "payload", "material"), CancellationToken.None));
    }

    [Fact]
    public async Task AllocateAsync_replays_idempotency_record_and_rejects_conflicting_payload()
    {
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var firstRequest = new CodeAllocationRequest("org", "env", SkuRule(), null, null, "idem-1", "payload-a", "sku");
        var replayRequest = firstRequest with { PayloadFingerprint = "payload-a" };
        var conflictRequest = firstRequest with { PayloadFingerprint = "payload-b" };

        var first = await allocator.AllocateAsync(firstRequest, CancellationToken.None);
        var replay = await allocator.AllocateAsync(replayRequest, CancellationToken.None);

        Assert.Equal(first.Code, replay.Code);
        Assert.True(replay.IsIdempotentReplay);
        await Assert.ThrowsAsync<KnownException>(() => allocator.AllocateAsync(conflictRequest, CancellationToken.None));
    }

    [Fact]
    public async Task AllocateAsync_retries_store_concurrency_conflict()
    {
        var store = new InMemoryCodeStore { ConcurrencyFailuresBeforeSuccess = 1 };
        var allocator = new CodeAllocator(store, new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, null, null, "payload", "sku");

        var result = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("SKU-20260612-000001", result.Code);
        Assert.Equal(2, store.ReserveAttempts);
    }

    [Fact]
    public void Validate_rejects_rule_without_sequence()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "bad",
            DisplayName = "bad",
            Segments = [CodeRuleSegment.ConstantOf("BAD")],
        };

        Assert.Throws<ArgumentException>(() => rule.Validate());
    }

    private sealed class InMemoryCodeStore : ICodeStore
    {
        private readonly Dictionary<string, CodeIdempotencyKey> _idempotency = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public int ConcurrencyFailuresBeforeSuccess { get; init; }
        public int ReserveAttempts { get; private set; }

        public Task<CodeIdempotencyKey?> FindIdempotencyRecordAsync(
            string organizationId,
            string environmentId,
            string ruleKey,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            _idempotency.TryGetValue(Key(organizationId, environmentId, ruleKey, idempotencyKey), out var record);
            return Task.FromResult(record);
        }

        public void AddIdempotencyRecord(CodeIdempotencyKey idempotencyKey)
        {
            _idempotency.Add(
                Key(idempotencyKey.OrganizationId, idempotencyKey.EnvironmentId, idempotencyKey.RuleKey, idempotencyKey.IdempotencyKey),
                idempotencyKey);
        }

        public Task<long> ReserveNextCounterValueAsync(CodeCounterScope scope, CancellationToken cancellationToken)
        {
            ReserveAttempts++;
            if (ReserveAttempts <= ConcurrencyFailuresBeforeSuccess)
            {
                throw new CodeConcurrencyException("simulated");
            }

            var key = Key(scope.OrganizationId, scope.EnvironmentId, scope.RuleKey, scope.SiteCode, scope.ResetKey);
            _counters.TryGetValue(key, out var current);
            var next = current < scope.Start - 1 ? scope.Start : current + 1;
            _counters[key] = next;
            return Task.FromResult(next);
        }

        private static string Key(params string[] parts) => string.Join('|', parts.Select(part => part.Trim().ToLowerInvariant()));
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }
}

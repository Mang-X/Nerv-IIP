using System.Globalization;
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
    public async Task TryPeekReplayAsync_returns_existing_allocation_without_allocating_again()
    {
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, null, "idem-peek", "payload-a", "sku");

        Assert.Null(await allocator.TryPeekReplayAsync(request, CancellationToken.None));
        var first = await allocator.AllocateAsync(request, CancellationToken.None);
        var replay = await allocator.TryPeekReplayAsync(request, CancellationToken.None);

        Assert.NotNull(replay);
        Assert.Equal(first.Code, replay.Code);
        Assert.True(replay.IsIdempotentReplay);
    }

    [Fact]
    public async Task TryPeekReplayAsync_rejects_conflicting_payload_with_allocator_semantics()
    {
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, null, "idem-peek-conflict", "payload-a", "sku");
        await allocator.AllocateAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() => allocator.TryPeekReplayAsync(
            request with { PayloadFingerprint = "payload-b" },
            CancellationToken.None));
    }

    [Fact]
    public async Task AllocateAsync_replays_concurrent_in_memory_idempotency_requests()
    {
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, null, "idem-concurrent", "payload-a", "sku");
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(1, 20)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                return await allocator.AllocateAsync(request, CancellationToken.None);
            }))
            .ToArray();

        start.Set();
        var allocations = await Task.WhenAll(tasks);

        Assert.Single(allocations.Select(x => x.Code).Distinct(StringComparer.Ordinal));
        Assert.Equal(19, allocations.Count(x => x.IsIdempotentReplay));
    }

    [Fact]
    public async Task AllocateAsync_supports_explicit_hash_mod_checksum_algorithms()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "hash-check",
            DisplayName = "Hash check",
            Segments =
            [
                CodeRuleSegment.ConstantOf("HC"),
                CodeRuleSegment.SequenceOf(width: 2),
                CodeRuleSegment.ChecksumOf("hash-mod10"),
            ],
        };
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));

        var result = await allocator.AllocateAsync(new CodeAllocationRequest("org", "env", rule, null, null, null, "payload", "hash-check"), CancellationToken.None);

        Assert.Matches("^HC[0-9]{3}$", result.Code);
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

    [Fact]
    public void Validate_rejects_rule_with_multiple_sequence_segments()
    {
        var rule = new CodeRuleDefinition
        {
            RuleKey = "bad",
            DisplayName = "bad",
            Segments =
            [
                CodeRuleSegment.ConstantOf("BAD"),
                CodeRuleSegment.SequenceOf(width: 2),
                CodeRuleSegment.SequenceOf(width: 2),
            ],
        };

        var exception = Assert.Throws<ArgumentException>(() => rule.Validate());
        Assert.Contains("exactly one sequence", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// GitHub #3454 的回归反例。改前 <c>CodeAllocator</c> 对 <c>RequestedCode</c> 只 <c>Trim()</c>、
    /// 之后原样采用，超过 <see cref="CodeIdempotencyKey.CodeMaxLength"/> 的码会一路走到
    /// <c>SaveChangesAsync</c> 才撞 PostgreSQL <c>22001</c>，用户看到的是 500。
    ///
    /// <para>⭐ <b>本条断言的不只是「抛了异常」，还断言 store 完全没被碰过</b>
    /// （<c>ReserveAttempts</c> 与 <c>AddAttempts</c> 都是 0）。
    /// 「最终抛了个业务异常」在改前的世界里也可能成立（<c>DbUpdateException</c> 被上层包装），
    /// 真正区分新旧实现的是**在触库之前就拒掉**。</para>
    ///
    /// <para>合同分类（<c>docs/governance/testing/validity.md</c>）：<c>Regression</c>（权威来源 = #3454 验收条件）
    /// + <c>DomainInvariant</c>（分配器不得产出自己存不下的码）。</para>
    /// </summary>
    [Fact]
    public async Task AllocateAsync_rejects_requested_code_wider_than_its_own_column_before_touching_the_store()
    {
        var store = new InMemoryCodeStore();
        var allocator = new CodeAllocator(store, new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var oversized = new string('X', CodeIdempotencyKey.CodeMaxLength + 1);
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, oversized, "idem-oversized", "payload", "sku");

        var exception = await Assert.ThrowsAsync<KnownException>(() => allocator.AllocateAsync(request, CancellationToken.None));

        Assert.Contains((CodeIdempotencyKey.CodeMaxLength + 1).ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains(CodeIdempotencyKey.CodeMaxLength.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(oversized, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.ReserveAttempts);
        Assert.Equal(0, store.AddAttempts);
    }

    /// <summary>
    /// 边界另一侧：恰好等于列宽的码必须原样通过并被登记。
    /// ⛔ 没有这一条，把守卫写成 <c>&gt;=</c>（或干脆拒掉一切 <c>RequestedCode</c>）也会全绿。
    /// </summary>
    [Fact]
    public async Task AllocateAsync_accepts_requested_code_exactly_at_its_own_column_width()
    {
        var store = new InMemoryCodeStore();
        var allocator = new CodeAllocator(store, new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var atBound = new string('X', CodeIdempotencyKey.CodeMaxLength);
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, atBound, "idem-at-bound", "payload", "sku");

        var allocation = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal(atBound, allocation.Code);
        Assert.False(allocation.IsIdempotentReplay);
        Assert.Equal(1, store.AddAttempts);
        Assert.Equal(0, store.ReserveAttempts);
    }

    /// <summary>
    /// ⭐ <b>守卫量的是哪个数</b>：落库的是 <c>Trim()</c> 之后的值，所以长度也必须量在 <c>Trim()</c> 之后。
    /// 本条的原始输入长度 = 列宽 + 12（两侧各 6 个空白），trim 后恰好等于列宽。
    /// 把守卫挪到 <c>Normalize</c> 之前（量原始串）会让这条红，而只看「有没有校验」的用例看不出差别。
    /// </summary>
    [Fact]
    public async Task AllocateAsync_measures_the_trimmed_requested_code_not_the_raw_one()
    {
        var store = new InMemoryCodeStore();
        var allocator = new CodeAllocator(store, new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var atBound = new string('X', CodeIdempotencyKey.CodeMaxLength);
        var padded = $"      {atBound}      ";
        Assert.Equal(CodeIdempotencyKey.CodeMaxLength + 12, padded.Length);
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, padded, "idem-padded", "payload", "sku");

        var allocation = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal(atBound, allocation.Code);
        Assert.Equal(1, store.AddAttempts);
    }

    /// <summary>
    /// 无 store 的内存分支（<c>CodeAllocator</c> 的 <c>AllocateInMemory</c> 路径）同样拒收。
    /// 这条钉的是「守卫放在两条分支的**共同上游**」——放进 store 分支里会让本条红。
    /// </summary>
    [Fact]
    public async Task AllocateAsync_rejects_requested_code_wider_than_its_own_column_on_the_in_memory_path()
    {
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var oversized = new string('X', CodeIdempotencyKey.CodeMaxLength + 1);
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, oversized, IdempotencyKey: null, "payload", "sku");

        var exception = await Assert.ThrowsAsync<KnownException>(() => allocator.AllocateAsync(request, CancellationToken.None));

        Assert.Contains(CodeIdempotencyKey.CodeMaxLength.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// CONTROL：不带 <c>RequestedCode</c> 的正常分配路径不受守卫影响，连一次长度判断都不该改变它的产出。
    /// ⛔ 没有这一条，「把 <c>RequestedCode</c> 一律拒掉」这种过宽实现也会让上面几条全绿。
    /// </summary>
    [Fact]
    public async Task AllocateAsync_leaves_generated_codes_untouched_when_no_code_is_requested()
    {
        var store = new InMemoryCodeStore();
        var allocator = new CodeAllocator(store, new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest("org", "env", SkuRule(), null, RequestedCode: null, "idem-generated", "payload", "sku");

        var allocation = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("SKU-20260612-000001", allocation.Code);
        Assert.Equal(1, store.ReserveAttempts);
        Assert.Equal(1, store.AddAttempts);
    }

    /// <summary>
    /// ⭐ <b>钉的是射程边界，不是「这样对」</b>：#3454 只收 <c>RequestedCode</c> 这条口子，
    /// 按规则**生成**出来的码即使越过列宽也照旧放行（规则段配得够宽就能造出来）。
    /// 把守卫扩到生成侧是另一件事；真去做的时候应当**改这条用例**，而不是绕开它——
    /// 本条的作用是让那次扩张在 diff 里显形，而不是静默改变一条没人声明过的行为。
    /// </summary>
    [Fact]
    public async Task AllocateAsync_does_not_guard_generated_codes_which_stays_out_of_scope()
    {
        var wideRule = new CodeRuleDefinition
        {
            RuleKey = "wide",
            DisplayName = "宽段",
            Segments =
            [
                CodeRuleSegment.FieldOf("wide"),
                CodeRuleSegment.SequenceOf(width: 3),
            ],
        };
        var allocator = new CodeAllocator(timeProvider: new FrozenTimeProvider(new DateTimeOffset(2026, 6, 12, 1, 0, 0, TimeSpan.Zero)));
        var request = new CodeAllocationRequest(
            "org",
            "env",
            wideRule,
            new Dictionary<string, string> { ["wide"] = new string('W', CodeIdempotencyKey.CodeMaxLength) },
            RequestedCode: null,
            IdempotencyKey: null,
            "payload",
            "wide");

        var allocation = await allocator.AllocateAsync(request, CancellationToken.None);

        Assert.True(allocation.Code.Length > CodeIdempotencyKey.CodeMaxLength);
    }

    private sealed class InMemoryCodeStore : ICodeStore
    {
        private readonly Dictionary<string, CodeIdempotencyKey> _idempotency = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public int ConcurrencyFailuresBeforeSuccess { get; init; }
        public int ReserveAttempts { get; private set; }

        /// <summary>写入尝试次数。#3454 用它证明越界的 <c>RequestedCode</c> **根本没走到 store**，
        /// 而不只是「最后抛了个异常」——改前的缺陷正是「走到了 store、崩在 SaveChanges」。</summary>
        public int AddAttempts { get; private set; }

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
            AddAttempts++;
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

using Nerv.IIP.Numbering;

namespace Nerv.IIP.Numbering.Tests;

public sealed class NumberingServiceCoreTests
{
    [Fact]
    public async Task AllocateAsync_GeneratesDateSegmentedNumberAndReplaysIdempotencyKey()
    {
        var store = new InMemoryNumberingStore();
        var service = new NumberingServiceCore(store, new FrozenTimeProvider(new DateTimeOffset(2026, 5, 29, 1, 2, 3, TimeSpan.Zero)));
        var request = new NumberingAllocationRequest(
            "org-001",
            "env-dev",
            "work-order",
            "WO",
            RequestedNumber: null,
            IdempotencyKey: "idem-001",
            PayloadFingerprint: "payload-a",
            ConflictResourceLabel: "MES");

        var first = await service.AllocateAsync(request, CancellationToken.None);
        var replay = await service.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("WO-20260529-000001", first.Number);
        Assert.False(first.IsIdempotentReplay);
        Assert.Equal(first.Number, replay.Number);
        Assert.True(replay.IsIdempotentReplay);
    }

    [Fact]
    public async Task AllocateAsync_RetriesCounterReservationAfterConcurrencyFailure()
    {
        var store = new InMemoryNumberingStore { ConcurrencyFailuresBeforeSuccess = 1 };
        var service = new NumberingServiceCore(store, new FrozenTimeProvider(new DateTimeOffset(2026, 5, 29, 1, 2, 3, TimeSpan.Zero)));

        var allocation = await service.AllocateAsync(
            new NumberingAllocationRequest("org-001", "env-dev", "sku", "SKU", null, null, "payload-a", "sku"),
            CancellationToken.None);

        Assert.Equal("SKU-20260529-000001", allocation.Number);
        Assert.Equal(2, store.ReservationAttempts);
    }

    [Fact]
    public async Task AllocateAsync_TreatsMaxConcurrencyAttemptsAsTotalAttempts()
    {
        var store = new InMemoryNumberingStore { ConcurrencyFailuresBeforeSuccess = 4 };
        var service = new NumberingServiceCore(store, new FrozenTimeProvider(new DateTimeOffset(2026, 5, 29, 1, 2, 3, TimeSpan.Zero)));

        var allocation = await service.AllocateAsync(
            new NumberingAllocationRequest("org-001", "env-dev", "sku", "SKU", null, null, "payload-a", "sku"),
            CancellationToken.None);

        Assert.Equal("SKU-20260529-000001", allocation.Number);
        Assert.Equal(5, store.ReservationAttempts);
    }

    [Fact]
    public async Task AllocateAsync_UsesInstanceStateOnlyForInMemoryCounters()
    {
        var timeProvider = new FrozenTimeProvider(new DateTimeOffset(2026, 5, 29, 1, 2, 3, TimeSpan.Zero));
        var firstService = new NumberingServiceCore(store: null, timeProvider);
        var secondService = new NumberingServiceCore(store: null, timeProvider);
        var request = new NumberingAllocationRequest("org-001", "env-dev", "demand", "DEMAND", null, null, "payload-a", "demand");

        var first = await firstService.AllocateAsync(request, CancellationToken.None);
        var second = await secondService.AllocateAsync(request, CancellationToken.None);

        Assert.Equal("DEMAND-20260529-000001", first.Number);
        Assert.Equal("DEMAND-20260529-000001", second.Number);
    }

    [Fact]
    public async Task AllocateAsync_SerializesInMemoryIdempotencyAllocation()
    {
        var timeProvider = new FrozenTimeProvider(new DateTimeOffset(2026, 5, 29, 1, 2, 3, TimeSpan.Zero));
        var service = new NumberingServiceCore(store: null, timeProvider);
        var request = new NumberingAllocationRequest("org-001", "env-dev", "demand", "DEMAND", null, "idem-001", "payload-a", "demand");

        var allocations = await Task.WhenAll(Enumerable.Range(1, 20).Select(_ =>
            Task.Run(() => service.AllocateAsync(request, CancellationToken.None))));

        Assert.Single(allocations.Select(x => x.Number).Distinct(StringComparer.Ordinal));
        Assert.Equal("DEMAND-20260529-000001", allocations[0].Number);
        Assert.Equal(19, allocations.Count(x => x.IsIdempotentReplay));
    }

    private sealed class InMemoryNumberingStore : INumberingStore
    {
        private readonly Dictionary<string, NumberingIdempotencyKey> _idempotency = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public int ConcurrencyFailuresBeforeSuccess { get; init; }
        public int ReservationAttempts { get; private set; }

        public Task<NumberingIdempotencyKey?> FindIdempotencyRecordAsync(
            string organizationId,
            string environmentId,
            string documentType,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            _idempotency.TryGetValue(Key(organizationId, environmentId, documentType, idempotencyKey), out var record);
            return Task.FromResult<NumberingIdempotencyKey?>(record);
        }

        public void AddIdempotencyRecord(NumberingIdempotencyKey idempotencyKey)
        {
            _idempotency.Add(
                Key(idempotencyKey.OrganizationId, idempotencyKey.EnvironmentId, idempotencyKey.DocumentType, idempotencyKey.IdempotencyKey),
                idempotencyKey);
        }

        public Task<long> ReserveNextCounterValueAsync(NumberingCounterScope scope, CancellationToken cancellationToken)
        {
            ReservationAttempts++;
            if (ReservationAttempts <= ConcurrencyFailuresBeforeSuccess)
            {
                throw new NumberingConcurrencyException("Simulated counter write conflict.");
            }

            var key = Key(scope.OrganizationId, scope.EnvironmentId, scope.DocumentType, scope.SiteCode, scope.DateSegment);
            _counters.TryGetValue(key, out var current);
            var next = current + 1;
            _counters[key] = next;
            return Task.FromResult(next);
        }

        private static string Key(params string[] parts)
        {
            return string.Join('|', parts.Select(part => part.Trim().ToLowerInvariant()));
        }
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

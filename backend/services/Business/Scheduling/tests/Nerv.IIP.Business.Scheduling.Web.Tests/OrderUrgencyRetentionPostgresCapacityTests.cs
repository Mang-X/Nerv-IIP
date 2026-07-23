using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.FileStorage;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class OrderUrgencyRetentionPostgresCapacityTests
{
    private const int OrderCount = 5_001;
    private const int BatchSize = 1_000;
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [SchedulingPostgresFact]
    public async Task Representative_capacity_scan_and_overlapping_workers_are_safe_on_PostgreSQL()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_scheduling_retention_capacity");
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            for (var index = 0; index < OrderCount; index++)
            {
                var orderId = $"WO-{index:D6}";
                setup.OrderUrgencySnapshots.Add(CreateSnapshot(orderId, Now.AddDays(-200).AddSeconds(-index), 1));
                setup.OrderUrgencySnapshots.Add(CreateSnapshot(orderId, Now.AddDays(-1).AddSeconds(-index), 2));
            }
            await setup.SaveChangesAsync();
        }

        var archive = new BlockingArchiveStore();
        var policy = new OrderUrgencyRetentionScope(
            "org-capacity",
            "prod",
            TimeSpan.FromDays(180),
            TimeSpan.FromDays(1_095),
            BatchSize,
            false,
            new OrderUrgencyDeletionAuthorization(
                "CAPACITY-VALIDATION",
                "test:retention-capacity",
                "Representative PostgreSQL retention validation",
                Now.AddHours(-1),
                Now.AddHours(1)),
            null);

        var stopwatch = Stopwatch.StartNew();
        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        var firstService = new OrderUrgencyRetentionService(firstContext, archive, new FixedTimeProvider(Now), "worker-a");
        var secondService = new OrderUrgencyRetentionService(secondContext, archive, new FixedTimeProvider(Now), "worker-b");
        var firstRun = firstService.RunScopeAsync(policy, CancellationToken.None);
        var secondRun = secondService.RunScopeAsync(policy, CancellationToken.None);
        await archive.PutStarted.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var nonOwnerTask = await Task.WhenAny(firstRun, secondRun).WaitAsync(TimeSpan.FromSeconds(60));
        var nonOwnerRun = await nonOwnerTask;
        Assert.False(nonOwnerRun.LeaseAcquired);
        archive.ReleasePut.TrySetResult();
        var runs = await Task.WhenAll(firstRun, secondRun);
        var completedRun = Assert.Single(runs, x => x.LeaseAcquired);
        stopwatch.Stop();

        await using var assertion = CreateContext(database.ConnectionString);
        var remaining = await assertion.OrderUrgencySnapshots.CountAsync();
        var latestRemaining = await assertion.OrderUrgencySnapshots.CountAsync(x => x.BusinessPriorityRevision == 2);
        var batches = await assertion.OrderUrgencyArchiveBatches.ToArrayAsync();

        Assert.False(nonOwnerRun.LeaseAcquired);
        Assert.Equal(OrderCount, completedRun.EligibleSnapshots);
        Assert.Equal(BatchSize, completedRun.ArchivedSnapshots);
        Assert.Equal(BatchSize, completedRun.SourceDeletedSnapshots);
        Assert.Equal((OrderCount * 2) - BatchSize, remaining);
        Assert.Equal(OrderCount, latestRemaining);
        Assert.Single(batches);
        Assert.Equal("source-deleted", batches[0].Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(2));

        await WriteEvidenceIfRequestedAsync(new
        {
            schemaVersion = "order-urgency-retention-capacity-v1",
            provider = "PostgreSQL",
            seededSnapshots = OrderCount * 2,
            eligibleSnapshots = completedRun.EligibleSnapshots,
            archivedSnapshots = completedRun.ArchivedSnapshots,
            sourceDeletedSnapshots = completedRun.SourceDeletedSnapshots,
            latestSnapshotsRemaining = latestRemaining,
            remainingSnapshots = remaining,
            overlappingWorkerLeaseAcquired = nonOwnerRun.LeaseAcquired,
            archiveBatchCount = batches.Length,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        });
    }

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "scheduling"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static OrderUrgencySnapshot CreateSnapshot(string orderId, DateTimeOffset calculatedAtUtc, long revision) =>
        new(
            "org-capacity",
            "prod",
            orderId,
            $"SO-{orderId}",
            OrderUrgencyLevel.Normal,
            "order-urgency-v1",
            $"fingerprint-{orderId}-{revision}",
            revision,
            calculatedAtUtc,
            calculatedAtUtc,
            "{}");

    private static async Task WriteEvidenceIfRequestedAsync(object evidence)
    {
        var path = Environment.GetEnvironmentVariable("NERV_IIP_URGENCY_RETENTION_EVIDENCE");
        if (string.IsNullOrWhiteSpace(path)) return;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class BlockingArchiveStore : IOrderUrgencyArchiveStore
    {
        private readonly Dictionary<string, byte[]> content = new(StringComparer.Ordinal);
        public TaskCompletionSource PutStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<VersionedArchiveEvidence> PutAsync(
            PutVersionedArchiveRequest request,
            CancellationToken cancellationToken)
        {
            PutStarted.TrySetResult();
            await ReleasePut.Task.WaitAsync(cancellationToken);
            var bytes = Convert.FromBase64String(request.ContentBase64);
            var key = $"compliance-archives/{request.OrganizationId}/{request.EnvironmentId}/{request.ArchiveKind}/{request.BatchId}.json";
            content[key] = bytes;
            return new VersionedArchiveEvidence(key, "capacity-version-1", request.Sha256, bytes.LongLength, Now);
        }

        public Task<GetVersionedArchiveResponse> GetAsync(
            GetVersionedArchiveRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GetVersionedArchiveResponse(
                new VersionedArchiveEvidence(request.ObjectKey, request.VersionId, request.Sha256, request.SizeBytes, Now),
                Convert.ToBase64String(content[request.ObjectKey])));

        public Task DeleteAsync(DeleteVersionedArchiveRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}

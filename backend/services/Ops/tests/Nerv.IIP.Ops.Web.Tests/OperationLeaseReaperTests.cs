using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Ops.Infrastructure.Repositories;
using Nerv.IIP.Ops.Web.Application.Commands;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OperationLeaseReaperTests
{
    [Fact]
    public async Task Lease_reaper_requeues_expired_dispatched_task_without_new_claim_request()
    {
        await using var fixture = await OpsSqliteFixture.CreateAsync();
        var service = new EfOperationTaskApplicationService(
            new OperationTaskRepository(fixture.Db),
            new OperationTemplateRepository(fixture.Db),
            fixture.Db);
        var created = await service.CreateAsync(
            CreateRestartRequest("idem-reaper"),
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            CancellationToken.None);
        var task = await fixture.Db.OperationTasks
            .Include(x => x.Attempts)
            .Include(x => x.AuditRecords)
            .SingleAsync(x => x.Id == new OperationTaskId(created.OperationTaskId));
        task.Claim(
            new OperationAttemptId("attempt-reaper"),
            new AuditRecordId("audit-reaper-claim"),
            "lease-reaper",
            "connector-host-001",
            DateTimeOffset.Parse("2026-06-29T00:00:01Z"),
            TimeSpan.FromSeconds(30),
            maxAttempts: 3);
        await fixture.Db.SaveChangesAsync();

        var reaper = new EfOperationLeaseReaper(
            new OperationTaskRepository(fixture.Db),
            fixture.Db);

        var result = await reaper.ReapExpiredLeasesAsync(
            "org-001",
            "env-dev",
            DateTimeOffset.Parse("2026-06-29T00:00:32Z"),
            take: 10,
            CancellationToken.None);

        Assert.Equal(1, result.RequeuedCount);
        var detail = await service.GetAsync(created.OperationTaskId, CancellationToken.None);
        Assert.Equal("queued", detail.Status);
        Assert.Contains(detail.Attempts, attempt => attempt.Status == "abandoned" && attempt.AbandonReason == "lease-timeout");
        Assert.Contains(detail.AuditRecords, audit => audit.Action == "operation.lease-timeout");
    }

    [Fact]
    public async Task Concurrent_duplicate_create_returns_persisted_operation_task()
    {
        await using var fixture = await OpsSqliteFixture.CreateAsync();
        var first = new EfOperationTaskApplicationService(
            new OperationTaskRepository(fixture.Db),
            new OperationTemplateRepository(fixture.Db),
            fixture.Db);
        var created = await first.CreateAsync(
            CreateRestartRequest("idem-race"),
            DateTimeOffset.Parse("2026-06-29T00:00:00Z"),
            CancellationToken.None);

        await using var secondDb = fixture.CreateContext();
        var second = new EfOperationTaskApplicationService(
            new OperationTaskRepository(secondDb),
            new OperationTemplateRepository(secondDb),
            secondDb);
        var duplicate = await second.CreateAsync(
            CreateRestartRequest("idem-race") with { RequestedBy = "other-user" },
            DateTimeOffset.Parse("2026-06-29T00:00:01Z"),
            CancellationToken.None);

        Assert.Equal(created.OperationTaskId, duplicate.OperationTaskId);
        Assert.Equal("local-admin", duplicate.RequestedBy);
        Assert.Equal(1, await fixture.Db.OperationTasks.CountAsync());
    }

    private static CreateOperationTaskRequest CreateRestartRequest(string idempotencyKey)
    {
        return new CreateOperationTaskRequest(
            "org-001",
            "env-dev",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            idempotencyKey,
            "local-admin",
            "manual smoke restart",
            $"corr-{idempotencyKey}",
            new Dictionary<string, string>());
    }

    private sealed class OpsSqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ApplicationDbContext> _options;

        private OpsSqliteFixture(SqliteConnection connection, DbContextOptions<ApplicationDbContext> options)
        {
            _connection = connection;
            _options = options;
            Db = CreateContext();
        }

        public ApplicationDbContext Db { get; }

        public static async Task<OpsSqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var fixture = new OpsSqliteFixture(connection, options);
            await fixture.Db.Database.EnsureCreatedAsync();
            return fixture;
        }

        public ApplicationDbContext CreateContext() => new(_options, mediator: null!);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

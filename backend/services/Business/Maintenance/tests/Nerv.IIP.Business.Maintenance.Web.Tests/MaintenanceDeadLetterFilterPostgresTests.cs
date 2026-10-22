using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3739 的过滤谓词在 Maintenance **自有** store 上的真 PostgreSQL 证据。
///
/// Maintenance 没有走共享的 <c>PersistentIntegrationEventDeadLetterStore</c>，而是复制了一套
/// （<see cref="MaintenanceIntegrationEventDeadLetterStore"/>）。「照抄了 canonical」在这里是**待核主张**
/// 而不是证据——两边的 <c>eventType</c> 谓词今天就已经不同形（#3758）。因此新增的失败码与时间窗
/// 谓词必须在这一份实现上单独钉住，不能靠共享实现那侧的用例传递过来。
///
/// 默认 skip；设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。
/// </summary>
public sealed class MaintenanceDeadLetterFilterPostgresTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    private static readonly DateTimeOffset WindowStart = new(2026, 9, 22, 8, 0, 1, TimeSpan.Zero);

    [MaintenanceDeadLetterPostgresFact]
    public async Task Failure_code_and_dead_lettered_window_are_filtered_by_the_maintenance_store()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!,
            "nerv_iip_maintenance_dlq");
        await using var context = await CreateContextAsync(database);
        var store = new MaintenanceIntegrationEventDeadLetterStore(context);

        var before = await AddAsync(store, "handler-retry-exhausted", WindowStart.AddMinutes(-1));
        var atWindowStart = await AddAsync(store, "handler-retry-exhausted", WindowStart);
        var atWindowEnd = await AddAsync(store, "missing-work-center-cost-rate", WindowStart.AddMinutes(1));
        var after = await AddAsync(store, "handler-retry-exhausted", WindowStart.AddMinutes(2));

        Assert.Equal(
            [before.Id, atWindowStart.Id, after.Id],
            await IdsAsync(store, new IntegrationEventDeadLetterQuery(null, null, null, FailureCode: "handler-retry-exhausted")));
        Assert.Empty(
            await IdsAsync(store, new IntegrationEventDeadLetterQuery(null, null, null, FailureCode: "never-emitted")));

        // 时间窗是**闭区间**：两个端点行都要在内。删成开区间（`>=`→`>`）会红。
        Assert.Equal(
            [atWindowStart.Id, atWindowEnd.Id],
            await IdsAsync(
                store,
                new IntegrationEventDeadLetterQuery(
                    null,
                    null,
                    null,
                    DeadLetteredFromUtc: atWindowStart.DeadLetteredAtUtc,
                    DeadLetteredToUtc: atWindowEnd.DeadLetteredAtUtc)));

        Assert.Equal(
            [atWindowStart.Id],
            await IdsAsync(
                store,
                new IntegrationEventDeadLetterQuery(
                    null,
                    null,
                    null,
                    FailureCode: "handler-retry-exhausted",
                    DeadLetteredFromUtc: atWindowStart.DeadLetteredAtUtc,
                    DeadLetteredToUtc: atWindowEnd.DeadLetteredAtUtc)));
    }

    private static async Task<IReadOnlyList<Guid>> IdsAsync(
        IIntegrationEventDeadLetterStore store,
        IntegrationEventDeadLetterQuery query) =>
        (await store.ListAsync(query, CancellationToken.None)).Select(message => message.Id).ToArray();

    private static async Task<IntegrationEventDeadLetterMessage> AddAsync(
        IIntegrationEventDeadLetterStore store,
        string failureCode,
        DateTimeOffset deadLetteredAtUtc)
    {
        var message = new IntegrationEventDeadLetterMessage(
            Guid.CreateVersion7(),
            "maintenance.asset-unavailable",
            $"event-{Guid.CreateVersion7():N}",
            "AssetUnavailable",
            2,
            "business-maintenance",
            $"idem-{Guid.CreateVersion7():N}",
            "Nerv.IIP.Contracts.Maintenance.AssetUnavailableIntegrationEvent",
            """{"eventType":"maintenance.AssetUnavailable"}""",
            failureCode,
            "下游暂时不可用",
            IntegrationEventDeadLetterStatus.Pending,
            deadLetteredAtUtc,
            null);
        return await store.AddAsync(message, CancellationToken.None);
    }

    private static async Task<ApplicationDbContext> CreateContextAsync(PostgreSqlTestDatabase database)
    {
        var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(
                    database.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MaintenanceFacts.Schema))
                .Options,
            new NoopMediator());
        await context.Database.MigrateAsync();
        return context;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse>(default!);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<object?>();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;
    }

    private sealed class MaintenanceDeadLetterPostgresFactAttribute : FactAttribute
    {
        public MaintenanceDeadLetterPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run the real PostgreSQL Maintenance dead-letter filter tests.";
            }
        }
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

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

    private static readonly DateTimeOffset Midpoint =
        new DateTimeOffset(2026, 9, 22, 8, 0, 1, TimeSpan.Zero).AddTicks(5_000_000);

    [MaintenanceDeadLetterPostgresFact]
    public async Task Failure_code_and_dead_lettered_window_are_filtered_by_the_maintenance_store()
    {
        await using var context = await CreateContextAsync();
        var store = new MaintenanceIntegrationEventDeadLetterStore(context);

        var before = await AddAsync(store, "handler-retry-exhausted", Midpoint.AddMinutes(-1));
        var atWindowStart = await AddAsync(store, "handler-retry-exhausted", Midpoint);
        var atWindowEnd = await AddAsync(store, "missing-work-center-cost-rate", Midpoint.AddMinutes(1));
        var after = await AddAsync(store, "handler-retry-exhausted", Midpoint.AddMinutes(2));

        Assert.Equal(
            [before.Id, atWindowStart.Id, after.Id],
            await IdsAsync(store, new IntegrationEventDeadLetterQuery(null, null, null, FailureCode: "handler-retry-exhausted")));
        Assert.Empty(
            await IdsAsync(store, new IntegrationEventDeadLetterQuery(null, null, null, FailureCode: "never-emitted")));

        // 闭区间：两个端点行都在内。
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

    /// <inheritdoc cref="Failure_code_and_dead_lettered_window_are_filtered_by_the_maintenance_store"/>
    [MaintenanceDeadLetterPostgresFact]
    public async Task A_stored_row_is_found_by_its_own_round_tripped_timestamp_as_both_bounds()
    {
        await using var context = await CreateContextAsync();
        var store = new MaintenanceIntegrationEventDeadLetterStore(context);
        await AddAsync(store, "handler-retry-exhausted", Midpoint);

        var stored = Assert.Single(await store.ListAsync(new IntegrationEventDeadLetterQuery(null, null, null), CancellationToken.None));

        Assert.Equal(
            [stored.Id],
            await IdsAsync(
                store,
                new IntegrationEventDeadLetterQuery(
                    null,
                    null,
                    null,
                    DeadLetteredFromUtc: stored.DeadLetteredAtUtc,
                    DeadLetteredToUtc: stored.DeadLetteredAtUtc)));
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

    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable))
        {
            Database = $"maint_dlq_filter_{Guid.CreateVersion7():N}",
        };
        await CreateDatabaseAsync(builder);

        var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(
                    builder.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MaintenanceFacts.Schema))
                .Options,
            new NoopMediator());
        await context.Database.MigrateAsync();
        return context;
    }

    private static async Task CreateDatabaseAsync(NpgsqlConnectionStringBuilder target)
    {
        var admin = new NpgsqlConnectionStringBuilder(target.ConnectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(target.Database!)}";
        await command.ExecuteNonQueryAsync();
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

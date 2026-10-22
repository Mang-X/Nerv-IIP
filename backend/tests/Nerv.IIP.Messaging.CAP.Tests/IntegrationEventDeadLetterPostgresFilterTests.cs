using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Testing.PostgreSql;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3739 的过滤谓词在**真 PostgreSQL** 上的证据。
///
/// 为什么这条必须落在真 provider 上：`failureCode` / `deadLetteredFromUtc` / `deadLetteredToUtc`
/// 三段谓词由 EF 翻译成 SQL、在数据库里执行，而 <see cref="IntegrationEventDeadLetterQuery"/> 的
/// InMemory 实现是进程内 LINQ。两者是**各自独立的一份实现**：把
/// <see cref="PersistentIntegrationEventDeadLetterStore{TDbContext}"/> 的三段过滤整块删掉，
/// InMemory 侧的断言全绿（#3739 审核实测），所以那侧的绿证不到这侧。
///
/// facade 覆盖的 10 个服务里有 9 个走这份共享实现（此外 Notification 也注册它，只是不在 facade
/// 的来源表里）——Maintenance 是例外，它注册的是自己的副本
/// （`Maintenance.Web/Program.cs`），由 `MaintenanceDeadLetterFilterPostgresTests` 单独钉住。
///
/// 默认 skip；设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与既有 *PostgresProfileTests 同一 env gate）。
/// </summary>
public sealed class IntegrationEventDeadLetterPostgresFilterTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    private static readonly DateTimeOffset WindowStart = new(2026, 9, 22, 8, 0, 1, TimeSpan.Zero);

    [DeadLetterPostgresFact]
    public async Task Failure_code_and_dead_lettered_window_are_filtered_by_postgres()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!,
            "nerv_iip_dlq_filter");
        await using var context = new DeadLetterPostgresDbContext(
            new DbContextOptionsBuilder<DeadLetterPostgresDbContext>()
                .UseNpgsql(database.ConnectionString)
                .Options);
        await context.Database.EnsureCreatedAsync();
        var store = new PersistentIntegrationEventDeadLetterStore<DeadLetterPostgresDbContext>(context);

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

        // 单边下界同样闭区间，且把更早的行排除掉。
        Assert.Equal(
            [atWindowStart.Id, atWindowEnd.Id, after.Id],
            await IdsAsync(
                store,
                new IntegrationEventDeadLetterQuery(null, null, null, DeadLetteredFromUtc: atWindowStart.DeadLetteredAtUtc)));

        // 三段谓词叠加，只剩一行。
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
            "sample.consumer",
            $"event-{Guid.CreateVersion7():N}",
            "SampleEvent",
            2,
            "business-sample",
            $"idem-{Guid.CreateVersion7():N}",
            "Nerv.IIP.Contracts.Sample.SampleIntegrationEvent",
            """{"eventType":"Sample"}""",
            failureCode,
            "下游暂时不可用",
            IntegrationEventDeadLetterStatus.Pending,
            deadLetteredAtUtc,
            null);
        return await store.AddAsync(message, CancellationToken.None);
    }

    private sealed class DeadLetterPostgresDbContext(DbContextOptions<DeadLetterPostgresDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureIntegrationEventDeadLetters();
        }
    }

    private sealed class DeadLetterPostgresFactAttribute : FactAttribute
    {
        public DeadLetterPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run the real PostgreSQL dead-letter filter tests.";
            }
        }
    }
}

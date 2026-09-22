using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Nerv.IIP.Messaging.CAP.Tests;

/// <summary>
/// #3739 的过滤谓词在**真 PostgreSQL** 上的证据。
///
/// 为什么不能用 InMemory/SQLite 答这条：`failureCode` / `deadLetteredFromUtc` / `deadLetteredToUtc`
/// 三段谓词由 EF 翻译成 SQL 在数据库里执行，而生产 10 个服务走的是
/// <see cref="PersistentIntegrationEventDeadLetterStore{TDbContext}"/> + Npgsql。
/// `timestamptz` 只存到**微秒**，而 facade 发出的是 7 位小数（100ns tick），
/// 所以边界行上的 `&lt;=` 在两侧语义不等价——InMemory 上的闭区间断言不传递到这里。
///
/// 默认 skip；设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行（与既有 *PostgresProfileTests 同一 env gate）。
/// </summary>
public sealed class IntegrationEventDeadLetterPostgresFilterTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";

    /// <summary>整微秒，且 100ns 位非零——用来区分「按微秒截断」与「按 tick 比较」。</summary>
    private static readonly DateTimeOffset Midpoint =
        new DateTimeOffset(2026, 9, 22, 8, 0, 1, TimeSpan.Zero).AddTicks(5_000_000);

    [DeadLetterPostgresFact]
    public async Task Failure_code_and_dead_lettered_window_are_filtered_by_postgres_not_in_memory()
    {
        await using var context = await CreateContextAsync();
        var store = new PersistentIntegrationEventDeadLetterStore<DeadLetterPostgresDbContext>(context);

        var before = await AddAsync(store, "handler-retry-exhausted", Midpoint.AddMinutes(-1));
        var atWindowStart = await AddAsync(store, "handler-retry-exhausted", Midpoint);
        var atWindowEnd = await AddAsync(store, "missing-work-center-cost-rate", Midpoint.AddMinutes(1));
        var after = await AddAsync(store, "handler-retry-exhausted", Midpoint.AddMinutes(2));

        // 失败码：精确匹配，且确实由 SQL 过滤（下面的时间窗用例证明不是把全表拉回来再筛）。
        Assert.Equal(
            [before.Id, atWindowStart.Id, after.Id],
            await IdsAsync(store, new IntegrationEventDeadLetterQuery(null, null, null, FailureCode: "handler-retry-exhausted")));
        Assert.Empty(
            await IdsAsync(store, new IntegrationEventDeadLetterQuery(null, null, null, FailureCode: "never-emitted")));

        // 时间窗是**闭区间**：两个端点行都要在内。这正是微秒/tick 精度差会咬人的那两行。
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

        // 单边下界同样是闭区间，且把更早的行排除掉。
        Assert.Equal(
            [atWindowStart.Id, atWindowEnd.Id, after.Id],
            await IdsAsync(
                store,
                new IntegrationEventDeadLetterQuery(null, null, null, DeadLetteredFromUtc: atWindowStart.DeadLetteredAtUtc)));

        // 三段谓词叠加：失败码 + 闭区间上界，只剩一行。
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

    /// <summary>
    /// 存回来的时间戳必须能被自己作为端点查回来。生产链路上 facade 把 `DeadLetteredAtUtc`
    /// 原样回显给页面，页面再拿它当筛选端点；若 `timestamptz` 的微秒截断让这条不成立，
    /// 「按这一行的时间点筛」就会查不到这一行本身。
    /// </summary>
    [DeadLetterPostgresFact]
    public async Task A_stored_row_is_found_by_its_own_round_tripped_timestamp_as_both_bounds()
    {
        await using var context = await CreateContextAsync();
        var store = new PersistentIntegrationEventDeadLetterStore<DeadLetterPostgresDbContext>(context);
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

    private static async Task<DeadLetterPostgresDbContext> CreateContextAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable))
        {
            // 每条用例独占一个库：断言比较的是精确 id 序列，共享库会让并行/重跑互相污染。
            Database = $"dlq_filter_{Guid.CreateVersion7():N}",
        };
        await CreateDatabaseAsync(builder);

        var context = new DeadLetterPostgresDbContext(
            new DbContextOptionsBuilder<DeadLetterPostgresDbContext>()
                .UseNpgsql(builder.ConnectionString)
                .Options);
        await context.Database.EnsureCreatedAsync();
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

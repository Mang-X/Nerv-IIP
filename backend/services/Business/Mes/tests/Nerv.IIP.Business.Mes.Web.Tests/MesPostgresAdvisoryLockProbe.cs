using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// Bounded wait for the real edge behind "the competing operation is still blocked": a backend of the
/// run-scoped database parked on <c>pg_advisory_xact_lock</c>.
/// </summary>
/// <remarks>
/// The MES scope coordinators serialize competing writers with <c>pg_advisory_xact_lock</c>, and
/// <c>pg_stat_activity</c> exposes that wait as an observable fact. Waiting for that edge is strictly
/// stronger than a settle window: the concurrency tests observe the blocked state instead of assuming it
/// happened within some sleep.
/// </remarks>
/// <remarks>
/// <para>
/// NERV-688 拆解③起，<c>MesSchedulePlanProvenancePostgresTests</c>、<c>SkuDisabledConsumerTests</c> 与
/// <c>WorkOrderCapitalizationConcurrencyPostgresTests</c> 不再各自新建内层数据库，而是共用
/// <see cref="MesPostgresLaneDatabase"/> 治理的成员数据库，因此"该数据库内任意一个 advisory-lock
/// waiter"这条判据不再依赖"数据库是每测试独占的"这条前提。它依赖的是另一条更弱但仍然成立的前提：
/// 这三个类共同加入 <see cref="WebApplicationFactoryCollection"/>（<c>DisableParallelization = true</c>），
/// 因此同一时刻整个成员数据库上只可能有一个测试方法在跑，该测试方法自己制造的 advisory-lock
/// 竞争因而是"该数据库内此刻唯一可能的 waiter 来源"。前一个测试遗留的 waiter 不会被误计入：
/// xUnit 的串行化保证前一个测试已经 <c>await</c> 完成、连接已释放（advisory lock 是事务/连接作用域，
/// 连接释放即随之释放）之后，下一个测试才会开始。若这条串行化保证被移除（例如把某个类挪出该
/// collection、或未来允许并行子集），这条判据必须收紧到测试自己申请的那把锁的精确
/// <c>classid</c>/<c>objid</c>，否则"某个 advisory lock 被争用"将不再等价于"这个测试的锁被争用"。
/// </para>
/// </remarks>
internal static class MesPostgresAdvisoryLockProbe
{
    private const string WaiterCountSql = """
        SELECT count(*)
        FROM pg_stat_activity
        WHERE datname = current_database()
          AND pid <> pg_backend_pid()
          AND wait_event_type = 'Lock'
          AND wait_event = 'advisory'
        """;

    /// <remarks>
    /// <para>
    /// Each observation opens and disposes its own <see cref="NpgsqlConnection"/> rather than closing over one
    /// owned by this method. <c>Eventually</c> is a positive assertion, so it runs without a grace budget: an
    /// observation still in flight when the 15s window closes is abandoned outright, and this method then
    /// returns. A shared connection would be disposed by that return while the abandoned observation was still
    /// executing against it — the use-after-window bug the resource invariant on
    /// <c>BoundedObservationWindow</c> describes. Owning the connection per observation makes the abandoned
    /// one self-contained: it cancels on the window token, disposes its own connection back to the Npgsql
    /// pool, and any late fault is consumed by the window driver.
    /// </para>
    /// <para>
    /// The cost is a pool rent/return per poll (50ms interval, 15s budget, so ~300 in the worst case). The
    /// per-test database connection string leaves pooling at its default (only the admin string sets
    /// <c>Pooling=false</c>), so no observation actually performs a TCP handshake after the first.
    /// </para>
    /// </remarks>
    public static async Task WaitForWaitersAsync(
        string connectionString,
        int expectedWaiters,
        string scopeDescription,
        CancellationToken cancellationToken = default)
    {
        await Eventually.WaitAsync(
            condition: $"{expectedWaiters} PostgreSQL advisory-lock waiter(s) for {scopeDescription}",
            observe: async token =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                // Explicit budget for the single hangable operation; the window/caller token propagates
                // unchanged, so only the helper's own expiry is reported as a timeout.
                await TestTimeout.RunAsync(
                    operation: $"open the advisory-lock probe connection for {scopeDescription}",
                    action: async openToken => await connection.OpenAsync(openToken),
                    timeout: TimeSpan.FromSeconds(10),
                    token,
                    sensitiveValues: [connectionString]);
                await using var command = new NpgsqlCommand(WaiterCountSql, connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync(token));
            },
            isSatisfied: waiters => waiters >= expectedWaiters,
            describe: waiters => $"advisoryLockWaiters={waiters}; expected>={expectedWaiters}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(15),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [connectionString]),
            cancellationToken);
    }
}

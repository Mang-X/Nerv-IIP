using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using static Nerv.IIP.Business.Mes.Web.Tests.MesSqliteTestDatabase;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #1323：停机恢复查询必须在真实关系 provider 上可翻译（InMemory 会把
/// x.Id.Id.ToString() 之类不可翻译谓词跑成假绿），因此全部用 SQLite 实跑。
/// #1947：停机读面（列表行投影 + 按原因聚合）改由 <see cref="MesDowntimeReadFacePostgresTests"/>
/// 在真实 PostgreSQL 上证明——按原因聚合把时长差值下推成 <c>date_part('epoch', ...)</c>，
/// SQLite 连同 <see cref="MesSqliteTestDatabase"/> 的 DateTimeOffset→long 值转换器都翻译不了，留在这里只会变成翻译不了的假红。
/// </summary>
public sealed class MesDowntimeRecoveryPersistenceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    [Fact]
    public async Task Recover_by_downtime_event_no_translates_and_closes_event_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var downtime = WorkCenterUnavailability.Open(
            Org, Env, "DT-0001", "WC-01",
            DateTimeOffset.Parse("2026-07-30T01:00:00Z"), null, "equipment-fault", "EQ-001");
        dbContext.WorkCenterUnavailabilities.Add(downtime);
        await dbContext.SaveChangesAsync();

        var handler = new ConfirmDowntimeRecoveryCommandHandler(dbContext);
        var recoveredAt = DateTimeOffset.Parse("2026-07-30T03:00:00Z");
        var response = await handler.Handle(
            new ConfirmDowntimeRecoveryCommand(Org, Env, "DT-0001", recoveredAt),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.Equal("Accepted", response.Status);
        var persisted = await dbContext.WorkCenterUnavailabilities.SingleAsync();
        Assert.Equal(recoveredAt, persisted.ToUtc);
    }

    [Fact]
    public async Task Recover_by_guid_id_translates_and_closes_event_on_relational_provider()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var downtime = WorkCenterUnavailability.Open(
            Org, Env, "DT-0002", "WC-02",
            DateTimeOffset.Parse("2026-07-30T01:00:00Z"), null, "equipment-fault", null);
        dbContext.WorkCenterUnavailabilities.Add(downtime);
        await dbContext.SaveChangesAsync();
        var guidId = downtime.Id.Id.ToString();
        dbContext.ChangeTracker.Clear();

        var handler = new ConfirmDowntimeRecoveryCommandHandler(dbContext);
        var recoveredAt = DateTimeOffset.Parse("2026-07-30T04:00:00Z");
        await handler.Handle(
            new ConfirmDowntimeRecoveryCommand(Org, Env, guidId, recoveredAt),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.WorkCenterUnavailabilities.SingleAsync();
        Assert.Equal(recoveredAt, persisted.ToUtc);
    }

    [Fact]
    public async Task Recover_unknown_downtime_event_throws_known_exception()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var handler = new ConfirmDowntimeRecoveryCommandHandler(dbContext);

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() =>
            handler.Handle(
                new ConfirmDowntimeRecoveryCommand(Org, Env, "DT-MISSING", DateTimeOffset.UtcNow),
                CancellationToken.None));
    }

    [Fact]
    public async Task Open_downtime_blocks_start_and_recovery_releases_the_gate()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
            Org, Env, "DT-0003", "WC-03",
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"), null, "equipment-fault", "EQ-003"));
        await dbContext.SaveChangesAsync();
        var effectiveAt = DateTimeOffset.Parse("2026-07-30T02:00:00Z");

        var blockingBefore = await ReadinessReasonCodes.GetEquipmentBlockingIssuesAsync(
            dbContext, Org, Env, "WC-03", null, effectiveAt, CancellationToken.None);
        Assert.NotEmpty(blockingBefore);

        var handler = new ConfirmDowntimeRecoveryCommandHandler(dbContext);
        await handler.Handle(
            new ConfirmDowntimeRecoveryCommand(Org, Env, "DT-0003", DateTimeOffset.Parse("2026-07-30T01:30:00Z")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var blockingAfter = await ReadinessReasonCodes.GetEquipmentBlockingIssuesAsync(
            dbContext, Org, Env, "WC-03", null, effectiveAt, CancellationToken.None);
        Assert.Empty(blockingAfter);
    }
}

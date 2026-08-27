using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #1323：停机恢复查询必须在真实关系 provider 上可翻译（InMemory 会把
/// x.Id.Id.ToString() 之类不可翻译谓词跑成假绿），因此全部用 SQLite 实跑。
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

    [Fact]
    public async Task Downtime_list_rows_keep_work_center_code_out_of_work_order_field()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
            Org, Env, "DT-0004", "WC-04",
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"), null, "equipment-fault", "EQ-004"));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var handler = new ListDowntimeEventsQueryHandler(dbContext, TimeProvider.System);
        var response = await handler.Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null),
            CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal("DT-0004", row.DowntimeEventId);
        Assert.Null(row.WorkOrderId);
        Assert.Equal("WC-04", row.WorkCenterId);
        Assert.Equal("equipment-fault", row.ReasonCode);
    }

    /// <summary>
    /// #1947：按原因过滤只收窄列表，不收窄原因汇总——汇总要一直列全所有原因，
    /// 否则用户选中一个原因之后就再也看不到别的原因、换不回去。
    /// </summary>
    [Fact]
    public async Task Downtime_list_filters_rows_by_reason_code_while_summary_still_spans_every_reason()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedReasonMix(dbContext);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var handler = new ListDowntimeEventsQueryHandler(dbContext, new FakeTimeProvider(AsOfUtc));
        var response = await handler.Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null, ReasonCode: "material-shortage"),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        Assert.Equal("DT-B1", Assert.Single(response.Items).DowntimeEventId);
        Assert.Equal(
            new[] { "equipment-fault", "material-shortage" },
            response.ReasonSummary.Select(x => x.ReasonCode).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// #1947：停机时长按原因汇总——已恢复事件按恢复时刻结算，未恢复事件按查询时刻仍在累计，
    /// 且按时长降序排（现场先看最费时间的那个原因）。
    /// </summary>
    [Fact]
    public async Task Downtime_reason_summary_sums_recovered_and_ongoing_minutes_and_ranks_by_duration()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        SeedReasonMix(dbContext);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var handler = new ListDowntimeEventsQueryHandler(dbContext, new FakeTimeProvider(AsOfUtc));
        var response = await handler.Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null),
            CancellationToken.None);

        var summary = response.ReasonSummary.ToArray();
        Assert.Equal(2, summary.Length);
        Assert.Equal("equipment-fault", summary[0].ReasonCode);
        Assert.Equal(2, summary[0].EventCount);
        Assert.Equal(1, summary[0].OpenCount);
        // 60 分钟已恢复 + 30 分钟仍在停机（10:00 → AsOfUtc 10:30）。
        Assert.Equal(90m, summary[0].DurationMinutes);
        Assert.Equal("material-shortage", summary[1].ReasonCode);
        Assert.Equal(1, summary[1].EventCount);
        Assert.Equal(0, summary[1].OpenCount);
        Assert.Equal(20m, summary[1].DurationMinutes);
    }

    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-07-30T10:30:00Z");

    private static void SeedReasonMix(ApplicationDbContext dbContext)
    {
        dbContext.WorkCenterUnavailabilities.AddRange(
            WorkCenterUnavailability.Open(
                Org, Env, "DT-A1", "WC-10",
                DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-30T09:00:00Z"),
                "equipment-fault", "EQ-010"),
            WorkCenterUnavailability.Open(
                Org, Env, "DT-A2", "WC-10",
                DateTimeOffset.Parse("2026-07-30T10:00:00Z"),
                null,
                "equipment-fault", "EQ-010"),
            WorkCenterUnavailability.Open(
                Org, Env, "DT-B1", "WC-11",
                DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-30T08:20:00Z"),
                "material-shortage", "EQ-011"));
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetModelCustomizer>()
            .Options;
        return new ApplicationDbContext(options, new NoopRecoveryMediator());
    }

    // SQLite provider 无法翻译 DateTimeOffset 的排序/比较（仓库已知坑：EF 测试 provider 翻译差异），
    // 测试专用 ModelCustomizer 把所有 DateTimeOffset 列统一转成 long（值均为 UTC，ToBinary 排序与时间序一致）。
    private sealed class SqliteDateTimeOffsetModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        private static readonly DateTimeOffsetToBinaryConverter Converter = new();

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(Converter);
                }
            }
        }
    }

    private sealed class NoopRecoveryMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot create streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Noop mediator cannot create streams.");
        }
    }
}

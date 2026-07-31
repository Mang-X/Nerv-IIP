using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

        var handler = new ListDowntimeEventsQueryHandler(dbContext);
        var response = await handler.Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null),
            CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal("DT-0004", row.DowntimeEventId);
        Assert.Null(row.WorkOrderId);
        Assert.Equal("WC-04", row.WorkCenterId);
        Assert.Equal("equipment-fault", row.ReasonCode);
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

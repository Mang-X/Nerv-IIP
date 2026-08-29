using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #1947 停机读面：列表行投影、按原因过滤与按原因聚合都必须在真实 PostgreSQL 上成立。
/// 聚合整条下推数据库（<c>GROUP BY reason</c> + <c>sum(date_part('epoch', COALESCE(to_utc, @asOf) - from_utc)/60)</c>），
/// SQLite 与 InMemory 都证明不了它：前者翻译不了这个表达式，后者一律客户端求值，改坏了照绿。
/// </summary>
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesDowntimeReadFacePostgresTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-07-30T10:30:00Z");

    /// <summary>#48 字段归位：停机事实不挂工单，WorkOrderId 一律为空；工作中心码只放 WorkCenterId。</summary>
    [MesRealPostgresFact]
    public async Task Downtime_list_rows_keep_work_center_code_out_of_work_order_field_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = await CreateMigratedDbContextAsync();
        dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
            Org, Env, "DT-0004", "WC-04",
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"), null, "DT-MECH", "EQ-004"));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var response = await CreateHandler(dbContext).Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null),
            CancellationToken.None);

        var row = Assert.Single(response.Items);
        Assert.Equal("DT-0004", row.DowntimeEventId);
        Assert.Null(row.WorkOrderId);
        Assert.Equal("WC-04", row.WorkCenterId);
        Assert.Equal("DT-MECH", row.ReasonCode);
    }

    /// <summary>
    /// 已恢复事件按恢复时刻结算，未恢复事件按查询时刻仍在累计；排名按时长降序，
    /// 同时长按原因码升序——现场先看最费时间的原因，平局也必须是稳定的名次而不是随机行序。
    /// </summary>
    [MesRealPostgresFact]
    public async Task Reason_summary_settles_recovered_and_ongoing_minutes_and_ranks_by_duration_then_code()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = await CreateMigratedDbContextAsync();
        await SeedReasonMixAsync(dbContext);

        var response = await CreateHandler(dbContext).Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null),
            CancellationToken.None);

        // DT-MECH 与 DT-PM 都是 60 分钟：名次只能由原因码升序决定，落在写入顺序之后。
        Assert.Equal(
            new[] { "DT-MECH", "DT-PM", "DT-ELEC" },
            response.ReasonSummary.Select(x => x.ReasonCode));
        // DT-MECH：30 分钟已恢复 + 30 分钟仍在停机（10:00 → AsOfUtc 10:30）。
        Assert.Equal(new[] { 60m, 60m, 20m }, response.ReasonSummary.Select(x => x.DurationMinutes));
        Assert.Equal(new[] { 1, 0, 0 }, response.ReasonSummary.Select(x => x.OpenCount));
    }

    /// <summary>
    /// 按原因过滤只收窄列表，不收窄汇总：汇总要一直列全所有原因，否则用户选中一个原因后
    /// 就再也看不到别的原因、换不回去。空白 reasonCode 视作没过滤。
    /// </summary>
    [MesRealPostgresFact]
    public async Task Reason_code_filter_narrows_rows_only_and_blank_reason_code_is_not_a_filter()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = await CreateMigratedDbContextAsync();
        await SeedReasonMixAsync(dbContext);
        var handler = CreateHandler(dbContext);

        var filtered = await handler.Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null, ReasonCode: "DT-ELEC"),
            CancellationToken.None);
        var blank = await handler.Handle(
            new ListDowntimeEventsQuery(Org, Env, null, null, ReasonCode: "   "),
            CancellationToken.None);

        Assert.Equal(1, filtered.Total);
        Assert.Equal("DT-ELEC-1", Assert.Single(filtered.Items).DowntimeEventId);
        Assert.Equal(
            new[] { "DT-ELEC", "DT-MECH", "DT-PM" },
            filtered.ReasonSummary.Select(x => x.ReasonCode).Order(StringComparer.Ordinal));
        Assert.Equal(4, blank.Total);
    }

    /// <summary>
    /// 写入顺序（DT-ELEC → DT-PM → DT-MECH）与期望名次（DT-MECH → DT-PM → DT-ELEC）不同，
    /// 因此聚合上的排序被整段删掉时用例必须转红：生产查询在 GROUP BY 之上没有别的定序来源。
    /// </summary>
    private static async Task SeedReasonMixAsync(ApplicationDbContext dbContext)
    {
        dbContext.WorkCenterUnavailabilities.AddRange(
            WorkCenterUnavailability.Open(
                Org, Env, "DT-ELEC-1", "WC-11",
                DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-30T08:20:00Z"),
                "DT-ELEC", "EQ-011"),
            WorkCenterUnavailability.Open(
                Org, Env, "DT-PM-1", "WC-12",
                DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-30T09:00:00Z"),
                "DT-PM", "EQ-012"),
            WorkCenterUnavailability.Open(
                Org, Env, "DT-MECH-1", "WC-10",
                DateTimeOffset.Parse("2026-07-30T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-30T08:30:00Z"),
                "DT-MECH", "EQ-010"),
            WorkCenterUnavailability.Open(
                Org, Env, "DT-MECH-2", "WC-10",
                DateTimeOffset.Parse("2026-07-30T10:00:00Z"),
                null,
                "DT-MECH", "EQ-010"));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static ListDowntimeEventsQueryHandler CreateHandler(ApplicationDbContext dbContext) =>
        new(dbContext, new FakeTimeProvider(AsOfUtc));

    private static async Task<ApplicationDbContext> CreateMigratedDbContextAsync()
    {
        var dbContext = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), NoopMediator.Instance);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    private sealed class NoopMediator : IMediator
    {
        public static NoopMediator Instance { get; } = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("Noop mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot create streams.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Noop mediator cannot create streams.");
    }
}

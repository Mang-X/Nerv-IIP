using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #2780 首件门禁在**服务端报工提交路径**上的落点。
/// 「这一次是不是首件那一件」由 Quality 的首件进度回答，MES 不用本地报工历史推断，
/// 因此每次报工都要带着该工单该工序去问，且问到的结论每次现取（复检结论不缓存）。
/// </summary>
public sealed class MesFirstArticleReportGateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-31T08:00:00Z");

    [Fact]
    public async Task Every_report_asks_quality_with_that_work_order_and_operation()
    {
        var (services, gate) = CreateServices(nameof(Every_report_asks_quality_with_that_work_order_and_operation));
        await using var _ = services;
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-10", 10);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, gate);

        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-first");
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-second");

        Assert.Equal(
            [("org-001", "env-dev", "WO-A", "OP-10"), ("org-001", "env-dev", "WO-A", "OP-10")],
            gate.Calls);
    }

    /// <summary>
    /// 拍板决策 2：首件那一件不该被自己拦住。它不靠「该工序此前没报过工」识别——那个本地判据与
    /// Quality 的建单条件不等价——而是 Quality 明说任务尚未开出（<c>not-opened</c>），
    /// 而开单的唯一触发点正是本次报工的事件。
    /// </summary>
    [Fact]
    public async Task Operation_whose_first_article_task_is_not_opened_yet_still_reports_even_after_earlier_reports()
    {
        var (services, gate) = CreateServices(nameof(Operation_whose_first_article_task_is_not_opened_yet_still_reports_even_after_earlier_reports));
        await using var _ = services;
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-10", 10);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, gate);
        // 门禁上线前就在制、已经报过工的工序：Quality 侧没有任何首件任务，且命中生效首件档。
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-legacy");

        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-after-gate");

        // 这一次放行才让 ProductionReportRecorded 落到 outbox，Quality 据此开出首件任务；
        // 拦掉它就再没有任何路径能开出该工序的首件任务（全仓唯一建单触发点就是报工事件）。
        Assert.Equal(2, await dbContext.ProductionReports.CountAsync(x => x.OperationTaskId == "OP-10"));
    }

    [Fact]
    public async Task Rejected_first_article_blocks_the_report_and_persists_nothing()
    {
        var (services, gate) = CreateServices(nameof(Rejected_first_article_blocks_the_report_and_persists_nothing));
        await using var _ = services;
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-10", 10);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, gate);
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-first");
        gate.Rejection = "本工序首件判定不合格，不能继续报工。请返工后重新首件检验。";

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-second"));

        Assert.Equal("本工序首件判定不合格，不能继续报工。请返工后重新首件检验。", exception.Message);
        Assert.Equal(1, await dbContext.ProductionReports.CountAsync(x => x.OperationTaskId == "OP-10"));
    }

    /// <summary>首件判合格后同一工序放行：门禁每次现取结论，不缓存首次判定。</summary>
    [Fact]
    public async Task Passed_first_article_lets_the_same_operation_keep_reporting()
    {
        var (services, gate) = CreateServices(nameof(Passed_first_article_lets_the_same_operation_keep_reporting));
        await using var _ = services;
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-10", 10);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, gate);
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-first");
        gate.Rejection = "本工序首件尚未判定，暂不能继续报工。请等待质量完成首件确认。";
        await Assert.ThrowsAsync<KnownException>(() =>
            ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-second"));

        gate.Rejection = null;
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-third");

        Assert.Equal(2, await dbContext.ProductionReports.CountAsync(x => x.OperationTaskId == "OP-10"));
    }

    /// <summary>
    /// 波 1 的读面把「任务未开出」与「Quality 还不掌握该工序」拆成两个取值（#2780），
    /// 判据是**该状态靠什么恢复**：前者只能靠一次报工恢复，后者只能靠工单发布事实到达恢复。
    /// 合成一个取值时门禁无论放行还是拒绝都必然错一种，这里把「不可合并」钉住。
    /// </summary>
    [Fact]
    public void First_article_progress_separates_states_by_how_they_recover()
    {
        Assert.NotEqual(
            QualityFirstArticleConfirmationStatuses.NotOpened,
            QualityFirstArticleConfirmationStatuses.NotSynchronized);
    }

    /// <summary>
    /// 每次报工后立刻落库：门禁判据查的是**已落库的报工**，只留在变更跟踪器里会让判据永远读不到前一次报工。
    /// </summary>
    private static async Task ReportAsync(
        ApplicationDbContext dbContext,
        RecordProductionReportCommandHandler handler,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string idempotencyKey)
    {
        await handler.Handle(
            new RecordProductionReportCommand(
                organizationId,
                environmentId,
                workOrderId,
                operationTaskId,
                1m,
                0m,
                false,
                Now,
                idempotencyKey),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
    }

    private static RecordProductionReportCommandHandler CreateHandler(
        ApplicationDbContext dbContext,
        IMesFirstArticleGate gate) =>
        new(dbContext, TestProductionReportOeeDimensionSnapshotProvider.Instance, gate);

    private static void SeedOperation(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        int operationSequence,
        bool seedWorkOrder = true)
    {
        if (seedWorkOrder)
        {
            dbContext.WorkOrders.Add(WorkOrder.Create(
                organizationId, environmentId, workOrderId, "FG-FSA", "PV-FSA-1", 100m, 10, Now.AddHours(8)));
        }

        dbContext.OperationTasks.Add(OperationTask.Create(
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            OperationTaskLifecycleStatus.InProgress,
            operationSequence,
            "WC-FILL",
            [],
            Now,
            TimeSpan.FromMinutes(45),
            Now,
            null));
    }

    private static (ServiceProvider Services, RecordingFirstArticleGate Gate) CreateServices(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return (services.BuildServiceProvider(), new RecordingFirstArticleGate());
    }

    private sealed class RecordingFirstArticleGate : IMesFirstArticleGate
    {
        public List<(string OrganizationId, string EnvironmentId, string WorkOrderId, string OperationTaskId)> Calls { get; } = [];

        public string? Rejection { get; set; }

        public Task EnsureBatchReportAllowedAsync(
            string organizationId,
            string environmentId,
            string workOrderId,
            string operationTaskId,
            CancellationToken cancellationToken)
        {
            Calls.Add((organizationId, environmentId, workOrderId, operationTaskId));
            return Rejection is null ? Task.CompletedTask : throw new KnownException(Rejection);
        }
    }
}

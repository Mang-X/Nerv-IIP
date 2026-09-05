using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #2780 首件门禁在**服务端报工提交路径**上的落点：报工是否被门禁拦住、拦住时是否什么都不落库。
/// **取值判据本身不在这里**——「Quality 回哪个取值时放行」由
/// <see cref="HttpMesFirstArticleGateTests"/> 按 wire 字符串逐值承担（含验收标准第 3 条要的
/// <c>not-opened</c> 放行）。这里的桩只表达「放行 / 拒绝」两态，不复制一份判据。
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
        gate.Rejection = "本工序首件判定不合格，请返工后重新首件检验；记录见工序行操作。";

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-second"));

        Assert.Equal("本工序首件判定不合格，请返工后重新首件检验；记录见工序行操作。", exception.Message);
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
        gate.Rejection = "本工序首件尚未判定，暂不能继续报工。可在工序行操作打开首件检验记录。";
        // 断言到消息，不只断言异常类型：裸 ThrowsAsync<KnownException> 会被**任何**其它业务守卫兜住，
        // 于是这条用例可能「因为错误的原因继续绿」——#3119 的未下达守卫就差点落进这一格
        // （夹具工单当时从未下达；若不修夹具，这里会拿到工单守卫的拒绝而不是首件门禁的拒绝）。
        var rejection = await Assert.ThrowsAsync<KnownException>(() =>
            ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-second"));
        Assert.Equal(gate.Rejection, rejection.Message);

        gate.Rejection = null;
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-third");

        Assert.Equal(2, await dbContext.ProductionReports.CountAsync(x => x.OperationTaskId == "OP-10"));
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
            // #3119：未下达的工单不受理报工，夹具因此必须先补记发布（生产上这一步由下达完成）。
            var workOrder = WorkOrder.Create(
                organizationId, environmentId, workOrderId, "FG-FSA", "PV-FSA-1", 100m, 10, Now.AddHours(8));
            workOrder.MarkReleased();
            workOrder.ClearDomainEvents();
            dbContext.WorkOrders.Add(workOrder);
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

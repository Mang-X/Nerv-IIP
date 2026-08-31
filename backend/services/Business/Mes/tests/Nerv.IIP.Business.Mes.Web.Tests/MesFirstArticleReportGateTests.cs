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
/// #2780 首件门禁在**服务端报工提交路径**上的落点：拦的是批量报工，不是首件那一件。
/// </summary>
public sealed class MesFirstArticleReportGateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-31T08:00:00Z");

    [Fact]
    public async Task First_report_of_an_operation_is_the_first_article_itself_and_is_not_gated()
    {
        // 其它 scope 里都已经报过工：删掉门禁判据里任何一个 scope 谓词，本次报工都会被当成「已有报工」而去问 Quality。
        var (services, gate) = CreateServices(nameof(First_report_of_an_operation_is_the_first_article_itself_and_is_not_gated));
        await using var _ = services;
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-10", 10);
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-20", 20, seedWorkOrder: false);
        SeedOperation(dbContext, "org-002", "env-dev", "WO-A", "OP-10", 10);
        SeedOperation(dbContext, "org-001", "env-prod", "WO-A", "OP-10", 10);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, gate);

        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-20", "k-other-operation");
        await ReportAsync(dbContext, handler, "org-002", "env-dev", "WO-A", "OP-10", "k-other-organization");
        await ReportAsync(dbContext, handler, "org-001", "env-prod", "WO-A", "OP-10", "k-other-environment");

        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-subject");

        Assert.Empty(gate.Calls);
    }

    [Fact]
    public async Task Second_report_of_an_operation_is_gated_on_that_work_order_and_operation()
    {
        var (services, gate) = CreateServices(nameof(Second_report_of_an_operation_is_gated_on_that_work_order_and_operation));
        await using var _ = services;
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedOperation(dbContext, "org-001", "env-dev", "WO-A", "OP-10", 10);
        await dbContext.SaveChangesAsync();
        var handler = CreateHandler(dbContext, gate);
        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-first");

        await ReportAsync(dbContext, handler, "org-001", "env-dev", "WO-A", "OP-10", "k-second");

        Assert.Equal(
            [("org-001", "env-dev", "WO-A", "OP-10")],
            gate.Calls);
    }

    [Fact]
    public async Task Rejected_first_article_blocks_the_second_report_and_persists_nothing()
    {
        var (services, gate) = CreateServices(nameof(Rejected_first_article_blocks_the_second_report_and_persists_nothing));
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

    /// <summary>门禁未接入来源时不许放行——缺来源等于「不知道」，不是「合格」。</summary>
    [Fact]
    public async Task Unconfigured_gate_refuses_instead_of_allowing()
    {
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UnconfiguredMesFirstArticleGate.Instance.EnsureBatchReportAllowedAsync(
                "org-001", "env-dev", "WO-A", "OP-10", CancellationToken.None));

        Assert.StartsWith("FIRST_ARTICLE_SOURCE_UNAVAILABLE:", exception.Message, StringComparison.Ordinal);
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

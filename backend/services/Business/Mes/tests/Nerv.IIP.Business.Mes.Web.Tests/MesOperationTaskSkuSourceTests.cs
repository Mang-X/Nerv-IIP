using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.MasterData;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3112：MES 两个事件对同一道工序必须用同一个 SKU 源。
///
/// <para>权威源是 <c>WorkOrder.SkuId</c>：持久化契约（<c>OperationTaskEntityTypeConfiguration</c> 的列注释）
/// 把该列声明为 "copied from the MES work order"，领域里不存在工序级 SKU 概念
/// （<c>RoutingStepSnapshot</c> 不带 SKU、<c>SkuCode</c> 构造后无变更方法），
/// 而 <c>WorkOrderReleasedPayload.SkuCode</c> 是一个标量配 N 个 operations，本身已断言"同一工单所有工序共享一个 SKU"。</para>
///
/// <para>本组用例钉的是三条曾经不传 SKU、于是让 <c>OperationTask</c> 回落成工单号的建工序路径。
/// 回落已删除、<c>skuCode</c> 已是必填形参，所以"漏传"现在编译期就不可表达；
/// 这些用例负责的是"传的是不是**工单真实 SKU**"——那一条类型系统管不了。</para>
/// </summary>
public sealed class MesOperationTaskSkuSourceTests
{
    /// <summary>
    /// 计划转工单的**带 WorkCenterId 捷径分支**。同文件的常规分支（无 WorkCenterId）早有
    /// <c>MesRoutingSnapshotTests</c> 钉住 SKU，捷径分支此前无人钉，正是漏传的那一条。
    /// </summary>
    [Fact]
    public async Task Convert_plan_shortcut_branch_stamps_the_work_order_sku_on_the_operation_task()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var requestedAtUtc = DateTimeOffset.Parse("2026-09-09T08:00:00Z");

        var handler = new ConvertPlanToWorkOrderCommandHandler(
            dbContext,
            new RuleScheduler(),
            null,
            NoRequirementsProvider.Instance,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext),
            null);
        await handler.Handle(
            new ConvertPlanToWorkOrderCommand(
                "org-001", "env-dev", "SUG-3112", "WO-3112-SHORTCUT", requestedAtUtc,
                "SKU-FG-3112", "PV-001", 12m, "PCS", requestedAtUtc.AddDays(2),
                // 带 WorkCenterId ⇒ 走捷径分支
                "WC-SHORTCUT",
                null,
                null,
                null,
                null,
                "convert-3112-shortcut"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.AsNoTracking().SingleAsync(CancellationToken.None);
        var task = await dbContext.OperationTasks.AsNoTracking().SingleAsync(CancellationToken.None);

        Assert.Equal("WC-SHORTCUT", task.WorkCenterId);
        Assert.Equal("SKU-FG-3112", workOrder.SkuId);
        // 回落时这里会是 "WO-3112-SHORTCUT"（工单号形状的 junk SKU）。
        Assert.Equal(workOrder.SkuId, task.SkuCode);
        Assert.NotEqual(task.WorkOrderId, task.SkuCode);
    }

    private sealed class NoRequirementsProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoRequirementsProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}

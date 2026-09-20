using MediatR;
using System.Reflection;
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
/// <para>三条曾经不传 SKU、于是让 <c>OperationTask</c> 回落成工单号的建工序路径**分散在三处**登记，
/// 不要以为都在本类里：计划转工单捷径分支在本类；排程计划发布补建在
/// <c>SchedulingPlanReleasedHandlerTests</c>（复用该类既有夹具）；加急建单在
/// <c>MesOperationTaskSkuSourcePostgresTests</c>（走真库）。</para>
///
/// <para>回落已删除、<c>skuCode</c> 已是必填形参，所以"漏传"现在编译期就不可表达，
/// 且该结构性闭合由本类的反射断言钉住。这些用例负责的是另一件事——
/// "传的是不是**工单真实 SKU**"，那一条类型系统管不了。</para>
/// </summary>
public sealed class MesOperationTaskSkuSourceTests
{
    /// <summary>
    /// #3112 的结构性闭合本身要有断言承担：`OperationTask.Create/Queue` 的 <c>skuCode</c>
    /// **不得有默认值**。删掉 <c>?? workOrderId</c> 只是移走了替换动作，真正让「建工序却漏传 SKU」
    /// 不可表达的是「该形参必填」；而必填这件事此前只写在注释里，把三个签名整体改回
    /// <c>string? skuCode = null</c> 并恢复回落，全量门禁**逐位不变**（本 PR 实测过）。
    ///
    /// <para>用反射读形参元数据，不是源码文本扫描：本仓 #3214/#3176 三轮实证文本近似护栏不收敛。
    /// 编译产物里的 <c>HasDefaultValue</c> 是**编译器给出的事实**，换行、注释、空白都动不了它。</para>
    ///
    /// <para><b>这条断言不覆盖什么</b>：它只关掉「漏传」。<c>skuCode</c> 是 14 个位置参数里的第 13 个、
    /// 类型仍是 <c>string</c>，所以把工单号当实参传进去照样编译——「传错」由本文件其余用例
    /// （断言工序 SKU 等于工单 SkuId）承担，不由类型系统承担。</para>
    /// </summary>
    [Fact]
    public void Operation_task_factories_must_not_give_sku_code_a_default_value()
    {
        foreach (var methodName in new[] { nameof(OperationTask.Create), nameof(OperationTask.Queue) })
        {
            var method = Assert.Single(
                typeof(OperationTask).GetMethods(BindingFlags.Public | BindingFlags.Static),
                candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal));
            var parameter = Assert.Single(
                method.GetParameters(),
                candidate => string.Equals(candidate.Name, "skuCode", StringComparison.Ordinal));
            Assert.Equal(typeof(string), parameter.ParameterType);
            Assert.False(
                parameter.HasDefaultValue,
                $"OperationTask.{methodName} 的 skuCode 不得有默认值，否则「建工序却漏传 SKU」重新变得可表达（#3112）。");
        }
    }

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
        // 同一条命令的常规分支（无 WorkCenterId）本来就把这两项从 request 抄给工序；
        // 捷径分支此前吃 OperationTask 的 "pcs" / 1m 默认值，于是同一命令两个分支给出不同答案。
        Assert.Equal("PCS", task.UomCode);
        Assert.Equal(12m, task.PlannedQuantity);
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

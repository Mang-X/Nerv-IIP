using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #2977 的**跨服务那一半**：四条把上游 <c>IdempotencyKey</c> 透传（或再追加一段来源行号）
/// 的写面，其有效上界不在 Quality 的 EF 模型里，而在 **WMS / ERP / MES 的承载列宽**里。
///
/// 本类不算数、不抄数：每一条都**用生产侧真实 converter 造出真实事件、再交给 Quality 真实消费者建任务**，
/// 输入的每一段业务串都按**生产侧 EF 模型读回的列宽**顶格填满，最后把**落到聚合上的键长**
/// 与**从 Quality EF 模型读回的 <c>trigger_idempotency_key</c> 列宽**直接对撞。
/// 任何一侧（生产侧列宽、converter 前缀、Quality 侧列宽、Quality 侧拼接方式）被单边改动都会红。
///
/// **为什么必须跨服务读**：<c>IIntegrationEventEnvelope.IdempotencyKey</c> 与
/// <c>WmsIntegrationPayloadLine.LineReference</c> / <c>PurchaseReceiptRecordedLinePayload.LineReference</c>
/// 在公开契约里**都是无 <c>MaxLength</c> 的裸 <c>string</c>**（<c>Contracts.Wms</c> / <c>Contracts.Erp</c> /
/// <c>Contracts.Mes</c> 实读）。#2977 票面因此漏算了 <c>:73</c> / <c>:129</c> 两条更长的写面。
///
/// **合同分类（`docs/governance/testing/validity.md`）**：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是四个服务的 migration/schema 列宽约束与 PostgreSQL 的
/// <c>22001</c> 行为；<c>Regression</c> 的权威来源是 GitHub #2977 正文与编排者第一条评论里的写面判定表。
/// **执行形态是「EF 模型读取 + 内存聚合 + EF Core InMemory」**，按 validity.md 的证明范围表，
/// 它证明的是**长度关系**，**不能**证明真库落库；真库那一面由 PR 正文一次性 <c>postgres:18</c> 读数承担。
///
/// **值域边界（别读成完备）**：
/// <list type="number">
/// <item>本类算出的是**今天这四个 converter 的形状**，不是契约保证。上游换更长的前缀、
/// 或新增一个直接构造信封的生产者（外部适配器直投 CAP），本类算不到。兜底的是
/// <see cref="InspectionTaskTriggerKey.EnsureWithinColumn"/>，不是这些数字。</item>
/// <item>MES 工序完工那条键里含 <c>UtcTicks</c>，真实时间只有 18 位，理论上界 19 位。
/// 本类**按机器可查的方式补上这一位**（用 <see cref="DateTimeOffset.MaxValue"/> 的 ticks 位数
/// 减去实测 ticks 位数），不是手抄「+1」。</item>
/// <item>Quality 自己拼的三种形状（首件 / 周期检 / 世界观历史 seed）**不在本类射程内**，
/// 由 <c>Nerv.IIP.Business.Quality.Web.Tests.InspectionTaskTriggerIdempotencyKeyLengthContractTests</c>
/// 看守。列宽的取值来源是**首件**那条（474，全部受治理写面里最宽）；
/// 世界观历史 seed 的输入全部来自它自己的定长生成器（实测最长键 39 字符），
/// **只进「装得下」不进「定界」**。</item>
/// </list>
/// </summary>
public sealed class InspectionTaskTriggerKeyCrossServiceWidthContractTests
{
    /// <summary>改前列宽（#2977 复现基线，**不是现行上界**）。</summary>
    private const int PreChangeColumnMaxLength = 300;

    [Fact]
    public async Task Wms_receiving_trigger_key_at_producer_column_widths_fits_the_quality_column()
    {
        using var wmsModel = CreateWmsModelContext();
        var organizationId = Saturated(wmsModel, typeof(InboundOrder), "organization_id");
        var environmentId = Saturated(wmsModel, typeof(InboundOrder), "environment_id");
        var inboundOrderNo = Saturated(wmsModel, typeof(InboundOrder), "inbound_order_no");
        var lineNo = Saturated(wmsModel, typeof(InboundOrderLine), "line_no");

        var inbound = InboundOrder.Create(
            organizationId,
            environmentId,
            inboundOrderNo,
            "purchase-order",
            "PO-2977-001",
            "SITE-01",
            [
                new InboundOrderLineDraft(
                    lineNo,
                    "SKU-2977",
                    "kg",
                    10m,
                    "RAW-A-01",
                    null,
                    null,
                    "inspection-required",
                    "company",
                    null),
            ]);
        var integrationEvent = new InboundOrderCompletedIntegrationEventConverter()
            .Convert(new InboundOrderCompletedDomainEvent(inbound));

        await using var qualityDb = CreateQualityContext();
        await SeedActivePlanAsync(qualityDb, organizationId, environmentId, QualityInspectionSourceTypes.Receiving);
        var handler = new WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks(
            qualityDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var measured = await RunHandlerAndMeasureAsync(qualityDb, () => handler.HandleAsync(integrationEvent, CancellationToken.None));
        AssertFitsAndUsedToOverflow("WMS 入库完成 → 收货检验", measured.Length);
        var task = RequirePersisted("WMS 入库完成 → 收货检验", measured);

        // 键构成不变：上游事件幂等键 + ":" + 来源行号。各段填充串互不相同，
        // 这条断言才有鉴别力（全填同一个字符时「少拼一段」会被 EndsWith 之类的弱断言放过）。
        Assert.Equal($"{integrationEvent.IdempotencyKey}:{lineNo}", task.TriggerIdempotencyKey);
        Assert.NotEqual(integrationEvent.IdempotencyKey, task.TriggerIdempotencyKey);
    }

    [Fact]
    public async Task Erp_receiving_trigger_key_at_producer_column_widths_fits_the_quality_column()
    {
        using var erpModel = CreateErpModelContext();
        var organizationId = Saturated(erpModel, typeof(PurchaseReceipt), "organization_id");
        var environmentId = Saturated(erpModel, typeof(PurchaseReceipt), "environment_id");
        var purchaseReceiptNo = Saturated(erpModel, typeof(PurchaseReceipt), "purchase_receipt_no");
        var purchaseOrderLineNo = Saturated(erpModel, typeof(PurchaseReceiptLine), "purchase_order_line_no");

        var order = PurchaseOrder.Create(
            organizationId,
            environmentId,
            "PO-2977-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft(purchaseOrderLineNo, "SKU-2977", "kg", 10m, 12m, new DateOnly(2026, 9, 1))]);
        order.MarkApprovalRequested("approval-chain-2977");
        order.ReleaseAfterApproval("approval-chain-2977");
        var receipt = PurchaseReceipt.Record(
            order,
            purchaseReceiptNo,
            [new PurchaseReceiptLineDraft(purchaseOrderLineNo, 10m, "accepted")]);
        var integrationEvent = new PurchaseReceiptRecordedIntegrationEventConverter()
            .Convert(new PurchaseReceiptRecordedDomainEvent(receipt));

        await using var qualityDb = CreateQualityContext();
        await SeedActivePlanAsync(qualityDb, organizationId, environmentId, QualityInspectionSourceTypes.Receiving);
        var handler = new ErpPurchaseReceiptRecordedIntegrationEventHandlerForCreateInspectionTasks(
            qualityDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var measured = await RunHandlerAndMeasureAsync(qualityDb, () => handler.HandleAsync(integrationEvent, CancellationToken.None));
        AssertFitsAndUsedToOverflow("ERP 采购收货 → 收货检验", measured.Length);
        var task = RequirePersisted("ERP 采购收货 → 收货检验", measured);

        // 同上：键构成不变，且**确实**比上游事件键更长。
        Assert.Equal($"{integrationEvent.IdempotencyKey}:{purchaseOrderLineNo}", task.TriggerIdempotencyKey);
        Assert.NotEqual(integrationEvent.IdempotencyKey, task.TriggerIdempotencyKey);
    }

    [Fact]
    public async Task Mes_operation_completed_trigger_key_at_producer_column_widths_fits_the_quality_column()
    {
        using var mesModel = CreateMesModelContext();
        var organizationId = Saturated(mesModel, typeof(OperationTask), "organization_id");
        var environmentId = Saturated(mesModel, typeof(OperationTask), "environment_id");
        var operationTaskId = Saturated(mesModel, typeof(OperationTask), "operation_task_id");

        var completedAtUtc = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        var operationTask = OperationTask.Create(
            organizationId,
            environmentId,
            "WO-2977-001",
            operationTaskId,
            OperationTaskLifecycleStatus.Completed,
            10,
            "WC-01",
            [],
            completedAtUtc.AddHours(-2),
            TimeSpan.FromHours(1),
            completedAtUtc.AddHours(-1),
            completedAtUtc,
            "SKU-2977",
            "pcs",
            10m,
            requiresQualityInspection: true);
        var integrationEvent = new OperationTaskCompletedIntegrationEventConverter()
            .Convert(new OperationTaskCompletedDomainEvent(operationTask));

        await using var qualityDb = CreateQualityContext();
        await SeedActivePlanAsync(qualityDb, organizationId, environmentId, QualityInspectionSourceTypes.Operation);
        var handler = new MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks(
            qualityDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var measured = await RunHandlerAndMeasureAsync(qualityDb, () => handler.HandleAsync(integrationEvent, CancellationToken.None));

        // 键里含 UtcTicks：实测时间 18 位，理论上界（DateTimeOffset.MaxValue）19 位。
        // 这一位差由机器算出来补上，不手抄。
        var observedTickDigits = completedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture).Length;
        var widestTickDigits = DateTimeOffset.MaxValue.UtcTicks.ToString(CultureInfo.InvariantCulture).Length;
        var worstCaseLength = measured.Length - observedTickDigits + widestTickDigits;

        Assert.True(widestTickDigits >= observedTickDigits, "ticks 位数上界不应小于实测位数。");
        AssertFitsAndUsedToOverflow("MES 工序完工 → 工序检验", worstCaseLength);
        var task = RequirePersisted("MES 工序完工 → 工序检验", measured);

        // 键构成不变：本触发点是裸透传，不得在 Quality 侧再拼任何东西。
        Assert.Equal(integrationEvent.IdempotencyKey, task.TriggerIdempotencyKey);
    }

    [Fact]
    public async Task Mes_finished_goods_receipt_trigger_key_at_producer_column_widths_fits_the_quality_column()
    {
        using var mesModel = CreateMesModelContext();
        var organizationId = Saturated(mesModel, typeof(FinishedGoodsReceiptRequest), "organization_id");
        var environmentId = Saturated(mesModel, typeof(FinishedGoodsReceiptRequest), "environment_id");
        var requestNo = Saturated(mesModel, typeof(FinishedGoodsReceiptRequest), "request_no");

        var request = FinishedGoodsReceiptRequest.Create(
            organizationId,
            environmentId,
            requestNo,
            "WO-2977-001",
            "SKU-2977",
            10m,
            "pcs",
            new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero));
        var integrationEvent = new FinishedGoodsReceiptRequestedForQualityIntegrationEventConverter()
            .Convert(new FinishedGoodsReceiptRequestedDomainEvent(request, 10m, "mes:fgr:2977"));

        await using var qualityDb = CreateQualityContext();
        await SeedActivePlanAsync(qualityDb, organizationId, environmentId, QualityInspectionSourceTypes.Final);
        var handler = new MesFinishedGoodsReceiptRequestedIntegrationEventHandlerForCreateInspectionTasks(
            qualityDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var measured = await RunHandlerAndMeasureAsync(qualityDb, () => handler.HandleAsync(integrationEvent, CancellationToken.None));
        AssertFitsAndUsedToOverflow("MES 成品入库申请 → 终检", measured.Length);
        var task = RequirePersisted("MES 成品入库申请 → 终检", measured);

        // 键构成不变：本触发点同为裸透传。
        Assert.Equal(integrationEvent.IdempotencyKey, task.TriggerIdempotencyKey);
    }

    /// <summary>
    /// 跑真实消费者并量出**落到聚合上的触发键长度**。
    ///
    /// **为什么要捕 <see cref="ArgumentOutOfRangeException"/>**：键超宽时
    /// <c>InspectionTaskTriggerKey.EnsureWithinColumn</c> 会在聚合构造函数里先抛，
    /// 于是 <see cref="AssertFitsAndUsedToOverflow"/> 的「≤ 列宽」那半边**永远走不到**，
    /// 鉴别力为零，失败消息也只剩一句异常堆栈。捕获后把守卫携带的实际长度取出来交给断言，
    /// 那一半才真正生效，超宽时给出的是「写面 X 的最坏情况 N 超出列宽 M」而不是裸异常。
    /// （本 PR 的 M7 变异实测到过这个形状：读数是 <c>Actual value was 725</c> 的裸异常。）
    /// </summary>
    private static async Task<MeasuredTriggerKey> RunHandlerAndMeasureAsync(
        QualityDbContext dbContext,
        Func<Task> handle)
    {
        try
        {
            await handle();
        }
        catch (ArgumentOutOfRangeException exception)
            when (string.Equals(exception.ParamName, "triggerIdempotencyKey", StringComparison.Ordinal))
        {
            return new MeasuredTriggerKey(null, (int)exception.ActualValue!);
        }

        var task = await dbContext.InspectionTasks.SingleAsync();
        return new MeasuredTriggerKey(task.TriggerIdempotencyKey, task.TriggerIdempotencyKey.Length);
    }

    /// <summary>键构成类断言只在任务真的建出来时才有对象；被守卫挡下时给出可归因的失败消息。</summary>
    private static InspectionTaskProbe RequirePersisted(string faceName, MeasuredTriggerKey measured)
    {
        Assert.True(
            measured.PersistedKey is not null,
            $"写面「{faceName}」的键长 {measured.Length} 被列宽守卫挡下，任务没有建出来，无法核对键构成。");
        return new InspectionTaskProbe(measured.PersistedKey!);
    }

    /// <summary>
    /// 每条写面同时断言两件事：现行列宽装得下（缺陷已闭合），且改前列宽装不下（缺陷不是假想的）。
    /// 列宽从 Quality 的 EF 模型闭集枚举读回，不读常量。
    /// </summary>
    private static void AssertFitsAndUsedToOverflow(string faceName, int worstCaseLength)
    {
        var columnWidth = QualityTriggerKeyColumnWidth();

        Assert.True(
            worstCaseLength <= columnWidth,
            $"写面「{faceName}」的最坏情况 {worstCaseLength} 超出 trigger_idempotency_key 列宽 {columnWidth}。");
        Assert.True(
            worstCaseLength > PreChangeColumnMaxLength,
            $"写面「{faceName}」的最坏情况 {worstCaseLength} 没有超出改前列宽 {PreChangeColumnMaxLength}，缺陷描述有误。");
    }

    private static int QualityTriggerKeyColumnWidth()
    {
        using var qualityModel = CreateQualityModelContext();
        var columns = qualityModel.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), "trigger_idempotency_key", StringComparison.Ordinal))
            .ToArray();
        return Assert.Single(columns).GetMaxLength()!.Value;
    }

    /// <summary>
    /// 把某个生产侧列填满到它的列宽——上界从 EF 模型读，不手抄。
    ///
    /// **填充串按列名区分，不是同一个字符重复**：全填同一个字符时，
    /// 「键里少拼了一段」这类变异会被 <c>EndsWith</c> / 长度类断言整体放过
    /// （本 PR 的 M4 实测踩到过：ERP 写面去掉 <c>:{LineReference}</c> 后，
    /// 因为收货单号与行号都是同一串 <c>x</c>，尾段恰好仍然匹配，全绿）。
    /// 现在每段的内容互不相同，等价输入被拆开。
    /// </summary>
    private static string Saturated(ModelOnlyContext context, Type entityClrType, string columnName)
    {
        var entityType = context.Model.FindEntityType(entityClrType)!;
        var property = Assert.Single(
            entityType.GetProperties(),
            candidate => string.Equals(candidate.GetColumnName(), columnName, StringComparison.Ordinal));
        var width = property.GetMaxLength()!.Value;
        var marker = $"{entityClrType.Name}-{columnName}-".Replace('_', '-');
        Assert.True(marker.Length < width, $"列 {columnName} 的列宽 {width} 容不下区分标记 {marker}。");
        return marker + new string('x', width - marker.Length);
    }

    private static async Task SeedActivePlanAsync(
        QualityDbContext dbContext,
        string organizationId,
        string environmentId,
        string category)
    {
        var plan = InspectionPlan.Create(
            organizationId,
            environmentId,
            $"PLAN-2977-{category}",
            category,
            null,
            null,
            null,
            null,
            null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static QualityDbContext CreateQualityContext()
    {
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseInMemoryDatabase($"quality-trigger-key-{Guid.NewGuid():N}")
            .Options;
        return new QualityDbContext(options, new NoopMediator());
    }

    private static ModelOnlyContext CreateQualityModelContext()
    {
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseNpgsql("Host=unused;Database=nerv_iip_trigger_key_cross_service;Username=nerv;Password=nerv")
            .Options;
        return new ModelOnlyContext(new QualityDbContext(options, new NoopMediator()));
    }

    private static ModelOnlyContext CreateWmsModelContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql("Host=unused;Database=nerv_iip_trigger_key_cross_service;Username=nerv;Password=nerv")
            .Options;
        return new ModelOnlyContext(new WmsDbContext(options, new NoopMediator()));
    }

    private static ModelOnlyContext CreateErpModelContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql("Host=unused;Database=nerv_iip_trigger_key_cross_service;Username=nerv;Password=nerv")
            .Options;
        return new ModelOnlyContext(new ErpDbContext(options, new NoopMediator()));
    }

    private static ModelOnlyContext CreateMesModelContext()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseNpgsql("Host=unused;Database=nerv_iip_trigger_key_cross_service;Username=nerv;Password=nerv")
            .Options;
        return new ModelOnlyContext(new MesDbContext(options, new NoopMediator()));
    }

    private sealed class ModelOnlyContext(DbContext dbContext) : IDisposable
    {
        public IModel Model { get; } = dbContext.GetService<IDesignTimeModel>().Model;

        private DbContext DbContext { get; } = dbContext;

        public void Dispose() => DbContext.Dispose();
    }

    /// <summary>落到聚合上的触发键；被守卫挡下时 <see cref="PersistedKey"/> 为 null，长度取自守卫携带的实际值。</summary>
    private sealed record MeasuredTriggerKey(string? PersistedKey, int Length);

    /// <summary>只承载触发键的轻量投影，让键构成断言不必再回查 <c>DbContext</c>。</summary>
    private sealed class InspectionTaskProbe(string triggerIdempotencyKey)
    {
        public string TriggerIdempotencyKey { get; } = triggerIdempotencyKey;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Test mediator cannot send requests.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("Test mediator cannot send requests.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Test mediator cannot send requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Test mediator cannot stream.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Test mediator cannot stream.");
    }
}

using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #3191：<c>SourceDocumentId</c> 一个字段承载多种形状的身份，消费者需要的是「哪张工单／哪道工序」。
/// 这里钉住生产者一侧解出来的结构化身份——解错的方向是静默的（下游只会表现为「什么都没发生」）。
///
/// #3319 起来源行有了自己的列：周期检的复合窗口身份留在 <c>SourceDocumentLineId</c>，
/// <c>SourceDocumentId</c> 恢复成工单，因此周期检的工单号也能给出了。
/// </summary>
public sealed class InspectionResultMesScopeTests
{
    private const string PeriodicTimeLineId = "OP-010:periodic-time:0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f:3";
    private const string PeriodicQuantityLineId = "OP-010:periodic-quantity:0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f:1";

    private static readonly Guid RuntimeContextId = Guid.Parse("0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f");

    public static TheoryData<string, string, string, string?, string?, string?> Cases() => new()
    {
        // 工序检：来源单据身份就是工单公开 id。来源行（工序任务 id）自 #3319 起在记录上是有的，
        // 但**刻意不发布**——发布它会把 MES QualityHoldContext.OperationTaskId 从整张工单收窄到某道工序。
        { QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.Mes, "WO-001", "OP-010", "WO-001", null },
        // 首件：{workOrderId}:{operationTaskId} 复合串，两段都能还原。
        { QualityInspectionSourceTypes.FirstArticle, QualityInspectionSourceServices.Mes, "WO-001:OP-010", "OP-010", "WO-001", "OP-010" },
        // 周期检：工序从来源行的复合窗口身份还原，工单直接取来源单据。
        { QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.Mes, "WO-001", PeriodicTimeLineId, "WO-001", "OP-010" },
        { QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.MesOperation, "WO-001", PeriodicQuantityLineId, "WO-001", "OP-010" },
        // 周期检那一支读的是**来源行**而不是来源单据：把复合窗口身份摆到来源单据那一列上，
        // 工序就不该被还原出来。没有这一行，把解析对象改回 SourceDocumentId 照样绿。
        { QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.Mes, PeriodicTimeLineId, "OP-010", PeriodicTimeLineId, null },
        // 终检：来源单据是入库申请单号，不是工单／工序身份。
        { QualityInspectionSourceTypes.Final, QualityInspectionSourceServices.Mes, "FGR-REQ-001", "WO-001", null, null },
        // MesOwned 守卫的**唯一**鉴别格：来源环节是工序、但来源服务不归 MES。既有的非 MES 行都同时
        // 是非工序来源环节，会被后面那道 sourceType 判据兜住，删掉 MesOwned 守卫照样绿——
        // 没有这一行，那道守卫零鉴别力。
        { QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.Erp, "WO-001", "OP-010", null, null },
        // 非 MES 归属：收货检的来源单据是收货单号。
        { QualityInspectionSourceTypes.Receiving, QualityInspectionSourceServices.PurchaseReceipt, "RCV-001", "LINE-1", null, null },
        { QualityInspectionSourceTypes.CustomerReturn, QualityInspectionSourceServices.CustomerReturn, "RMA-001", null, null, null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Inspection_result_payload_carries_the_structured_mes_identity(
        string sourceType,
        string sourceService,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string? expectedWorkOrderId,
        string? expectedOperationTaskId)
    {
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            sourceType,
            sourceService,
            sourceDocumentId,
            sourceDocumentLineId,
            "SKU-RM-1000",
            10m,
            null,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);
        var converter = new InspectionPassedIntegrationEventConverter(new StubQualityIntegrationEventContextAccessor());

        var payload = converter.Convert(new InspectionPassedDomainEvent(record)).Payload;

        Assert.Equal(expectedWorkOrderId, payload.WorkOrderId);
        Assert.Equal(expectedOperationTaskId, payload.OperationTaskId);
        // 来源单据身份原样过界，不再按来源形状二选一地被换掉（#3319）。
        Assert.Equal(sourceDocumentId, payload.SourceDocumentId);
    }

    /// <summary>
    /// 编码点与解码点同住一处的意义在于往返成立。这两条是往返断言，改了任一侧都会红。
    /// </summary>
    [Fact]
    public void First_article_source_document_identity_round_trips()
    {
        var minted = FirstArticleInspection.SourceDocumentId("WO-001", "OP-010");

        Assert.True(FirstArticleInspection.TryParseSourceDocumentId(minted, out var workOrderId, out var operationId));
        Assert.Equal("WO-001", workOrderId);
        Assert.Equal("OP-010", operationId);
        Assert.False(FirstArticleInspection.TryParseSourceDocumentId("WO-001", out _, out _));
    }

    /// <summary>
    /// 改前这里还断言 <c>IsPeriodicTriggerKey(TriggerIdempotencyKey(...))</c> 往返成立。#3319 把
    /// 「按触发幂等键的文本形状判来源形状」这个职责整段退休（<c>InspectionTask</c> 的构造守卫与取值分派
    /// 都不再存在），<c>IsPeriodicTriggerKey</c> 随之删除，那条断言因此没有了主语——不是被放弃，
    /// 是它要证的判别路径已不存在。来源行本身的编解码往返仍由下面两条看守。
    /// </summary>
    [Theory]
    [InlineData(PeriodicInspectionSourceLine.TimeKind)]
    [InlineData(PeriodicInspectionSourceLine.QuantityKind)]
    public void Periodic_source_line_identity_round_trips(string kind)
    {
        var minted = PeriodicInspectionSourceLine.LineId("OP-010", kind, RuntimeContextId, 7);

        Assert.True(PeriodicInspectionSourceLine.TryParseOperationId(minted, out var operationId));
        Assert.Equal("OP-010", operationId);
        Assert.Contains(kind, PeriodicInspectionSourceLine.TriggerIdempotencyKey(kind, RuntimeContextId, 7), StringComparison.Ordinal);
        // 工单公开 id 与首件复合串都不得被误判成周期检来源行。
        Assert.False(PeriodicInspectionSourceLine.TryParseOperationId("WO-001", out _));
        Assert.False(PeriodicInspectionSourceLine.TryParseOperationId("WO-001:OP-010", out _));
    }

    private sealed class StubQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() =>
            new("corr-quality-3191", "cause-quality-3191", "system:test");
    }
}

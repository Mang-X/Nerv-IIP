using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #3191：<c>SourceDocumentId</c> 一个字段承载三种形状的身份，消费者需要的是「哪张工单／哪道工序」。
/// 这里钉住生产者一侧解出来的结构化身份——解错的方向是静默的（下游只会表现为「什么都没发生」）。
/// </summary>
public sealed class InspectionResultMesScopeTests
{
    private static readonly Guid RuntimeContextId = Guid.Parse("0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f");

    public static TheoryData<string, string, string, string?, string?> Cases() => new()
    {
        // 工序检：来源单据身份就是工单公开 id。
        { QualityInspectionSourceTypes.Operation, QualityInspectionSourceServices.Mes, "WO-001", "WO-001", null },
        // 首件：{workOrderId}:{operationTaskId} 复合串，两段都能还原。
        { QualityInspectionSourceTypes.FirstArticle, QualityInspectionSourceServices.Mes, "WO-001:OP-010", "WO-001", "OP-010" },
        // 周期检：复合行号里没有工单号，只能还原工序。
        {
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.Mes,
            "OP-010:periodic-time:0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f:3",
            null,
            "OP-010"
        },
        {
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.MesOperation,
            "OP-010:periodic-quantity:0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f:1",
            null,
            "OP-010"
        },
        // 终检：来源单据是入库申请单号，不是工单／工序身份。
        { QualityInspectionSourceTypes.Final, QualityInspectionSourceServices.Mes, "FGR-REQ-001", null, null },
        // 非 MES 归属：收货检的来源单据是收货单号。
        { QualityInspectionSourceTypes.Receiving, QualityInspectionSourceServices.PurchaseReceipt, "RCV-001", null, null },
        { QualityInspectionSourceTypes.CustomerReturn, QualityInspectionSourceServices.CustomerReturn, "RMA-001", null, null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Inspection_result_payload_carries_the_structured_mes_identity(
        string sourceType,
        string sourceService,
        string sourceDocumentId,
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

    [Theory]
    [InlineData(PeriodicInspectionSourceLine.TimeKind)]
    [InlineData(PeriodicInspectionSourceLine.QuantityKind)]
    public void Periodic_source_line_identity_round_trips(string kind)
    {
        var minted = PeriodicInspectionSourceLine.LineId("OP-010", kind, RuntimeContextId, 7);

        Assert.True(PeriodicInspectionSourceLine.TryParseOperationId(minted, out var operationId));
        Assert.Equal("OP-010", operationId);
        Assert.True(PeriodicInspectionSourceLine.IsPeriodicTriggerKey(
            PeriodicInspectionSourceLine.TriggerIdempotencyKey(kind, RuntimeContextId, 7)));
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

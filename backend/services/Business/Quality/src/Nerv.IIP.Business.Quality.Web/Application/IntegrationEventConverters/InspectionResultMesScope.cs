using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

/// <summary>
/// 把检验记录的来源单据身份还原成结构化的 MES 工单／工序身份，供
/// <c>InspectionResultPayload.WorkOrderId</c> / <c>OperationTaskId</c> 发布（#3191）。
///
/// 存在的理由：<c>SourceDocumentId</c> 仍承载两种形状的身份——工序检（含周期检）是工单公开 id、
/// 首件是 <c>{workOrderId}:{operationTaskId}</c> 复合串。消费者（MES 保留上下文、Scheduling
/// 计划失效）需要的是「哪张工单／哪道工序」，让每个消费者各自去猜这些形状，就是把 Quality 的编码
/// 约定复制 N 份。这里在**生产者一侧**解一次，之后跨服务传的就是结构化取值。
///
/// #3319 起周期检的来源行身份不再被搬进 <c>SourceDocumentId</c>，而是留在
/// <c>SourceDocumentLineId</c>，因此周期检的工单号现在也能给出（改前只能给工序）。
/// 工序检的来源行就是工序任务 id，但本处**刻意不发布**它：那会把 MES
/// <c>QualityHoldContext.OperationTaskId</c> 从「整张工单」收窄到「某道工序」，属于另一件事。
/// </summary>
internal static class InspectionResultMesScope
{
    public static (string? WorkOrderId, string? OperationTaskId) Resolve(InspectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!QualityInspectionSourceServices.MesOwned.Contains(record.SourceService, StringComparer.Ordinal))
        {
            return (null, null);
        }

        if (string.Equals(record.SourceType, QualityInspectionSourceTypes.FirstArticle, StringComparison.Ordinal))
        {
            return FirstArticleInspection.TryParseSourceDocumentId(record.SourceDocumentId, out var workOrderId, out var operationId)
                ? (workOrderId, operationId)
                : (null, null);
        }

        if (!string.Equals(record.SourceType, QualityInspectionSourceTypes.Operation, StringComparison.Ordinal))
        {
            // final（来源单据是收货申请单号）等来源环节即使由 MES 触发，来源单据也不是工单／工序身份。
            return (null, null);
        }

        // 工序检与周期检的来源单据身份都是工单；周期检的工序另由来源行的复合窗口身份还原。
        return PeriodicInspectionSourceLine.TryParseOperationId(record.SourceDocumentLineId, out var periodicOperationId)
            ? (record.SourceDocumentId, periodicOperationId)
            : (record.SourceDocumentId, null);
    }
}

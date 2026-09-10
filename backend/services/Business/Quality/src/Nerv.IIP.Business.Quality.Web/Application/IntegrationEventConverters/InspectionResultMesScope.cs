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
///
/// <para><b>存量形状必须继续读得懂。</b>#3319 的迁移不回填，因此库里同时存在两种周期检记录：
/// 新记录是 <c>(工单, 复合窗口身份)</c>，#3319 之前写入的记录是 <c>(复合窗口身份, NULL)</c>。
/// 后者不是惰性数据——复检会把它原样拷到新记录上并重新发布集成事件。只解来源行那一列会让这类
/// 记录解出 <c>(复合窗口身份, null)</c>：MES 按它查工单表与工序任务表两边都查不到，整条结论落
/// <c>unknown-source-document</c> 死信（正是 #3177 修掉的形状），Scheduling 侧则静默不失效。
/// 因此下面保留一条按**同一个结构解码器**识别存量形状的分支，还原成改前的
/// <c>(null, 工序)</c>——那时工单号根本没有被编进任何一列，能还原的确实只有工序。</para>
///
/// <para><b>这条分支的退役条件</b>（可执行判据，不是「以后再说」）：当每个已部署环境的下列查询都返回 0，
/// 它就可以整段删除。
/// <code>
/// SELECT count(*) FROM quality.inspection_records
/// WHERE source_document_line_id IS NULL
///   AND source_type = 'operation'
///   AND source_document_id ~ '^[^:]+:(periodic-time|periodic-quantity):[0-9a-fA-F-]{36}:[0-9]+$';
/// </code>
/// 本票没有用一次数据迁移把它清零，理由写在
/// <c>20260910111924_AddInspectionRecordSourceDocumentLine</c> 的取舍说明里（改写这批记录的来源身份
/// 会把 MES 侧按旧身份建的保留上下文行悬空，那是一次跨服务数据迁移）。</para>
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
        if (PeriodicInspectionSourceLine.TryParseOperationId(record.SourceDocumentLineId, out var periodicOperationId))
        {
            return (record.SourceDocumentId, periodicOperationId);
        }

        // #3319 之前写入的周期检记录：复合窗口身份在**来源单据**那一列，来源行为空。
        // 那时工单号没有被编进任何一列，能还原的只有工序——与改前逐字相同的答案。
        // 守卫写成「来源行为空 且 来源单据是复合窗口身份」，与类注释里那条退役查询的谓词逐字同构：
        // 查询返回 0 的那一刻，这个分支在生产数据上恒不成立，可以整段删除。
        if (record.SourceDocumentLineId is null
            && PeriodicInspectionSourceLine.TryParseOperationId(record.SourceDocumentId, out var legacyOperationId))
        {
            return (null, legacyOperationId);
        }

        return (record.SourceDocumentId, null);
    }
}

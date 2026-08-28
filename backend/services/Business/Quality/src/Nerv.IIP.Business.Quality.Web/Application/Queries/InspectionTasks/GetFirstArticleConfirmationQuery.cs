using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

/// <summary>
/// 某工单某工序的首件判定结论（#2779）。<paramref name="Status"/> 取
/// <see cref="QualityFirstArticleConfirmationStatuses"/>；已判定时 <paramref name="Result"/> 取
/// <see cref="QualityInspectionDispositionStatuses"/> 并配 <paramref name="AttemptNumber"/>（复检累加），
/// 未判定时两者为空。
/// </summary>
public sealed record FirstArticleConfirmationResponse(
    string WorkOrderId,
    string OperationId,
    string Status,
    string? Result,
    int? AttemptNumber,
    InspectionTaskId? InspectionTaskId,
    InspectionRecordId? InspectionRecordId);

/// <summary>
/// <paramref name="OperationId"/> 是 MES 的工序任务标识：工单发布事件按 <c>OperationId</c> 发出，
/// 报工事件按 <c>OperationTaskId</c> 发出，指同一个工序身份。
/// </summary>
public sealed record GetFirstArticleConfirmationQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationId) : IQuery<FirstArticleConfirmationResponse>;

public sealed class GetFirstArticleConfirmationQueryValidator : AbstractValidator<GetFirstArticleConfirmationQuery>
{
    public GetFirstArticleConfirmationQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(150);
    }
}

public sealed class GetFirstArticleConfirmationQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetFirstArticleConfirmationQuery, FirstArticleConfirmationResponse>
{
    public async Task<FirstArticleConfirmationResponse> Handle(
        GetFirstArticleConfirmationQuery request,
        CancellationToken cancellationToken)
    {
        var workOrderId = request.WorkOrderId.Trim();
        var operationId = request.OperationId.Trim();
        // 写面按同一个键建任务，读面不另写一份等价谓词；该键上有唯一索引 ux_inspection_tasks_scope_trigger_key。
        var triggerIdempotencyKey = FirstArticleInspection.TriggerIdempotencyKey(
            request.OrganizationId,
            request.EnvironmentId,
            workOrderId,
            operationId);
        var task = await dbContext.InspectionTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.TriggerIdempotencyKey == triggerIdempotencyKey)
            .Select(x => new
            {
                x.Id,
                x.InspectionRecordId,
                x.SourceDocumentId,
                x.SkuCode,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return await ResolveMissingTaskStatusAsync(request, workOrderId, operationId, cancellationToken);
        }

        // 检验记录 id 只在任务完成时回填，因此它同时是「是否已判定」的判据。
        if (task.InspectionRecordId is null)
        {
            return new FirstArticleConfirmationResponse(
                workOrderId,
                operationId,
                QualityFirstArticleConfirmationStatuses.Pending,
                null,
                null,
                task.Id,
                null);
        }

        // 复检新建 attempt N 记录且不回写任务，因此结论取该来源身份下 attempt 最大的一次，
        // 否则返工复检合格后本契约会永远回吐初检的不合格结论。
        var latest = await dbContext.InspectionRecords
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceType == FirstArticleInspection.SourceType
                && x.SourceService == FirstArticleInspection.SourceService
                && x.SourceDocumentId == task.SourceDocumentId
                && x.SkuCode == task.SkuCode)
            .OrderByDescending(x => x.AttemptNumber)
            .Select(x => new
            {
                x.Id,
                x.Result,
                x.AttemptNumber,
            })
            .FirstAsync(cancellationToken);
        return new FirstArticleConfirmationResponse(
            workOrderId,
            operationId,
            QualityFirstArticleConfirmationStatuses.Decided,
            latest.Result,
            latest.AttemptNumber,
            task.Id,
            latest.Id);
    }

    /// <summary>
    /// 没有首件任务时区分「本工序无需首件」与「应开未开」：只有 Quality 已掌握该工序的 SKU/工作中心
    /// 且没有命中生效首件档，才是无需首件；工序发布事实未到达时按应开未开处理，让门禁 fail closed。
    /// </summary>
    private async Task<FirstArticleConfirmationResponse> ResolveMissingTaskStatusAsync(
        GetFirstArticleConfirmationQuery request,
        string workOrderId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var operation = await dbContext.PeriodicInspectionOperations
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.WorkOrderId == workOrderId
                && x.OperationId == operationId)
            .Select(x => new
            {
                x.SkuCode,
                x.WorkCenterId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        var status = QualityFirstArticleConfirmationStatuses.NotOpened;
        if (!string.IsNullOrWhiteSpace(operation?.SkuCode))
        {
            // 与触发点用同一套档案匹配，避免读面另写一份近似的方案匹配规则。
            var plan = await InspectionTaskGeneration.MatchPlanAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                FirstArticleInspection.SourceType,
                operation.SkuCode,
                operation.WorkCenterId,
                sourceDocumentType: null,
                cancellationToken);
            if (plan is null)
            {
                status = QualityFirstArticleConfirmationStatuses.NotRequired;
            }
        }

        return new FirstArticleConfirmationResponse(workOrderId, operationId, status, null, null, null, null);
    }
}

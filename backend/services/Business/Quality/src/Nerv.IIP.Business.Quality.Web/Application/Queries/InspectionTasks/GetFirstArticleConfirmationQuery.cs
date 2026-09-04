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
        // 写面按同一个键建任务，读面不另写一份等价谓词。键串里已含 org/env，下面仍带这两个谓词不是二次校验，
        // 而是 ux_inspection_tasks_scope_trigger_key 是 (org, env, key) 复合索引，少了前缀列就走不到索引。
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

        var latest = await ResolveLatestAttemptAsync(task.InspectionRecordId, cancellationToken);
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
    /// 复检新建 attempt N 记录且不回写任务，因此结论沿谱系链取。链只按
    /// <c>ReinspectionOfInspectionRecordId</c> 走（每条记录最多一个直接后继，由唯一索引保证），
    /// 不按来源身份重查——来源身份表达不了工序，重查会串到同工单另一道工序的记录上。
    /// </summary>
    private async Task<InspectionAttempt> ResolveLatestAttemptAsync(
        InspectionRecordId inspectionRecordId,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.InspectionRecords
            .AsNoTracking()
            .Where(x => x.Id == inspectionRecordId)
            .Select(x => new InspectionAttempt(x.Id, x.Result, x.AttemptNumber))
            .SingleAsync(cancellationToken);
        while (true)
        {
            var next = await dbContext.InspectionRecords
                .AsNoTracking()
                .Where(x => x.ReinspectionOfInspectionRecordId == current.Id)
                .Select(x => new InspectionAttempt(x.Id, x.Result, x.AttemptNumber))
                .SingleOrDefaultAsync(cancellationToken);
            if (next is null)
            {
                return current;
            }

            current = next;
        }
    }

    private sealed record InspectionAttempt(InspectionRecordId Id, string Result, int AttemptNumber);

    /// <summary>
    /// 没有首件任务时分三种，判据是**该状态靠什么恢复**（#2780）：Quality 不掌握该工序事实时是
    /// <c>not-synchronized</c>（靠工单发布事实到达恢复，与报工无关，消费方 fail closed）；
    /// 掌握了但没命中生效首件档是 <c>not-required</c>；掌握了且命中首件档、任务未开出是
    /// <c>not-opened</c>——开单的唯一触发点是报工事件，所以下一次报工就是首件那一件。
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
        var status = QualityFirstArticleConfirmationStatuses.NotSynchronized;
        if (!string.IsNullOrWhiteSpace(operation?.SkuCode))
        {
            status = QualityFirstArticleConfirmationStatuses.NotOpened;
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

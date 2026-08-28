using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

/// <summary>
/// 某工单某工序的首件判定结论（#2779）。<paramref name="Status"/> 取
/// <see cref="QualityFirstArticleConfirmationStatuses"/>；已判定时 <paramref name="Result"/> 取
/// <see cref="QualityInspectionDispositionStatuses"/>，未判定时为空。
/// </summary>
public sealed record FirstArticleConfirmationResponse(
    string WorkOrderId,
    string OperationId,
    string Status,
    string? Result,
    InspectionTaskId? InspectionTaskId,
    InspectionRecordId? InspectionRecordId,
    DateTimeOffset? DecidedAtUtc);

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
        var task = await dbContext.InspectionTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceType == "first-article"
                && x.SourceService == "mes"
                && x.SourceDocumentId == workOrderId
                && x.SourceDocumentLineId == operationId)
            .Select(x => new
            {
                x.Id,
                x.InspectionRecordId,
                x.CompletedAtUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return new FirstArticleConfirmationResponse(
                workOrderId,
                operationId,
                QualityFirstArticleConfirmationStatuses.NotOpened,
                null,
                null,
                null,
                null);
        }

        // 检验记录 id 只在任务完成时回填，因此它同时是「是否已判定」的判据。
        if (task.InspectionRecordId is null)
        {
            return new FirstArticleConfirmationResponse(
                workOrderId,
                operationId,
                QualityFirstArticleConfirmationStatuses.Pending,
                null,
                task.Id,
                null,
                null);
        }

        var result = await dbContext.InspectionRecords
            .AsNoTracking()
            .Where(x => x.Id == task.InspectionRecordId)
            .Select(x => x.Result)
            .SingleAsync(cancellationToken);
        return new FirstArticleConfirmationResponse(
            workOrderId,
            operationId,
            QualityFirstArticleConfirmationStatuses.Decided,
            result,
            task.Id,
            task.InspectionRecordId,
            task.CompletedAtUtc);
    }
}

using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record GetWorkOrderTransformationQuery(
    string OrganizationId,
    string EnvironmentId,
    WorkOrderTransformationId TransformationId) : IQuery<WorkOrderTransformationReadback>;

public sealed class GetWorkOrderTransformationQueryValidator : AbstractValidator<GetWorkOrderTransformationQuery>
{
    public GetWorkOrderTransformationQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed record WorkOrderTransformationReadback(
    WorkOrderTransformationId TransformationId,
    WorkOrderTransformationType Type,
    string IdempotencyKey,
    string Actor,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyCollection<WorkOrderTransformationLineReadback> Lines);

public sealed record WorkOrderTransformationLineReadback(
    string SourceWorkOrderId,
    string TargetWorkOrderId,
    decimal Quantity,
    string UomCode,
    string SourceStatus,
    string TargetStatus,
    long SourceVersion,
    long TargetVersion);

public sealed class GetWorkOrderTransformationQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWorkOrderTransformationQuery, WorkOrderTransformationReadback>
{
    public async Task<WorkOrderTransformationReadback> Handle(
        GetWorkOrderTransformationQuery request,
        CancellationToken cancellationToken)
    {
        return await dbContext.WorkOrderTransformations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId &&
                x.Id == request.TransformationId)
            .Select(x => new WorkOrderTransformationReadback(
                x.Id,
                x.Type,
                x.IdempotencyKey,
                x.ActorId,
                x.Reason,
                x.OccurredAtUtc,
                x.Lines.OrderBy(line => line.SourceWorkOrderId).ThenBy(line => line.TargetWorkOrderId)
                    .Select(line => new WorkOrderTransformationLineReadback(
                        line.SourceWorkOrderId,
                        line.TargetWorkOrderId,
                        line.Quantity,
                        line.UomCode,
                        line.SourceStatus,
                        line.TargetStatus,
                        line.SourceVersion,
                        line.TargetVersion))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"未找到工单转换记录，TransformationId = {request.TransformationId}");
    }
}

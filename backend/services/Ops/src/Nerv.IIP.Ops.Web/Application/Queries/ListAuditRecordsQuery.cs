using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Queries;

public sealed record ListAuditRecordsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? OperationTaskId) : IQuery<AuditRecordListResponse>;

public sealed class ListAuditRecordsQueryHandler(IServiceProvider serviceProvider)
    : IQueryHandler<ListAuditRecordsQuery, AuditRecordListResponse>
{
    private const int MaxAuditRecordsResponseSize = 500;

    public async Task<AuditRecordListResponse> Handle(ListAuditRecordsQuery request, CancellationToken cancellationToken)
    {
        var context = serviceProvider.GetService<ApplicationDbContext>();
        if (context is null)
        {
            return serviceProvider.GetRequiredService<IOpsStateStore>()
                .ListAuditRecords(request.OrganizationId, request.EnvironmentId, request.OperationTaskId);
        }

        var query = context.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.OperationTaskId))
        {
            query = query.Where(x => x.Id.Id == request.OperationTaskId);
        }

        var items = await query
            .SelectMany(x => x.AuditRecords)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(MaxAuditRecordsResponseSize)
            .Select(x => new AuditRecordSummary(
                x.Id.Id,
                x.OperationTaskId.Id,
                x.Action,
                x.Actor,
                x.OccurredAtUtc,
                x.CorrelationId))
            .ToListAsync(cancellationToken);

        return new AuditRecordListResponse(items);
    }
}

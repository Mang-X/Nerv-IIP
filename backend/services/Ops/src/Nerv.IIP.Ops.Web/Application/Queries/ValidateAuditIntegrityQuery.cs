using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Infrastructure;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Ops.Web.Application.Queries;

public sealed record ValidateAuditIntegrityQuery(
    string OrganizationId,
    string EnvironmentId) : IQuery<AuditIntegrityValidationResponse>;

public sealed class ValidateAuditIntegrityQueryHandler(IServiceProvider serviceProvider)
    : IQueryHandler<ValidateAuditIntegrityQuery, AuditIntegrityValidationResponse>
{
    public async Task<AuditIntegrityValidationResponse> Handle(ValidateAuditIntegrityQuery request, CancellationToken cancellationToken)
    {
        var context = serviceProvider.GetService<ApplicationDbContext>();
        if (context is null)
        {
            return ToContract(serviceProvider.GetRequiredService<IOpsStateStore>()
                .ValidateAuditIntegrity(request.OrganizationId, request.EnvironmentId));
        }

        var query = context.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var records = await query
            .SelectMany(x => x.AuditRecords)
            .OrderBy(x => x.SequenceNo)
            .ThenBy(x => x.Id)
            .Select(x => new AuditRecordFact(
                x.Id.Id,
                x.OperationTaskId.Id,
                x.SequenceNo,
                x.PreviousIntegrityHash,
                x.Action,
                x.Actor,
                x.OccurredAtUtc,
                x.CorrelationId,
                x.IntegrityHash))
            .ToListAsync(cancellationToken);

        return ToContract(AuditIntegrityValidator.Validate(records));
    }

    private static AuditIntegrityValidationResponse ToContract(AuditIntegrityValidationResult result)
    {
        return new AuditIntegrityValidationResponse(
            result.IsValid,
            result.CheckedRecords,
            result.FirstInvalidAuditRecordId,
            result.FirstInvalidSequenceNo,
            result.FailureCode,
            result.FailureMessage);
    }
}

using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record ApprovalPrincipalDepartmentResponse(string? DepartmentId);

public sealed record GetApprovalPrincipalDepartmentQuery(
    string OrganizationId,
    string EnvironmentId,
    string UserId) : IQuery<ApprovalPrincipalDepartmentResponse>;

public sealed class GetApprovalPrincipalDepartmentQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetApprovalPrincipalDepartmentQuery, ApprovalPrincipalDepartmentResponse>
{
    public async Task<ApprovalPrincipalDepartmentResponse> Handle(
        GetApprovalPrincipalDepartmentQuery request, CancellationToken cancellationToken)
    {
        var workers = await PrincipalWorkerMapping.Find(
                dbContext, request.OrganizationId, request.EnvironmentId, request.UserId)
            .Select(worker => new { worker.EmploymentStatus, worker.DepartmentCode })
            .ToListAsync(cancellationToken);

        return new ApprovalPrincipalDepartmentResponse(
            workers is [var worker] && worker.EmploymentStatus == Worker.StatusActive
                ? worker.DepartmentCode
                : null);
    }
}

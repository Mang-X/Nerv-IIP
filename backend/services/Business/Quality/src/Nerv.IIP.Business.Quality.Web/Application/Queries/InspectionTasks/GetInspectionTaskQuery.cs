using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Errors;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

public sealed record InspectionTaskCharacteristicResponse(
    string CharacteristicCode,
    string Name,
    string Method,
    string Severity,
    bool IsRequired,
    string SamplingRule,
    string CharacteristicType,
    decimal? NominalValue,
    decimal? LowerSpecLimit,
    decimal? UpperSpecLimit,
    string? UnitCode);

public sealed record InspectionTaskDetailResponse(
    InspectionTaskResponse Task,
    string PlanCode,
    string Category,
    IReadOnlyCollection<InspectionTaskCharacteristicResponse> Characteristics);

public sealed record GetInspectionTaskQuery(
    InspectionTaskId InspectionTaskId,
    string OrganizationId,
    string EnvironmentId,
    string ScopeKind,
    string PrincipalId,
    IReadOnlyCollection<string> AuthorizedTeamIds,
    DateTimeOffset? AsOfUtc = null) : IQuery<InspectionTaskDetailResponse>;

public sealed class GetInspectionTaskQueryValidator : AbstractValidator<GetInspectionTaskQuery>
{
    public GetInspectionTaskQueryValidator()
    {
        RuleFor(x => x.InspectionTaskId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).Must(x => x is "self" or "team" or "organization");
        RuleFor(x => x.PrincipalId).NotEmpty().MaximumLength(150);
    }
}

public sealed class GetInspectionTaskQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetInspectionTaskQuery, InspectionTaskDetailResponse>
{
    public async Task<InspectionTaskDetailResponse> Handle(
        GetInspectionTaskQuery request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.InspectionTasks.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.InspectionTaskId
                && x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId,
            cancellationToken)
            ?? throw new KnownException($"Inspection task '{request.InspectionTaskId}' was not found.");
        var inSelectedScope = request.ScopeKind switch
        {
            "self" => string.Equals(
                task.AssignedUserId,
                request.PrincipalId,
                StringComparison.Ordinal),
            "team" => task.AssignedTeamId is not null
                && request.AuthorizedTeamIds.Contains(task.AssignedTeamId, StringComparer.Ordinal),
            "organization" => true,
            _ => false,
        };
        if (!inSelectedScope)
        {
            throw QualityAuthorizationException.Forbidden("task-outside-selected-work-scope");
        }

        var plan = await dbContext.InspectionPlans.AsNoTracking()
            .Include(x => x.Characteristics)
            .SingleAsync(x => x.Id == task.InspectionPlanId, cancellationToken);
        var actorOwnsTask = string.Equals(
            task.AssignedUserId,
            request.PrincipalId,
            StringComparison.Ordinal);
        var actorOwnsTeam = task.AssignedTeamId is not null
            && request.AuthorizedTeamIds.Contains(task.AssignedTeamId, StringComparer.Ordinal);
        string[] actions = task.Status switch
        {
            InspectionTaskStatuses.Pending when actorOwnsTask
                || (task.AssignedUserId is null && actorOwnsTeam) => ["claim"],
            InspectionTaskStatuses.InProgress when actorOwnsTask => ["submit-inspection"],
            _ => [],
        };
        var blocks = InspectionTaskBlockReasonResolver.Resolve(
            task.Status,
            task.AssignedUserId,
            task.AssignedTeamId,
            actorOwnsTask,
            actorOwnsTeam,
            actions.Length > 0);
        var asOfUtc = request.AsOfUtc ?? DateTimeOffset.UtcNow;
        var response = new InspectionTaskResponse(
            task.Id,
            task.InspectionPlanId,
            task.SourceType,
            task.SourceService,
            task.SourceDocumentId,
            task.SourceDocumentLineId,
            task.SkuCode,
            task.Quantity,
            task.UomCode,
            task.BatchNo,
            task.SerialNo,
            task.Status,
            task.AssignedUserId,
            task.AssignedTeamId,
            task.Version,
            task.Status != InspectionTaskStatuses.Completed && task.DueAtUtc < asOfUtc,
            actions,
            blocks,
            task.DueAtUtc,
            task.CreatedAtUtc,
            task.InspectionRecordId);
        return new InspectionTaskDetailResponse(
            response,
            plan.PlanCode,
            plan.Category,
            plan.Characteristics.Select(x => new InspectionTaskCharacteristicResponse(
                x.CharacteristicCode,
                x.Name,
                x.Method,
                x.Severity,
                x.IsRequired,
                x.SamplingRule,
                x.CharacteristicType,
                x.NominalValue,
                x.LowerSpecLimit,
                x.UpperSpecLimit,
                x.UnitCode)).ToArray());
    }
}

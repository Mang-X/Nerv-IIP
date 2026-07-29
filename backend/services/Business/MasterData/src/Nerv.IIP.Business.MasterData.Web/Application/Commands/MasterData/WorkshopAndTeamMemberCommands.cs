using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record CreateWorkshopCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? ManagerUserId,
    string? Description,
    string? IdempotencyKey = null,
    MasterDataIntegrationEventContext? AuditContext = null) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkshopCommandHandler(
    IWorkshopRepository repository,
    MasterDataCodingService? codingService = null,
    ApplicationDbContext? dbContext = null)
    : ICommandHandler<CreateWorkshopCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkshopCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "workshop",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.Name, request.SiteCode, request.ManagerUserId, request.Description),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("workshop", allocation.Code, request.Name);
        }

        var code = allocation.Code;
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken))
        {
            throw new KnownException($"Workshop '{code}' already exists.");
        }

        var workshop = Workshop.Create(
            request.OrganizationId,
            request.EnvironmentId,
            code,
            request.Name,
            request.SiteCode,
            request.ManagerUserId,
            request.Description);
        await repository.AddAsync(workshop, cancellationToken);
        MasterDataScopeContextAudit.AddCreated(
            dbContext ?? throw new KnownException("A scope audit store is required for workshop creation."),
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "workshop",
            workshop.Id.ToString(),
            workshop.Code,
            new { siteCode = workshop.SiteCode, disabled = workshop.Disabled });
        return new MasterDataResourceResult("workshop", workshop.Code, workshop.Name);
    }
}

public sealed record AddTeamMemberCommand(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    bool IsLeader,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    MasterDataIntegrationEventContext AuditContext) : ICommand<MasterDataResourceResult>;

public sealed class AddTeamMemberCommandHandler(ITeamMemberRepository repository, ApplicationDbContext dbContext)
    : ICommandHandler<AddTeamMemberCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(AddTeamMemberCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsActiveAsync(request.OrganizationId, request.EnvironmentId, request.TeamCode, request.UserId, cancellationToken))
        {
            throw new KnownException($"Team member '{request.TeamCode}:{request.UserId}' already exists.");
        }

        var member = TeamMember.Assign(
            request.OrganizationId,
            request.EnvironmentId,
            request.TeamCode,
            request.UserId,
            request.IsLeader,
            request.EffectiveFrom,
            request.EffectiveTo);
        await repository.AddAsync(member, cancellationToken);
        MasterDataScopeContextAudit.Add(
            dbContext,
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "team-member-assigned",
            "team-member",
            member.Id.ToString(),
            member.Code,
            member.Code,
            before: null,
            after: new
            {
                teamCode = member.TeamCode,
                userId = member.UserId,
                isLeader = member.IsLeader,
                effectiveFrom = member.EffectiveFrom,
                effectiveTo = member.EffectiveTo,
                disabled = member.Disabled,
            },
            "scope-candidate-assigned");
        return new MasterDataResourceResult("team-member", member.Code, member.UserId);
    }
}

public sealed record RemoveTeamMemberCommand(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    string Reason,
    MasterDataIntegrationEventContext AuditContext) : ICommand<MasterDataResourceResult>;

public sealed class RemoveTeamMemberCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RemoveTeamMemberCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var member = await dbContext.TeamMembers
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.TeamCode == request.TeamCode &&
                x.UserId == request.UserId &&
                !x.Disabled,
                cancellationToken)
            ?? throw new KnownException($"Team member '{request.TeamCode}:{request.UserId}' was not found.");

        var before = new
        {
            teamCode = member.TeamCode,
            userId = member.UserId,
            isLeader = member.IsLeader,
            effectiveFrom = member.EffectiveFrom,
            effectiveTo = member.EffectiveTo,
            disabled = member.Disabled,
        };
        member.Remove(string.IsNullOrWhiteSpace(request.Reason) ? "removed" : request.Reason);
        MasterDataScopeContextAudit.Add(
            dbContext,
            request.AuditContext,
            request.OrganizationId,
            request.EnvironmentId,
            "team-member-removed",
            "team-member",
            member.Id.ToString(),
            member.Code,
            member.Code,
            before,
            after: new
            {
                teamCode = member.TeamCode,
                userId = member.UserId,
                isLeader = member.IsLeader,
                effectiveFrom = member.EffectiveFrom,
                effectiveTo = member.EffectiveTo,
                disabled = member.Disabled,
            },
            string.IsNullOrWhiteSpace(request.Reason) ? "removed" : request.Reason.Trim());
        return new MasterDataResourceResult("team-member", member.Code, member.UserId);
    }
}

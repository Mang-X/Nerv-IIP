using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record CreateWorkshopCommand(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string SiteCode,
    string? ManagerUserId,
    string? Description) : ICommand<MasterDataResourceResult>;

public sealed class CreateWorkshopCommandHandler(IWorkshopRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateWorkshopCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateWorkshopCommand request, CancellationToken cancellationToken)
    {
        var code = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "workshop",
            request.OrganizationId,
            request.EnvironmentId,
            request.Code,
            MasterDataCodingService.Fingerprint(request.Name, request.SiteCode, request.ManagerUserId, request.Description),
            cancellationToken);
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
    DateOnly? EffectiveTo) : ICommand<MasterDataResourceResult>;

public sealed class AddTeamMemberCommandHandler(ITeamMemberRepository repository)
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
        return new MasterDataResourceResult("team-member", member.Code, member.UserId);
    }
}

public sealed record RemoveTeamMemberCommand(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    string UserId,
    string Reason) : ICommand<MasterDataResourceResult>;

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

        member.Remove(string.IsNullOrWhiteSpace(request.Reason) ? "removed" : request.Reason);
        return new MasterDataResourceResult("team-member", member.Code, member.UserId);
    }
}

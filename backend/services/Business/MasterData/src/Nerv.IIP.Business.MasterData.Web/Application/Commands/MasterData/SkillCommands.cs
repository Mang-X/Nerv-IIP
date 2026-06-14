using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record CreateSkillCommand(
    string OrganizationId,
    string EnvironmentId,
    string? SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description,
    string? IdempotencyKey = null) : ICommand<MasterDataResourceResult>;

public sealed record UpdateSkillCommand(
    string OrganizationId,
    string EnvironmentId,
    string SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description) : ICommand<SkillItem>;

public sealed record ArchiveSkillCommand(
    string OrganizationId,
    string EnvironmentId,
    string SkillCode,
    string Reason) : ICommand<SkillItem>;

public sealed class CreateSkillCommandHandler(ISkillRepository repository, MasterDataCodingService? codingService = null)
    : ICommandHandler<CreateSkillCommand, MasterDataResourceResult>
{
    public async Task<MasterDataResourceResult> Handle(CreateSkillCommand request, CancellationToken cancellationToken)
    {
        var allocation = await MasterDataCodeGenerator.AllocateAsync(
            codingService,
            "skill",
            request.OrganizationId,
            request.EnvironmentId,
            request.SkillCode,
            request.IdempotencyKey,
            MasterDataCodingService.Fingerprint(request.SkillName, request.GroupName, request.RequiresCertification, request.ValidityMonths, request.Description),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MasterDataResourceResult("skill", allocation.Code, request.SkillName);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, cancellationToken))
        {
            throw new KnownException($"Skill '{allocation.Code}' already exists.");
        }

        var skill = Skill.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SkillName,
            request.GroupName,
            request.RequiresCertification,
            request.ValidityMonths,
            request.Description);
        await repository.AddAsync(skill, cancellationToken);
        return new MasterDataResourceResult("skill", skill.SkillCode, skill.SkillName);
    }
}

public sealed class UpdateSkillCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UpdateSkillCommand, SkillItem>
{
    public async Task<SkillItem> Handle(UpdateSkillCommand request, CancellationToken cancellationToken)
    {
        var skill = await FindAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.SkillCode, cancellationToken);
        skill.Update(request.SkillName, request.GroupName, request.RequiresCertification, request.ValidityMonths, request.Description);
        return ListSkillsQueryHandler.ToItem(skill);
    }

    internal static async Task<Skill> FindAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string skillCode,
        CancellationToken cancellationToken)
    {
        return await dbContext.Skills.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.SkillCode == skillCode,
            cancellationToken)
            ?? throw new KnownException($"Skill '{skillCode}' was not found.");
    }
}

public sealed class ArchiveSkillCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ArchiveSkillCommand, SkillItem>
{
    public async Task<SkillItem> Handle(ArchiveSkillCommand request, CancellationToken cancellationToken)
    {
        var skill = await UpdateSkillCommandHandler.FindAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkillCode,
            cancellationToken);
        skill.Disable(MasterDataArchiveReason.Normalize(request.Reason));
        return ListSkillsQueryHandler.ToItem(skill);
    }
}

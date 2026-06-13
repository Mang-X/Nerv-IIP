using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.Coding;
using System.Text.Json;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed record CodeRuleVersionResponse(
    string RuleKey,
    int Version,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason);

public sealed record CodeRulePreviewResponse(string RuleKey, string SampleCode);

public sealed record CreateCodeRuleVersionCommand(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    string DisplayName,
    string AppliesTo,
    ScopeDimension Scope,
    IReadOnlyList<CodeRuleSegment> Segments,
    bool IsActive,
    DateTimeOffset EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason) : ICommand<CodeRuleVersionResponse>;

public sealed record PreviewCodeRuleCommand(
    string OrganizationId,
    string EnvironmentId,
    string RuleKey,
    IReadOnlyList<CodeRuleSegment> Segments,
    IReadOnlyDictionary<string, string>? Fields,
    string SiteCode = "") : ICommand<CodeRulePreviewResponse>;

public sealed class CreateCodeRuleVersionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateCodeRuleVersionCommand, CodeRuleVersionResponse>
{
    public async Task<CodeRuleVersionResponse> Handle(CreateCodeRuleVersionCommand request, CancellationToken cancellationToken)
    {
        var current = await dbContext.CodeRules.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.RuleKey == request.RuleKey,
            cancellationToken);

        var latestVersion = await dbContext.CodeRuleVersions
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RuleKey == request.RuleKey)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken);
        var nextVersion = Math.Max(current?.Version ?? 0, latestVersion ?? 0) + 1;

        var definition = new CodeRuleDefinition
        {
            RuleKey = request.RuleKey,
            DisplayName = request.DisplayName,
            AppliesTo = request.AppliesTo,
            Scope = request.Scope,
            Segments = request.Segments,
            IsActive = request.IsActive,
            Version = nextVersion,
        };
        definition.Validate();

        var now = DateTimeOffset.UtcNow;
        var status = request.EffectiveFromUtc <= now ? CodeRuleVersionStatus.Active : CodeRuleVersionStatus.Scheduled;
        var segmentsJson = JsonSerializer.Serialize(request.Segments, CodeRuleJson.Options);
        var version = CodeRuleVersion.Record(
            request.OrganizationId,
            request.EnvironmentId,
            request.RuleKey,
            request.DisplayName,
            request.AppliesTo,
            (int)request.Scope,
            segmentsJson,
            request.IsActive,
            nextVersion,
            status,
            request.EffectiveFromUtc,
            request.CreatedBy,
            request.ChangeReason,
            now);
        dbContext.CodeRuleVersions.Add(version);

        if (status == CodeRuleVersionStatus.Active)
        {
            if (current is null)
            {
                dbContext.CodeRules.Add(CodeRule.Create(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.RuleKey,
                    request.DisplayName,
                    request.AppliesTo,
                    (int)request.Scope,
                    segmentsJson,
                    request.IsActive,
                    nextVersion));
            }
            else
            {
                current.ReplaceDefinition(
                    request.DisplayName,
                    request.AppliesTo,
                    (int)request.Scope,
                    segmentsJson,
                    request.IsActive,
                    nextVersion);
            }
        }

        return new CodeRuleVersionResponse(
            request.RuleKey,
            nextVersion,
            status,
            request.EffectiveFromUtc,
            request.CreatedBy,
            request.ChangeReason);
    }
}

public sealed class PreviewCodeRuleCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<PreviewCodeRuleCommand, CodeRulePreviewResponse>
{
    public async Task<CodeRulePreviewResponse> Handle(PreviewCodeRuleCommand request, CancellationToken cancellationToken)
    {
        var current = await dbContext.CodeRules
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RuleKey == request.RuleKey,
                cancellationToken);

        var definition = new CodeRuleDefinition
        {
            RuleKey = request.RuleKey,
            DisplayName = current?.DisplayName ?? request.RuleKey,
            AppliesTo = current?.AppliesTo ?? string.Empty,
            Scope = current is null ? ScopeDimension.Organization | ScopeDimension.Environment : (ScopeDimension)current.Scope,
            Segments = request.Segments,
            IsActive = true,
            Version = current?.Version ?? 1,
        };

        var allocation = await new CodeAllocator().AllocateAsync(
            new CodeAllocationRequest(
                request.OrganizationId,
                request.EnvironmentId,
                definition,
                request.Fields,
                null,
                null,
                CodeAllocator.Fingerprint(request.RuleKey, "preview"),
                "code rule preview",
                request.SiteCode),
            cancellationToken);

        return new CodeRulePreviewResponse(request.RuleKey, allocation.Code);
    }
}

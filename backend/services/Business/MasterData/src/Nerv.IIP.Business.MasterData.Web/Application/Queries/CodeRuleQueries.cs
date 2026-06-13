using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Contracts.Coding;
using System.Text.Json;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record CodeRuleItem(
    string RuleKey,
    string DisplayName,
    string AppliesTo,
    ScopeDimension Scope,
    IReadOnlyList<CodeRuleSegment> Segments,
    bool IsActive,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CodeRuleVersionItem(
    string RuleKey,
    int Version,
    string Status,
    DateTimeOffset EffectiveFromUtc,
    string CreatedBy,
    string ChangeReason,
    DateTimeOffset CreatedAtUtc);

public sealed record ListCodeRulesResponse(IReadOnlyCollection<CodeRuleItem> Rules);

public sealed record CodeRuleDetailResponse(CodeRuleItem Rule, IReadOnlyCollection<CodeRuleVersionItem> Versions);

public sealed record ListCodeRulesQuery(string OrganizationId, string EnvironmentId) : IQuery<ListCodeRulesResponse>;

public sealed record GetCodeRuleDetailQuery(string OrganizationId, string EnvironmentId, string RuleKey) : IQuery<CodeRuleDetailResponse>;

public sealed class ListCodeRulesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListCodeRulesQuery, ListCodeRulesResponse>
{
    public async Task<ListCodeRulesResponse> Handle(ListCodeRulesQuery request, CancellationToken cancellationToken)
    {
        var rules = await dbContext.CodeRules
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderBy(x => x.RuleKey)
            .Select(x => ToItem(x.RuleKey, x.DisplayName, x.AppliesTo, x.Scope, x.SegmentsJson, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListCodeRulesResponse(rules);
    }

    internal static CodeRuleItem ToItem(
        string ruleKey,
        string displayName,
        string appliesTo,
        int scope,
        string segmentsJson,
        bool isActive,
        int version,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(
            ruleKey,
            displayName,
            appliesTo,
            (ScopeDimension)scope,
            DeserializeSegments(segmentsJson),
            isActive,
            version,
            createdAtUtc,
            updatedAtUtc);

    internal static IReadOnlyList<CodeRuleSegment> DeserializeSegments(string segmentsJson)
    {
        return JsonSerializer.Deserialize<IReadOnlyList<CodeRuleSegment>>(segmentsJson, CodeRuleJson.Options) ?? [];
    }
}

public sealed class GetCodeRuleDetailQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetCodeRuleDetailQuery, CodeRuleDetailResponse>
{
    public async Task<CodeRuleDetailResponse> Handle(GetCodeRuleDetailQuery request, CancellationToken cancellationToken)
    {
        var rule = await dbContext.CodeRules
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RuleKey == request.RuleKey,
                cancellationToken)
            ?? throw new KnownException($"Code rule '{request.RuleKey}' was not found.");

        var versions = await dbContext.CodeRuleVersions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RuleKey == request.RuleKey)
            .OrderByDescending(x => x.Version)
            .Select(x => new CodeRuleVersionItem(
                x.RuleKey,
                x.Version,
                x.Status,
                x.EffectiveFromUtc,
                x.CreatedBy,
                x.ChangeReason,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new CodeRuleDetailResponse(
            ListCodeRulesQueryHandler.ToItem(
                rule.RuleKey,
                rule.DisplayName,
                rule.AppliesTo,
                rule.Scope,
                rule.SegmentsJson,
                rule.IsActive,
                rule.Version,
                rule.CreatedAtUtc,
                rule.UpdatedAtUtc),
            versions);
    }
}

internal static class CodeRuleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

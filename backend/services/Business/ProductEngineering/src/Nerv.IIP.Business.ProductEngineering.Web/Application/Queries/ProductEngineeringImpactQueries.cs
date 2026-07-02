using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;

public sealed record BomDiffFieldChange(string FieldName, string? OldValue, string? NewValue);

public sealed record BomDiffLineItem(
    string ChangeType,
    string? OldItemCode,
    string? NewItemCode,
    decimal? OldQuantity,
    decimal? NewQuantity,
    string? OldUnitOfMeasureCode,
    string? NewUnitOfMeasureCode,
    decimal? OldScrapRate,
    decimal? NewScrapRate,
    decimal? OldYieldRate,
    decimal? NewYieldRate,
    string? OldAlternateGroup,
    string? NewAlternateGroup,
    string? OldSubstituteSkuCodes,
    string? NewSubstituteSkuCodes,
    IReadOnlyCollection<BomDiffFieldChange> FieldChanges);

public sealed record BomDiffSummary(int Added, int Removed, int Replaced, int Changed);

public sealed record BomDiffResponse(
    string BomKind,
    string FromVersionId,
    string ToVersionId,
    string RootItemCode,
    IReadOnlyCollection<BomDiffLineItem> Lines,
    BomDiffSummary Summary);

public sealed record GetBomDiffQuery(
    string OrganizationId,
    string EnvironmentId,
    string BomKind,
    string FromBomCode,
    string FromRevision,
    string ToBomCode,
    string ToRevision) : IQuery<BomDiffResponse>;

public sealed record EngineeringChangeImpactAffectedVersionInput(
    string VersionKind,
    string VersionId);

public sealed record EngineeringChangeImpactNode(
    string NodeType,
    string VersionId,
    string DisplayName,
    string ImpactLevel,
    string? RelatedVersionId,
    string? SkuCode,
    string? ConsoleRoute);

public sealed record EngineeringChangeImpactRisk(
    string Code,
    string Severity,
    string Message,
    string? RelatedVersionId);

public sealed record EngineeringChangeImpactPreviewResponse(
    DateOnly EffectiveDate,
    IReadOnlyCollection<EngineeringChangeImpactNode> Nodes,
    IReadOnlyCollection<EngineeringChangeImpactRisk> Risks);

public sealed record GetEngineeringChangeImpactPreviewQuery(
    string OrganizationId,
    string EnvironmentId,
    DateOnly EffectiveDate,
    IReadOnlyCollection<EngineeringChangeImpactAffectedVersionInput> AffectedVersions) : IQuery<EngineeringChangeImpactPreviewResponse>;

public sealed class GetBomDiffQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetBomDiffQuery, BomDiffResponse>
{
    public async Task<BomDiffResponse> Handle(GetBomDiffQuery request, CancellationToken cancellationToken)
    {
        return NormalizeBomKind(request.BomKind) switch
        {
            "engineering-bom" => await DiffEngineeringBomAsync(request, cancellationToken),
            "manufacturing-bom" => await DiffManufacturingBomAsync(request, cancellationToken),
            _ => throw new KnownException("BOM kind is invalid. Allowed values: EngineeringBom, ManufacturingBom.")
        };
    }

    private async Task<BomDiffResponse> DiffEngineeringBomAsync(GetBomDiffQuery request, CancellationToken cancellationToken)
    {
        var source = await dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.BomCode == request.FromBomCode
                && x.Revision == request.FromRevision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Engineering BOM '{request.FromBomCode}' revision '{request.FromRevision}' was not found.");
        var target = await dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.BomCode == request.ToBomCode
                && x.Revision == request.ToRevision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Engineering BOM '{request.ToBomCode}' revision '{request.ToRevision}' was not found.");

        if (!string.Equals(source.ParentItemCode, target.ParentItemCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("Engineering BOM versions must share the same parent item before diff.");
        }

        var lines = DiffLines(
            source.Lines.Select(BomComparableLine.FromEngineering),
            target.Lines.Select(BomComparableLine.FromEngineering));

        return new BomDiffResponse(
            "EngineeringBom",
            VersionId(source.BomCode, source.Revision),
            VersionId(target.BomCode, target.Revision),
            source.ParentItemCode,
            lines,
            Summarize(lines));
    }

    private async Task<BomDiffResponse> DiffManufacturingBomAsync(GetBomDiffQuery request, CancellationToken cancellationToken)
    {
        var source = await dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.BomCode == request.FromBomCode
                && x.Revision == request.FromRevision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Manufacturing BOM '{request.FromBomCode}' revision '{request.FromRevision}' was not found.");
        var target = await dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.BomCode == request.ToBomCode
                && x.Revision == request.ToRevision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Manufacturing BOM '{request.ToBomCode}' revision '{request.ToRevision}' was not found.");

        if (!string.Equals(source.SkuCode, target.SkuCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("Manufacturing BOM versions must share the same SKU before diff.");
        }

        var lines = DiffLines(
            source.MaterialLines.Select(BomComparableLine.FromManufacturing),
            target.MaterialLines.Select(BomComparableLine.FromManufacturing));

        return new BomDiffResponse(
            "ManufacturingBom",
            VersionId(source.BomCode, source.Revision),
            VersionId(target.BomCode, target.Revision),
            source.SkuCode,
            lines,
            Summarize(lines));
    }

    private static IReadOnlyCollection<BomDiffLineItem> DiffLines(
        IEnumerable<BomComparableLine> sourceLines,
        IEnumerable<BomComparableLine> targetLines)
    {
        var source = sourceLines.ToDictionary(x => x.ItemCode, StringComparer.OrdinalIgnoreCase);
        var target = targetLines.ToDictionary(x => x.ItemCode, StringComparer.OrdinalIgnoreCase);
        var matchedSource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BomDiffLineItem>();

        foreach (var sourceLine in source.Values.OrderBy(x => x.ItemCode))
        {
            if (!target.TryGetValue(sourceLine.ItemCode, out var targetLine))
            {
                continue;
            }

            matchedSource.Add(sourceLine.ItemCode);
            matchedTarget.Add(targetLine.ItemCode);
            var changes = FieldChanges(sourceLine, targetLine);
            if (changes.Count > 0)
            {
                result.Add(ToDiff("changed", sourceLine, targetLine, changes));
            }
        }

        var removed = source.Values
            .Where(x => !matchedSource.Contains(x.ItemCode))
            .OrderBy(x => x.ItemCode)
            .ToList();
        var added = target.Values
            .Where(x => !matchedTarget.Contains(x.ItemCode))
            .OrderBy(x => x.ItemCode)
            .ToList();

        foreach (var pair in MatchReplacements(removed, added))
        {
            matchedSource.Add(pair.Source.ItemCode);
            matchedTarget.Add(pair.Target.ItemCode);
            result.Add(ToDiff("replaced", pair.Source, pair.Target, FieldChanges(pair.Source, pair.Target)));
        }

        result.AddRange(removed
            .Where(x => !matchedSource.Contains(x.ItemCode))
            .Select(x => ToDiff("removed", x, null, [])));
        result.AddRange(added
            .Where(x => !matchedTarget.Contains(x.ItemCode))
            .Select(x => ToDiff("added", null, x, [])));

        return result
            .OrderBy(x => x.ChangeType, StringComparer.Ordinal)
            .ThenBy(x => x.OldItemCode ?? x.NewItemCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(BomComparableLine Source, BomComparableLine Target)> MatchReplacements(
        IReadOnlyCollection<BomComparableLine> removed,
        IReadOnlyCollection<BomComparableLine> added)
    {
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in removed)
        {
            var target = added.FirstOrDefault(candidate =>
                !usedTargets.Contains(candidate.ItemCode) &&
                !string.IsNullOrWhiteSpace(source.AlternateGroup) &&
                string.Equals(source.AlternateGroup, candidate.AlternateGroup, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                continue;
            }

            usedTargets.Add(target.ItemCode);
            yield return (source, target);
        }
    }

    private static List<BomDiffFieldChange> FieldChanges(BomComparableLine source, BomComparableLine target)
    {
        var changes = new List<BomDiffFieldChange>();
        AddChange(changes, "quantity", source.Quantity, target.Quantity);
        AddChange(changes, "unitOfMeasureCode", source.UnitOfMeasureCode, target.UnitOfMeasureCode);
        AddChange(changes, "scrapRate", source.ScrapRate, target.ScrapRate);
        AddChange(changes, "yieldRate", source.YieldRate, target.YieldRate);
        AddChange(changes, "isPhantom", source.IsPhantom, target.IsPhantom);
        AddChange(changes, "alternateGroup", source.AlternateGroup, target.AlternateGroup);
        AddChange(changes, "alternatePriority", source.AlternatePriority, target.AlternatePriority);
        AddChange(changes, "substituteSkuCodes", source.SubstituteSkuCodes, target.SubstituteSkuCodes);
        AddChange(changes, "referenceDesignators", source.ReferenceDesignators, target.ReferenceDesignators);
        AddChange(changes, "backflush", source.Backflush, target.Backflush);
        return changes;
    }

    private static void AddChange<T>(List<BomDiffFieldChange> changes, string fieldName, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        changes.Add(new BomDiffFieldChange(fieldName, FormatValue(oldValue), FormatValue(newValue)));
    }

    private static BomDiffLineItem ToDiff(
        string changeType,
        BomComparableLine? source,
        BomComparableLine? target,
        IReadOnlyCollection<BomDiffFieldChange> changes) =>
        new(
            changeType,
            source?.ItemCode,
            target?.ItemCode,
            source?.Quantity,
            target?.Quantity,
            source?.UnitOfMeasureCode,
            target?.UnitOfMeasureCode,
            source?.ScrapRate,
            target?.ScrapRate,
            source?.YieldRate,
            target?.YieldRate,
            source?.AlternateGroup,
            target?.AlternateGroup,
            source?.SubstituteSkuCodes,
            target?.SubstituteSkuCodes,
            changes);

    private static BomDiffSummary Summarize(IReadOnlyCollection<BomDiffLineItem> lines) =>
        new(
            lines.Count(x => x.ChangeType == "added"),
            lines.Count(x => x.ChangeType == "removed"),
            lines.Count(x => x.ChangeType == "replaced"),
            lines.Count(x => x.ChangeType == "changed"));

    private static string NormalizeBomKind(string bomKind)
    {
        var normalized = bomKind.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "engineeringbom" or "engineering-bom" => "engineering-bom",
            "manufacturingbom" or "manufacturing-bom" => "manufacturing-bom",
            _ => normalized
        };
    }

    private static string VersionId(string code, string revision) => $"{code}:{revision}";

    private static string? FormatValue<T>(T value) =>
        value switch
        {
            null => null,
            decimal decimalValue => decimalValue.ToString("0.#############################", CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

    private sealed record BomComparableLine(
        string ItemCode,
        decimal Quantity,
        string UnitOfMeasureCode,
        decimal ScrapRate,
        decimal YieldRate,
        bool IsPhantom,
        string? AlternateGroup,
        int? AlternatePriority,
        string? SubstituteSkuCodes,
        string? ReferenceDesignators,
        bool Backflush)
    {
        public static BomComparableLine FromEngineering(EngineeringBomLine line) =>
            new(
                line.ChildItemCode,
                line.Quantity,
                line.UnitOfMeasureCode,
                line.ScrapRate,
                line.YieldRate,
                line.IsPhantom,
                line.AlternateGroup,
                line.AlternatePriority,
                null,
                line.ReferenceDesignators,
                line.Backflush);

        public static BomComparableLine FromManufacturing(ManufacturingBomMaterialLine line) =>
            new(
                line.SkuCode,
                line.Quantity,
                line.UnitOfMeasureCode,
                line.ScrapRate,
                line.YieldRate,
                line.IsPhantom,
                line.AlternateGroup,
                line.AlternatePriority,
                line.SubstituteSkuCodes,
                line.ReferenceDesignators,
                line.Backflush);
    }
}

public sealed class GetEngineeringChangeImpactPreviewQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringChangeImpactPreviewQuery, EngineeringChangeImpactPreviewResponse>
{
    public async Task<EngineeringChangeImpactPreviewResponse> Handle(
        GetEngineeringChangeImpactPreviewQuery request,
        CancellationToken cancellationToken)
    {
        if (request.AffectedVersions.Count == 0)
        {
            throw new KnownException("At least one affected version is required for impact preview.");
        }

        var nodes = new Dictionary<string, EngineeringChangeImpactNode>(StringComparer.OrdinalIgnoreCase);
        var risks = new Dictionary<string, EngineeringChangeImpactRisk>(StringComparer.OrdinalIgnoreCase);

        foreach (var affected in request.AffectedVersions)
        {
            var kind = NormalizeVersionKind(affected.VersionKind);
            var versionId = NormalizeVersionId(kind, affected.VersionId);
            AddNode(nodes, kind, versionId, DisplayName(kind, versionId), "direct", null, null, ConsoleRoute(kind, versionId));

            switch (kind)
            {
                case "engineering-bom":
                    await AddEngineeringBomImpactAsync(request, versionId, nodes, risks, cancellationToken);
                    break;
                case "manufacturing-bom":
                    await AddManufacturingBomImpactAsync(request, versionId, nodes, risks, cancellationToken);
                    break;
                case "routing":
                    await AddRoutingImpactAsync(request, versionId, nodes, risks, cancellationToken);
                    break;
                case "production-version":
                    await AddProductionVersionCandidatesAsync(request, versionId, nodes, risks, cancellationToken);
                    break;
                default:
                    AddRisk(risks, "unsupported-affected-version", "warning", $"Affected version kind '{affected.VersionKind}' is not supported by impact preview.", versionId);
                    break;
            }
        }

        return new EngineeringChangeImpactPreviewResponse(
            request.EffectiveDate,
            nodes.Values
                .OrderBy(x => ImpactSort(x.ImpactLevel))
                .ThenBy(x => x.NodeType, StringComparer.Ordinal)
                .ThenBy(x => x.VersionId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            risks.Values
                .OrderBy(x => x.Severity, StringComparer.Ordinal)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private async Task AddEngineeringBomImpactAsync(
        GetEngineeringChangeImpactPreviewQuery request,
        string engineeringBomVersionId,
        Dictionary<string, EngineeringChangeImpactNode> nodes,
        Dictionary<string, EngineeringChangeImpactRisk> risks,
        CancellationToken cancellationToken)
    {
        var manufacturingBoms = await dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Status == EngineeringVersionStatus.Published
                && x.EngineeringBomVersionId == engineeringBomVersionId)
            .ToArrayAsync(cancellationToken);

        foreach (var mbom in manufacturingBoms)
        {
            var mbomVersionId = VersionId(mbom.BomCode, mbom.Revision);
            AddNode(nodes, "manufacturing-bom", mbomVersionId, $"MBOM {mbomVersionId}", "derived", engineeringBomVersionId, mbom.SkuCode, ConsoleRoute("manufacturing-bom", mbomVersionId));
            await AddManufacturingBomImpactAsync(request, mbomVersionId, nodes, risks, cancellationToken);
        }
    }

    private async Task AddManufacturingBomImpactAsync(
        GetEngineeringChangeImpactPreviewQuery request,
        string mbomVersionId,
        Dictionary<string, EngineeringChangeImpactNode> nodes,
        Dictionary<string, EngineeringChangeImpactRisk> risks,
        CancellationToken cancellationToken)
    {
        var productionVersions = await dbContext.ProductionVersions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Status == ProductionVersionStatus.Active
                && x.MbomVersionId == mbomVersionId)
            .ToArrayAsync(cancellationToken);

        foreach (var version in productionVersions)
        {
            await AddProductionVersionImpactAsync(request, version, mbomVersionId, nodes, risks, cancellationToken);
        }
    }

    private async Task AddRoutingImpactAsync(
        GetEngineeringChangeImpactPreviewQuery request,
        string routingVersionId,
        Dictionary<string, EngineeringChangeImpactNode> nodes,
        Dictionary<string, EngineeringChangeImpactRisk> risks,
        CancellationToken cancellationToken)
    {
        var productionVersions = await dbContext.ProductionVersions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Status == ProductionVersionStatus.Active
                && x.RoutingVersionId == routingVersionId)
            .ToArrayAsync(cancellationToken);

        foreach (var version in productionVersions)
        {
            await AddProductionVersionImpactAsync(request, version, routingVersionId, nodes, risks, cancellationToken);
        }
    }

    private async Task AddProductionVersionCandidatesAsync(
        GetEngineeringChangeImpactPreviewQuery request,
        string productionVersionId,
        Dictionary<string, EngineeringChangeImpactNode> nodes,
        Dictionary<string, EngineeringChangeImpactRisk> risks,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(productionVersionId, out var parsedId))
        {
            AddRisk(risks, "invalid-production-version-id", "warning", $"Production version id '{productionVersionId}' is not a GUID.", productionVersionId);
            return;
        }

        var version = await dbContext.ProductionVersions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.Id == new ProductionVersionId(parsedId))
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            AddRisk(risks, "production-version-not-found", "warning", $"Production version '{productionVersionId}' was not found.", productionVersionId);
            return;
        }

        await AddProductionVersionImpactAsync(request, version, parsedId.ToString("D"), nodes, risks, cancellationToken);
    }

    private async Task AddProductionVersionImpactAsync(
        GetEngineeringChangeImpactPreviewQuery request,
        ProductionVersion version,
        string relatedVersionId,
        Dictionary<string, EngineeringChangeImpactNode> nodes,
        Dictionary<string, EngineeringChangeImpactRisk> risks,
        CancellationToken cancellationToken)
    {
        var productionVersionId = version.Id.Id.ToString("D");
        AddNode(nodes, "production-version", productionVersionId, $"生产版本 {version.SkuCode}", "downstream", relatedVersionId, version.SkuCode, $"/engineering/production-versions?skuCode={Uri.EscapeDataString(version.SkuCode)}");
        AddNode(nodes, "mrp-candidate", $"mrp:{productionVersionId}", "MRP 需求重算候选", "candidate", productionVersionId, version.SkuCode, "/planning");
        AddNode(nodes, "mes-work-order-candidate", $"mes:{productionVersionId}", "MES 工单/WIP 候选", "candidate", productionVersionId, version.SkuCode, "/mes/work-orders");
        AddNode(nodes, "aps-plan-candidate", $"aps:{productionVersionId}", "APS 排程候选", "candidate", productionVersionId, version.SkuCode, "/scheduling");
        AddRisk(risks, "downstream-execution-impact", "warning", "工程变更影响生产版本，发布前需要评估 MRP、MES 工单/WIP 与 APS 已发布计划。", productionVersionId);

        var routing = await FindRoutingAsync(request.OrganizationId, request.EnvironmentId, version.RoutingVersionId, cancellationToken);
        if (routing is not null)
        {
            AddNode(nodes, "routing", version.RoutingVersionId, $"工艺路线 {version.RoutingVersionId}", "derived", productionVersionId, routing.SkuCode, ConsoleRoute("routing", version.RoutingVersionId));
        }
    }

    private async Task<Routing?> FindRoutingAsync(string organizationId, string environmentId, string versionId, CancellationToken cancellationToken)
    {
        var parsed = ParseVersionId(versionId);
        if (parsed is null)
        {
            return null;
        }

        return await dbContext.Routings
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.RoutingCode == parsed.Value.Code
                && x.Revision == parsed.Value.Revision)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static void AddNode(
        Dictionary<string, EngineeringChangeImpactNode> nodes,
        string nodeType,
        string versionId,
        string displayName,
        string impactLevel,
        string? relatedVersionId,
        string? skuCode,
        string? consoleRoute)
    {
        nodes.TryAdd(
            $"{nodeType}:{versionId}",
            new EngineeringChangeImpactNode(nodeType, versionId, displayName, impactLevel, relatedVersionId, skuCode, consoleRoute));
    }

    private static void AddRisk(
        Dictionary<string, EngineeringChangeImpactRisk> risks,
        string code,
        string severity,
        string message,
        string? relatedVersionId)
    {
        risks.TryAdd($"{code}:{relatedVersionId}", new EngineeringChangeImpactRisk(code, severity, message, relatedVersionId));
    }

    private static string NormalizeVersionKind(string kind)
    {
        var normalized = kind.Trim().Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "engineeringbom" or "engineering-bom" => "engineering-bom",
            "manufacturingbom" or "manufacturing-bom" => "manufacturing-bom",
            "productionversion" or "production-version" => "production-version",
            _ => normalized
        };
    }

    private static string NormalizeVersionId(string kind, string versionId)
    {
        var normalized = versionId.Trim();
        return kind == "production-version" && Guid.TryParse(normalized, out var parsedId)
            ? parsedId.ToString("D")
            : normalized;
    }

    private static string DisplayName(string kind, string versionId) =>
        kind switch
        {
            "engineering-bom" => $"EBOM {versionId}",
            "manufacturing-bom" => $"MBOM {versionId}",
            "routing" => $"工艺路线 {versionId}",
            "production-version" => $"生产版本 {versionId}",
            _ => versionId
        };

    private static string? ConsoleRoute(string kind, string versionId)
    {
        var parsed = ParseVersionId(versionId);
        return kind switch
        {
            "engineering-bom" when parsed is not null => $"/engineering/ebom?bomCode={Uri.EscapeDataString(parsed.Value.Code)}&revision={Uri.EscapeDataString(parsed.Value.Revision)}",
            "manufacturing-bom" when parsed is not null => $"/engineering/mbom?bomCode={Uri.EscapeDataString(parsed.Value.Code)}&revision={Uri.EscapeDataString(parsed.Value.Revision)}",
            "routing" when parsed is not null => $"/engineering/routings?routingCode={Uri.EscapeDataString(parsed.Value.Code)}&revision={Uri.EscapeDataString(parsed.Value.Revision)}",
            "production-version" => "/engineering/production-versions",
            _ => null
        };
    }

    private static (string Code, string Revision)? ParseVersionId(string versionId)
    {
        var separator = versionId.LastIndexOf(':');
        if (separator <= 0 || separator >= versionId.Length - 1)
        {
            return null;
        }

        return (versionId[..separator], versionId[(separator + 1)..]);
    }

    private static int ImpactSort(string impactLevel) =>
        impactLevel switch
        {
            "direct" => 0,
            "derived" => 1,
            "downstream" => 2,
            _ => 3
        };

    private static string VersionId(string code, string revision) => $"{code}:{revision}";
}

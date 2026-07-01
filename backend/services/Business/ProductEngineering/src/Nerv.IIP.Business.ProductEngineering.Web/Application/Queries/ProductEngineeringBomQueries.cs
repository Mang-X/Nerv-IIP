using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;

public sealed record BomExplosionDiagnostic(string Code, string Severity, string ItemCode, string Message, string Path);

public sealed record BomExplosionNode(
    string ItemCode,
    string? ParentItemCode,
    string? BomCode,
    string? Revision,
    DateOnly? EffectiveDate,
    int Level,
    string Path,
    decimal LineQuantity,
    decimal RequiredQuantity,
    string UnitOfMeasureCode,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    bool Backflush = false,
    IReadOnlyCollection<BomExplosionNode>? Children = null);

public sealed record BomExplosionResponse(
    string BomKind,
    string SelectionMode,
    BomExplosionNode Root,
    IReadOnlyCollection<BomExplosionDiagnostic> Diagnostics);

public sealed record BomWhereUsedItem(
    string BomKind,
    string BomCode,
    string Revision,
    string ParentItemCode,
    DateOnly? EffectiveDate,
    decimal LineQuantity,
    string UnitOfMeasureCode,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    bool Backflush = false);

public sealed record BomWhereUsedResponse(string ComponentCode, IReadOnlyCollection<BomWhereUsedItem> Items);

public sealed record GetEngineeringBomExplosionQuery(
    string OrganizationId,
    string EnvironmentId,
    string ItemCode,
    DateOnly EffectiveDate,
    decimal LotSize = 1m,
    string? BomCode = null,
    string? Revision = null) : IQuery<BomExplosionResponse>;

public sealed record GetManufacturingBomExplosionQuery(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    DateOnly EffectiveDate,
    decimal LotSize = 1m,
    string? BomCode = null,
    string? Revision = null) : IQuery<BomExplosionResponse>;

public sealed record GetEngineeringBomWhereUsedQuery(
    string OrganizationId,
    string EnvironmentId,
    string ComponentCode,
    DateOnly EffectiveDate) : IQuery<BomWhereUsedResponse>;

public sealed record GetManufacturingBomWhereUsedQuery(
    string OrganizationId,
    string EnvironmentId,
    string ComponentCode,
    DateOnly EffectiveDate) : IQuery<BomWhereUsedResponse>;

public sealed class GetEngineeringBomExplosionQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringBomExplosionQuery, BomExplosionResponse>
{
    public async Task<BomExplosionResponse> Handle(GetEngineeringBomExplosionQuery request, CancellationToken cancellationToken)
    {
        var boms = await LoadEngineeringBomsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken);
        var diagnostics = new List<BomExplosionDiagnostic>();
        var rootBom = SelectEngineeringBom(boms, request.ItemCode, request.EffectiveDate, request.BomCode, request.Revision)
            ?? throw new KnownException($"No published engineering BOM can resolve item '{request.ItemCode}' for {request.EffectiveDate:yyyy-MM-dd}.");
        var root = BuildEngineeringNode(
            boms,
            rootBom,
            request.ItemCode,
            parentItemCode: null,
            path: request.ItemCode,
            level: 0,
            lineQuantity: 1m,
            requiredQuantity: BomQuantityMath.NormalizeLotSize(request.LotSize),
            unitOfMeasureCode: string.Empty,
            line: null,
            request.EffectiveDate,
            diagnostics,
            [request.ItemCode]);

        return new BomExplosionResponse("EngineeringBom", request.BomCode is null ? "EffectiveBom" : "ExplicitVersion", root, diagnostics);
    }

    private static BomExplosionNode BuildEngineeringNode(
        IReadOnlyCollection<EngineeringBom> boms,
        EngineeringBom bom,
        string itemCode,
        string? parentItemCode,
        string path,
        int level,
        decimal lineQuantity,
        decimal requiredQuantity,
        string unitOfMeasureCode,
        EngineeringBomLine? line,
        DateOnly effectiveDate,
        List<BomExplosionDiagnostic> diagnostics,
        HashSet<string> ancestors)
    {
        var children = new List<BomExplosionNode>();
        foreach (var childLine in bom.Lines.OrderBy(x => x.ChildItemCode))
        {
            var childPath = $"{path}>{childLine.ChildItemCode}";
            var childRequired = BomQuantityMath.RollQuantity(requiredQuantity, childLine.Quantity, childLine.ScrapRate, childLine.YieldRate);
            if (ancestors.Contains(childLine.ChildItemCode))
            {
                diagnostics.Add(new BomExplosionDiagnostic(
                    "cycle-detected",
                    "error",
                    childLine.ChildItemCode,
                    $"BOM cycle detected at '{childLine.ChildItemCode}'.",
                    childPath));
                children.Add(ToEngineeringLineNode(childLine, itemCode, childPath, level + 1, childRequired, []));
                continue;
            }

            var childBom = SelectEngineeringBom(boms, childLine.ChildItemCode, effectiveDate, null, null);
            if (childBom is null)
            {
                diagnostics.Add(new BomExplosionDiagnostic(
                    "missing-child-bom",
                    "warning",
                    childLine.ChildItemCode,
                    $"No published child engineering BOM resolved for '{childLine.ChildItemCode}' on {effectiveDate:yyyy-MM-dd}.",
                    childPath));
                children.Add(ToEngineeringLineNode(childLine, itemCode, childPath, level + 1, childRequired, []));
                continue;
            }

            var nextAncestors = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase) { childLine.ChildItemCode };
            children.Add(BuildEngineeringNode(
                boms,
                childBom,
                childLine.ChildItemCode,
                itemCode,
                childPath,
                level + 1,
                childLine.Quantity,
                childRequired,
                childLine.UnitOfMeasureCode,
                childLine,
                effectiveDate,
                diagnostics,
                nextAncestors));
        }

        return new BomExplosionNode(
            itemCode,
            parentItemCode,
            bom.BomCode,
            bom.Revision,
            bom.EffectiveDate,
            level,
            path,
            lineQuantity,
            requiredQuantity,
            unitOfMeasureCode,
            line?.ScrapRate ?? 0m,
            line?.YieldRate ?? 1m,
            line?.IsPhantom ?? false,
            line?.AlternateGroup,
            line?.AlternatePriority,
            null,
            line?.ReferenceDesignators,
            line?.Backflush ?? false,
            children);
    }

    private static BomExplosionNode ToEngineeringLineNode(
        EngineeringBomLine line,
        string parentItemCode,
        string path,
        int level,
        decimal requiredQuantity,
        IReadOnlyCollection<BomExplosionNode> children) =>
        new(
            line.ChildItemCode,
            parentItemCode,
            null,
            null,
            null,
            level,
            path,
            line.Quantity,
            requiredQuantity,
            line.UnitOfMeasureCode,
            line.ScrapRate,
            line.YieldRate,
            line.IsPhantom,
            line.AlternateGroup,
            line.AlternatePriority,
            null,
            line.ReferenceDesignators,
            line.Backflush,
            children);

    private static EngineeringBom? SelectEngineeringBom(
        IEnumerable<EngineeringBom> boms,
        string itemCode,
        DateOnly effectiveDate,
        string? bomCode,
        string? revision)
    {
        var query = boms.Where(x =>
            x.ParentItemCode == itemCode &&
            x.Status == EngineeringVersionStatus.Published &&
            x.EffectiveDate is not null &&
            x.EffectiveDate <= effectiveDate);
        if (!string.IsNullOrWhiteSpace(bomCode))
        {
            query = query.Where(x => x.BomCode == bomCode);
        }

        if (!string.IsNullOrWhiteSpace(revision))
        {
            query = query.Where(x => x.Revision == revision);
        }

        return query
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Revision)
            .FirstOrDefault();
    }

    private Task<EngineeringBom[]> LoadEngineeringBomsAsync(string organizationId, string environmentId, CancellationToken cancellationToken) =>
        dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Status == EngineeringVersionStatus.Published)
            .ToArrayAsync(cancellationToken);
}

public sealed class GetManufacturingBomExplosionQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetManufacturingBomExplosionQuery, BomExplosionResponse>
{
    public async Task<BomExplosionResponse> Handle(GetManufacturingBomExplosionQuery request, CancellationToken cancellationToken)
    {
        var boms = await LoadManufacturingBomsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken);
        var diagnostics = new List<BomExplosionDiagnostic>();
        var selection = await SelectManufacturingBomAsync(boms, request, cancellationToken)
            ?? throw new KnownException($"No published manufacturing BOM can resolve SKU '{request.SkuCode}' for {request.EffectiveDate:yyyy-MM-dd} and lot size {request.LotSize}.");
        var root = BuildManufacturingNode(
            boms,
            selection.Bom,
            selection.Bom.SkuCode,
            parentItemCode: null,
            path: selection.Bom.SkuCode,
            level: 0,
            lineQuantity: 1m,
            requiredQuantity: BomQuantityMath.NormalizeLotSize(request.LotSize),
            unitOfMeasureCode: string.Empty,
            line: null,
            request.EffectiveDate,
            diagnostics,
            [selection.Bom.SkuCode]);

        return new BomExplosionResponse("ManufacturingBom", selection.Mode, root, diagnostics);
    }

    private async Task<ManufacturingBomSelection?> SelectManufacturingBomAsync(
        IReadOnlyCollection<ManufacturingBom> boms,
        GetManufacturingBomExplosionQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.BomCode))
        {
            var explicitBom = SelectManufacturingBom(boms, request.SkuCode, request.EffectiveDate, request.BomCode, request.Revision);
            return explicitBom is null ? null : new ManufacturingBomSelection(explicitBom, "ExplicitVersion");
        }

        var productionVersion = await dbContext.ProductionVersions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SkuCode == request.SkuCode &&
                x.Status == ProductionVersionStatus.Active &&
                x.ValidFrom <= request.EffectiveDate &&
                (x.ValidTo == null || request.EffectiveDate <= x.ValidTo) &&
                (x.LotSizeMin == null || x.LotSizeMin <= request.LotSize) &&
                (x.LotSizeMax == null || request.LotSize <= x.LotSizeMax))
            .OrderByDescending(x => x.LotSizeMin != null || x.LotSizeMax != null)
            .ThenBy(x => x.Priority)
            .ThenByDescending(x => x.IsDefault)
            .Select(x => x.MbomVersionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (productionVersion is not null)
        {
            var separator = productionVersion.LastIndexOf(':');
            if (separator > 0 && separator < productionVersion.Length - 1)
            {
                var bomCode = productionVersion[..separator];
                var revision = productionVersion[(separator + 1)..];
                var bom = SelectManufacturingBom(boms, request.SkuCode, request.EffectiveDate, bomCode, revision);
                if (bom is not null)
                {
                    return new ManufacturingBomSelection(bom, "ProductionVersion");
                }
            }
        }

        var effectiveBom = SelectManufacturingBom(boms, request.SkuCode, request.EffectiveDate, null, null);
        return effectiveBom is null ? null : new ManufacturingBomSelection(effectiveBom, "EffectiveBom");
    }

    private static BomExplosionNode BuildManufacturingNode(
        IReadOnlyCollection<ManufacturingBom> boms,
        ManufacturingBom bom,
        string itemCode,
        string? parentItemCode,
        string path,
        int level,
        decimal lineQuantity,
        decimal requiredQuantity,
        string unitOfMeasureCode,
        ManufacturingBomMaterialLine? line,
        DateOnly effectiveDate,
        List<BomExplosionDiagnostic> diagnostics,
        HashSet<string> ancestors)
    {
        var children = new List<BomExplosionNode>();
        foreach (var materialLine in bom.MaterialLines.OrderBy(x => x.SkuCode))
        {
            var childPath = $"{path}>{materialLine.SkuCode}";
            var childRequired = BomQuantityMath.RollQuantity(requiredQuantity, materialLine.Quantity, materialLine.ScrapRate, materialLine.YieldRate);
            if (ancestors.Contains(materialLine.SkuCode))
            {
                diagnostics.Add(new BomExplosionDiagnostic(
                    "cycle-detected",
                    "error",
                    materialLine.SkuCode,
                    $"BOM cycle detected at '{materialLine.SkuCode}'.",
                    childPath));
                children.Add(ToManufacturingLineNode(materialLine, itemCode, childPath, level + 1, childRequired, []));
                continue;
            }

            var childBom = SelectManufacturingBom(boms, materialLine.SkuCode, effectiveDate, null, null);
            if (childBom is null)
            {
                diagnostics.Add(new BomExplosionDiagnostic(
                    "missing-child-bom",
                    "warning",
                    materialLine.SkuCode,
                    $"No published child manufacturing BOM resolved for '{materialLine.SkuCode}' on {effectiveDate:yyyy-MM-dd}.",
                    childPath));
                children.Add(ToManufacturingLineNode(materialLine, itemCode, childPath, level + 1, childRequired, []));
                continue;
            }

            var nextAncestors = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase) { materialLine.SkuCode };
            children.Add(BuildManufacturingNode(
                boms,
                childBom,
                materialLine.SkuCode,
                itemCode,
                childPath,
                level + 1,
                materialLine.Quantity,
                childRequired,
                materialLine.UnitOfMeasureCode,
                materialLine,
                effectiveDate,
                diagnostics,
                nextAncestors));
        }

        return new BomExplosionNode(
            itemCode,
            parentItemCode,
            bom.BomCode,
            bom.Revision,
            bom.EffectiveDate,
            level,
            path,
            lineQuantity,
            requiredQuantity,
            unitOfMeasureCode,
            line?.ScrapRate ?? 0m,
            line?.YieldRate ?? 1m,
            line?.IsPhantom ?? false,
            line?.AlternateGroup,
            line?.AlternatePriority,
            line?.SubstituteSkuCodes,
            line?.ReferenceDesignators,
            line?.Backflush ?? false,
            children);
    }

    private static BomExplosionNode ToManufacturingLineNode(
        ManufacturingBomMaterialLine line,
        string parentItemCode,
        string path,
        int level,
        decimal requiredQuantity,
        IReadOnlyCollection<BomExplosionNode> children) =>
        new(
            line.SkuCode,
            parentItemCode,
            null,
            null,
            null,
            level,
            path,
            line.Quantity,
            requiredQuantity,
            line.UnitOfMeasureCode,
            line.ScrapRate,
            line.YieldRate,
            line.IsPhantom,
            line.AlternateGroup,
            line.AlternatePriority,
            line.SubstituteSkuCodes,
            line.ReferenceDesignators,
            line.Backflush,
            children);

    private static ManufacturingBom? SelectManufacturingBom(
        IEnumerable<ManufacturingBom> boms,
        string skuCode,
        DateOnly effectiveDate,
        string? bomCode,
        string? revision)
    {
        var query = boms.Where(x =>
            x.SkuCode == skuCode &&
            x.Status == EngineeringVersionStatus.Published &&
            x.EffectiveDate is not null &&
            x.EffectiveDate <= effectiveDate);
        if (!string.IsNullOrWhiteSpace(bomCode))
        {
            query = query.Where(x => x.BomCode == bomCode);
        }

        if (!string.IsNullOrWhiteSpace(revision))
        {
            query = query.Where(x => x.Revision == revision);
        }

        return query
            .OrderByDescending(x => x.EffectiveDate)
            .ThenByDescending(x => x.Revision)
            .FirstOrDefault();
    }

    private Task<ManufacturingBom[]> LoadManufacturingBomsAsync(string organizationId, string environmentId, CancellationToken cancellationToken) =>
        dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Status == EngineeringVersionStatus.Published)
            .ToArrayAsync(cancellationToken);

    private sealed record ManufacturingBomSelection(ManufacturingBom Bom, string Mode);
}

public sealed class GetEngineeringBomWhereUsedQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringBomWhereUsedQuery, BomWhereUsedResponse>
{
    public async Task<BomWhereUsedResponse> Handle(GetEngineeringBomWhereUsedQuery request, CancellationToken cancellationToken)
    {
        var items = await dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Status == EngineeringVersionStatus.Published &&
                x.EffectiveDate != null &&
                x.EffectiveDate <= request.EffectiveDate)
            .SelectMany(
                bom => bom.Lines.Where(line => line.ChildItemCode == request.ComponentCode),
                (bom, line) => new BomWhereUsedItem(
                    "EngineeringBom",
                    bom.BomCode,
                    bom.Revision,
                    bom.ParentItemCode,
                    bom.EffectiveDate,
                    line.Quantity,
                    line.UnitOfMeasureCode,
                    line.ScrapRate,
                    line.YieldRate,
                    line.IsPhantom,
                    line.AlternateGroup,
                    line.AlternatePriority,
                    null,
                    line.ReferenceDesignators,
                    line.Backflush))
            .OrderBy(x => x.BomCode)
            .ThenBy(x => x.Revision)
            .ToArrayAsync(cancellationToken);

        return new BomWhereUsedResponse(request.ComponentCode, items);
    }
}

public sealed class GetManufacturingBomWhereUsedQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetManufacturingBomWhereUsedQuery, BomWhereUsedResponse>
{
    public async Task<BomWhereUsedResponse> Handle(GetManufacturingBomWhereUsedQuery request, CancellationToken cancellationToken)
    {
        var items = await dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Status == EngineeringVersionStatus.Published &&
                x.EffectiveDate != null &&
                x.EffectiveDate <= request.EffectiveDate)
            .SelectMany(
                bom => bom.MaterialLines.Where(line => line.SkuCode == request.ComponentCode),
                (bom, line) => new BomWhereUsedItem(
                    "ManufacturingBom",
                    bom.BomCode,
                    bom.Revision,
                    bom.SkuCode,
                    bom.EffectiveDate,
                    line.Quantity,
                    line.UnitOfMeasureCode,
                    line.ScrapRate,
                    line.YieldRate,
                    line.IsPhantom,
                    line.AlternateGroup,
                    line.AlternatePriority,
                    line.SubstituteSkuCodes,
                    line.ReferenceDesignators,
                    line.Backflush))
            .OrderBy(x => x.BomCode)
            .ThenBy(x => x.Revision)
            .ToArrayAsync(cancellationToken);

        return new BomWhereUsedResponse(request.ComponentCode, items);
    }
}

file static class BomQuantityMath
{
    public static decimal NormalizeLotSize(decimal lotSize) => lotSize <= 0m ? 1m : lotSize;

    public static decimal RollQuantity(decimal parentRequired, decimal lineQuantity, decimal scrapRate, decimal yieldRate)
    {
        var effectiveYield = yieldRate <= 0m ? 1m : yieldRate;
        return parentRequired * lineQuantity * (1m + scrapRate) / effectiveYield;
    }
}

using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;

internal static class EngineeringQueryParameters
{
    internal static int NormalizeSkip(int skip) => Math.Max(0, skip);

    internal static int NormalizeTake(int take) => Math.Clamp(take, 1, 500);

    internal static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static EngineeringVersionStatus? ParseStatusOrThrow(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return Enum.TryParse<EngineeringVersionStatus>(status, true, out var parsed)
            ? parsed
            : throw new KnownException($"Engineering version status '{status}' is invalid. Allowed values: Draft, Published, Archived.");
    }
}

public sealed record EngineeringBomLineItem(
    string ChildItemCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? ReferenceDesignators = null,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool Backflush = false);

public sealed record EngineeringBomListItem(
    string BomCode,
    string Revision,
    string ParentItemCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<EngineeringBomLineItem> Lines);

public sealed record ListEngineeringBomsResponse(IReadOnlyCollection<EngineeringBomListItem> Items, int Total);

public sealed record ListEngineeringBomsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ParentItemCode,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListEngineeringBomsResponse>;

public sealed class ListEngineeringBomsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListEngineeringBomsQuery, ListEngineeringBomsResponse>
{
    public async Task<ListEngineeringBomsResponse> Handle(ListEngineeringBomsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var parentItemCode = EngineeringQueryParameters.NormalizeOptionalText(request.ParentItemCode);
        if (parentItemCode is not null)
        {
            query = query.Where(x => x.ParentItemCode == parentItemCode);
        }

        var status = EngineeringQueryParameters.ParseStatusOrThrow(request.Status);
        if (status is not null)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.BomCode)
            .ThenBy(x => x.Revision)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new EngineeringBomListItem(
                x.BomCode,
                x.Revision,
                x.ParentItemCode,
                x.Status.ToString(),
                x.EffectiveDate,
                x.Lines
                    .OrderBy(line => line.ChildItemCode)
                    .Select(line => new EngineeringBomLineItem(
                        line.ChildItemCode,
                        line.Quantity,
                        line.UnitOfMeasureCode,
                        line.IsPhantom,
                        line.AlternateGroup,
                        line.AlternatePriority,
                        line.ReferenceDesignators,
                        line.ScrapRate,
                        line.YieldRate,
                        line.Backflush))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new ListEngineeringBomsResponse(items, total);
    }
}

public sealed record GetEngineeringBomQuery(string OrganizationId, string EnvironmentId, string BomCode, string Revision)
    : IQuery<EngineeringBomListItem>;

public sealed class GetEngineeringBomQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringBomQuery, EngineeringBomListItem>
{
    public async Task<EngineeringBomListItem> Handle(GetEngineeringBomQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.BomCode == request.BomCode
                && x.Revision == request.Revision)
            .Select(x => new EngineeringBomListItem(
                x.BomCode,
                x.Revision,
                x.ParentItemCode,
                x.Status.ToString(),
                x.EffectiveDate,
                x.Lines
                    .OrderBy(line => line.ChildItemCode)
                    .Select(line => new EngineeringBomLineItem(
                        line.ChildItemCode,
                        line.Quantity,
                        line.UnitOfMeasureCode,
                        line.IsPhantom,
                        line.AlternateGroup,
                        line.AlternatePriority,
                        line.ReferenceDesignators,
                        line.ScrapRate,
                        line.YieldRate,
                        line.Backflush))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Engineering BOM '{request.BomCode}' revision '{request.Revision}' was not found.");
    }
}

public sealed record ManufacturingBomMaterialLineItem(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    decimal YieldRate = 1m,
    bool Backflush = false);

public sealed record ManufacturingBomRecipeLineItem(string ParameterCode, string TargetValue, string UnitOfMeasureCode);

public sealed record ManufacturingBomListItem(
    string BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomVersionId,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineItem> MaterialLines,
    IReadOnlyCollection<ManufacturingBomRecipeLineItem> RecipeLines);

public sealed record ListManufacturingBomsResponse(IReadOnlyCollection<ManufacturingBomListItem> Items, int Total);

public sealed record ListManufacturingBomsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListManufacturingBomsResponse>;

public sealed class ListManufacturingBomsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListManufacturingBomsQuery, ListManufacturingBomsResponse>
{
    public async Task<ListManufacturingBomsResponse> Handle(ListManufacturingBomsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var skuCode = EngineeringQueryParameters.NormalizeOptionalText(request.SkuCode);
        if (skuCode is not null)
        {
            query = query.Where(x => x.SkuCode == skuCode);
        }

        var status = EngineeringQueryParameters.ParseStatusOrThrow(request.Status);
        if (status is not null)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.SkuCode)
            .ThenBy(x => x.BomCode)
            .ThenBy(x => x.Revision)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new ManufacturingBomListItem(
                x.BomCode,
                x.Revision,
                x.SkuCode,
                x.EngineeringBomVersionId,
                x.Status.ToString(),
                x.EffectiveDate,
                x.MaterialLines
                    .OrderBy(line => line.SkuCode)
                    .Select(line => new ManufacturingBomMaterialLineItem(
                        line.SkuCode,
                        line.Quantity,
                        line.UnitOfMeasureCode,
                        line.ScrapRate,
                        line.IsPhantom,
                        line.AlternateGroup,
                        line.AlternatePriority,
                        line.SubstituteSkuCodes,
                        line.ReferenceDesignators,
                        line.YieldRate,
                        line.Backflush))
                    .ToArray(),
                x.RecipeLines
                    .OrderBy(line => line.ParameterCode)
                    .Select(line => new ManufacturingBomRecipeLineItem(
                        line.ParameterCode,
                        line.TargetValue,
                        line.UnitOfMeasureCode))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new ListManufacturingBomsResponse(items, total);
    }
}

public sealed record GetManufacturingBomQuery(string OrganizationId, string EnvironmentId, string BomCode, string Revision)
    : IQuery<ManufacturingBomListItem>;

public sealed class GetManufacturingBomQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetManufacturingBomQuery, ManufacturingBomListItem>
{
    public async Task<ManufacturingBomListItem> Handle(GetManufacturingBomQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.BomCode == request.BomCode
                && x.Revision == request.Revision)
            .Select(x => new ManufacturingBomListItem(
                x.BomCode,
                x.Revision,
                x.SkuCode,
                x.EngineeringBomVersionId,
                x.Status.ToString(),
                x.EffectiveDate,
                x.MaterialLines
                    .OrderBy(line => line.SkuCode)
                    .Select(line => new ManufacturingBomMaterialLineItem(
                        line.SkuCode,
                        line.Quantity,
                        line.UnitOfMeasureCode,
                        line.ScrapRate,
                        line.IsPhantom,
                        line.AlternateGroup,
                        line.AlternatePriority,
                        line.SubstituteSkuCodes,
                        line.ReferenceDesignators,
                        line.YieldRate,
                        line.Backflush))
                    .ToArray(),
                x.RecipeLines
                    .OrderBy(line => line.ParameterCode)
                    .Select(line => new ManufacturingBomRecipeLineItem(line.ParameterCode, line.TargetValue, line.UnitOfMeasureCode))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Manufacturing BOM '{request.BomCode}' revision '{request.Revision}' was not found.");
    }
}

public sealed record RoutingOperationItem(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    string OperationName,
    int StandardMinutes,
    int SetupMinutes = 0,
    int RunMinutes = 0,
    int TeardownMinutes = 0,
    string ControlKey = "",
    bool RequiresReporting = true,
    bool RequiresQualityInspection = false,
    bool IsOutsourced = false);

public sealed record RoutingListItem(
    string RoutingCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<RoutingOperationItem> Operations);

public sealed record ListRoutingsResponse(IReadOnlyCollection<RoutingListItem> Items, int Total);

public sealed record ListRoutingsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListRoutingsResponse>;

public sealed class ListRoutingsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListRoutingsQuery, ListRoutingsResponse>
{
    public async Task<ListRoutingsResponse> Handle(ListRoutingsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Routings
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var skuCode = EngineeringQueryParameters.NormalizeOptionalText(request.SkuCode);
        if (skuCode is not null)
        {
            query = query.Where(x => x.SkuCode == skuCode);
        }

        var status = EngineeringQueryParameters.ParseStatusOrThrow(request.Status);
        if (status is not null)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.RoutingCode)
            .ThenBy(x => x.Revision)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new RoutingListItem(
                x.RoutingCode,
                x.Revision,
                x.SkuCode,
                x.Status.ToString(),
                x.EffectiveDate,
                x.Operations
                    .OrderBy(operation => operation.Sequence)
                    .Select(operation => new RoutingOperationItem(
                        operation.Sequence,
                        operation.WorkCenterCode,
                        operation.OperationCode,
                        operation.OperationName,
                        operation.StandardMinutes,
                        operation.SetupMinutes,
                        operation.RunMinutes,
                        operation.TeardownMinutes,
                        operation.ControlKey,
                        operation.RequiresReporting,
                        operation.RequiresQualityInspection,
                        operation.IsOutsourced))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new ListRoutingsResponse(items, total);
    }
}

public sealed record GetRoutingQuery(string OrganizationId, string EnvironmentId, string RoutingCode, string Revision)
    : IQuery<RoutingListItem>;

public sealed class GetRoutingQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetRoutingQuery, RoutingListItem>
{
    public async Task<RoutingListItem> Handle(GetRoutingQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.Routings
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.RoutingCode == request.RoutingCode
                && x.Revision == request.Revision)
            .Select(x => new RoutingListItem(
                x.RoutingCode,
                x.Revision,
                x.SkuCode,
                x.Status.ToString(),
                x.EffectiveDate,
                x.Operations
                    .OrderBy(operation => operation.Sequence)
                    .Select(operation => new RoutingOperationItem(
                        operation.Sequence,
                        operation.WorkCenterCode,
                        operation.OperationCode,
                        operation.OperationName,
                        operation.StandardMinutes,
                        operation.SetupMinutes,
                        operation.RunMinutes,
                        operation.TeardownMinutes,
                        operation.ControlKey,
                        operation.RequiresReporting,
                        operation.RequiresQualityInspection,
                        operation.IsOutsourced))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Routing '{request.RoutingCode}' revision '{request.Revision}' was not found.");
    }
}

public sealed record EngineeringDocumentItem(
    string DocumentNumber,
    string Revision,
    string? ItemCode,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType,
    DateTime RegisteredAtUtc);

public sealed record ListEngineeringDocumentsResponse(IReadOnlyCollection<EngineeringDocumentItem> Items, int Total);

public sealed record ListEngineeringDocumentsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode,
    string? DocumentType,
    int Skip = 0,
    int Take = 100) : IQuery<ListEngineeringDocumentsResponse>;

public sealed class ListEngineeringDocumentsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListEngineeringDocumentsQuery, ListEngineeringDocumentsResponse>
{
    public async Task<ListEngineeringDocumentsResponse> Handle(ListEngineeringDocumentsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.EngineeringDocuments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var itemCode = EngineeringQueryParameters.NormalizeOptionalText(request.ItemCode);
        if (itemCode is not null)
        {
            query = query.Where(x => x.ItemCode == itemCode);
        }

        var documentType = EngineeringQueryParameters.NormalizeOptionalText(request.DocumentType);
        if (documentType is not null)
        {
            query = query.Where(x => x.DocumentType == documentType);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.DocumentNumber)
            .ThenBy(x => x.Revision)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new EngineeringDocumentItem(x.DocumentNumber, x.Revision, x.ItemCode, x.FileId, x.FileName, x.ContentType, x.DocumentType, x.RegisteredAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListEngineeringDocumentsResponse(items, total);
    }
}

public sealed record GetEngineeringDocumentQuery(string OrganizationId, string EnvironmentId, string DocumentNumber, string Revision)
    : IQuery<EngineeringDocumentItem>;

public sealed class GetEngineeringDocumentQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringDocumentQuery, EngineeringDocumentItem>
{
    public async Task<EngineeringDocumentItem> Handle(GetEngineeringDocumentQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.EngineeringDocuments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DocumentNumber == request.DocumentNumber
                && x.Revision == request.Revision)
            .Select(x => new EngineeringDocumentItem(x.DocumentNumber, x.Revision, x.ItemCode, x.FileId, x.FileName, x.ContentType, x.DocumentType, x.RegisteredAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Engineering document '{request.DocumentNumber}' revision '{request.Revision}' was not found.");
    }
}

public sealed record EngineeringItemRevisionItem(
    string ItemCode,
    string Revision,
    string Name,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ListEngineeringItemsResponse(IReadOnlyCollection<EngineeringItemRevisionItem> Items, int Total);

public sealed record ListEngineeringItemsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListEngineeringItemsResponse>;

public sealed class ListEngineeringItemsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListEngineeringItemsQuery, ListEngineeringItemsResponse>
{
    public async Task<ListEngineeringItemsResponse> Handle(ListEngineeringItemsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.EngineeringItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var itemCode = EngineeringQueryParameters.NormalizeOptionalText(request.ItemCode);
        if (itemCode is not null)
        {
            query = query.Where(x => x.ItemCode == itemCode);
        }

        var status = EngineeringQueryParameters.ParseStatusOrThrow(request.Status);
        if (status is not null)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.ItemCode)
            .ThenBy(x => x.Revision)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new EngineeringItemRevisionItem(x.ItemCode, x.Revision, x.Name, x.Status.ToString(), x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new ListEngineeringItemsResponse(items, total);
    }
}

public sealed record GetEngineeringItemQuery(string OrganizationId, string EnvironmentId, string ItemCode, string Revision)
    : IQuery<EngineeringItemRevisionItem>;

public sealed class GetEngineeringItemQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringItemQuery, EngineeringItemRevisionItem>
{
    public async Task<EngineeringItemRevisionItem> Handle(GetEngineeringItemQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.EngineeringItems
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ItemCode == request.ItemCode
                && x.Revision == request.Revision)
            .Select(x => new EngineeringItemRevisionItem(x.ItemCode, x.Revision, x.Name, x.Status.ToString(), x.CreatedAtUtc, x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Engineering item '{request.ItemCode}' revision '{request.Revision}' was not found.");
    }
}

public sealed record EngineeringChangeAffectedVersionItem(string VersionKind, string VersionId, string? SupersededByVersionId);

public sealed record EngineeringChangeItem(
    string ChangeNumber,
    string Reason,
    string ApprovalReferenceId,
    string Status,
    DateOnly? EffectiveDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyCollection<EngineeringChangeAffectedVersionItem> AffectedVersions);

public sealed record ListEngineeringChangesResponse(IReadOnlyCollection<EngineeringChangeItem> Items, int Total);

public sealed record ListEngineeringChangesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<ListEngineeringChangesResponse>;

public sealed class ListEngineeringChangesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListEngineeringChangesQuery, ListEngineeringChangesResponse>
{
    public async Task<ListEngineeringChangesResponse> Handle(ListEngineeringChangesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.EngineeringChanges
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        var status = EngineeringQueryParameters.ParseStatusOrThrow(request.Status);
        if (status is not null)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.ChangeNumber)
            .Skip(EngineeringQueryParameters.NormalizeSkip(request.Skip))
            .Take(EngineeringQueryParameters.NormalizeTake(request.Take))
            .Select(x => new EngineeringChangeItem(
                x.ChangeNumber,
                x.Reason,
                x.ApprovalReferenceId,
                x.Status.ToString(),
                x.EffectiveDate,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.AffectedVersions
                    .OrderBy(version => version.VersionKind)
                    .ThenBy(version => version.VersionId)
                    .Select(version => new EngineeringChangeAffectedVersionItem(version.VersionKind, version.VersionId, version.SupersededByVersionId))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new ListEngineeringChangesResponse(items, total);
    }
}

public sealed record GetEngineeringChangeQuery(string OrganizationId, string EnvironmentId, string ChangeNumber)
    : IQuery<EngineeringChangeItem>;

public sealed class GetEngineeringChangeQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetEngineeringChangeQuery, EngineeringChangeItem>
{
    public async Task<EngineeringChangeItem> Handle(GetEngineeringChangeQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.EngineeringChanges
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.ChangeNumber == request.ChangeNumber)
            .Select(x => new EngineeringChangeItem(
                x.ChangeNumber,
                x.Reason,
                x.ApprovalReferenceId,
                x.Status.ToString(),
                x.EffectiveDate,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.AffectedVersions
                    .OrderBy(version => version.VersionKind)
                    .ThenBy(version => version.VersionId)
                    .Select(version => new EngineeringChangeAffectedVersionItem(version.VersionKind, version.VersionId, version.SupersededByVersionId))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Engineering change '{request.ChangeNumber}' was not found.");
    }
}

using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;

public sealed record ListEngineeringBomsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ParentItemCode,
    string? Status) : IQuery<ListEngineeringBomsResponse>;

public sealed record EngineeringBomListItem(
    string BomCode,
    string Revision,
    string ParentItemCode,
    string Status,
    DateOnly? EffectiveDate);

public sealed record ListEngineeringBomsResponse(IReadOnlyCollection<EngineeringBomListItem> Items);

public sealed class ListEngineeringBomsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListEngineeringBomsQuery, ListEngineeringBomsResponse>
{
    public async Task<ListEngineeringBomsResponse> Handle(ListEngineeringBomsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.EngineeringBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.ParentItemCode))
        {
            query = query.Where(x => x.ParentItemCode == request.ParentItemCode);
        }

        if (Enum.TryParse<EngineeringVersionStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        var versions = await query
            .OrderBy(x => x.BomCode)
            .ThenBy(x => x.Revision)
            .Select(x => new
            {
                x.BomCode,
                x.Revision,
                x.ParentItemCode,
                x.Status,
                x.EffectiveDate,
            })
            .ToArrayAsync(cancellationToken);

        var items = versions
            .Select(x => new EngineeringBomListItem(x.BomCode, x.Revision, x.ParentItemCode, x.Status.ToString(), x.EffectiveDate))
            .ToArray();
        return new ListEngineeringBomsResponse(items);
    }
}

public sealed record ListManufacturingBomsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? Status) : IQuery<ListManufacturingBomsResponse>;

public sealed record ManufacturingBomListItem(
    string BomCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineItem> MaterialLines);

public sealed record ManufacturingBomMaterialLineItem(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate);

public sealed record ListManufacturingBomsResponse(IReadOnlyCollection<ManufacturingBomListItem> Items);

public sealed class ListManufacturingBomsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListManufacturingBomsQuery, ListManufacturingBomsResponse>
{
    public async Task<ListManufacturingBomsResponse> Handle(ListManufacturingBomsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ManufacturingBoms
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (Enum.TryParse<EngineeringVersionStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        var versions = await query
            .OrderBy(x => x.SkuCode)
            .ThenBy(x => x.BomCode)
            .ThenBy(x => x.Revision)
            .Select(x => new ManufacturingBomListItem(
                x.BomCode,
                x.Revision,
                x.SkuCode,
                x.Status.ToString(),
                x.EffectiveDate,
                x.MaterialLines
                    .OrderBy(line => line.SkuCode)
                    .Select(line => new ManufacturingBomMaterialLineItem(
                        line.SkuCode,
                        line.Quantity,
                        line.UnitOfMeasureCode,
                        line.ScrapRate))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return new ListManufacturingBomsResponse(versions);
    }
}

public sealed record ListRoutingsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode,
    string? Status) : IQuery<ListRoutingsResponse>;

public sealed record RoutingListItem(
    string RoutingCode,
    string Revision,
    string SkuCode,
    string Status,
    DateOnly? EffectiveDate);

public sealed record ListRoutingsResponse(IReadOnlyCollection<RoutingListItem> Items);

public sealed class ListRoutingsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListRoutingsQuery, ListRoutingsResponse>
{
    public async Task<ListRoutingsResponse> Handle(ListRoutingsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Routings
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (Enum.TryParse<EngineeringVersionStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        var versions = await query
            .OrderBy(x => x.RoutingCode)
            .ThenBy(x => x.Revision)
            .Select(x => new
            {
                x.RoutingCode,
                x.Revision,
                x.SkuCode,
                x.Status,
                x.EffectiveDate,
            })
            .ToArrayAsync(cancellationToken);

        var items = versions
            .Select(x => new RoutingListItem(x.RoutingCode, x.Revision, x.SkuCode, x.Status.ToString(), x.EffectiveDate))
            .ToArray();
        return new ListRoutingsResponse(items);
    }
}

using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record MasterDataReferenceRequest(string ResourceType, string Code, string? CodeSet = null);

public sealed record MasterDataReferenceResponse(
    string ResourceType,
    string Code,
    bool Exists,
    bool Active,
    string DisplayName,
    string SnapshotVersion,
    string DisabledReason);

public sealed record ResolveMasterDataReferencesResponse(IReadOnlyCollection<MasterDataReferenceResponse> References);

public sealed record ResolveMasterDataReferencesQuery(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<MasterDataReferenceRequest> References) : IQuery<ResolveMasterDataReferencesResponse>;

public sealed class ResolveMasterDataReferencesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ResolveMasterDataReferencesQuery, ResolveMasterDataReferencesResponse>
{
    public async Task<ResolveMasterDataReferencesResponse> Handle(ResolveMasterDataReferencesQuery request, CancellationToken cancellationToken)
    {
        var responses = new List<MasterDataReferenceResponse>();
        foreach (var reference in request.References)
        {
            responses.Add(await ResolveAsync(request.OrganizationId, request.EnvironmentId, reference, cancellationToken));
        }

        return new ResolveMasterDataReferencesResponse(responses);
    }

    private async Task<MasterDataReferenceResponse> ResolveAsync(
        string organizationId,
        string environmentId,
        MasterDataReferenceRequest reference,
        CancellationToken cancellationToken)
    {
        var type = reference.ResourceType.Trim().ToLowerInvariant();
        var code = reference.Code.Trim();
        return type switch
        {
            "sku" => await ResolveSkuAsync(organizationId, environmentId, type, code, cancellationToken),
            "unit-of-measure" or "uom" => await ResolveUnitAsync(organizationId, environmentId, "unit-of-measure", code, cancellationToken),
            "business-partner" or "partner" => await ResolvePartnerAsync(organizationId, environmentId, "business-partner", code, cancellationToken),
            "work-center" => await ResolveWorkCenterAsync(organizationId, environmentId, type, code, cancellationToken),
            "work-calendar" => await ResolveWorkCalendarAsync(organizationId, environmentId, type, code, cancellationToken),
            "device-asset" => await ResolveDeviceAssetAsync(organizationId, environmentId, type, code, cancellationToken),
            "site" => await ResolveSiteAsync(organizationId, environmentId, type, code, cancellationToken),
            "production-line" => await ResolveProductionLineAsync(organizationId, environmentId, type, code, cancellationToken),
            "shift" => await ResolveShiftAsync(organizationId, environmentId, type, code, cancellationToken),
            "reference-data" or "reference-data-code" => await ResolveReferenceDataCodeAsync(organizationId, environmentId, reference, type, code, cancellationToken),
            _ when type.StartsWith("reference-data:", StringComparison.Ordinal) => await ResolveReferenceDataCodeAsync(organizationId, environmentId, reference, "reference-data", code, cancellationToken),
            _ => Missing(type, code),
        };
    }

    private async Task<MasterDataReferenceResponse> ResolveSkuAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.Skus
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveUnitAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.UnitsOfMeasure
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolvePartnerAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.BusinessPartners
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveWorkCenterAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.WorkCenters
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveWorkCalendarAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.WorkCalendars
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveDeviceAssetAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.DeviceAssets
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { Name = x.Model, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveSiteAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.Sites
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveProductionLineAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.ProductionLines
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveShiftAsync(string organizationId, string environmentId, string resourceType, string code, CancellationToken cancellationToken)
    {
        var item = await dbContext.Shifts
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private async Task<MasterDataReferenceResponse> ResolveReferenceDataCodeAsync(
        string organizationId,
        string environmentId,
        MasterDataReferenceRequest reference,
        string resourceType,
        string code,
        CancellationToken cancellationToken)
    {
        var codeSet = ResolveCodeSet(reference);
        if (string.IsNullOrWhiteSpace(codeSet))
        {
            return Missing(resourceType, code);
        }

        var item = await dbContext.ReferenceDataCodes
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.CodeSet == codeSet &&
                x.Code == code)
            .Select(x => new { x.Name, x.Disabled, x.UpdatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        return item is null ? Missing(resourceType, code) : Found(resourceType, code, item.Name, item.Disabled, item.UpdatedAtUtc);
    }

    private static string ResolveCodeSet(MasterDataReferenceRequest reference)
    {
        if (!string.IsNullOrWhiteSpace(reference.CodeSet))
        {
            return reference.CodeSet.Trim();
        }

        var resourceType = reference.ResourceType.Trim();
        const string prefix = "reference-data:";
        return resourceType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? resourceType[prefix.Length..].Trim()
            : string.Empty;
    }

    private static MasterDataReferenceResponse Found(string resourceType, string code, string displayName, bool disabled, DateTime updatedAtUtc)
    {
        return new MasterDataReferenceResponse(resourceType, code, true, !disabled, displayName, updatedAtUtc.ToString("O"), disabled ? "disabled" : string.Empty);
    }

    private static MasterDataReferenceResponse Missing(string resourceType, string code)
    {
        return new MasterDataReferenceResponse(resourceType, code, false, false, string.Empty, string.Empty, "not-found");
    }
}

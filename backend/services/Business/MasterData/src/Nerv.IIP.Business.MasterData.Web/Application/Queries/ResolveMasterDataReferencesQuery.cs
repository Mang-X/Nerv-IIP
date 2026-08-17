using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record MasterDataReferenceRequest(string ResourceType, string Code, string? CodeSet = null);

public sealed record MasterDataReferenceResponse(
    string ResourceType,
    string Code,
    bool Exists,
    bool Active,
    string DisplayName,
    string SnapshotVersion,
    string DisabledReason,
    string? DeviceAssetId = null,
    string? CanonicalCode = null,
    string? OrganizationId = null,
    string? EnvironmentId = null);

public sealed record ResolveMasterDataReferencesResponse(IReadOnlyCollection<MasterDataReferenceResponse> References);

public sealed record ResolveMasterDataReferencesQuery(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<MasterDataReferenceRequest> References) : IQuery<ResolveMasterDataReferencesResponse>;

public sealed class ResolveMasterDataReferencesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ResolveMasterDataReferencesQuery, ResolveMasterDataReferencesResponse>
{
    private const int MaxReferences = 200;

    public async Task<ResolveMasterDataReferencesResponse> Handle(ResolveMasterDataReferencesQuery request, CancellationToken cancellationToken)
    {
        var references = request.References.ToArray();
        if (references.Length is < 1 or > MaxReferences)
        {
            throw new KnownException("主数据引用批次必须包含 1 至 200 条引用。");
        }

        var deviceReferences = references
            .Where(reference => string.Equals(
                reference.ResourceType.Trim(),
                "device-asset",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var resolvedDevices = await ResolveDeviceAssetBatchAsync(
            request.OrganizationId,
            request.EnvironmentId,
            deviceReferences,
            cancellationToken);
        var responses = new List<MasterDataReferenceResponse>();
        foreach (var reference in references)
        {
            var type = reference.ResourceType.Trim().ToLowerInvariant();
            responses.Add(type == "device-asset"
                ? resolvedDevices[reference.Code.Trim()]
                : await ResolveAsync(request.OrganizationId, request.EnvironmentId, reference, cancellationToken));
        }

        return new ResolveMasterDataReferencesResponse(responses);
    }

    private async Task<IReadOnlyDictionary<string, MasterDataReferenceResponse>> ResolveDeviceAssetBatchAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<MasterDataReferenceRequest> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return new Dictionary<string, MasterDataReferenceResponse>(StringComparer.Ordinal);
        }

        var requestedReferences = references.Select(reference => reference.Code.Trim()).ToArray();
        var distinctReferences = requestedReferences.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctReferences.Length != requestedReferences.Length)
        {
            return distinctReferences.ToDictionary(
                reference => reference,
                reference => Missing("device-asset", reference, "duplicate-reference"),
                StringComparer.Ordinal);
        }

        var requestedIds = distinctReferences
            .Select(reference => Guid.TryParse(reference, out var parsed) && parsed != Guid.Empty
                ? new DeviceAssetId(parsed)
                : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToArray();
        var directCandidates = await DeviceAuthorityCandidatesQuery(
                dbContext,
                organizationId,
                environmentId,
                requestedIds,
                distinctReferences)
            .ToArrayAsync(cancellationToken);

        var authorityReferences = distinctReferences
            .Concat(directCandidates.Select(candidate => candidate.DeviceAssetId.ToString()))
            .Concat(directCandidates.Select(candidate => candidate.Code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var authorityIds = authorityReferences
            .Select(reference => Guid.TryParse(reference, out var parsed) && parsed != Guid.Empty
                ? new DeviceAssetId(parsed)
                : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToArray();
        var authorityCandidates = await DeviceAuthorityCandidatesQuery(
                dbContext,
                organizationId,
                environmentId,
                authorityIds,
                authorityReferences)
            .ToArrayAsync(cancellationToken);

        var resolved = new Dictionary<string, DeviceAssetAuthoritySnapshot>(StringComparer.Ordinal);
        var invalidReason = string.Empty;
        foreach (var reference in distinctReferences)
        {
            var matches = MatchingDevices(reference, authorityCandidates);
            if (matches.Length != 1)
            {
                invalidReason = matches.Length == 0 ? "not-found" : "ambiguous";
                break;
            }
            resolved.Add(reference, matches[0]);
        }

        if (invalidReason.Length == 0)
        {
            foreach (var device in resolved.Values.DistinctBy(candidate => candidate.DeviceAssetId))
            {
                var aliases = new[] { device.DeviceAssetId.ToString(), device.Code }
                    .Distinct(StringComparer.Ordinal);
                if (aliases.Any(alias =>
                {
                    var matches = MatchingDevices(alias, authorityCandidates);
                    return matches.Length != 1 || matches[0].DeviceAssetId != device.DeviceAssetId;
                }))
                {
                    invalidReason = "ambiguous";
                    break;
                }
            }
        }

        if (invalidReason.Length > 0)
        {
            return distinctReferences.ToDictionary(
                reference => reference,
                reference => Missing("device-asset", reference, invalidReason),
                StringComparer.Ordinal);
        }

        return resolved.ToDictionary(
            pair => pair.Key,
            pair => FoundDevice(pair.Key, pair.Value),
            StringComparer.Ordinal);
    }

    private static IQueryable<DeviceAssetAuthoritySnapshot> DeviceAuthorityCandidatesQuery(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<DeviceAssetId> deviceAssetIds,
        IReadOnlyCollection<string> references) =>
        dbContext.DeviceAssets
            .AsNoTracking()
            .Where(device => device.OrganizationId == organizationId && device.EnvironmentId == environmentId)
            .Where(device => deviceAssetIds.Contains(device.Id) || references.Contains(device.Code))
            .Select(device => new DeviceAssetAuthoritySnapshot(
                device.Id,
                device.Code,
                device.Model,
                device.Disabled,
                device.UpdatedAtUtc,
                device.OrganizationId,
                device.EnvironmentId));

    private static DeviceAssetAuthoritySnapshot[] MatchingDevices(
        string reference,
        IReadOnlyCollection<DeviceAssetAuthoritySnapshot> candidates)
    {
        var hasId = Guid.TryParse(reference, out var parsed) && parsed != Guid.Empty;
        var deviceAssetId = hasId ? new DeviceAssetId(parsed) : null;
        return candidates
            .Where(candidate => string.Equals(candidate.Code, reference, StringComparison.Ordinal)
                || (deviceAssetId is not null && candidate.DeviceAssetId == deviceAssetId))
            .DistinctBy(candidate => candidate.DeviceAssetId)
            .ToArray();
    }

    private static MasterDataReferenceResponse FoundDevice(
        string reference,
        DeviceAssetAuthoritySnapshot device) =>
        new(
            "device-asset",
            reference,
            true,
            !device.Disabled,
            device.DisplayName,
            device.UpdatedAtUtc.ToString("O"),
            device.Disabled ? "disabled" : string.Empty,
            device.DeviceAssetId.ToString(),
            device.Code,
            device.OrganizationId,
            device.EnvironmentId);

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

    private static MasterDataReferenceResponse Missing(string resourceType, string code, string reason = "not-found")
    {
        return new MasterDataReferenceResponse(resourceType, code, false, false, string.Empty, string.Empty, reason);
    }

    private sealed record DeviceAssetAuthoritySnapshot(
        DeviceAssetId DeviceAssetId,
        string Code,
        string DisplayName,
        bool Disabled,
        DateTime UpdatedAtUtc,
        string OrganizationId,
        string EnvironmentId);
}

using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public sealed record DeviceAssetReferenceValidationResult(
    string SupplierPartnerCode,
    string ParentDeviceId);

public interface IDeviceAssetReferenceValidator
{
    Task<DeviceAssetReferenceValidationResult> ValidateForCreateAsync(
        string organizationId,
        string environmentId,
        string? supplierPartnerCode,
        string? parentDeviceId,
        CancellationToken cancellationToken);

    Task<DeviceAssetReferenceValidationResult> ValidateForUpdateAsync(
        DeviceAsset device,
        string? supplierPartnerCode,
        string? parentDeviceId,
        CancellationToken cancellationToken);
}

public sealed class DeviceAssetReferenceValidator(ApplicationDbContext dbContext)
    : IDeviceAssetReferenceValidator
{
    public async Task<DeviceAssetReferenceValidationResult> ValidateForCreateAsync(
        string organizationId,
        string environmentId,
        string? supplierPartnerCode,
        string? parentDeviceId,
        CancellationToken cancellationToken)
    {
        var normalizedSupplierCode = await NormalizeAndValidateSupplierAsync(
            organizationId,
            environmentId,
            supplierPartnerCode,
            cancellationToken);
        var normalizedParentId = await NormalizeAndValidateParentAsync(
            organizationId,
            environmentId,
            parentDeviceId,
            currentDeviceId: null,
            cancellationToken);
        return new DeviceAssetReferenceValidationResult(normalizedSupplierCode, normalizedParentId);
    }

    public async Task<DeviceAssetReferenceValidationResult> ValidateForUpdateAsync(
        DeviceAsset device,
        string? supplierPartnerCode,
        string? parentDeviceId,
        CancellationToken cancellationToken)
    {
        var normalizedSupplierCode = supplierPartnerCode is null
            ? device.SupplierPartnerCode
            : await NormalizeAndValidateSupplierAsync(
                device.OrganizationId,
                device.EnvironmentId,
                supplierPartnerCode,
                cancellationToken);
        var normalizedParentId = parentDeviceId is null
            ? device.ParentDeviceId
            : await NormalizeAndValidateParentAsync(
                device.OrganizationId,
                device.EnvironmentId,
                parentDeviceId,
                device.Id,
                cancellationToken);
        return new DeviceAssetReferenceValidationResult(normalizedSupplierCode, normalizedParentId);
    }

    private async Task<string> NormalizeAndValidateSupplierAsync(
        string organizationId,
        string environmentId,
        string? supplierPartnerCode,
        CancellationToken cancellationToken)
    {
        var normalizedSupplierCode = supplierPartnerCode?.Trim() ?? string.Empty;
        if (normalizedSupplierCode.Length == 0)
        {
            return string.Empty;
        }

        var supplier = await dbContext.BusinessPartners.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Code == normalizedSupplierCode &&
                !x.Disabled,
            cancellationToken);
        if (supplier is null ||
            !supplier.PartnerRoles.Any(role =>
                string.Equals(role, "supplier", StringComparison.OrdinalIgnoreCase)))
        {
            throw new KnownException(
                $"Device asset supplier '{normalizedSupplierCode}' must reference an active supplier partner in the same organization and environment.");
        }

        return normalizedSupplierCode;
    }

    private async Task<string> NormalizeAndValidateParentAsync(
        string organizationId,
        string environmentId,
        string? parentDeviceId,
        DeviceAssetId? currentDeviceId,
        CancellationToken cancellationToken)
    {
        var candidate = parentDeviceId?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            return string.Empty;
        }

        if (!Guid.TryParse(candidate, out var publicId))
        {
            throw new KnownException("Device asset parentDeviceId must be a valid DeviceAsset public GUID.");
        }

        var parentId = new DeviceAssetId(publicId);
        if (currentDeviceId is not null && parentId == currentDeviceId)
        {
            throw new KnownException("Device asset cannot reference itself as its parent.");
        }

        var parent = await dbContext.DeviceAssets.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Id == parentId &&
                !x.Disabled,
            cancellationToken);
        if (parent is null)
        {
            throw new KnownException(
                $"Device asset parent '{publicId}' must reference an active device in the same organization and environment.");
        }

        var visited = new HashSet<Guid>();
        for (var depth = 0; depth < 256; depth++)
        {
            var ancestorPublicId = parent.Id.Id;
            if (currentDeviceId is not null && parent.Id == currentDeviceId)
            {
                throw new KnownException("Device asset parent hierarchy would create a cycle.");
            }

            if (!visited.Add(ancestorPublicId))
            {
                throw new KnownException("Device asset parent hierarchy contains a pre-existing cycle.");
            }

            var storedParentId = parent.ParentDeviceId.Trim();
            if (storedParentId.Length == 0)
            {
                return publicId.ToString();
            }

            if (!Guid.TryParse(storedParentId, out var nextPublicId))
            {
                throw new KnownException(
                    $"Device asset parent hierarchy contains malformed stored ancestry at '{ancestorPublicId}'.");
            }

            var nextId = new DeviceAssetId(nextPublicId);
            if (currentDeviceId is not null && nextId == currentDeviceId)
            {
                throw new KnownException("Device asset parent hierarchy would create a cycle.");
            }

            parent = await dbContext.DeviceAssets.SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Id == nextId &&
                    !x.Disabled,
                cancellationToken)
                ?? throw new KnownException(
                    $"Device asset parent hierarchy references missing, inactive, or wrong-scope ancestor '{nextPublicId}'.");
        }

        throw new KnownException("Device asset parent hierarchy exceeds the supported depth.");
    }
}

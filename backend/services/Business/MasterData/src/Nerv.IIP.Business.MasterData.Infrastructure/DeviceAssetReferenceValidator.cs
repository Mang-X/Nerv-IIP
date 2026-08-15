using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
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

    Task ValidateStoredReferencesForEnableAsync(
        DeviceAsset device,
        CancellationToken cancellationToken);

    Task EnsureSupplierRoleRemovalAllowedAsync(
        BusinessPartner partner,
        IReadOnlyCollection<string> proposedRoles,
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

    public async Task ValidateStoredReferencesForEnableAsync(
        DeviceAsset device,
        CancellationToken cancellationToken)
    {
        await NormalizeAndValidateSupplierAsync(
            device.OrganizationId,
            device.EnvironmentId,
            device.SupplierPartnerCode,
            cancellationToken);
        await NormalizeAndValidateParentAsync(
            device.OrganizationId,
            device.EnvironmentId,
            device.ParentDeviceId,
            device.Id,
            cancellationToken);
    }

    public async Task EnsureSupplierRoleRemovalAllowedAsync(
        BusinessPartner partner,
        IReadOnlyCollection<string> proposedRoles,
        CancellationToken cancellationToken)
    {
        var isSupplier = partner.PartnerRoles.Any(IsSupplierRole);
        var remainsSupplier = proposedRoles.Any(IsSupplierRole);
        if (!isSupplier || remainsSupplier)
        {
            return;
        }

        var referenced = await dbContext.DeviceAssets.AnyAsync(
            x => x.OrganizationId == partner.OrganizationId &&
                x.EnvironmentId == partner.EnvironmentId &&
                !x.Disabled &&
                x.SupplierPartnerCode == partner.Code,
            cancellationToken);
        if (referenced)
        {
            throw new KnownException(
                $"业务伙伴 '{partner.Code}' 仍被启用的设备资产引用，不能移除供应商角色。请先清除相关设备资产的供应商引用。");
        }
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
            !supplier.PartnerRoles.Any(IsSupplierRole))
        {
            throw new KnownException(
                $"设备资产供应商 '{normalizedSupplierCode}' 必须引用同组织、同环境内已启用且具有供应商角色的业务伙伴。");
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
            throw new KnownException("父设备 parentDeviceId 必须是有效的 DeviceAsset 公共 GUID。");
        }

        var parentId = new DeviceAssetId(publicId);
        if (currentDeviceId is not null && parentId == currentDeviceId)
        {
            throw new KnownException("设备资产不能将自身设置为父设备。");
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
                $"父设备 '{publicId}' 必须引用同组织、同环境内已启用的设备资产。");
        }

        var visited = new HashSet<Guid>();
        for (var depth = 0; depth < 256; depth++)
        {
            var ancestorPublicId = parent.Id.Id;
            if (currentDeviceId is not null && parent.Id == currentDeviceId)
            {
                throw new KnownException("父设备层级将形成环路，请选择其他父设备。");
            }

            if (!visited.Add(ancestorPublicId))
            {
                throw new KnownException("父设备层级已存在环路，请先修复既有层级。");
            }

            var storedParentId = parent.ParentDeviceId.Trim();
            if (storedParentId.Length == 0)
            {
                return publicId.ToString();
            }

            if (!Guid.TryParse(storedParentId, out var nextPublicId))
            {
                throw new KnownException(
                    $"父设备层级在 '{ancestorPublicId}' 处包含格式错误的既有父设备引用。");
            }

            var nextId = new DeviceAssetId(nextPublicId);
            if (currentDeviceId is not null && nextId == currentDeviceId)
            {
                throw new KnownException("父设备层级将形成环路，请选择其他父设备。");
            }

            parent = await dbContext.DeviceAssets.SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.Id == nextId &&
                    !x.Disabled,
                cancellationToken)
                ?? throw new KnownException(
                    $"父设备层级引用了缺失、已停用或不在当前范围内的祖先设备 '{nextPublicId}'。请先修复父设备层级。");
        }

        throw new KnownException("父设备层级超过支持的最大深度 256，请检查层级是否异常。");
    }

    private static bool IsSupplierRole(string role) =>
        string.Equals(role, "supplier", StringComparison.OrdinalIgnoreCase);
}

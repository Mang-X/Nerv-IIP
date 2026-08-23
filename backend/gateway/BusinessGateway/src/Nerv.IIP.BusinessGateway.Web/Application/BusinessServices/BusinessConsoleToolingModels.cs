namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public enum BusinessConsoleToolingAssetStatus
{
    Available = 0,
    Maintenance = 1,
    Retired = 2,
}

public sealed record BusinessConsoleListToolingAssetsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    BusinessConsoleToolingAssetStatus? Status = null,
    int Skip = 0,
    int Take = 100);

public sealed record BusinessConsoleToolingAssetItem(
    string Code,
    string Name,
    string ToolingType,
    BusinessConsoleToolingAssetStatus Status,
    long? MaintenanceLifeCount,
    long UsageCount,
    bool IsSchedulable,
    IReadOnlyCollection<string> WorkCenterCodes,
    IReadOnlyCollection<string> SkuCodes);

public sealed record BusinessConsoleToolingAssetListResponse(
    IReadOnlyCollection<BusinessConsoleToolingAssetItem> Items,
    int Total);

public sealed record BusinessConsoleRegisterToolingAssetRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Code,
    string Name,
    string ToolingType,
    IReadOnlyCollection<string> WorkCenterCodes,
    IReadOnlyCollection<string> SkuCodes,
    long? MaintenanceLifeCount,
    string? IdempotencyKey);

public sealed record BusinessConsoleToolingRegistrationResponse(
    string ResourceType,
    string Code,
    string DisplayName);

public sealed record BusinessConsoleChangeToolingStatusRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    BusinessConsoleToolingAssetStatus Status,
    string Reason);

public sealed record BusinessConsoleRecordToolingUsageRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    long Count);

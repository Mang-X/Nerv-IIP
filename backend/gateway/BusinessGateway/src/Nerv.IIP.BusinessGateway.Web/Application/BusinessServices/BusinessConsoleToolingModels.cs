using System.Text.Json.Serialization;

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
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string ToolingType,
    [property: JsonRequired] BusinessConsoleToolingAssetStatus Status,
    [property: JsonRequired] long? MaintenanceLifeCount,
    [property: JsonRequired] long UsageCount,
    [property: JsonRequired] bool IsSchedulable,
    [property: JsonRequired] IReadOnlyCollection<string> WorkCenterCodes,
    [property: JsonRequired] IReadOnlyCollection<string> SkuCodes);

public sealed record BusinessConsoleToolingAssetListResponse(
    [property: JsonRequired] IReadOnlyCollection<BusinessConsoleToolingAssetItem> Items,
    [property: JsonRequired] int Total);

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
    [property: JsonRequired] string ResourceType,
    [property: JsonRequired] string Code,
    [property: JsonRequired] string DisplayName);

public sealed record BusinessConsoleChangeToolingStatusRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    BusinessConsoleToolingAssetStatus Status,
    string Reason,
    string? IdempotencyKey = null);

public sealed record BusinessConsoleRecordToolingUsageRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    long Count,
    string? IdempotencyKey = null);

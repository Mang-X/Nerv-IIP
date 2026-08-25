namespace Nerv.IIP.Contracts.Inventory;

public sealed record LineSideInventoryBalancesRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode = null,
    string? LocationCode = null,
    string? SkuCode = null,
    DateOnly? AsOfDate = null,
    int Page = 1,
    int PageSize = 50);

public sealed record LineSideInventoryBalancesResponse(
    IReadOnlyCollection<LineSideInventoryBalanceItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    DateOnly AsOfDate);

public sealed record LineSideInventoryBalanceItem(
    string SiteCode,
    string LocationCode,
    string SkuCode,
    string UomCode,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int LotCount,
    DateOnly? OldestProductionDate,
    int? AgeDays,
    string AgeCompleteness);

public static class LineSideInventoryAgeCompleteness
{
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
}

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesLineSideInventoryBalancesRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode = null,
    string? LocationCode = null,
    string? SkuCode = null,
    DateOnly? AsOfDate = null,
    int Page = 1,
    int PageSize = 50);

public sealed record BusinessConsoleMesLineSideInventoryBalancesResponse(
    IReadOnlyCollection<BusinessConsoleMesLineSideInventoryBalanceItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    DateOnly AsOfDate);

public sealed record BusinessConsoleMesLineSideInventoryBalanceItem(
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

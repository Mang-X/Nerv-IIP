using System.Text.Json;
using System.Text.Json.Serialization;

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
    [property: JsonConverter(typeof(LineSideInventoryAgeCompletenessJsonConverter))]
    LineSideInventoryAgeCompleteness AgeCompleteness);

public enum LineSideInventoryAgeCompleteness
{
    Complete,
    Partial,
    Unavailable,
}

public sealed class LineSideInventoryAgeCompletenessJsonConverter()
    : JsonStringEnumConverter<LineSideInventoryAgeCompleteness>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

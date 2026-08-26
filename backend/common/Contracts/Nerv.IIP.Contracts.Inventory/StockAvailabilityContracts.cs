using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.Inventory;

public sealed record StockAvailabilityResponse(
    [property: JsonRequired, Required] string OrganizationId,
    [property: JsonRequired, Required] string EnvironmentId,
    [property: JsonRequired, Required] string SkuCode,
    [property: JsonRequired, Required] string UomCode,
    [property: JsonRequired, Required] string SiteCode,
    [property: JsonRequired] string? LocationCode,
    [property: JsonRequired] string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    [property: JsonRequired, Required] decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal InventoryValue,
    [property: JsonRequired, Required] IReadOnlyCollection<StockAvailabilityLineResponse> Items);

public sealed record StockAvailabilityLineResponse(
    [property: JsonRequired, Required] string LocationCode,
    [property: JsonRequired] string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    int? ShelfLifeDays,
    string? ExpiryDateSource,
    [property: JsonRequired, Required] bool IsExpired,
    bool IsBlocked,
    string? BlockReasonCode,
    string? BlockReason,
    [property: JsonRequired, Required] bool MovementAllowed,
    string? MovementBlockReasonCode,
    string? MovementBlockReason,
    bool CountAllowed,
    string? CountBlockReasonCode,
    string? CountBlockReason,
    [property: JsonRequired, Required] decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal InventoryValue);

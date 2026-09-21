namespace Nerv.IIP.Contracts.Erp;

public sealed record ResolveMaterialSupplyEtasRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<MaterialSupplyEtaRequestItem> Items);

public sealed record MaterialSupplyEtaRequestItem(
    string SkuCode,
    string UomCode,
    decimal ShortageQuantity);

public sealed record ResolveMaterialSupplyEtasResponse(
    IReadOnlyCollection<MaterialSupplyEtaResponseItem> Items);

public sealed record MaterialSupplyEtaResponseItem(
    string SkuCode,
    string UomCode,
    decimal ShortageQuantity,
    decimal OpenPurchaseQuantity,
    DateOnly? ExpectedAvailableDate);

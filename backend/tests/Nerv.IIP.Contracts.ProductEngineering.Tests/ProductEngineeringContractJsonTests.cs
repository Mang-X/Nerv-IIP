using System.Text.Json;
using Nerv.IIP.Contracts.ProductEngineering;

namespace Nerv.IIP.Contracts.ProductEngineering.Tests;

public sealed class ProductEngineeringContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Resolve_production_version_response_serializes_for_mes_work_order_contract()
    {
        var response = new ResolveProductionVersionResponse(
            ProductionVersionId: "018f9f7e-78c8-73b3-b4b0-2e26a7442b1a",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            SkuCode: "SKU-FG-1000",
            MbomVersionId: "mbom-A",
            RoutingVersionId: "routing-A",
            EffectiveDate: new DateOnly(2026, 6, 1),
            LotSize: 24m,
            Status: ProductionEngineeringContractStatuses.Active);

        var json = JsonSerializer.Serialize(response, JsonOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("018f9f7e-78c8-73b3-b4b0-2e26a7442b1a", root.GetProperty("productionVersionId").GetString());
        Assert.Equal("SKU-FG-1000", root.GetProperty("skuCode").GetString());
        Assert.Equal("mbom-A", root.GetProperty("mbomVersionId").GetString());
        Assert.Equal("routing-A", root.GetProperty("routingVersionId").GetString());
        Assert.Equal("2026-06-01", root.GetProperty("effectiveDate").GetString());
        Assert.Equal("active", root.GetProperty("status").GetString());
    }

    [Fact]
    public void Resolve_production_version_request_carries_scope_sku_effective_date_and_lot_size()
    {
        var request = new ResolveProductionVersionRequest(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            new DateOnly(2026, 6, 1),
            24m);

        Assert.Equal("org-001", request.OrganizationId);
        Assert.Equal("env-dev", request.EnvironmentId);
        Assert.Equal("SKU-FG-1000", request.SkuCode);
        Assert.Equal(new DateOnly(2026, 6, 1), request.EffectiveDate);
        Assert.Equal(24m, request.LotSize);
    }
}

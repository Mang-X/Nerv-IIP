using System.Text.Json;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;
using Nerv.IIP.Business.ProductEngineering.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.ProductEngineering;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringIntegrationEventTests
{
    [Fact]
    public void Engineering_bom_release_converter_emits_stable_bom_released_event()
    {
        var bom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-1000", "A", "ENG-1000")
            .AddLine("ENG-1001", 2m, "EA");
        bom.Release(new DateOnly(2026, 6, 1));

        var converter = new EngineeringBomReleasedIntegrationEventConverter(new StubContextAccessor());
        var integrationEvent = converter.Convert(new EngineeringBomReleasedDomainEvent(bom));

        Assert.Equal(ProductEngineeringIntegrationEventTypes.BomReleased, integrationEvent.EventType);
        Assert.Equal(ProductEngineeringIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal(ProductEngineeringIntegrationEventSources.BusinessProductEngineering, integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("engineering", integrationEvent.Payload.BomType);
        Assert.Contains("product-engineering:engineering-bom-released:org-001:env-dev:EBOM-1000:A", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
        AssertJsonUsesCamelCase(integrationEvent, "eventType", "bomVersionId", "componentCode");
    }

    [Fact]
    public void Manufacturing_bom_release_converter_emits_stable_bom_released_event()
    {
        var bom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 1.5m, "KG", 0m);
        bom.ReleaseFromEngineeringBom("ebom-001", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));

        var converter = new ManufacturingBomReleasedIntegrationEventConverter(new StubContextAccessor());
        var integrationEvent = converter.Convert(new ManufacturingBomReleasedDomainEvent(bom));

        Assert.Equal(ProductEngineeringIntegrationEventTypes.BomReleased, integrationEvent.EventType);
        Assert.Equal("manufacturing", integrationEvent.Payload.BomType);
        Assert.Equal("SKU-FG-1000", integrationEvent.Payload.ItemOrSkuCode);
        AssertJsonUsesCamelCase(integrationEvent, "eventType", "itemOrSkuCode", "unitOfMeasureCode");
    }

    [Fact]
    public void Routing_release_converter_emits_stable_routing_released_event()
    {
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-MIX-01", "mixing", "混合", 30);
        routing.Release(new DateOnly(2026, 6, 1));

        var converter = new RoutingReleasedIntegrationEventConverter(new StubContextAccessor());
        var integrationEvent = converter.Convert(new RoutingReleasedDomainEvent(routing));

        Assert.Equal(ProductEngineeringIntegrationEventTypes.RoutingReleased, integrationEvent.EventType);
        Assert.Equal("SKU-FG-1000", integrationEvent.Payload.SkuCode);
        Assert.Equal(10, integrationEvent.Payload.Operations.Single().Sequence);
        Assert.Equal("mixing", integrationEvent.Payload.Operations.Single().OperationCode);
        AssertJsonUsesCamelCase(integrationEvent, "eventType", "routingVersionId", "operationCode", "workCenterCode");
    }

    [Fact]
    public void Production_version_converter_emits_stable_created_event()
    {
        var version = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-A",
            new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);

        var converter = new ProductionVersionCreatedIntegrationEventConverter(new StubContextAccessor());
        var integrationEvent = converter.Convert(new ProductionVersionCreatedDomainEvent(version));

        Assert.Equal(ProductEngineeringIntegrationEventTypes.ProductionVersionCreated, integrationEvent.EventType);
        Assert.Equal("SKU-FG-1000", integrationEvent.Payload.SkuCode);
        AssertJsonUsesCamelCase(integrationEvent, "eventType", "productionVersionId", "routingVersionId");
    }

    [Fact]
    public void Engineering_change_release_converter_emits_stable_change_released_event()
    {
        var change = EngineeringChange.Open("org-001", "env-dev", "ECO-0001", "Release MBOM A")
            .Affect("manufacturing-bom", "mbom-001")
            .Approve("approval-chain-001");
        change.Release(new DateOnly(2026, 6, 1));

        var converter = new EngineeringChangeReleasedIntegrationEventConverter(new StubContextAccessor());
        var integrationEvent = converter.Convert(new EngineeringChangeReleasedDomainEvent(change));

        Assert.Equal(ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased, integrationEvent.EventType);
        Assert.Equal("ECO-0001", integrationEvent.Payload.ChangeNumber);
        Assert.Equal("mbom-001", integrationEvent.Payload.AffectedVersionIds.Single());
        var affectedVersion = Assert.Single(integrationEvent.Payload.AffectedVersions);
        Assert.Equal("manufacturing-bom", affectedVersion.VersionKind);
        Assert.Equal("mbom-001", affectedVersion.VersionId);
        Assert.Null(affectedVersion.SupersededByVersionId);
        Assert.Equal(new DateOnly(2026, 6, 1), integrationEvent.Payload.EffectiveDate);
        AssertJsonUsesCamelCase(integrationEvent, "eventType", "changeNumber", "affectedVersionIds", "affectedVersions", "effectiveDate");
    }

    private static void AssertJsonUsesCamelCase<T>(T value, params string[] expectedPropertyNames)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var propertyName in expectedPropertyNames)
        {
            Assert.Contains($"\"{propertyName}\"", json, StringComparison.Ordinal);
        }
    }

    private sealed class StubContextAccessor : IProductEngineeringIntegrationEventContextAccessor
    {
        public ProductEngineeringIntegrationEventContext GetContext()
        {
            return new ProductEngineeringIntegrationEventContext("corr-001", "cause-001", "user:engineer-001");
        }
    }
}

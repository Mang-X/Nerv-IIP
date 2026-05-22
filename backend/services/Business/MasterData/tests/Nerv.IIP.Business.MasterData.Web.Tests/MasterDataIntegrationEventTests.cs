using System.Text.Json;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Business.MasterData.Domain.DomainEvents;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataIntegrationEventTests
{
    [Fact]
    public void Sku_changed_event_uses_stable_adr0011_envelope_shape()
    {
        var converter = new SkuChangedIntegrationEventConverter(new StubMasterDataIntegrationEventContextAccessor());
        var domainEvent = new SkuChangedDomainEvent("org-001", "env-dev", "SKU-FG-1000");

        var integrationEvent = converter.Convert(domainEvent);
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("masterData.SkuChanged", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("business-masterdata", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("masterdata:sku-changed:org-001:env-dev:SKU-FG-1000", integrationEvent.IdempotencyKey);
        Assert.Equal("sku", integrationEvent.Payload.ResourceType);
        Assert.Contains("\"eventType\":\"masterData.SkuChanged\"", json, StringComparison.Ordinal);
        Assert.Contains("\"payload\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Sku_changed_event_propagates_correlation_causation_and_actor_context()
    {
        var converter = new SkuChangedIntegrationEventConverter(new StubMasterDataIntegrationEventContextAccessor(
            new MasterDataIntegrationEventContext(
                "corr-masterdata-001",
                "cmd-create-sku-001",
                "user:planner-001")));
        var domainEvent = new SkuChangedDomainEvent("org-001", "env-dev", "SKU-FG-1000");

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("corr-masterdata-001", integrationEvent.CorrelationId);
        Assert.Equal("cmd-create-sku-001", integrationEvent.CausationId);
        Assert.Equal("user:planner-001", integrationEvent.Actor);
    }

    [Fact]
    public void Sku_disabled_event_carries_reason_without_sensitive_payload()
    {
        var converter = new SkuDisabledIntegrationEventConverter(new StubMasterDataIntegrationEventContextAccessor());
        var domainEvent = new SkuDisabledDomainEvent("org-001", "env-dev", "SKU-OLD", "duplicate registration");

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("masterData.SkuDisabled", integrationEvent.EventType);
        Assert.Equal("disabled", integrationEvent.Payload.Status);
        Assert.Equal("duplicate registration", integrationEvent.Payload.DisabledReason);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reference_data_event_uses_code_set_in_payload_and_idempotency_key()
    {
        var converter = new ReferenceDataCodeChangedIntegrationEventConverter(new StubMasterDataIntegrationEventContextAccessor());
        var domainEvent = new ReferenceDataCodeChangedDomainEvent("org-001", "env-dev", "material-form", "powder");

        var integrationEvent = converter.Convert(domainEvent);

        Assert.Equal("masterData.ReferenceDataCodeChanged", integrationEvent.EventType);
        Assert.Equal("material-form", integrationEvent.Payload.CodeSet);
        Assert.Equal("powder", integrationEvent.Payload.Code);
        Assert.Equal("masterdata:reference-data-code-changed:org-001:env-dev:material-form:powder", integrationEvent.IdempotencyKey);
    }

    private sealed class StubMasterDataIntegrationEventContextAccessor(
        MasterDataIntegrationEventContext? context = null)
        : IMasterDataIntegrationEventContextAccessor
    {
        public MasterDataIntegrationEventContext GetContext()
        {
            return context ?? new MasterDataIntegrationEventContext(
                "corr-test-001",
                "cause-test-001",
                "system:business-masterdata");
        }
    }
}

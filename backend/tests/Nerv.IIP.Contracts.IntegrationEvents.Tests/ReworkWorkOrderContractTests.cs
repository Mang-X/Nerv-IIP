using System.Text.Json;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Contracts.IntegrationEvents.Tests;

public sealed class ReworkWorkOrderContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Quality_rework_request_round_trips_source_scope_and_quantity()
    {
        var requested = new NcrReworkRequestedIntegrationEvent(
            "evt-quality-001",
            QualityIntegrationEventTypes.NcrReworkRequested,
            QualityIntegrationEventVersions.V1,
            At(1),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "user:quality-manager",
            "quality:ncr-rework-requested:org-001:env-dev:ncr-001",
            new NcrReworkRequestedPayload(
                "ncr-001",
                "NCR-2026-0001",
                "DEF-001",
                "SKU-001",
                3m,
                "LOT-001",
                "SN-001",
                At(1)));

        var roundTrip = JsonSerializer.Deserialize<NcrReworkRequestedIntegrationEvent>(
            JsonSerializer.Serialize(requested, JsonOptions),
            JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal("org-001", roundTrip.OrganizationId);
        Assert.Equal("env-dev", roundTrip.EnvironmentId);
        Assert.Equal("ncr-001", roundTrip.Payload.NcrId);
        Assert.Equal("DEF-001", roundTrip.Payload.SourceDefectNo);
        Assert.Equal(3m, roundTrip.Payload.Quantity);
    }

    [Fact]
    public void Mes_rework_created_receipt_round_trips_created_work_order_lineage()
    {
        var created = new ReworkWorkOrderCreatedIntegrationEvent(
            "evt-mes-001",
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1,
            At(2),
            MesIntegrationEventSources.BusinessMes,
            "corr-001",
            "evt-quality-001",
            "org-001",
            "env-dev",
            "system:business-mes",
            "mes:rework-work-order-created:org-001:env-dev:ncr-001",
            new ReworkWorkOrderCreatedPayload(
                "ncr-001",
                "NCR-2026-0001",
                "WO-RW-001",
                "WO-SOURCE-001",
                "OP-SOURCE-10",
                "SKU-001",
                3m,
                "LOT-001",
                "SN-001",
                At(2)));

        var roundTrip = JsonSerializer.Deserialize<ReworkWorkOrderCreatedIntegrationEvent>(
            JsonSerializer.Serialize(created, JsonOptions),
            JsonOptions);

        Assert.NotNull(roundTrip);
        Assert.Equal("WO-RW-001", roundTrip.Payload.ReworkWorkOrderId);
        Assert.Equal("WO-SOURCE-001", roundTrip.Payload.SourceWorkOrderId);
        Assert.Equal("OP-SOURCE-10", roundTrip.Payload.SourceOperationTaskId);
        Assert.Equal("ncr-001", roundTrip.Payload.SourceNcrId);
    }

    private static DateTimeOffset At(int minute) =>
        DateTimeOffset.Parse("2026-08-29T08:00:00Z").AddMinutes(minute);
}

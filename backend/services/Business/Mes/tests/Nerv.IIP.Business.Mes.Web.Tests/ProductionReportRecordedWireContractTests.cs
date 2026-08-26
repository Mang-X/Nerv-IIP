using System.Text.Json;
using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class ProductionReportRecordedWireContractTests
{
    private static readonly string[] HistoricalDimensionWireNames =
    [
        "SiteCode",
        "WorkshopCode",
        "LineCode",
        "ShiftCode",
        "SiteTimezone",
        "ShiftStartsAt",
        "ShiftEndsAt",
        "ShiftCrossesMidnight",
        "ShiftPaidMinutes",
        "ShiftBreakMinutes"
    ];

    [Fact]
    public void V1_wire_uses_frozen_field_names_for_historical_dimension_snapshot()
    {
        var jsonOptions = CreateMesCapJsonOptions();
        var json = JsonSerializer.Serialize(CreateCurrentEvent(), jsonOptions);

        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("Payload");
        Assert.Equal(HistoricalDimensionWireNames.Order(), payload.EnumerateObject()
            .Select(property => property.Name)
            .Where(HistoricalDimensionWireNames.Contains)
            .Order());
        Assert.DoesNotContain(payload.EnumerateObject(), property =>
            HistoricalDimensionWireNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
            && !HistoricalDimensionWireNames.Contains(property.Name, StringComparer.Ordinal));

        var roundTrip = JsonSerializer.Deserialize<ProductionReportRecordedIntegrationEvent>(json, jsonOptions);
        Assert.NotNull(roundTrip);
        Assert.Equal(MesIntegrationEventVersions.V1, roundTrip.EventVersion);
        Assert.Equal("SITE-SH", roundTrip.Payload.SiteCode);
        Assert.Equal(new TimeOnly(20, 0), roundTrip.Payload.ShiftStartsAt);
        Assert.Equal(30, roundTrip.Payload.ShiftBreakMinutes);
    }

    [Fact]
    public void V1_wire_remains_readable_in_both_directions_across_optional_field_additions()
    {
        var jsonOptions = CreateMesCapJsonOptions();

        var currentJson = JsonSerializer.Serialize(CreateCurrentEvent(), jsonOptions);
        var legacyConsumerEvent = JsonSerializer.Deserialize<LegacyProductionReportRecordedIntegrationEvent>(
            currentJson,
            jsonOptions);

        Assert.NotNull(legacyConsumerEvent);
        Assert.Equal(MesIntegrationEventVersions.V1, legacyConsumerEvent.EventVersion);
        Assert.Equal("RPT-WIRE-001", legacyConsumerEvent.Payload.ReportNo);
        Assert.Equal("WC-PACK-01", legacyConsumerEvent.Payload.WorkCenterId);

        var legacyJson = JsonSerializer.Serialize(CreateLegacyEvent(), jsonOptions);
        var currentConsumerEvent = JsonSerializer.Deserialize<ProductionReportRecordedIntegrationEvent>(
            legacyJson,
            jsonOptions);

        Assert.NotNull(currentConsumerEvent);
        Assert.Equal(MesIntegrationEventVersions.V1, currentConsumerEvent.EventVersion);
        Assert.Null(currentConsumerEvent.Payload.SiteCode);
        Assert.Null(currentConsumerEvent.Payload.WorkshopCode);
        Assert.Null(currentConsumerEvent.Payload.LineCode);
        Assert.Null(currentConsumerEvent.Payload.ShiftCode);
        Assert.Null(currentConsumerEvent.Payload.SiteTimezone);
        Assert.Null(currentConsumerEvent.Payload.ShiftStartsAt);
        Assert.Null(currentConsumerEvent.Payload.ShiftEndsAt);
        Assert.Null(currentConsumerEvent.Payload.ShiftCrossesMidnight);
        Assert.Null(currentConsumerEvent.Payload.ShiftPaidMinutes);
        Assert.Null(currentConsumerEvent.Payload.ShiftBreakMinutes);
    }

    private static JsonSerializerOptions CreateMesCapJsonOptions()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "production-report-wire-contract"
            })
            .Build();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(nameof(ProductionReportRecordedWireContractTests)));
        services.AddMesCapIntegrationEvents(configuration, "Testing", isTesting: true);

        using var provider = services.BuildServiceProvider();
        return new JsonSerializerOptions(provider.GetRequiredService<IOptions<CapOptions>>().Value.JsonSerializerOptions);
    }

    private static ProductionReportRecordedIntegrationEvent CreateCurrentEvent() => new(
        "evt-production-report-wire-001",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-07-10T17:30:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-production-report-wire-001",
        "cause-production-report-wire-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:production-report-recorded:org-001:env-dev:RPT-WIRE-001",
        new ProductionReportRecordedPayload(
            "RPT-WIRE-001",
            "WO-001",
            "OP-10",
            "WC-PACK-01",
            "DEV-PACK-01",
            80m,
            10m,
            10m,
            "PCS",
            100m,
            DateTimeOffset.Parse("2026-07-10T17:30:00Z"),
            false,
            SiteCode: "SITE-SH",
            WorkshopCode: "WS-ASSEMBLY",
            LineCode: "LINE-A",
            ShiftCode: "NIGHT",
            SiteTimezone: "Asia/Shanghai",
            ShiftStartsAt: new TimeOnly(20, 0),
            ShiftEndsAt: new TimeOnly(4, 0),
            ShiftCrossesMidnight: true,
            ShiftPaidMinutes: 450,
            ShiftBreakMinutes: 30));

    private static LegacyProductionReportRecordedIntegrationEvent CreateLegacyEvent() => new(
        "evt-production-report-wire-legacy-001",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-07-10T17:30:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-production-report-wire-legacy-001",
        "cause-production-report-wire-legacy-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:production-report-recorded:org-001:env-dev:RPT-WIRE-LEGACY-001",
        new LegacyProductionReportRecordedPayload(
            "RPT-WIRE-LEGACY-001",
            "WO-001",
            "OP-10",
            "WC-PACK-01",
            "DEV-PACK-01",
            80m,
            10m,
            10m,
            "PCS",
            100m,
            DateTimeOffset.Parse("2026-07-10T17:30:00Z"),
            false,
            null,
            0));

    private sealed record LegacyProductionReportRecordedIntegrationEvent(
        string EventId,
        string EventType,
        int EventVersion,
        DateTimeOffset OccurredAtUtc,
        string SourceService,
        string CorrelationId,
        string CausationId,
        string OrganizationId,
        string EnvironmentId,
        string Actor,
        string IdempotencyKey,
        LegacyProductionReportRecordedPayload Payload);

    private sealed record LegacyProductionReportRecordedPayload(
        string ReportNo,
        string WorkOrderId,
        string OperationTaskId,
        string WorkCenterId,
        string? DeviceAssetId,
        decimal GoodQuantity,
        decimal ScrapQuantity,
        decimal ReworkQuantity,
        string UomCode,
        decimal? TheoreticalRatePerHour,
        DateTimeOffset ReportedAtUtc,
        bool IsReversal,
        string? ReversedReportNo,
        int MaterialMovementCount);
}

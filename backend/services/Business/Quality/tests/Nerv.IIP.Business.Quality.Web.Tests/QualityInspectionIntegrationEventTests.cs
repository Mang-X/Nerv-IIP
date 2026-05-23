using System.Text.Json;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionIntegrationEventTests
{
    [Fact]
    public void Inspection_passed_event_uses_unified_stable_adr0011_envelope_shape()
    {
        var record = NewPassedRecord();
        var converter = new InspectionPassedIntegrationEventConverter(new StubQualityIntegrationEventContextAccessor());

        var integrationEvent = converter.Convert(new InspectionPassedDomainEvent(record));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.IsType<InspectionResultIntegrationEvent>(integrationEvent);
        Assert.Equal(QualityIntegrationEventTypes.InspectionPassed, integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal(QualityIntegrationEventSources.BusinessQuality, integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("passed", integrationEvent.Payload.Result);
        Assert.Equal("quality:inspection-passed:org-001:env-dev:purchase-receipt:RCV-001", integrationEvent.IdempotencyKey);
        Assert.Contains("\"eventType\":\"quality.InspectionPassed\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stockMovement", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspection_rejected_event_preserves_disposition_reason_and_attachment_refs()
    {
        var record = NewRejectedRecord();
        var converter = new InspectionRejectedIntegrationEventConverter(new StubQualityIntegrationEventContextAccessor(
            new QualityIntegrationEventContext("corr-quality-001", "cmd-record-inspection-001", "user:qa-001")));

        var integrationEvent = converter.Convert(new InspectionRejectedDomainEvent(record));

        Assert.Equal(QualityIntegrationEventTypes.InspectionRejected, integrationEvent.EventType);
        Assert.IsType<InspectionResultIntegrationEvent>(integrationEvent);
        Assert.Equal("corr-quality-001", integrationEvent.CorrelationId);
        Assert.Equal("cmd-record-inspection-001", integrationEvent.CausationId);
        Assert.Equal("user:qa-001", integrationEvent.Actor);
        Assert.Equal("rejected", integrationEvent.Payload.Result);
        Assert.Equal("Supplier certificate mismatch", integrationEvent.Payload.DispositionReason);
        Assert.Equal(["file-mrb-001"], integrationEvent.Payload.DispositionAttachmentFileIds);
    }

    [Fact]
    public void Inspection_result_event_idempotency_key_is_deterministic_for_same_record_result()
    {
        var record = NewRejectedRecord();
        var converter = new InspectionRejectedIntegrationEventConverter(new StubQualityIntegrationEventContextAccessor());

        var first = converter.Convert(new InspectionRejectedDomainEvent(record));
        var second = converter.Convert(new InspectionRejectedDomainEvent(record));

        Assert.NotEqual(first.EventId, second.EventId);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal("quality:inspection-rejected:org-001:env-dev:purchase-receipt:RCV-001", first.IdempotencyKey);
    }

    private static InspectionRecord NewPassedRecord()
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            10m,
            "BATCH-001",
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);
    }

    private static InspectionRecord NewRejectedRecord()
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            10m,
            "BATCH-001",
            null,
            [InspectionResultLineInput.Fail("coa", "mismatch", "wrong-spec", 10m, ["file-photo-001"])],
            "Supplier certificate mismatch",
            ["file-mrb-001"]);
    }

    private sealed class StubQualityIntegrationEventContextAccessor(QualityIntegrationEventContext? context = null)
        : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return context ?? new QualityIntegrationEventContext(
                "corr-test-001",
                "cause-test-001",
                "system:business-quality");
        }
    }
}

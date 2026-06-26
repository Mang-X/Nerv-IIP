using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityNcrIntegrationEventTests
{
    [Fact]
    public void Ncr_scrap_disposition_maps_to_blocked_inventory_adjustment_request()
    {
        var ncr = NewInventoryRoutedNcr();
        ncr.SubmitDisposition(QualityNcrDispositionTypes.Scrap, "approval-chain-001", [], ApprovedMrbReview());
        var domainEvent = ncr.GetDomainEvents().OfType<NonconformanceReportInventoryDispositionRequestedDomainEvent>().Single();

        var integrationEvent = new NcrInventoryDispositionRequestedIntegrationEventConverter(new StubQualityIntegrationEventContextAccessor())
            .Convert(domainEvent);

        Assert.Equal(InventoryIntegrationEventTypes.InventoryMovementRequested, integrationEvent.EventType);
        Assert.Equal(InventoryIntegrationEventSources.BusinessQuality, integrationEvent.SourceService);
        Assert.Equal("adjustment", integrationEvent.Payload.MovementType);
        Assert.Equal("quality", integrationEvent.Payload.SourceService);
        Assert.Equal(ncr.Id.ToString(), integrationEvent.Payload.SourceDocumentId);
        Assert.Equal(ncr.NcrCode, integrationEvent.Payload.SourceDocumentLineId);
        Assert.Equal("blocked", integrationEvent.Payload.QualityStatus);
        Assert.Equal(-4m, integrationEvent.Payload.Quantity);
        Assert.Null(integrationEvent.Payload.TargetQualityStatus);
    }

    [Fact]
    public void Ncr_rework_disposition_maps_to_blocked_to_restricted_status_transfer_request()
    {
        var ncr = NewInventoryRoutedNcr();
        ncr.SubmitDisposition(QualityNcrDispositionTypes.Rework, "approval-chain-001", [], ApprovedMrbReview());
        var domainEvent = ncr.GetDomainEvents().OfType<NonconformanceReportInventoryDispositionRequestedDomainEvent>().Single();

        var integrationEvent = new NcrInventoryDispositionRequestedIntegrationEventConverter(new StubQualityIntegrationEventContextAccessor())
            .Convert(domainEvent);

        Assert.Equal("status-transfer", integrationEvent.Payload.MovementType);
        Assert.Equal("blocked", integrationEvent.Payload.QualityStatus);
        Assert.Equal(QualityStockReleaseTargetStatuses.Restricted, integrationEvent.Payload.TargetQualityStatus);
        Assert.Equal(4m, integrationEvent.Payload.Quantity);
        Assert.Equal("kg", integrationEvent.Payload.UomCode);
        Assert.Equal("SITE-01", integrationEvent.Payload.SiteCode);
        Assert.Equal("IQC-HOLD", integrationEvent.Payload.LocationCode);
        Assert.Equal("LOT-001", integrationEvent.Payload.LotNo);
        Assert.Equal("company", integrationEvent.Payload.OwnerType);
    }

    private static NonconformanceReport NewInventoryRoutedNcr()
    {
        var inspection = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            4m,
            "LOT-001",
            null,
            [InspectionResultLineInput.Fail("coa", "mismatch", "wrong-spec", 4m, [])],
            "Supplier certificate mismatch",
            [],
            StockReleaseDimension.Create("kg", "SITE-01", "IQC-HOLD", "quality", "company", null));
        var ncr = NonconformanceReport.OpenFromInspection("NCR-20260626-0001", inspection, "wrong-spec", []);
        ncr.ClearDomainEvents();
        return ncr;
    }

    private static IReadOnlyCollection<MrbReviewInput> ApprovedMrbReview()
    {
        return [MrbReviewInput.Approve("qa-manager-001", "MRB accepted disposition", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))];
    }

    private sealed class StubQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-test-001",
                "cause-test-001",
                "system:business-quality");
        }
    }
}

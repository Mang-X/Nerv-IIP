using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class NonconformanceReportAggregateTests
{
    [Fact]
    public void Open_requires_positive_defect_quantity_and_defect_reason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewNcr(defectQuantity: 0));
        Assert.Throws<ArgumentException>(() => NewNcr(defectReason: " "));
    }

    [Fact]
    public void Open_captures_source_defect_scope_and_raises_opened_event()
    {
        var ncr = NewNcr(attachmentFileIds: ["file-photo-001", "file-coa-001"]);

        Assert.Equal("NCR-20260522-0001", ncr.NcrCode);
        Assert.Equal("receiving", ncr.SourceType);
        Assert.Equal("PO-RECEIPT-001", ncr.SourceDocumentId);
        Assert.Equal("SKU-RM-1000", ncr.SkuCode);
        Assert.Equal(12.5m, ncr.DefectQuantity);
        Assert.Equal("scratch", ncr.DefectReason);
        Assert.Equal("open", ncr.Status);
        Assert.Equal(["file-photo-001", "file-coa-001"], ncr.AttachmentFileIds);
        Assert.IsType<NonconformanceReportOpenedDomainEvent>(ncr.GetDomainEvents().Single());
    }

    [Theory]
    [InlineData("rework")]
    [InlineData("scrap")]
    [InlineData("return-to-supplier")]
    [InlineData("conditional-release")]
    [InlineData("sort-and-screen")]
    public void Submit_disposition_accepts_supported_types_and_raises_decided_event(string dispositionType)
    {
        var ncr = NewNcr();
        ncr.ClearDomainEvents();

        ncr.SubmitDisposition(dispositionType, "approval-chain-001", ["file-plan-001"]);

        Assert.Equal("disposition-in-progress", ncr.Status);
        Assert.Equal(dispositionType, ncr.DispositionType);
        Assert.Equal("approval-chain-001", ncr.DispositionApprovalChainId);
        Assert.Contains("file-plan-001", ncr.AttachmentFileIds);
        Assert.IsType<NonconformanceReportDispositionDecidedDomainEvent>(ncr.GetDomainEvents().Single());
    }

    [Fact]
    public void Close_rework_requires_rework_work_order_id()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("rework", "approval-chain-001", []);

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.Close("RW-0001", null, null);

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("RW-0001", ncr.ReworkWorkOrderId);
    }

    [Fact]
    public void Close_scrap_requires_scrap_movement_id()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("scrap", "approval-chain-001", []);

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.Close(null, "SM-0001", null);

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("SM-0001", ncr.ScrapMovementId);
    }

    [Fact]
    public void Close_return_to_supplier_requires_return_document_id()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("return-to-supplier", "approval-chain-001", []);

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.Close(null, null, "RTV-0001");

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("RTV-0001", ncr.ReturnDocumentId);
    }

    [Fact]
    public void Closed_ncr_cannot_change_disposition()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("conditional-release", "approval-chain-001", []);
        ncr.Close(null, null, null);

        Assert.Equal("closed", ncr.Status);
        Assert.Throws<InvalidOperationException>(() => ncr.SubmitDisposition("scrap", "approval-chain-002", []));
    }

    [Fact]
    public void Disposition_in_progress_ncr_cannot_replace_existing_disposition()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("conditional-release", "approval-chain-001", []);

        Assert.Throws<InvalidOperationException>(() => ncr.SubmitDisposition("scrap", "approval-chain-002", []));
        Assert.Equal("conditional-release", ncr.DispositionType);
        Assert.Equal("approval-chain-001", ncr.DispositionApprovalChainId);
    }

    [Fact]
    public void Domain_events_are_sealed_records()
    {
        Assert.True(typeof(NonconformanceReportOpenedDomainEvent).IsSealed);
        Assert.True(typeof(NonconformanceReportDispositionDecidedDomainEvent).IsSealed);
        Assert.True(typeof(NonconformanceReportClosedDomainEvent).IsSealed);
    }

    [Fact]
    public void Close_requires_disposition_and_raises_closed_event()
    {
        var ncr = NewNcr();

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.SubmitDisposition("sort-and-screen", "approval-chain-001", []);
        ncr.ClearDomainEvents();
        ncr.Close(null, null, null);

        Assert.Equal("closed", ncr.Status);
        Assert.IsType<NonconformanceReportClosedDomainEvent>(ncr.GetDomainEvents().Single());
    }

    private static NonconformanceReport NewNcr(
        decimal defectQuantity = 12.5m,
        string defectReason = "scratch",
        IReadOnlyCollection<string>? attachmentFileIds = null)
    {
        return NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-20260522-0001",
            "receiving",
            "PO-RECEIPT-001",
            "SKU-RM-1000",
            defectQuantity,
            defectReason,
            "BATCH-001",
            null,
            attachmentFileIds ?? []);
    }
}

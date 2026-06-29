using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
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

        ncr.SubmitDisposition(
            dispositionType,
            "approval-chain-001",
            ["file-plan-001"],
            [MrbReviewInput.Approve("qa-manager-001", "MRB accepted disposition", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))]);

        Assert.Equal("disposition-in-progress", ncr.Status);
        Assert.Equal(dispositionType, ncr.DispositionType);
        Assert.Equal("approval-chain-001", ncr.DispositionApprovalChainId);
        Assert.Contains("file-plan-001", ncr.AttachmentFileIds);
        Assert.Equal("qa-manager-001", Assert.Single(ncr.MrbReviews).ReviewerId);
        Assert.IsType<NonconformanceReportDispositionDecidedDomainEvent>(ncr.GetDomainEvents().Single());
    }

    [Fact]
    public void Open_from_inspection_copies_inventory_locator_for_disposition_routing()
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
            "SER-001",
            [InspectionResultLineInput.Fail("coa", "mismatch", "wrong-spec", 4m, [])],
            "Supplier certificate mismatch",
            [],
            StockReleaseDimension.Create("kg", "SITE-01", "IQC-HOLD", "quality", "company", "owner-001"));

        var ncr = NonconformanceReport.OpenFromInspection("NCR-20260626-0001", inspection, "wrong-spec", []);

        Assert.Equal("kg", ncr.UomCode);
        Assert.Equal("SITE-01", ncr.SiteCode);
        Assert.Equal("IQC-HOLD", ncr.LocationCode);
        Assert.Equal("company", ncr.OwnerType);
        Assert.Equal("owner-001", ncr.OwnerId);
    }

    [Theory]
    [InlineData("rework")]
    [InlineData("scrap")]
    [InlineData("conditional-release")]
    public void Inventory_routed_dispositions_raise_inventory_disposition_requested_event_when_locator_exists(string dispositionType)
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

        ncr.SubmitDisposition(dispositionType, "approval-chain-001", [], ApprovedMrbReview());

        Assert.Contains(ncr.GetDomainEvents(), x => x is NonconformanceReportInventoryDispositionRequestedDomainEvent);
    }

    [Theory]
    [InlineData("rework")]
    [InlineData("scrap")]
    [InlineData("return-to-supplier")]
    [InlineData("conditional-release")]
    public void Disposition_types_with_material_decisions_require_mrb_review(string dispositionType)
    {
        var ncr = NewNcr();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ncr.SubmitDisposition(dispositionType, "approval-chain-001", []));

        Assert.Contains("MRB", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(NonconformanceReport.RequiresCentralApproval(dispositionType));
    }

    [Fact]
    public void Submit_disposition_requires_all_mrb_decisions_approved()
    {
        var ncr = NewNcr();

        var exception = Assert.Throws<InvalidOperationException>(() => ncr.SubmitDisposition(
            "scrap",
            "approval-chain-001",
            [],
            [
                MrbReviewInput.Approve("qa-manager-001", "MRB accepted disposition", DateTimeOffset.Parse("2026-06-16T08:00:00Z")),
                new MrbReviewInput("production-manager-001", "rejected", "Disposition quantity not balanced", DateTimeOffset.Parse("2026-06-16T09:00:00Z")),
            ]));

        Assert.Contains("approved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sort_and_screen_is_low_risk_and_does_not_require_central_approval()
    {
        var ncr = NewNcr();

        ncr.SubmitDisposition("sort-and-screen", null, []);

        Assert.False(NonconformanceReport.RequiresCentralApproval("sort-and-screen"));
        Assert.Equal("disposition-in-progress", ncr.Status);
        Assert.Empty(ncr.MrbReviews);
        Assert.Null(ncr.DispositionApprovalChainId);
    }

    [Fact]
    public void Close_rework_requires_rework_work_order_id()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("rework", "approval-chain-001", [], ApprovedMrbReview());

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.Close("RW-0001", null, null);

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("RW-0001", ncr.ReworkWorkOrderId);
    }

    [Fact]
    public void Close_scrap_requires_scrap_movement_id()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("scrap", "approval-chain-001", [], ApprovedMrbReview());

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.Close(null, "SM-0001", null);

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("SM-0001", ncr.ScrapMovementId);
    }

    [Fact]
    public void Close_return_to_supplier_requires_return_document_id()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("return-to-supplier", "approval-chain-001", [], ApprovedMrbReview());

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.Close(null, null, "RTV-0001");

        Assert.Equal("closed", ncr.Status);
        Assert.Equal("RTV-0001", ncr.ReturnDocumentId);
    }

    [Fact]
    public void Close_conditional_release_requires_waiver_evidence()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("conditional-release", "approval-chain-001", [], ApprovedMrbReview());

        var exception = Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        Assert.Contains("evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Close_sort_and_screen_requires_screening_evidence()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("sort-and-screen", null, []);

        var exception = Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        Assert.Contains("evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Closed_ncr_cannot_change_disposition()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("conditional-release", "approval-chain-001", ["file-waiver-001"], ApprovedMrbReview());
        ncr.Close(null, null, null);

        Assert.Equal("closed", ncr.Status);
        Assert.Throws<InvalidOperationException>(() => ncr.SubmitDisposition("scrap", "approval-chain-002", [], ApprovedMrbReview()));
    }

    [Fact]
    public void Disposition_in_progress_ncr_cannot_replace_existing_disposition()
    {
        var ncr = NewNcr();
        ncr.SubmitDisposition("conditional-release", "approval-chain-001", [], ApprovedMrbReview());

        Assert.Throws<InvalidOperationException>(() => ncr.SubmitDisposition("scrap", "approval-chain-002", [], ApprovedMrbReview()));
        Assert.Equal("conditional-release", ncr.DispositionType);
        Assert.Equal("approval-chain-001", ncr.DispositionApprovalChainId);
    }

    [Fact]
    public void Domain_events_are_sealed_records()
    {
        Assert.True(typeof(NonconformanceReportOpenedDomainEvent).IsSealed);
        Assert.True(typeof(NonconformanceReportDispositionDecidedDomainEvent).IsSealed);
        Assert.True(typeof(NonconformanceReportInventoryDispositionRequestedDomainEvent).IsSealed);
        Assert.True(typeof(NonconformanceReportClosedDomainEvent).IsSealed);
    }

    [Fact]
    public void Close_requires_disposition_and_raises_closed_event()
    {
        var ncr = NewNcr();

        Assert.Throws<InvalidOperationException>(() => ncr.Close(null, null, null));

        ncr.SubmitDisposition("sort-and-screen", "approval-chain-001", ["file-screening-result-001"]);
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

    private static IReadOnlyCollection<MrbReviewInput> ApprovedMrbReview()
    {
        return [MrbReviewInput.Approve("qa-manager-001", "MRB accepted disposition", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))];
    }
}

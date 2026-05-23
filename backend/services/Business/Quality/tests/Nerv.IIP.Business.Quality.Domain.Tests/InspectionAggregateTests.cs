using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class InspectionAggregateTests
{
    [Fact]
    public void Draft_inspection_plan_can_add_characteristics()
    {
        var plan = NewPlan();

        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");

        var characteristic = Assert.Single(plan.Characteristics);
        Assert.Equal("appearance", characteristic.CharacteristicCode);
        Assert.Equal("draft", plan.Status);
    }

    [Fact]
    public void Activated_inspection_plan_cannot_change_execution_characteristics()
    {
        var plan = NewPlan();
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();

        Assert.Equal("active", plan.Status);
        Assert.Throws<InvalidOperationException>(() =>
            plan.AddCharacteristic("length", "Length", "caliper", "major", true, "sample-5"));
    }

    [Fact]
    public void New_plan_version_supersedes_previous_plan()
    {
        var plan = NewPlan();
        plan.AddCharacteristic("appearance", "Appearance", "visual", "critical", true, "zero-defect");
        plan.Activate();

        var nextVersion = plan.Supersede("IQP-RECEIVING-002");

        Assert.Equal("superseded", plan.Status);
        Assert.Equal("draft", nextVersion.Status);
        Assert.Equal(2, nextVersion.Version);
        Assert.Equal(plan.Id, nextVersion.SupersedesPlanId);
        Assert.Equal("appearance", Assert.Single(nextVersion.Characteristics).CharacteristicCode);
    }

    [Fact]
    public void Inspection_record_passes_when_all_required_characteristics_pass()
    {
        var record = InspectionRecord.Create(
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
            [
                InspectionResultLineInput.Pass("appearance", "visual ok", null, []),
                InspectionResultLineInput.Pass("coa", "matched", null, ["file-coa-001"]),
            ],
            null,
            []);

        Assert.Equal("passed", record.Result);
        Assert.Empty(record.DispositionAttachmentFileIds);
        Assert.IsType<InspectionPassedDomainEvent>(record.GetDomainEvents().Single());
    }

    [Fact]
    public void Inspection_record_rejects_when_required_characteristic_fails()
    {
        var record = InspectionRecord.Create(
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
            [
                InspectionResultLineInput.Pass("appearance", "visual ok", null, []),
                InspectionResultLineInput.Fail("coa", "certificate mismatch", "wrong-spec", 10m, ["file-photo-001"]),
            ],
            "Supplier certificate mismatch",
            ["file-mrb-001"]);

        Assert.Equal("rejected", record.Result);
        Assert.Equal("Supplier certificate mismatch", record.DispositionReason);
        Assert.Equal(["file-mrb-001"], record.DispositionAttachmentFileIds);
        Assert.IsType<InspectionRejectedDomainEvent>(record.GetDomainEvents().Single());
    }

    [Fact]
    public void Conditional_release_preserves_disposition_reason_and_file_references()
    {
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "operation",
            "mes-operation",
            "OP-REPORT-001",
            "SKU-FG-1000",
            5m,
            "BATCH-002",
            null,
            [InspectionResultLineInput.ConditionalRelease("torque", "slightly below target", "waiver-approved", 1m, ["file-waiver-001"])],
            "Released by MRB waiver",
            ["file-waiver-001"]);

        Assert.Equal("conditional-release", record.Result);
        Assert.Equal("Released by MRB waiver", record.DispositionReason);
        Assert.Equal(["file-waiver-001"], record.DispositionAttachmentFileIds);
        Assert.Equal(1m, record.FailedQuantity());
        Assert.IsType<InspectionRejectedDomainEvent>(record.GetDomainEvents().Single());
    }

    [Fact]
    public void Conditional_release_line_requires_positive_defect_quantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "operation",
            "mes-operation",
            "OP-REPORT-001",
            "SKU-FG-1000",
            5m,
            "BATCH-002",
            null,
            [
                new InspectionResultLineInput(
                    "torque",
                    "slightly below target",
                    null,
                    InspectionLineResults.ConditionalRelease,
                    "waiver-approved",
                    null,
                    ["file-waiver-001"]),
            ],
            "Released by MRB waiver",
            ["file-waiver-001"]));
    }

    [Fact]
    public void Failed_inspection_can_open_ncr_linked_to_inspection_record()
    {
        var record = InspectionRecord.Create(
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
            [InspectionResultLineInput.Fail("appearance", "rust", "corrosion", 3m, ["file-photo-001"])],
            "Rust on incoming material",
            ["file-photo-001"]);

        var ncr = NonconformanceReport.OpenFromInspection(
            "NCR-INS-001",
            record,
            "Rust on incoming material",
            ["file-photo-001"]);

        Assert.Equal(record.Id, ncr.SourceInspectionRecordId);
        Assert.Equal(record.SourceType, ncr.SourceType);
        Assert.Equal("RCV-001", ncr.SourceDocumentId);
        Assert.Equal("SKU-RM-1000", ncr.SkuCode);
        Assert.Equal(3m, ncr.DefectQuantity);
    }

    [Fact]
    public void Operation_inspection_opens_in_process_ncr_source_type()
    {
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "operation",
            "mes-operation",
            "OP-REPORT-001",
            "SKU-FG-1000",
            5m,
            "BATCH-002",
            null,
            [InspectionResultLineInput.Fail("torque", "below target", "out-of-tolerance", 2m, [])],
            "Torque below target",
            []);

        var ncr = NonconformanceReport.OpenFromInspection(
            "NCR-INS-002",
            record,
            "Torque below target",
            []);

        Assert.Equal("in-process", ncr.SourceType);
        Assert.Equal("OP-REPORT-001", ncr.SourceDocumentId);
    }

    [Fact]
    public void Maintenance_inspection_cannot_open_ncr_without_explicit_source_mapping()
    {
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "maintenance",
            "maintenance",
            "MAINT-001",
            "ASSET-001",
            1m,
            null,
            "SER-001",
            [InspectionResultLineInput.Fail("alignment", "misaligned", "out-of-tolerance", 1m, [])],
            "Maintenance inspection failed",
            []);

        var exception = Assert.Throws<InvalidOperationException>(() => NonconformanceReport.OpenFromInspection(
            "NCR-INS-003",
            record,
            "Maintenance inspection failed",
            []));
        Assert.Contains("maintenance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static InspectionPlan NewPlan()
    {
        return InspectionPlan.Create(
            "org-001",
            "env-dev",
            "IQP-RECEIVING-001",
            "receiving",
            "SKU-RM-1000",
            "supplier-001",
            null,
            null,
            "purchase-receipt");
    }
}

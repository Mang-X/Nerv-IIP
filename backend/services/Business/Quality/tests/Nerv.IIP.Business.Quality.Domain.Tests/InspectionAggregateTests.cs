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
    public void Planned_variable_inspection_rejects_measurement_outside_specification_limits()
    {
        var plan = NewPlan();
        plan.AddCharacteristic(
            "length",
            "Tube length",
            "caliper",
            "critical",
            required: true,
            samplingRule: "aql-general-ii",
            characteristicType: InspectionCharacteristicTypes.Variable,
            nominalValue: 10m,
            lowerSpecLimit: 9.5m,
            upperSpecLimit: 10.5m,
            unitCode: "mm",
            samplingPlan: InspectionSamplingPlan.Create("general-ii", "1.0", sampleSize: 3, acceptanceNumber: 0, rejectionNumber: 1));
        plan.Activate();

        var record = InspectionRecord.CreateFromPlan(
            plan,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            inspectedQuantity: 3m,
            batchNo: "BATCH-001",
            serialNo: null,
            stockRelease: StockReleaseDimension.Create("ea", "SITE-01", "IQC-HOLD", "quality", "company", null),
            resultLines:
            [
                InspectionResultLineInput.Measure("length", measuredValue: 11m, unitCode: "mm", attachmentFileIds: []),
            ],
            dispositionReason: "Tube length above upper specification limit",
            dispositionAttachmentFileIds: []);

        var line = Assert.Single(record.ResultLines);
        Assert.Equal("rejected", record.Result);
        Assert.Equal("failed", line.Result);
        Assert.Equal(11m, line.MeasuredValue);
        Assert.Equal("Tube length above upper specification limit", record.DispositionReason);
    }

    [Fact]
    public void Planned_record_requires_all_required_characteristics()
    {
        var plan = NewPlan();
        plan.AddCharacteristic(
            "length",
            "Tube length",
            "caliper",
            "critical",
            required: true,
            samplingRule: "aql-general-ii",
            characteristicType: InspectionCharacteristicTypes.Variable,
            nominalValue: 10m,
            lowerSpecLimit: 9.5m,
            upperSpecLimit: 10.5m,
            unitCode: "mm",
            samplingPlan: InspectionSamplingPlan.Create("general-ii", "1.0", sampleSize: 3, acceptanceNumber: 0, rejectionNumber: 1));
        plan.Activate();

        var exception = Assert.Throws<InvalidOperationException>(() => InspectionRecord.CreateFromPlan(
            plan,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            inspectedQuantity: 3m,
            batchNo: "BATCH-001",
            serialNo: null,
            stockRelease: StockReleaseDimension.Create("ea", "SITE-01", "IQC-HOLD", "quality", "company", null),
            resultLines:
            [
                InspectionResultLineInput.Measure("appearance", measuredValue: 1m, unitCode: "mm", attachmentFileIds: []),
            ],
            dispositionReason: null,
            dispositionAttachmentFileIds: []));

        Assert.Contains("length", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Planned_attribute_inspection_uses_aql_acceptance_and_rejection_numbers()
    {
        var plan = NewPlan();
        plan.AddCharacteristic(
            "appearance",
            "Appearance",
            "visual",
            "major",
            required: true,
            samplingRule: "aql-general-ii",
            characteristicType: InspectionCharacteristicTypes.Attribute,
            nominalValue: null,
            lowerSpecLimit: null,
            upperSpecLimit: null,
            unitCode: null,
            samplingPlan: InspectionSamplingPlan.Create("general-ii", "1.0", sampleSize: 5, acceptanceNumber: 1, rejectionNumber: 3));
        plan.Activate();

        var accepted = InspectionRecord.CreateFromPlan(
            plan,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            inspectedQuantity: 5m,
            batchNo: "BATCH-001",
            serialNo: null,
            stockRelease: StockReleaseDimension.Create("ea", "SITE-01", "IQC-HOLD", "quality", "company", null),
            resultLines:
            [
                InspectionResultLineInput.Attribute("appearance", observedText: "one minor scratch", defectReason: "scratch", defectQuantity: 1m, attachmentFileIds: []),
            ],
            dispositionReason: null,
            dispositionAttachmentFileIds: []);
        var conditional = InspectionRecord.CreateFromPlan(
            plan,
            "receiving",
            "purchase-receipt",
            "RCV-002",
            "SKU-RM-1000",
            inspectedQuantity: 5m,
            batchNo: "BATCH-002",
            serialNo: null,
            stockRelease: StockReleaseDimension.Create("ea", "SITE-01", "IQC-HOLD", "quality", "company", null),
            resultLines:
            [
                InspectionResultLineInput.Attribute("appearance", observedText: "two defects", defectReason: "scratch", defectQuantity: 2m, attachmentFileIds: []),
            ],
            dispositionReason: "MRB conditional release review required",
            dispositionAttachmentFileIds: []);
        var rejected = InspectionRecord.CreateFromPlan(
            plan,
            "receiving",
            "purchase-receipt",
            "RCV-003",
            "SKU-RM-1000",
            inspectedQuantity: 5m,
            batchNo: "BATCH-003",
            serialNo: null,
            stockRelease: StockReleaseDimension.Create("ea", "SITE-01", "IQC-HOLD", "quality", "company", null),
            resultLines:
            [
                InspectionResultLineInput.Attribute("appearance", observedText: "three defects", defectReason: "scratch", defectQuantity: 3m, attachmentFileIds: []),
            ],
            dispositionReason: "AQL rejection number reached",
            dispositionAttachmentFileIds: []);

        Assert.Equal("passed", accepted.Result);
        Assert.Equal("conditional-release", conditional.Result);
        Assert.Equal("rejected", rejected.Result);
    }

    [Fact]
    public void Planned_attribute_without_sampling_fails_when_defect_is_reported()
    {
        var plan = NewPlan();
        plan.AddCharacteristic(
            "appearance",
            "Appearance",
            "visual",
            "major",
            required: true,
            samplingRule: "zero-defect",
            characteristicType: InspectionCharacteristicTypes.Attribute,
            nominalValue: null,
            lowerSpecLimit: null,
            upperSpecLimit: null,
            unitCode: null,
            samplingPlan: null);
        plan.Activate();

        var record = InspectionRecord.CreateFromPlan(
            plan,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            inspectedQuantity: 5m,
            batchNo: "BATCH-001",
            serialNo: null,
            stockRelease: StockReleaseDimension.Create("ea", "SITE-01", "IQC-HOLD", "quality", "company", null),
            resultLines:
            [
                InspectionResultLineInput.Attribute("appearance", observedText: "scratch observed", defectReason: "scratch", defectQuantity: 1m, attachmentFileIds: []),
            ],
            dispositionReason: "Attribute defect observed",
            dispositionAttachmentFileIds: []);

        var line = Assert.Single(record.ResultLines);
        Assert.Equal("rejected", record.Result);
        Assert.Equal("failed", line.Result);
        Assert.Equal("scratch", line.DefectReason);
        Assert.Equal(1m, line.DefectQuantity);
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
    public void Ad_hoc_inspection_record_preserves_stock_release_dimension()
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
            "SER-001",
            [InspectionResultLineInput.Pass("appearance", "visual ok", null, [])],
            null,
            [],
            stockRelease: StockReleaseDimension.Create("kg", "SITE-01", "IQC-HOLD", "quality", "supplier", "supplier-001"));

        Assert.Equal("kg", record.UomCode);
        Assert.Equal("SITE-01", record.SiteCode);
        Assert.Equal("IQC-HOLD", record.LocationCode);
        Assert.Equal("quality", record.SourceQualityStatus);
        Assert.Equal("supplier", record.OwnerType);
        Assert.Equal("supplier-001", record.OwnerId);
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
        Assert.IsType<InspectionConditionalReleasedDomainEvent>(record.GetDomainEvents().Single());
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

using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class InspectionRecordEntityTypeConfiguration : IEntityTypeConfiguration<InspectionRecord>
{
    public void Configure(EntityTypeBuilder<InspectionRecord> builder)
    {
        builder.ToTable("inspection_records", tableBuilder =>
        {
            tableBuilder.HasComment("Quality inspection execution records and final result facts.");
            tableBuilder.HasCheckConstraint(
                "ck_inspection_records_attempt_positive",
                "attempt_number > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inspection record aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the record.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the inspection was recorded.");
        builder.Property(x => x.InspectionPlanId).HasColumnName("inspection_plan_id").HasComment("Optional inspection plan version id used for this record.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").IsRequired().HasMaxLength(50).HasComment("Inspection source type; value domain is QualityInspectionSourceTypes.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(100).HasComment("Source service or document family that requested the inspection.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(250).HasComment("Source document or operation public id, or the composite first-article source identity '{workOrderId}:{operationTaskId}' produced by FirstArticleInspection.SourceDocumentId.");
        builder.Property(x => x.SourceDocumentLineId).HasColumnName("source_document_line_id").HasMaxLength(250).HasComment("Optional source document line, operation task id or stable periodic-operation window identity copied from the inspection task; null for directly recorded inspections without a source line.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("SKU code inspected as a Quality reference.");
        builder.Property(x => x.AttemptNumber).HasColumnName("attempt_number").IsRequired().HasDefaultValue(1).HasComment("One-based inspection attempt number within the same source and SKU history.");
        builder.Property(x => x.ReinspectionOfInspectionRecordId).HasColumnName("reinspection_of_inspection_record_id").HasComment("Previous inspection record id targeted by this reinspection attempt; null for the initial attempt.");
        builder.Property(x => x.InspectedQuantity).HasColumnName("inspected_quantity").IsRequired().HasPrecision(18, 6).HasComment("Quantity inspected.");
        builder.Property(x => x.BatchNo).HasColumnName("batch_no").HasMaxLength(100).HasComment("Optional batch number reference.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number reference.");
        builder.Property(x => x.MeasuringDeviceId).HasColumnName("measuring_device_id").HasComment("Optional measuring device used for this inspection.");
        builder.Property(x => x.MeasuringDeviceCode).HasColumnName("measuring_device_code").HasMaxLength(128).HasComment("Snapshot of the measuring device business code used at inspection entry.");
        builder.Property(x => x.MeasuringDeviceCalibrationState).HasColumnName("measuring_device_calibration_state").HasMaxLength(50).HasComment("Snapshot of the device calibration state at inspection entry.");
        builder.Property(x => x.MeasuringDeviceCalibrationDueAtUtc).HasColumnName("measuring_device_calibration_due_at_utc").HasComment("Snapshot of the device calibration due UTC time at inspection entry.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(50).HasComment("Optional stock release UOM code for Inventory quality-status transfer.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(100).HasComment("Optional stock release site code for Inventory quality-status transfer.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").HasMaxLength(100).HasComment("Optional stock release location code for Inventory quality-status transfer.");
        builder.Property(x => x.SourceQualityStatus).HasColumnName("source_quality_status").HasMaxLength(50).HasComment("Optional source Inventory quality status to transfer from after inspection.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").HasMaxLength(50).HasComment("Optional stock owner type for Inventory quality-status transfer.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional stock owner reference id for Inventory quality-status transfer.");
        builder.Property(x => x.Result).HasColumnName("result").IsRequired().HasMaxLength(50).HasComment("Inspection result: passed, rejected or conditional-release.");
        builder.Property(x => x.DispositionReason).HasColumnName("disposition_reason").HasMaxLength(500).HasComment("Disposition reason preserved for rejected or conditional-release inspections.");
        builder.PrimitiveCollection(x => x.DispositionAttachmentFileIds).HasColumnName("disposition_attachment_file_ids").HasComment("File Storage attachment ids supporting the disposition.");
        builder.Property(x => x.NonconformanceReportId).HasColumnName("nonconformance_report_id").HasMaxLength(150).HasComment("Optional NCR id opened from this failed inspection.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the inspection was recorded.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the inspection record was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceService, x.SourceDocumentId });
        // 来源行维度进唯一键（#3319）：同一工单两道工序做同一 SKU 检验，改前会撞同一个 attempt 1，
        // 第二条被 FindBySourceDocumentAsync 跨行复用成同一条结论。
        // AreNullsDistinct(false)：直录检验没有来源行，该列为 NULL；PG 默认「NULL 互不相等」会让
        // 这一组去重整组失效（新行永远挡不住），因此必须显式把 NULL 当同一个值比较。
        // 射程说准：改后只有直录录入命令还写 NULL，任务驱动的写面一律带来源行，
        // 所以这条 NULL 语义保的是直录路径与迁移前存量行，不保「跨迁移的同一条链」——
        // 那条链会断开，属于本票声明的行为变化（见迁移 AddInspectionRecordSourceDocumentLine 的取舍说明）。
        // 本仓既有姿势见 ApprovalDecision / DeviceStateSnapshot / TelemetryRawSample / TelemetrySummary。
        builder.HasIndex(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.SourceType,
                x.SourceService,
                x.SourceDocumentId,
                x.SourceDocumentLineId,
                x.SkuCode,
                x.AttemptNumber,
            })
            .IsUnique()
            .HasDatabaseName("ux_inspection_records_source_attempt")
            .AreNullsDistinct(false);
        builder.HasIndex(x => x.ReinspectionOfInspectionRecordId)
            .IsUnique()
            .HasFilter("\"reinspection_of_inspection_record_id\" IS NOT NULL")
            .HasDatabaseName("ux_inspection_records_reinspection_predecessor");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceType, x.Result });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Result });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.MeasuringDeviceId });
        builder.HasMany(x => x.ResultLines)
            .WithOne()
            .HasForeignKey(x => x.InspectionRecordId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InspectionRecord>()
            .WithMany()
            .HasForeignKey(x => x.ReinspectionOfInspectionRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InspectionResultLineEntityTypeConfiguration : IEntityTypeConfiguration<InspectionResultLine>
{
    public void Configure(EntityTypeBuilder<InspectionResultLine> builder)
    {
        builder.ToTable("inspection_result_lines", tableBuilder =>
            tableBuilder.HasComment("Quality inspection result line measurements and defect facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inspection result line id.");
        builder.Property(x => x.InspectionRecordId).HasColumnName("inspection_record_id").IsRequired().HasComment("Owning inspection record id.");
        builder.Property(x => x.CharacteristicCode).HasColumnName("characteristic_code").IsRequired().HasMaxLength(100).HasComment("Measured or checked characteristic code.");
        builder.Property(x => x.ObservedValue).HasColumnName("observed_value").IsRequired().HasMaxLength(500).HasComment("Observed measurement value or check result.");
        builder.Property(x => x.MeasuredValue).HasColumnName("measured_value").HasPrecision(18, 6).HasComment("Numeric measured value for variable characteristics.");
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasMaxLength(50).HasComment("Optional unit of measure code for measured values.");
        builder.Property(x => x.Result).HasColumnName("result").IsRequired().HasMaxLength(50).HasComment("Line result: passed, failed or conditional-release.");
        builder.Property(x => x.DefectReason).HasColumnName("defect_reason").HasMaxLength(500).HasComment("Defect or waiver reason for failed or conditional-release lines.");
        builder.Property(x => x.DefectQuantity).HasColumnName("defect_quantity").HasPrecision(18, 6).HasComment("Quantity represented by this defect line.");
        builder.PrimitiveCollection(x => x.AttachmentFileIds).HasColumnName("attachment_file_ids").HasComment("File Storage attachment ids for this result line.");
        builder.HasIndex(x => new { x.InspectionRecordId, x.CharacteristicCode });
    }
}

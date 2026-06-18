using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class NonconformanceReportEntityTypeConfiguration : IEntityTypeConfiguration<NonconformanceReport>
{
    public void Configure(EntityTypeBuilder<NonconformanceReport> builder)
    {
        builder.ToTable("nonconformance_reports", tableBuilder =>
            tableBuilder.HasComment("Quality nonconformance reports and disposition closure facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("NCR aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the NCR.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the NCR was opened.");
        builder.Property(x => x.NcrCode).HasColumnName("ncr_code").IsRequired().HasMaxLength(100).HasComment("Human-readable automatically generated NCR code.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").IsRequired().HasMaxLength(50).HasComment("NCR source type: receiving, in-process, final or customer-return.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("External source document id such as inspection plan, report or return id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("SKU code copied as a Quality reference.");
        builder.Property(x => x.DefectQuantity).HasColumnName("defect_quantity").IsRequired().HasPrecision(18, 6).HasComment("Quantity found defective.");
        builder.Property(x => x.DefectReason).HasColumnName("defect_reason").IsRequired().HasMaxLength(200).HasComment("Defect reason code or normalized reason.");
        builder.Property(x => x.BatchNo).HasColumnName("batch_no").HasMaxLength(100).HasComment("Optional batch number reference.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number reference.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("NCR lifecycle status.");
        builder.Property(x => x.DispositionType).HasColumnName("disposition_type").HasMaxLength(50).HasComment("Chosen disposition type.");
        builder.Property(x => x.DispositionApprovalChainId).HasColumnName("disposition_approval_chain_id").HasMaxLength(150).HasComment("Approval chain id for disposition approval.");
        builder.Property(x => x.ReworkWorkOrderId).HasColumnName("rework_work_order_id").HasMaxLength(150).HasComment("MES rework work order id produced by downstream service.");
        builder.Property(x => x.ScrapMovementId).HasColumnName("scrap_movement_id").HasMaxLength(150).HasComment("Inventory scrap movement id produced by downstream service.");
        builder.Property(x => x.ReturnDocumentId).HasColumnName("return_document_id").HasMaxLength(150).HasComment("ERP supplier return document id produced by downstream service.");
        builder.Property(x => x.SourceInspectionRecordId).HasColumnName("source_inspection_record_id").HasComment("Optional Quality inspection record id that opened this NCR.");
        builder.PrimitiveCollection(x => x.AttachmentFileIds).HasColumnName("attachment_file_ids").HasComment("File Storage attachment ids.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the NCR was opened.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the NCR was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.NcrCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceType, x.SourceDocumentId });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceInspectionRecordId });
        builder.HasMany(x => x.MrbReviews)
            .WithOne()
            .HasForeignKey(x => x.NonconformanceReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MrbReviewEntityTypeConfiguration : IEntityTypeConfiguration<MrbReview>
{
    public void Configure(EntityTypeBuilder<MrbReview> builder)
    {
        builder.ToTable("ncr_mrb_reviews", tableBuilder =>
            tableBuilder.HasComment("Quality NCR material review board decisions captured before disposition execution."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("MRB review entry id.");
        builder.Property(x => x.NonconformanceReportId).HasColumnName("nonconformance_report_id").IsRequired().HasComment("Owning NCR id.");
        builder.Property(x => x.ReviewerId).HasColumnName("reviewer_id").IsRequired().HasMaxLength(150).HasComment("Reviewer user or committee member public id.");
        builder.Property(x => x.Decision).HasColumnName("decision").IsRequired().HasMaxLength(50).HasComment("MRB reviewer decision such as approved or rejected.");
        builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(500).HasComment("Optional MRB reviewer comment.");
        builder.Property(x => x.ReviewedAtUtc).HasColumnName("reviewed_at_utc").IsRequired().HasComment("UTC time when this MRB review was recorded.");
        builder.HasIndex(x => new { x.NonconformanceReportId, x.ReviewerId });
    }
}

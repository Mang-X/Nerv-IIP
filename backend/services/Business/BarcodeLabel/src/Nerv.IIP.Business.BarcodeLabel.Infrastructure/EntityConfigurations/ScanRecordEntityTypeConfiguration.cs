using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class ScanRecordEntityTypeConfiguration : IEntityTypeConfiguration<ScanRecord>
{
    public void Configure(EntityTypeBuilder<ScanRecord> builder)
    {
        builder.ToTable("scan_records", tableBuilder =>
            tableBuilder.HasComment("Append-only barcode scan facts captured from devices and workflows."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Scan record aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the scan fact.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the scan occurred.");
        builder.Property(x => x.DeviceCode).HasColumnName("device_code").IsRequired().HasMaxLength(100).HasComment("Device or PDA code that captured the scan.");
        builder.Property(x => x.ScannedValue).HasColumnName("scanned_value").IsRequired().HasMaxLength(200).HasComment("Raw barcode value scanned by the device.");
        builder.Property(x => x.SourceWorkflow).HasColumnName("source_workflow").IsRequired().HasMaxLength(100).HasComment("Workflow that produced the scan fact, such as receiving or picking.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Source business document public id associated with the scan.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Client supplied idempotency key for scan creation.");
        builder.Property(x => x.Result).HasColumnName("result").IsRequired().HasMaxLength(30).HasComment("Scan result: accepted or rejected.");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500).HasComment("Reason for rejected scans.");
        builder.Property(x => x.Gtin).HasColumnName("gtin").HasMaxLength(14).HasComment("GS1 GTIN parsed from an accepted scan value.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("GS1 lot or batch parsed from an accepted scan value.");
        builder.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(150).HasComment("GS1 serial number parsed from an accepted scan value.");
        builder.Property(x => x.EpcUri).HasColumnName("epc_uri").HasMaxLength(300).HasComment("EPC URI derived from parsed GTIN and serial number.");
        builder.Property(x => x.Sscc).HasColumnName("sscc").HasMaxLength(18).HasComment("GS1 SSCC-18 logistic unit identifier parsed from AI 00 when present.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Quantity parsed from GS1 AI 30 or supplied by the scan workflow.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(100).HasComment("SKU code supplied by scan context for downstream business action routing.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(50).HasComment("Unit of measure supplied by scan context for downstream business action routing.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(100).HasComment("Site code supplied by scan context for downstream business action routing.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").HasMaxLength(100).HasComment("Inventory or workflow location supplied by scan context.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").HasMaxLength(100).HasComment("Quality status supplied by scan context for inventory movement routing.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").HasMaxLength(100).HasComment("Inventory owner type supplied by scan context.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(150).HasComment("Optional inventory owner id supplied by scan context.");
        builder.Property(x => x.BusinessAction).HasColumnName("business_action").HasMaxLength(100).HasComment("Downstream business action selected for the accepted scan.");
        builder.Property(x => x.DownstreamEventId).HasColumnName("downstream_event_id").HasMaxLength(150).HasComment("Deterministic downstream event id for idempotent business action routing.");
        builder.Property(x => x.ScannedAtUtc).HasColumnName("scanned_at_utc").IsRequired().HasComment("UTC time when the scan was recorded.");
        builder.HasMany(x => x.EpcisEvents)
            .WithOne()
            .HasForeignKey(x => x.ScanRecordId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceCode, x.ScannedAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ScannedValue }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.EpcUri })
            .IsUnique()
            .HasFilter("epc_uri IS NOT NULL");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceWorkflow, x.SourceDocumentId });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Gtin, x.LotNo, x.SerialNumber })
            .IsUnique()
            .HasFilter("gtin IS NOT NULL AND serial_number IS NOT NULL");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Sscc });
    }
}

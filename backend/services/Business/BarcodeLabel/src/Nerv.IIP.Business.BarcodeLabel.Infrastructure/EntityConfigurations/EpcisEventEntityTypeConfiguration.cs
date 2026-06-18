using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class EpcisEventEntityTypeConfiguration : IEntityTypeConfiguration<EpcisEvent>
{
    public void Configure(EntityTypeBuilder<EpcisEvent> builder)
    {
        builder.ToTable("epcis_events", tableBuilder =>
            tableBuilder.HasComment("EPCIS traceability events generated from serialized label commissioning and accepted scans."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("EPCIS event id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the EPCIS event.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the EPCIS event occurred.");
        builder.Property(x => x.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(50).HasComment("EPCIS event type such as commissioning or objectEvent.");
        builder.Property(x => x.Action).HasColumnName("action").IsRequired().HasMaxLength(30).HasComment("EPCIS action value such as ADD or OBSERVE.");
        builder.Property(x => x.BusinessStep).HasColumnName("business_step").IsRequired().HasMaxLength(100).HasComment("Business step represented by the EPCIS event.");
        builder.Property(x => x.Disposition).HasColumnName("disposition").IsRequired().HasMaxLength(100).HasComment("Disposition associated with the EPCIS event.");
        builder.Property(x => x.LabelValue).HasColumnName("label_value").IsRequired().HasMaxLength(200).HasComment("Raw barcode value or generated label value associated with the event.");
        builder.Property(x => x.Gtin).HasColumnName("gtin").HasMaxLength(14).HasComment("GS1 GTIN associated with the event.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Lot or batch associated with the event.");
        builder.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(150).HasComment("Serialized unit associated with the event.");
        builder.Property(x => x.EpcUri).HasColumnName("epc_uri").HasMaxLength(300).HasComment("EPC URI associated with the serialized event.");
        builder.Property(x => x.SourceWorkflow).HasColumnName("source_workflow").IsRequired().HasMaxLength(100).HasComment("Source workflow that created the event.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Source business document public id associated with the event.");
        builder.Property(x => x.LabelPrintBatchId).HasColumnName("label_print_batch_id").HasComment("Optional label print batch id that owns commissioning events.");
        builder.Property(x => x.LabelPrintItemId).HasColumnName("label_print_item_id").HasComment("Optional label print item id that caused a commissioning event.");
        builder.Property(x => x.ScanRecordId).HasColumnName("scan_record_id").HasComment("Optional scan record id that caused an object event.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired().HasComment("UTC time when the EPCIS event occurred.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Gtin, x.LotNo, x.SerialNumber });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceWorkflow, x.SourceDocumentId });
        builder.HasIndex(x => x.LabelPrintBatchId);
        builder.HasIndex(x => x.LabelPrintItemId);
        builder.HasIndex(x => x.ScanRecordId);
    }
}

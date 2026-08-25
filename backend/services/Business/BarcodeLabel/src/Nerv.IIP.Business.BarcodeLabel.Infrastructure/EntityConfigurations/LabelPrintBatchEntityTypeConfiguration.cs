using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class LabelPrintBatchEntityTypeConfiguration : IEntityTypeConfiguration<LabelPrintBatch>
{
    private const string ReplaySnapshotCompletenessConstraint = "ck_label_print_batches_replay_snapshot_complete";

    public void Configure(EntityTypeBuilder<LabelPrintBatch> builder)
    {
        builder.ToTable("label_print_batches", tableBuilder =>
        {
            tableBuilder.HasComment("Label print batch facts and idempotency records.");
            tableBuilder.HasCheckConstraint(
                ReplaySnapshotCompletenessConstraint,
                "(template_file_id_snapshot IS NULL AND template_asset_sha256 IS NULL AND variable_schema_json_snapshot IS NULL AND barcode_type_snapshot IS NULL AND renderer_contract_version IS NULL) OR " +
                "(template_file_id_snapshot IS NOT NULL AND template_asset_sha256 IS NOT NULL AND variable_schema_json_snapshot IS NOT NULL AND barcode_type_snapshot IS NOT NULL AND renderer_contract_version IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Label print batch aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the print batch.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the print batch was created.");
        builder.Property(x => x.BarcodeRuleId).HasColumnName("barcode_rule_id").IsRequired().HasComment("Barcode rule id used for deterministic label generation.");
        builder.Property(x => x.LabelTemplateId).HasColumnName("label_template_id").IsRequired().HasComment("Label template id used for the print batch.");
        builder.Property(x => x.TemplateFileIdSnapshot).HasColumnName("template_file_id_snapshot").HasMaxLength(150).HasComment("Nullable FileStorage template file id snapshot; null only for legacy rows created before replay snapshots.");
        builder.Property(x => x.TemplateAssetSha256).HasColumnName("template_asset_sha256").HasMaxLength(71).HasComment("Nullable canonical sha256-prefixed template asset snapshot; null only for legacy rows and never synthesized.");
        builder.Property(x => x.VariableSchemaJsonSnapshot).HasColumnName("variable_schema_json_snapshot").HasColumnType("text").HasComment("Nullable variable schema JSON snapshot produced by BarcodeLabel for deterministic replay; null legacy rows are not replayable.");
        builder.Property(x => x.BarcodeTypeSnapshot).HasColumnName("barcode_type_snapshot").HasMaxLength(30).HasComment("Nullable barcode type snapshot used by the renderer; null only for legacy rows.");
        builder.Property(x => x.RendererContractVersion).HasColumnName("renderer_contract_version").HasMaxLength(30).HasComment("Nullable deterministic renderer contract version; null only for legacy rows.");
        builder.Property(x => x.SourceDocumentType).HasColumnName("source_document_type").IsRequired().HasMaxLength(100).HasComment("Source workflow or document type requesting label printing.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Source business document public id.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Client supplied idempotency key for print batch creation.");
        builder.Property(x => x.LabelValuesJson).HasColumnName("label_values_json").IsRequired().HasColumnType("text").HasComment("Label variable values JSON captured for repeatable printing.");
        builder.Property(x => x.RequestedQuantity).HasColumnName("requested_quantity").IsRequired().HasComment("Requested number of labels generated for the batch.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Whole-batch dispatch lifecycle status: pending, sent-to-printer, delivery-unknown, printed or failed; single-item reprint does not change it.");
        builder.Property(x => x.PrinterId).HasColumnName("printer_id").HasMaxLength(100).HasComment("Configured printer identity for the latest dispatch or reprint transport attempt.");
        builder.Property(x => x.PrintJobId).HasColumnName("print_job_id").HasMaxLength(150).HasComment("Job identifier for the latest dispatch or reprint transport attempt; it may replace the original whole-batch job.");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500).HasComment("Failure reason for the latest dispatch or reprint transport attempt; it is not by itself the whole-batch lifecycle conclusion.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the print batch was created.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC time when the print batch finished generation.");
        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.LabelPrintBatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.EpcisEvents)
            .WithOne()
            .HasForeignKey(x => x.LabelPrintBatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceDocumentType, x.SourceDocumentId });
    }
}

public sealed class LabelPrintItemEntityTypeConfiguration : IEntityTypeConfiguration<LabelPrintItem>
{
    public void Configure(EntityTypeBuilder<LabelPrintItem> builder)
    {
        builder.ToTable("label_print_items", tableBuilder =>
            tableBuilder.HasComment("Generated label print item facts for a print batch."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Label print item id.");
        builder.Property(x => x.LabelPrintBatchId).HasColumnName("label_print_batch_id").IsRequired().HasComment("Owning label print batch id.");
        builder.Property(x => x.SequenceNo).HasColumnName("sequence_no").IsRequired().HasComment("Generated label sequence number within the print batch.");
        builder.Property(x => x.LabelValue).HasColumnName("label_value").IsRequired().HasMaxLength(200).HasComment("Generated deterministic barcode or label value.");
        builder.Property(x => x.FileId).HasColumnName("file_id").HasMaxLength(150).HasComment("Optional FileStorage file id for rendered label output.");
        builder.Property(x => x.Gtin).HasColumnName("gtin").HasMaxLength(14).HasComment("Parsed or generated GS1 GTIN including check digit for serialized labels.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Batch or lot number encoded in the generated GS1 label.");
        builder.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(150).HasComment("Serialized unit identifier encoded in the generated GS1 label.");
        builder.Property(x => x.EpcUri).HasColumnName("epc_uri").HasMaxLength(300).HasComment("EPC URI derived from GTIN and serial number for EPCIS traceability.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Label lifecycle status: created, printed, reprinted, voided or consumed.");
        builder.Property(x => x.VoidReason).HasColumnName("void_reason").HasMaxLength(500).HasComment("Reason captured when the label is voided.");
        builder.Property(x => x.VoidedAtUtc).HasColumnName("voided_at_utc").HasComment("UTC time when the label became unusable.");
        builder.Property(x => x.ConsumedAtUtc).HasColumnName("consumed_at_utc").HasComment("UTC time when a printed label was accepted by scanner consumption.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the print item was generated.");
        builder.HasIndex(x => new { x.LabelPrintBatchId, x.SequenceNo }).IsUnique();
        builder.HasIndex(x => x.LabelValue);
        builder.HasIndex(x => new { x.Gtin, x.LotNo, x.SerialNumber });
    }
}

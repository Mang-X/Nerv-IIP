using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class EngineeringDocumentEntityTypeConfiguration : IEntityTypeConfiguration<EngineeringDocument>
{
    public void Configure(EntityTypeBuilder<EngineeringDocument> builder)
    {
        builder.ToTable("engineering_documents", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering engineering document and SOP references to File Storage files such as CAD drawings, process sheets, and work instructions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Engineering document aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the document is valid.");
        builder.Property(x => x.DocumentNumber).HasColumnName("document_number").IsRequired().HasMaxLength(100).HasComment("Business document number.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsRequired().HasMaxLength(50).HasComment("Document revision.");
        builder.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(100).HasComment("Optional engineering item code this document revision describes.");
        builder.Property(x => x.FileId).HasColumnName("file_id").IsRequired().HasMaxLength(150).HasComment("File Storage public file id; object keys are not stored.");
        builder.Property(x => x.FileName).HasColumnName("file_name").IsRequired().HasMaxLength(255).HasComment("Original or display file name.");
        builder.Property(x => x.ContentType).HasColumnName("content_type").IsRequired().HasMaxLength(120).HasComment("File content type.");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").IsRequired().HasMaxLength(100).HasComment("Engineering document type such as CAD drawing or process sheet.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(30).HasComment("Engineering document lifecycle status: Published or Archived for SOP dispatch.");
        builder.Property(x => x.OperationCode).HasColumnName("operation_code").HasMaxLength(100).HasComment("Optional StandardOperation code when this document is a SOP/work instruction.");
        builder.Property(x => x.WorkCenterCode).HasColumnName("work_center_code").HasMaxLength(100).HasComment("Optional work center code narrowing SOP dispatch for an operation.");
        builder.Property(x => x.RoutingCode).HasColumnName("routing_code").HasMaxLength(100).HasComment("Optional routing code narrowing SOP dispatch to a routing operation.");
        builder.Property(x => x.RoutingRevision).HasColumnName("routing_revision").HasMaxLength(50).HasComment("Optional routing revision narrowing SOP dispatch to a routing operation.");
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").HasComment("Factory business date from which the SOP/work instruction version is effective.");
        builder.Property(x => x.RegisteredAtUtc).HasColumnName("registered_at_utc").IsRequired().HasComment("UTC time when the document reference was registered.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DocumentNumber, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.FileId, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ItemCode, x.DocumentType });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DocumentType, x.OperationCode, x.WorkCenterCode, x.Status, x.EffectiveDate });
    }
}

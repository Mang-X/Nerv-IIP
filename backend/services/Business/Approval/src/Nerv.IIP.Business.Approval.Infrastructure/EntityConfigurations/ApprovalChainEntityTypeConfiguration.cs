using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

namespace Nerv.IIP.Business.Approval.Infrastructure.EntityConfigurations;

public sealed class ApprovalChainEntityTypeConfiguration : IEntityTypeConfiguration<ApprovalChain>
{
    public void Configure(EntityTypeBuilder<ApprovalChain> builder)
    {
        builder.ToTable("approval_chains", table => table.HasComment("Business approval chain instances for source document references."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Approval chain aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the chain.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the chain runs.");
        builder.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired().HasComment("Template id used to create the chain.");
        builder.Property(x => x.TemplateCode).HasColumnName("template_code").IsRequired().HasMaxLength(100).HasComment("Template code copied for historical lookup.");
        builder.Property(x => x.TemplateVersion).HasColumnName("template_version").IsRequired().HasComment("Template version copied for historical lookup.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Approval chain status: pending, approved, rejected or returned.");
        builder.Property(x => x.StartedBy).HasColumnName("started_by").IsRequired().HasMaxLength(150).HasComment("Public actor reference that started the chain.");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired().HasComment("UTC time when the chain started.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC time when the chain reached a terminal result.");
        builder.Property(x => x.RoundNo).HasColumnName("round_no").IsRequired().HasComment("Current submission round number; increments when a returned or withdrawn chain is resubmitted.");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRequired().IsConcurrencyToken().HasComment("Optimistic concurrency token for approval chain decisions and runtime step changes.");
        builder.Property(x => x.PendingIdentityKey).HasColumnName("pending_identity_key").HasMaxLength(64).HasComment("Stable unique identity held only while the source document approval chain is pending.");
        builder.OwnsOne(x => x.DocumentReference, document =>
        {
            document.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(100).HasComment("Source business service that owns the document.");
            document.Property(x => x.DocumentType).HasColumnName("document_type").IsRequired().HasMaxLength(100).HasComment("Source document type.");
            document.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired().HasMaxLength(150).HasComment("Source document id supplied by the owning service.");
            document.Property(x => x.DocumentLineId).HasColumnName("document_line_id").HasMaxLength(150).HasComment("Optional source document line id supplied by the owning service.");
            document.Property(x => x.Amount).HasColumnName("routing_amount").HasPrecision(18, 6).HasComment("Optional source amount used for structured approval routing and audit.");
            document.Property(x => x.OrganizationId).HasColumnName("routing_organization_id").HasMaxLength(100).HasComment("Optional organization dimension used for structured approval routing and audit.");
            document.Property(x => x.DepartmentId).HasColumnName("routing_department_id").HasMaxLength(100).HasComment("Optional department dimension used for structured approval routing and audit.");
        });
        builder.HasIndex(x => x.PendingIdentityKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TemplateCode, x.Status });
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.ChainId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Decisions).WithOne().HasForeignKey(x => x.ChainId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Decisions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;

namespace Nerv.IIP.Business.Approval.Infrastructure.EntityConfigurations;

public sealed class ApprovalTemplateEntityTypeConfiguration : IEntityTypeConfiguration<ApprovalTemplate>
{
    public void Configure(EntityTypeBuilder<ApprovalTemplate> builder)
    {
        builder.ToTable("approval_templates", table => table.HasComment("Business approval template facts by document type and environment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Approval template aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the template.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the template applies.");
        builder.Property(x => x.TemplateCode).HasColumnName("template_code").IsRequired().HasMaxLength(100).HasComment("Producer-stable template code.");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").IsRequired().HasMaxLength(100).HasComment("Business document type that can use this template.");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired().HasComment("Template version number controlled by BusinessApproval.");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasComment("Whether chains may be started from this template.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the template was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the template definition was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TemplateCode }).IsUnique();
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class LabelTemplateEntityTypeConfiguration : IEntityTypeConfiguration<LabelTemplate>
{
    public void Configure(EntityTypeBuilder<LabelTemplate> builder)
    {
        builder.ToTable("label_templates", tableBuilder =>
            tableBuilder.HasComment("Label template metadata with FileStorage file id references only."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Label template aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the template.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the template applies.");
        builder.Property(x => x.TemplateCode).HasColumnName("template_code").IsRequired().HasMaxLength(100).HasComment("Business label template code unique in an organization and environment.");
        builder.Property(x => x.TemplateName).HasColumnName("template_name").IsRequired().HasMaxLength(200).HasComment("Human readable label template name.");
        builder.Property(x => x.TemplateFileId).HasColumnName("template_file_id").IsRequired().HasMaxLength(150).HasComment("FileStorage file id for the template asset; object keys are not stored publicly.");
        builder.Property(x => x.VariableSchemaJson).HasColumnName("variable_schema_json").IsRequired().HasColumnType("text").HasComment("Template variable schema JSON consumed by print clients.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Template lifecycle status: active or inactive.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the template was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the template was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TemplateCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status });
    }
}

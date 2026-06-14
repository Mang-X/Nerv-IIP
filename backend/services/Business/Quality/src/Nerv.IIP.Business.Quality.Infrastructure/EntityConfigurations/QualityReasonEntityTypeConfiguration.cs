using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class QualityReasonEntityTypeConfiguration : IEntityTypeConfiguration<QualityReason>
{
    public void Configure(EntityTypeBuilder<QualityReason> builder)
    {
        builder.ToTable("quality_reasons", tableBuilder =>
            tableBuilder.HasComment("Quality reason code catalog with grouping, severity and default disposition."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Quality reason aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the quality reason.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the quality reason is valid.");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").IsRequired().HasMaxLength(100).HasComment("Business unique quality reason code.");
        builder.Property(x => x.ReasonName).HasColumnName("reason_name").IsRequired().HasMaxLength(200).HasComment("Quality reason display name.");
        builder.Property(x => x.GroupName).HasColumnName("group_name").IsRequired().HasMaxLength(100).HasComment("Reason group name for catalog organization.");
        builder.Property(x => x.Severity).HasColumnName("severity").IsRequired().HasMaxLength(50).HasComment("Severity classification: minor, major or critical.");
        builder.Property(x => x.DefaultDisposition).HasColumnName("default_disposition").HasMaxLength(100).HasComment("Optional default NCR disposition aligned with supported NCR disposition types.");
        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired().HasComment("Enabled flag that makes the reason selectable.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the quality reason was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the quality reason was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReasonCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.GroupName, x.Enabled });
        builder.HasIndex(x => x.Enabled);
    }
}

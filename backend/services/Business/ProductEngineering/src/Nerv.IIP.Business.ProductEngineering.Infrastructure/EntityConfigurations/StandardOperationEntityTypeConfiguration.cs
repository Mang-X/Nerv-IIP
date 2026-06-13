using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class StandardOperationEntityTypeConfiguration : IEntityTypeConfiguration<StandardOperation>
{
    public void Configure(EntityTypeBuilder<StandardOperation> builder)
    {
        builder.ToTable("standard_operations", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering standard operation master data with default work center, control flags and standard times."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Standard operation aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the standard operation is valid.");
        builder.Property(x => x.OperationCode).HasColumnName("operation_code").IsRequired().HasMaxLength(100).HasComment("Standard operation business code.");
        builder.Property(x => x.OperationName).HasColumnName("operation_name").IsRequired().HasMaxLength(200).HasComment("Standard operation display name.");
        builder.Property(x => x.DefaultWorkCenterCode).HasColumnName("default_work_center_code").IsRequired().HasMaxLength(100).HasComment("Default MasterData work center code used to prefill routing operation rows.");
        builder.Property(x => x.StandardSetupMinutes).HasColumnName("standard_setup_minutes").IsRequired().HasComment("Default setup duration in minutes before regular operation run time.");
        builder.Property(x => x.StandardRunMinutes).HasColumnName("standard_run_minutes").IsRequired().HasComment("Default run duration in minutes for this standard operation.");
        builder.Ignore(x => x.StandardMinutes);
        builder.Property(x => x.ControlKey).HasColumnName("control_key").IsRequired().HasMaxLength(100).HasComment("Control key or control profile for reporting, quality or outsourcing behavior.");
        builder.Property(x => x.RequiresReporting).HasColumnName("requires_reporting").IsRequired().HasComment("Whether MES reporting is expected for this operation by default.");
        builder.Property(x => x.RequiresQualityInspection).HasColumnName("requires_quality_inspection").IsRequired().HasComment("Whether quality inspection is expected for this operation by default.");
        builder.Property(x => x.IsOutsourced).HasColumnName("is_outsourced").IsRequired().HasComment("Whether this operation is normally outsourced.");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).HasComment("Optional standard operation description.");
        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired().HasComment("Whether this standard operation can be selected for new routing authoring.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the standard operation was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the standard operation was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Enabled });
    }
}

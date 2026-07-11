using Nerv.IIP.Business.Quality.Domain.AggregatesModel.SpcControlChartAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class SpcControlChartEntityTypeConfiguration : IEntityTypeConfiguration<SpcControlChart>
{
    public void Configure(EntityTypeBuilder<SpcControlChart> builder)
    {
        builder.ToTable("spc_control_charts", tableBuilder =>
            tableBuilder.HasComment("Quality SPC control chart limit locks by SKU, characteristic and work center."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("SPC control chart aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the SPC chart.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the SPC chart applies.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("SKU code for the measured SPC sequence.");
        builder.Property(x => x.CharacteristicCode).HasColumnName("characteristic_code").IsRequired().HasMaxLength(100).HasComment("Variable inspection characteristic code used for SPC.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("Work center scope for the SPC sequence.");
        builder.Property(x => x.SubgroupSize).HasColumnName("subgroup_size").IsRequired().HasComment("Xbar-R subgroup size used to calculate locked limits.");
        builder.Property(x => x.CenterLine).HasColumnName("center_line").HasPrecision(18, 6).HasComment("Locked Xbar center line.");
        builder.Property(x => x.AverageRange).HasColumnName("average_range").HasPrecision(18, 6).HasComment("Locked average subgroup range.");
        builder.Property(x => x.XbarUpperControlLimit).HasColumnName("xbar_upper_control_limit").HasPrecision(18, 6).HasComment("Locked Xbar upper control limit.");
        builder.Property(x => x.XbarLowerControlLimit).HasColumnName("xbar_lower_control_limit").HasPrecision(18, 6).HasComment("Locked Xbar lower control limit.");
        builder.Property(x => x.RangeUpperControlLimit).HasColumnName("range_upper_control_limit").HasPrecision(18, 6).HasComment("Locked R chart upper control limit.");
        builder.Property(x => x.RangeLowerControlLimit).HasColumnName("range_lower_control_limit").HasPrecision(18, 6).HasComment("Locked R chart lower control limit.");
        builder.Property(x => x.Locked).HasColumnName("locked").IsRequired().HasComment("Whether the current control limits are locked for operational judgment.");
        builder.Property(x => x.LimitsCalculatedAtUtc).HasColumnName("limits_calculated_at_utc").HasComment("UTC time when the locked control limits were calculated.");
        builder.Property(x => x.LockedAtUtc).HasColumnName("locked_at_utc").HasComment("UTC time when the limits were locked.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the SPC chart lock record was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the SPC chart lock record was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.CharacteristicCode, x.WorkCenterId, x.SubgroupSize }).IsUnique();
    }
}

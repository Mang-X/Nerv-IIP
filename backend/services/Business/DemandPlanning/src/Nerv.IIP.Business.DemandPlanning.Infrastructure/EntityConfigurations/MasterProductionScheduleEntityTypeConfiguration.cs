using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class MasterProductionScheduleEntityTypeConfiguration : IEntityTypeConfiguration<MasterProductionSchedule>
{
    public void Configure(EntityTypeBuilder<MasterProductionSchedule> builder)
    {
        builder.ToTable("master_production_schedules", table => table.HasComment("DemandPlanning owned daily master production schedule buckets."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Master production schedule aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id that owns the MPS bucket.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(64).IsRequired().HasComment("Scheduled SKU code snapshot.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(32).IsRequired().HasComment("Scheduled quantity unit of measure snapshot.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(64).IsRequired().HasComment("Scheduled site code snapshot.");
        builder.Property(x => x.BucketDate).HasColumnName("bucket_date").HasComment("Daily MPS bucket date.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Positive scheduled quantity.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).HasComment("MPS bucket lifecycle status.");
        builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(128).HasComment("Planner that reviewed the MPS bucket.");
        builder.Property(x => x.ReviewedAtUtc).HasColumnName("reviewed_at_utc").HasComment("UTC timestamp when the MPS bucket was reviewed.");
        builder.Property(x => x.ReleasedBy).HasColumnName("released_by").HasMaxLength(128).HasComment("Planner or manager that released the MPS bucket.");
        builder.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc").HasComment("UTC timestamp when the MPS bucket was released.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the MPS bucket was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC timestamp when the MPS bucket was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.SiteCode, x.BucketDate }).IsUnique();
    }
}

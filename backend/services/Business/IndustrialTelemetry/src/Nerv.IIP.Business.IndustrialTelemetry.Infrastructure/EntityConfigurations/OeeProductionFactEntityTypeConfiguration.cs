using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class OeeProductionFactEntityTypeConfiguration : IEntityTypeConfiguration<OeeProductionFact>
{
    public void Configure(EntityTypeBuilder<OeeProductionFact> builder)
    {
        builder.ToTable("oee_production_facts", tableBuilder =>
            tableBuilder.HasComment("MES production-report facts projected for explainable IndustrialTelemetry OEE calculations."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("OEE production fact aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id.");
        builder.Property(x => x.SourceReportNo).HasColumnName("source_report_no").IsRequired().HasMaxLength(100).HasComment("MES production report number used as the idempotent projection key.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MES work center snapshot for the reported operation.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("MES assigned device asset used to scope OEE.");
        builder.Property(x => x.GoodQuantity).HasColumnName("good_quantity").HasPrecision(18, 6).IsRequired().HasComment("Reported accepted output quantity; reversals are negative.");
        builder.Property(x => x.ScrapQuantity).HasColumnName("scrap_quantity").HasPrecision(18, 6).IsRequired().HasComment("Reported scrap output quantity; reversals are negative.");
        builder.Property(x => x.ReworkQuantity).HasColumnName("rework_quantity").HasPrecision(18, 6).IsRequired().HasComment("Reported rework output quantity; reversals are negative.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(30).HasComment("Output quantity unit copied from the MES operation snapshot.");
        builder.Property(x => x.TheoreticalRatePerHour).HasColumnName("theoretical_rate_per_hour").HasPrecision(18, 6).HasComment("Expected output per productive hour from the MES operation planning snapshot.");
        builder.Property(x => x.ReportedAtUtc).HasColumnName("reported_at_utc").IsRequired().HasComment("UTC instant assigned to the production report.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceReportNo })
            .IsUnique()
            .HasDatabaseName("ux_oee_production_facts_scope_source_report_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.ReportedAtUtc });
    }
}

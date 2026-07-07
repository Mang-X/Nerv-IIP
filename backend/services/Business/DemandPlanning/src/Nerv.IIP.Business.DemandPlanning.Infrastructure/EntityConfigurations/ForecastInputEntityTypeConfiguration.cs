using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class ForecastInputEntityTypeConfiguration : IEntityTypeConfiguration<ForecastInput>
{
    public void Configure(EntityTypeBuilder<ForecastInput> builder)
    {
        builder.ToTable("forecast_inputs", table => table.HasComment("DemandPlanning owned forecast input facts consumed by MRP."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Forecast input aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id that owns the forecast.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment id.");
        builder.Property(x => x.ForecastReference).HasColumnName("forecast_reference").HasMaxLength(128).IsRequired().HasComment("Business forecast reference unique in the planning scope.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(64).IsRequired().HasComment("Forecast SKU code snapshot.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(32).IsRequired().HasComment("Forecast quantity unit of measure snapshot.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(64).IsRequired().HasComment("Forecast site code snapshot.");
        builder.Property(x => x.PeriodStartDate).HasColumnName("period_start_date").HasComment("Forecast period start date.");
        builder.Property(x => x.PeriodEndDate).HasColumnName("period_end_date").HasComment("Forecast period end date and default MRP requirement date for remaining forecast.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Forecast quantity before order consumption.");
        builder.Property(x => x.BackwardConsumptionDays).HasColumnName("backward_consumption_days").HasComment("Days before the forecast period that sales orders may consume this forecast.");
        builder.Property(x => x.ForwardConsumptionDays).HasColumnName("forward_consumption_days").HasComment("Days after the forecast period that sales orders may consume this forecast.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the forecast input was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC timestamp when the forecast input was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ForecastReference }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.SiteCode, x.PeriodEndDate });
    }
}

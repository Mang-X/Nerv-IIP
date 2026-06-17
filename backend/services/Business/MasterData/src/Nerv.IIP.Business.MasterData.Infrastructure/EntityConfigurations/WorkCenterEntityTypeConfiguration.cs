using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class WorkCenterEntityTypeConfiguration : IEntityTypeConfiguration<WorkCenter>
{
    public void Configure(EntityTypeBuilder<WorkCenter> builder)
    {
        builder.ToTable("work_centers", tableBuilder =>
            tableBuilder.HasComment("Business master data work centers used for capacity planning and execution routing."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work center aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the work center.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the work center is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique work center code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Work center display name.");
        builder.Property(x => x.CapacityMinutesPerDay).HasColumnName("capacity_minutes_per_day").IsRequired().HasComment("Nominal available capacity per day in minutes.");
        builder.Property(x => x.ResourceType).HasColumnName("resource_type").IsRequired().HasMaxLength(100).HasComment("Resource type such as work-center, process-unit, labor-cell or equipment-group.");
        builder.Property(x => x.PlantCode).HasColumnName("plant_code").IsRequired().HasMaxLength(100).HasComment("Plant code where the work center belongs.");
        builder.Property(x => x.LineCode).HasColumnName("line_code").IsRequired().HasMaxLength(100).HasComment("Production line code where the work center belongs.");
        builder.Property(x => x.WorkshopCode).HasColumnName("workshop_code").HasMaxLength(100).HasComment("Optional workshop code that groups the work center under a site.");
        builder.Property(x => x.DefaultCalendarCode).HasColumnName("default_calendar_code").IsRequired().HasMaxLength(100).HasComment("Default work calendar code used for planning capacity.");
        builder.Property(x => x.CapacityUnit).HasColumnName("capacity_unit").IsRequired().HasMaxLength(50).HasComment("Unit for nominal resource capacity, for example minute, liter or kilogram.");
        builder.Property(x => x.FiniteCapacity).HasColumnName("finite_capacity").IsRequired().HasComment("Flag that indicates planning should treat the work center as finite capacity.");
        builder.Property(x => x.UtilizationRate).HasColumnName("utilization_rate").IsRequired().HasPrecision(9, 6).HasDefaultValue(1m).HasComment("Default utilization rate used to convert nominal capacity to rated capacity.");
        builder.Property(x => x.EfficiencyRate).HasColumnName("efficiency_rate").IsRequired().HasPrecision(9, 6).HasDefaultValue(1m).HasComment("Default efficiency rate used to convert nominal capacity to rated capacity.");
        builder.Property(x => x.NumberOfCapacities).HasColumnName("number_of_capacities").IsRequired().HasDefaultValue(1).HasComment("Parallel capacity count such as machine count or labor station count.");
        builder.Property(x => x.CostCenterCode).HasColumnName("cost_center_code").HasMaxLength(100).HasComment("Optional ERP costing cost center code for the work center.");
        builder.Property(x => x.Bottleneck).HasColumnName("bottleneck").IsRequired().HasComment("Whether this work center is treated as a bottleneck resource for planning.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the work center from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the work center was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the work center was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => x.Disabled);
        builder.HasIndex(x => new { x.WorkshopCode, x.Disabled });
    }
}

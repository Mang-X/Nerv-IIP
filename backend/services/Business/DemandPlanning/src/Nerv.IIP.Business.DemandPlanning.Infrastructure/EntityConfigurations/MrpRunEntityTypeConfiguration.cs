using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class MrpRunEntityTypeConfiguration : IEntityTypeConfiguration<MrpRun>
{
    public void Configure(EntityTypeBuilder<MrpRun> builder)
    {
        builder.ToTable("mrp_runs", table => table.HasComment("DemandPlanning MRP calculation run headers and input snapshot metadata."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("MRP run aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id that owns the MRP run.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment id.");
        builder.Property(x => x.HorizonStart).HasColumnName("horizon_start").HasComment("MRP calculation horizon start date.");
        builder.Property(x => x.HorizonEnd).HasColumnName("horizon_end").HasComment("MRP calculation horizon end date.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).HasComment("MRP run status.");
        builder.Property(x => x.ProductionEngineeringSnapshotSource).HasColumnName("production_engineering_snapshot_source").HasMaxLength(128).HasComment("Source adapter used for released version and MBOM snapshots.");
        builder.Property(x => x.InventorySnapshotSource).HasColumnName("inventory_snapshot_source").HasMaxLength(128).HasComment("Source adapter used for inventory availability snapshots.");
        builder.Property(x => x.InputSourceSummary).HasColumnName("input_source_summary").HasMaxLength(256).HasComment("Semicolon-separated MRP input source types included in this run.");
        builder.Ignore(x => x.InputSources);
        builder.Property(x => x.InputCoverageStart).HasColumnName("input_coverage_start").HasComment("Earliest input demand date included in this run.");
        builder.Property(x => x.InputCoverageEnd).HasColumnName("input_coverage_end").HasComment("Latest input demand date included in this run.");
        builder.Ignore(x => x.HasInputDegradation);
        builder.Ignore(x => x.InputDegradationSources);
        builder.Property(x => x.DemandCount).HasColumnName("demand_count").HasComment("Number of demand source snapshots included in the run.");
        builder.Property(x => x.AvailabilityCount).HasColumnName("availability_count").HasComment("Number of availability snapshots included in the run.");
        builder.Property(x => x.SuggestionCount).HasColumnName("suggestion_count").HasComment("Number of planning suggestions created by the run.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the MRP run was created.");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").HasComment("UTC timestamp when calculation started.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC timestamp when calculation completed.");
    }
}

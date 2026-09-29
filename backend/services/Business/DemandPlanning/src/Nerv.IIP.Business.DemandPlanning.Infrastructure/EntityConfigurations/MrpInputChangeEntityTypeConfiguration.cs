using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpInputChangeAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class MrpInputChangeEntityTypeConfiguration : IEntityTypeConfiguration<MrpInputChange>
{
    public void Configure(EntityTypeBuilder<MrpInputChange> builder)
    {
        builder.ToTable("mrp_input_changes", table => table.HasComment("Durable before-and-after facts for DemandPlanning MRP input changes."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Unique change fact id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization owning the input.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment owning the input.");
        builder.Property(x => x.InputType).HasColumnName("input_type").HasMaxLength(32).IsRequired().HasComment("MRP input category, such as demand or forecast.");
        builder.Property(x => x.DemandType).HasColumnName("demand_type").HasMaxLength(32).HasComment("Demand source type; null for forecast, MPS, or legacy facts with unknown type.");
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(128).IsRequired().HasComment("Stable source document or input identity.");
        builder.Property(x => x.SourceLineReference).HasColumnName("source_line_reference").HasMaxLength(128).IsRequired().HasComment("Stable source line identity, empty for a whole-input source.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired().HasComment("UTC time at which the source input changed.");
        builder.Property(x => x.Operation).HasColumnName("operation").HasConversion<string>().HasMaxLength(16).IsRequired().HasComment("Source change operation: Created, Updated, or Deleted.");
        builder.Property(x => x.PreviousStartDate).HasColumnName("previous_start_date").HasComment("First date in the input interval before the change, if present.");
        builder.Property(x => x.PreviousEndDate).HasColumnName("previous_end_date").HasComment("Last date in the input interval before the change, if present.");
        builder.Property(x => x.PreviouslyEligible).HasColumnName("previously_eligible").IsRequired().HasComment("Whether the previous source state qualified as an MRP input.");
        builder.Property(x => x.CurrentStartDate).HasColumnName("current_start_date").HasComment("First date in the input interval after the change, if present.");
        builder.Property(x => x.CurrentEndDate).HasColumnName("current_end_date").HasComment("Last date in the input interval after the change, if present.");
        builder.Property(x => x.CurrentlyEligible).HasColumnName("currently_eligible").IsRequired().HasComment("Whether the current source state qualifies as an MRP input.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.InputType, x.DemandType, x.SourceReference, x.SourceLineReference, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PreviousEndDate, x.PreviousStartDate });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CurrentEndDate, x.CurrentStartDate });
    }
}

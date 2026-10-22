using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class OperationExecutionDowntimeStateEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationExecutionDowntimeState>
{
    public void Configure(EntityTypeBuilder<OperationExecutionDowntimeState> builder)
    {
        builder.ToTable(
            "operation_execution_downtime_states",
            table => table.HasComment("Latest state of each MES downtime fact associated with a Scheduling operation execution projection."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Downtime state row id.");
        builder.Property(x => x.OperationExecutionProjectionId).HasColumnName("operation_execution_projection_id").HasComment("Owning operation execution projection id.");
        builder.Property(x => x.DowntimeEventNo).HasColumnName("downtime_event_no").HasMaxLength(128).IsRequired().HasComment("MES downtime business identity.");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasComment("Whether this downtime still blocks the operation.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired().HasComment("Ordering watermark for this downtime identity in UTC.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(128).IsRequired().HasComment("Integration event id that supplied this downtime state.");
        builder.HasIndex(x => new { x.OperationExecutionProjectionId, x.DowntimeEventNo }).IsUnique();
    }
}

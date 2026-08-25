using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class OperationTaskParticipantEntityTypeConfiguration : IEntityTypeConfiguration<OperationTaskParticipant>
{
    public void Configure(EntityTypeBuilder<OperationTaskParticipant> builder)
    {
        builder.ToTable("operation_task_participants", table =>
        {
            table.HasComment("Current MES operation collaboration roster with worker identity snapshots and labor shares.");
            table.HasCheckConstraint("ck_operation_task_participants_share_percent", "share_percent > 0 AND share_percent <= 100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Operation task participant fact id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant scope.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation task public id.");
        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired().HasMaxLength(100).HasComment("MasterData worker user id captured for collaboration.");
        builder.Property(x => x.WorkerName).HasColumnName("worker_name").HasMaxLength(200).HasComment("Worker display name snapshot resolved at dispatch time.");
        builder.Property(x => x.SharePercent).HasColumnName("share_percent").HasPrecision(7, 4).IsRequired().HasComment("Worker share of the operation labor time in percent.");
        builder.HasOne<OperationTask>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasConstraintName("fk_operation_task_participants_operation_tasks")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.WorkerId })
            .IsUnique()
            .HasDatabaseName("ux_operation_task_participants_scope_task_worker");
    }
}

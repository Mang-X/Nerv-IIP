using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class SchedulePlanEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlan>
{
    public void Configure(EntityTypeBuilder<SchedulePlan> builder)
    {
        builder.ToTable("schedule_plans", table => table.HasComment("BusinessScheduling generated, released, superseded, and revoked schedule plan headers."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule plan aggregate row id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.PlanId).HasColumnName("plan_id").HasMaxLength(96).IsRequired().HasComment("Public schedule plan id.");
        builder.Property(x => x.ProblemId).HasColumnName("problem_id").HasMaxLength(96).IsRequired().HasComment("Public scheduling problem id used to generate this plan.");
        builder.Property(x => x.ProblemFingerprint).HasColumnName("problem_fingerprint").HasMaxLength(128).IsRequired().HasComment("Deterministic fingerprint of the scheduling problem input.");
        builder.Property(x => x.AlgorithmVersion).HasColumnName("algorithm_version").HasMaxLength(64).IsRequired().HasComment("APS lite algorithm version used to generate the plan.");
        builder.Property(x => x.ContractVersion).HasColumnName("contract_version").HasComment("Schedule plan contract version.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).HasComment("Persisted plan lifecycle status.");
        builder.Property(x => x.GeneratedAtUtc).HasColumnName("generated_at_utc").HasComment("UTC timestamp when the plan was generated.");
        builder.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc").HasComment("UTC timestamp when the plan was released.");
        builder.Property(x => x.ReleaseRevision).HasColumnName("release_revision").HasComment("Monotonic release revision within the organization and environment scope.");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc").HasComment("UTC timestamp when the released plan was superseded or explicitly revoked.");
        builder.Property(x => x.SupersededByPlanId).HasColumnName("superseded_by_plan_id").HasMaxLength(96).HasComment("Successor schedule plan id for automatic supersession; null for explicit revoke.");
        builder.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasConversion<string>().HasMaxLength(32).HasComment("Released plan withdrawal reason: Superseded or Explicit.");
        builder.Property(x => x.ScheduledOperationCount).HasColumnName("scheduled_operation_count").HasComment("Number of operations assigned by this plan.");
        builder.Property(x => x.UnscheduledOperationCount).HasColumnName("unscheduled_operation_count").HasComment("Number of operations left unscheduled by this plan.");
        builder.Property(x => x.LockedOperationCount).HasColumnName("locked_operation_count").HasComment("Number of fixed locked operations retained by this plan.");
        builder.Property(x => x.OptimizableOperationCount).HasColumnName("optimizable_operation_count").HasComment("Number of non-locked scheduled operations included in optimization KPIs.");
        builder.Property(x => x.AssignedMinutes).HasColumnName("assigned_minutes").HasComment("Total resource occupied minutes, including operation processing plus setup/changeover time.");
        builder.Property(x => x.MakespanMinutes).HasColumnName("makespan_minutes").HasComment("Minutes between the earliest resource occupancy start and latest assignment end.");
        builder.Property(x => x.TotalTardinessMinutes).HasColumnName("total_tardiness_minutes").HasComment("Total minutes by which non-locked assigned operations finish after their due dates.");
        builder.Property(x => x.LateOperationCount).HasColumnName("late_operation_count").HasComment("Number of non-locked assigned operations finishing after their due dates.");
        builder.Property(x => x.OnTimeRate).HasColumnName("on_time_rate").HasPrecision(18, 6).HasComment("Non-locked operations completed on or before due date divided by non-locked assigned operations.");
        builder.Property(x => x.AverageResourceUtilization).HasColumnName("average_resource_utilization").HasPrecision(18, 6).HasComment("Total assigned minutes divided by total available minutes across resource load windows.");
        builder.HasIndex(x => x.PlanId).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId })
            .IsUnique()
            .HasDatabaseName("ux_schedule_plans_scope_active_release")
            .HasFilter("status = 'Released'");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReleaseRevision })
            .IsUnique()
            .HasDatabaseName("ux_schedule_plans_scope_release_revision")
            .HasFilter("release_revision IS NOT NULL");

        builder.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => x.SchedulePlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ResourceLoads).WithOne().HasForeignKey(x => x.SchedulePlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Conflicts).WithOne().HasForeignKey(x => x.SchedulePlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.UnscheduledOperations).WithOne().HasForeignKey(x => x.SchedulePlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

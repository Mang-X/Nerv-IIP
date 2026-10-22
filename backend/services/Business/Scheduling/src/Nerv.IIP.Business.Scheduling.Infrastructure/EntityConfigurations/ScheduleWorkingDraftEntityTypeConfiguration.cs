using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleWorkingDraftAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class ScheduleWorkingDraftEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleWorkingDraft>
{
    public void Configure(EntityTypeBuilder<ScheduleWorkingDraft> builder)
    {
        builder.ToTable("schedule_working_drafts", table => table.HasComment("User-owned scheduling editing state, separate from authoritative plan assignments."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Working draft row id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.PlanId).HasColumnName("plan_id").HasMaxLength(96).IsRequired().HasComment("Persisted baseline plan version id.");
        builder.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired().HasComment("Authenticated planner id forwarded by an internal caller.");
        builder.Property(x => x.StateJson).HasColumnName("state_json").HasColumnType("jsonb").IsRequired().HasComment("SchedulingWorkingDraftStateContract v1 editing state; producer and consumer are Scheduling API and Business Console. Baseline and undo history are excluded.");
        builder.Property(x => x.SavedAtUtc).HasColumnName("saved_at_utc").HasComment("UTC timestamp of the most recent draft save.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.UserId, x.PlanId }).IsUnique();
    }
}

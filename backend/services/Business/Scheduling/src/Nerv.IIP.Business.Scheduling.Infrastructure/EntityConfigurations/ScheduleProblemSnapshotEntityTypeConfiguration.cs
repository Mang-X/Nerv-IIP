using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class ScheduleProblemSnapshotEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleProblemSnapshot>
{
    public void Configure(EntityTypeBuilder<ScheduleProblemSnapshot> builder)
    {
        builder.ToTable("schedule_problems", table => table.HasComment("BusinessScheduling normalized scheduling problem snapshots."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Scheduling problem snapshot row id.");
        builder.Property(x => x.ProblemId).HasColumnName("problem_id").HasMaxLength(96).IsRequired().HasComment("Public scheduling problem id.");
        builder.Property(x => x.ContractVersion).HasColumnName("contract_version").HasComment("Scheduling problem contract version.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.ProblemFingerprint).HasColumnName("problem_fingerprint").HasMaxLength(128).IsRequired().HasComment("Deterministic fingerprint of the scheduling problem input.");
        builder.Property(x => x.HorizonStartUtc).HasColumnName("horizon_start_utc").HasComment("Scheduling horizon start timestamp in UTC.");
        builder.Property(x => x.HorizonEndUtc).HasColumnName("horizon_end_utc").HasComment("Scheduling horizon end timestamp in UTC.");
        builder.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").HasComment("UTC timestamp when the problem snapshot was captured.");
        builder.HasIndex(x => x.ProblemId).IsUnique();
    }
}

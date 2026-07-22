using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class OrderUrgencyBusinessPriorityEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencyBusinessPriority>
{
    public void Configure(EntityTypeBuilder<OrderUrgencyBusinessPriority> builder)
    {
        builder.ToTable("order_urgency_business_priorities", table => table.HasComment("Current audited business-priority input for the unified order urgency model."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Business-priority row id.");
        ConfigureScope(builder);
        builder.Property(x => x.Level).HasColumnName("level").HasConversion<string>().HasMaxLength(16).HasComment("Current P0-P3 business priority.");
        builder.Property(x => x.SetBy).HasColumnName("set_by").HasMaxLength(256).IsRequired().HasComment("Authenticated actor reference that set the priority.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired().HasComment("Human-readable reason for the priority.");
        builder.Property(x => x.SetAtUtc).HasColumnName("set_at_utc").HasComment("UTC timestamp when the priority was set.");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasComment("Optional UTC expiry for the manual priority.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken().HasComment("Monotonic audit revision and optimistic concurrency token.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OrderId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BusinessReference });
    }

    internal static void ConfigureScope<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<string>("OrganizationId").HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property<string>("EnvironmentId").HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property<string>("OrderId").HasColumnName("order_id").HasMaxLength(128).IsRequired().HasComment("Scheduling order/work-order id.");
        builder.Property<string>("BusinessReference").HasColumnName("business_reference").HasMaxLength(128).IsRequired().HasComment("Stable upstream business reference used across ERP, planning, MES, and scheduling.");
    }
}

public sealed class OrderUrgencyBusinessPriorityChangeEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencyBusinessPriorityChange>
{
    public void Configure(EntityTypeBuilder<OrderUrgencyBusinessPriorityChange> builder)
    {
        builder.ToTable("order_urgency_business_priority_changes", table => table.HasComment("Append-only business-priority audit history for the unified order urgency model."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Priority-change row id.");
        OrderUrgencyBusinessPriorityEntityTypeConfiguration.ConfigureScope(builder);
        builder.Property(x => x.Revision).HasColumnName("revision").HasComment("Monotonic priority revision.");
        builder.Property(x => x.PreviousLevel).HasColumnName("previous_level").HasConversion<string>().HasMaxLength(16).HasComment("Priority before the change.");
        builder.Property(x => x.NewLevel).HasColumnName("new_level").HasConversion<string>().HasMaxLength(16).HasComment("Priority after the change.");
        builder.Property(x => x.ChangedBy).HasColumnName("changed_by").HasMaxLength(256).IsRequired().HasComment("Authenticated actor reference.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired().HasComment("Human-readable change reason.");
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").HasComment("UTC change timestamp.");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasComment("Optional UTC expiry for the new priority.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OrderId, x.Revision }).IsUnique();
    }
}

public sealed class OrderUrgencySnapshotEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencySnapshot>
{
    public void Configure(EntityTypeBuilder<OrderUrgencySnapshot> builder)
    {
        builder.ToTable("order_urgency_snapshots", table => table.HasComment("Immutable explainable urgency calculation snapshots and input audit evidence."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Urgency snapshot row id.");
        OrderUrgencyBusinessPriorityEntityTypeConfiguration.ConfigureScope(builder);
        builder.Property(x => x.Level).HasColumnName("level").HasConversion<string>().HasMaxLength(32).HasComment("Unified urgency level.");
        builder.Property(x => x.ModelVersion).HasColumnName("model_version").HasMaxLength(64).IsRequired().HasComment("Versioned deterministic calculation model.");
        builder.Property(x => x.InputFingerprint).HasColumnName("input_fingerprint").HasMaxLength(128).IsRequired().HasComment("Fingerprint of authoritative source facts.");
        builder.Property(x => x.BusinessPriorityRevision).HasColumnName("business_priority_revision").HasComment("Priority revision used by this calculation.");
        builder.Property(x => x.CalculationBucketUtc).HasColumnName("calculation_bucket_utc").HasComment("Deterministic UTC time bucket used for idempotent recalculation.");
        builder.Property(x => x.CalculatedAtUtc).HasColumnName("calculated_at_utc").HasComment("UTC model calculation timestamp.");
        builder.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("jsonb").IsRequired().HasComment("Explainable contributions, reason codes, and source timestamps.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OrderId, x.ModelVersion, x.InputFingerprint, x.BusinessPriorityRevision, x.CalculationBucketUtc }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BusinessReference, x.CalculatedAtUtc });
    }
}

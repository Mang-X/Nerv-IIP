using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure.EntityConfigurations;

public sealed class ApplicationInstanceEntityTypeConfiguration : IEntityTypeConfiguration<ApplicationInstance>
{
    public void Configure(EntityTypeBuilder<ApplicationInstance> builder)
    {
        builder.ToTable("application_instances", tableBuilder =>
            tableBuilder.HasComment("AppHub managed application instance aggregate roots reported by connector hosts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Application instance aggregate id");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasComment("Organization id");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasComment("Environment id");
        builder.Property(x => x.ApplicationKey).IsRequired().HasMaxLength(160).HasComment("Application protocol key");
        builder.Property(x => x.Version).IsRequired().HasMaxLength(100).HasComment("Application version");
        builder.Property(x => x.NodeKey).IsRequired().HasMaxLength(160).HasComment("Node protocol key");
        builder.Property(x => x.InstanceKey).IsRequired().HasMaxLength(160).HasComment("Instance protocol key");
        builder.Property(x => x.InstanceName).IsRequired().HasMaxLength(200).HasComment("Instance display name");
        builder.Property(x => x.ReportedStatus).IsRequired().HasMaxLength(100).HasComment("Reported status");
        builder.Property(x => x.HealthStatus).IsRequired().HasMaxLength(100).HasComment("Health status");
        builder.Property(x => x.Metadata)
            .HasConversion(value => EntityConfigurationJson.SerializeDictionary(value), value => EntityConfigurationJson.DeserializeDictionary(value))
            .HasComment("JSON dictionary produced by Connector Host registration and state reporting, consumed by AppHub and Gateway readers; additive optional keys are compatible, removing or changing key semantics requires Connector Protocol versioning.")
            .Metadata.SetValueComparer(EntityConfigurationJson.DictionaryComparer);
        builder.Property(x => x.Capabilities)
            .HasConversion(value => EntityConfigurationJson.SerializeCapabilities(value), value => EntityConfigurationJson.DeserializeCapabilities(value))
            .HasComment("JSON capability descriptors produced by Connector Host discovery, consumed by Gateway and Ops action routing; additive capabilities are compatible, removing or changing action semantics requires Connector Protocol versioning.")
            .Metadata.SetValueComparer(EntityConfigurationJson.CapabilitiesComparer);
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version");
        builder.HasIndex(x => x.InstanceKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ApplicationKey });
        builder.HasOne(x => x.Heartbeat).WithOne().HasForeignKey<InstanceHeartbeat>(x => x.ApplicationInstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.StateHistory).WithOne().HasForeignKey(x => x.ApplicationInstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.StatusChanges).WithOne().HasForeignKey(x => x.ApplicationInstanceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InstanceHeartbeatEntityTypeConfiguration : IEntityTypeConfiguration<InstanceHeartbeat>
{
    public void Configure(EntityTypeBuilder<InstanceHeartbeat> builder)
    {
        builder.ToTable("instance_heartbeat", tableBuilder =>
            tableBuilder.HasComment("AppHub latest heartbeat facts for managed application instances."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Heartbeat id");
        builder.Property(x => x.ApplicationInstanceId).HasConversion(id => id.Id, value => new ApplicationInstanceId(value)).IsRequired().HasComment("Application instance aggregate id");
        builder.Property(x => x.LastHeartbeatAtUtc).IsRequired().HasComment("Last heartbeat time");
        builder.Property(x => x.Reachable).IsRequired().HasComment("Reachability flag");
        builder.Property(x => x.LatencyMs).IsRequired().HasComment("Observed latency in milliseconds");
        builder.HasIndex(x => x.ApplicationInstanceId).IsUnique();
    }
}

public sealed class InstanceStateHistoryEntityTypeConfiguration : IEntityTypeConfiguration<InstanceStateHistory>
{
    public void Configure(EntityTypeBuilder<InstanceStateHistory> builder)
    {
        builder.ToTable("instance_state_history", tableBuilder =>
            tableBuilder.HasComment("AppHub observed application instance state history for diagnostics and status timelines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("State history id");
        builder.Property(x => x.ApplicationInstanceId).HasConversion(id => id.Id, value => new ApplicationInstanceId(value)).IsRequired().HasComment("Application instance aggregate id");
        builder.Property(x => x.ObservedAtUtc).IsRequired().HasComment("State observation time");
        builder.Property(x => x.ReportedStatus).IsRequired().HasMaxLength(100).HasComment("Reported status");
        builder.Property(x => x.HealthStatus).IsRequired().HasMaxLength(100).HasComment("Health status");
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(1000).HasComment("State summary");
        builder.HasIndex(x => new { x.ApplicationInstanceId, x.ObservedAtUtc });
    }
}

public sealed class InstanceStatusChangeEntityTypeConfiguration : IEntityTypeConfiguration<InstanceStatusChange>
{
    public void Configure(EntityTypeBuilder<InstanceStatusChange> builder)
    {
        builder.ToTable("instance_status_changes", tableBuilder =>
            tableBuilder.HasComment("AppHub reported status transition history for managed application instances."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Status change id");
        builder.Property(x => x.ApplicationInstanceId).HasConversion(id => id.Id, value => new ApplicationInstanceId(value)).IsRequired().HasComment("Application instance aggregate id");
        builder.Property(x => x.PreviousStatus).IsRequired().HasMaxLength(100).HasComment("Previous reported status");
        builder.Property(x => x.CurrentStatus).IsRequired().HasMaxLength(100).HasComment("Current reported status");
        builder.Property(x => x.ChangedAtUtc).IsRequired().HasComment("Status change time");
        builder.HasIndex(x => new { x.ApplicationInstanceId, x.ChangedAtUtc });
    }
}

public sealed class RegistrationIdempotencyEntityTypeConfiguration : IEntityTypeConfiguration<RegistrationIdempotency>
{
    public void Configure(EntityTypeBuilder<RegistrationIdempotency> builder)
    {
        builder.ToTable("registration_idempotency", tableBuilder =>
            tableBuilder.HasComment("AppHub registration idempotency records used to deduplicate connector retries."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Registration idempotency id");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200).HasComment("Idempotency key");
        builder.Property(x => x.RegistrationId).IsRequired().HasMaxLength(100).HasComment("Registration id");
        builder.Property(x => x.InstanceKey).IsRequired().HasMaxLength(160).HasComment("Instance protocol key");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
    }
}

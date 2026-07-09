using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlCommandAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class DeviceControlCommandEntityTypeConfiguration : IEntityTypeConfiguration<DeviceControlCommand>
{
    public void Configure(EntityTypeBuilder<DeviceControlCommand> builder)
    {
        builder.ToTable("device_control_commands", table => table.HasComment("BusinessIndustrialTelemetry device control command ledger projecting Ops operation tasks for result/history read-face."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Device control command ledger identifier.");
        builder.Property(x => x.OperationTaskId).IsRequired().HasMaxLength(100).HasColumnName("operation_task_id").HasComment("Ops operation task identifier; the external command id resolved by the read-face.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.ConnectorHostId).IsRequired().HasMaxLength(128).HasColumnName("connector_host_id").HasComment("Connector host that owns the target device control channel.");
        builder.Property(x => x.InstanceKey).IsRequired().HasMaxLength(150).HasColumnName("instance_key").HasComment("Connector instance key routed by the Ops operation task.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.CommandType).IsRequired().HasMaxLength(50).HasColumnName("command_type").HasComment("Device control command type such as write-tag, start-stop or parameter-set.");
        builder.Property(x => x.TagKey).HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key targeted by single-tag commands; null for parameter-set commands.");
        builder.Property(x => x.Value).HasMaxLength(256).HasColumnName("value").HasComment("Requested control value for single-tag commands; null for parameter-set commands.");
        builder.Property(x => x.ParametersJson).HasColumnName("parameters_json").HasComment("JSON object of parameter-set command inputs (tag key to value); null for single-tag commands.");
        builder.Property(x => x.RequestedBy).IsRequired().HasMaxLength(150).HasColumnName("requested_by").HasComment("Authenticated principal recorded as the command requester.");
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500).HasColumnName("reason").HasComment("Operator-supplied reason captured for the control command audit.");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(150).HasColumnName("idempotency_key").HasComment("Idempotency key bound to the Ops operation task creation.");
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(150).HasColumnName("correlation_id").HasComment("Correlation identifier propagated to the Ops operation task.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50).HasColumnName("status").HasComment("Dispatch-time Ops task status snapshot; the single-command read-face refreshes live status from Ops.");
        builder.Property(x => x.ApprovalStatus).HasMaxLength(50).HasColumnName("approval_status").HasComment("Dispatch-time Ops approval status snapshot when the command required approval.");
        builder.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc").HasComment("UTC time when the command was dispatched to Ops.");
        builder.Property(x => x.RequestedAtUnixTimeMilliseconds).HasColumnName("requested_at_unix_time_milliseconds").HasComment("Requested UTC time as Unix time milliseconds for provider-neutral history range filtering and ordering.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when the ledger row was recorded.");
        builder.HasIndex(x => x.OperationTaskId).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RequestedAtUnixTimeMilliseconds });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.RequestedAtUnixTimeMilliseconds });
    }
}

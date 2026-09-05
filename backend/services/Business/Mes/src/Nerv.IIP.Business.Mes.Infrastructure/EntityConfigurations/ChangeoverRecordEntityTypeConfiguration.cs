using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ChangeoverRecordAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ChangeoverRecordEntityTypeConfiguration : IEntityTypeConfiguration<ChangeoverRecord>
{
    public void Configure(EntityTypeBuilder<ChangeoverRecord> builder)
    {
        builder.ToTable("changeover_records", tableBuilder =>
            tableBuilder.HasComment("MES actual changeover lifecycle records for production equipment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Changeover record aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id owning the changeover record.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id owning the changeover record.");
        builder.Property(x => x.ChangeoverNo).HasColumnName("changeover_no").IsRequired().HasMaxLength(100).HasComment("MES business number allocated for the changeover record.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MasterData work center public id where the changeover occurred.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("MasterData device asset public id changed over.");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").IsRequired().HasMaxLength(100).HasComment("IAM principal id of the operator performing the changeover.");
        builder.Property(x => x.ToolingCheckResult).HasColumnName("tooling_check_result").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Controlled tooling or mold verification result captured at changeover start.");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired().HasComment("UTC time when the changeover started.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC time when the changeover completed; null means it is still active.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ChangeoverNo })
            .IsUnique()
            .HasDatabaseName("ux_changeover_records_scope_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.CompletedAtUtc })
            .HasDatabaseName("ix_changeover_records_scope_device_open");
    }
}

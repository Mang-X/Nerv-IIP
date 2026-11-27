namespace Nerv.IIP.Business.Maintenance.Infrastructure;

public sealed record MaintenanceAlarmClearance(
    string OrganizationId,
    string EnvironmentId,
    string AlarmEventId,
    DateTimeOffset ClearedAtUtc)
{
    public static async Task AcquireAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string alarmEventId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("维修告警关联需要当前工作单元事务。");
        }

        var key = $"maintenance-alarm:{organizationId.Length}:{organizationId}:{environmentId.Length}:{environmentId}:{alarmEventId}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }
}

internal sealed class MaintenanceAlarmClearanceConfiguration : IEntityTypeConfiguration<MaintenanceAlarmClearance>
{
    public void Configure(EntityTypeBuilder<MaintenanceAlarmClearance> builder)
    {
        builder.ToTable("maintenance_alarm_clearances", table => table.HasComment("维修工单关联所需的告警清除事实。"));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.AlarmEventId });
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100).HasComment("告警所属组织。");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100).HasComment("告警所属环境。");
        builder.Property(x => x.AlarmEventId).HasColumnName("alarm_event_id").HasMaxLength(150).HasComment("遥测告警记录 ID。");
        builder.Property(x => x.ClearedAtUtc).HasColumnName("cleared_at_utc").HasComment("遥测记录的告警清除时间。");
    }
}

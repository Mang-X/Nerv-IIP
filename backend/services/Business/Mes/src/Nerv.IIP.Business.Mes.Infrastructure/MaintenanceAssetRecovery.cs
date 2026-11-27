using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nerv.IIP.Business.Mes.Infrastructure;

public sealed record MaintenanceAssetRecovery(
    string OrganizationId,
    string EnvironmentId,
    Guid MaintenanceWorkOrderId,
    string DeviceAssetId,
    DateTimeOffset RestoredAtUtc)
{
    public static string DowntimeEventNo(Guid workOrderId) => $"UNAV-MWO-{workOrderId:N}";

    public static async Task AcquireAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            var key = $"mes-maintenance-recovery:{organizationId}:{environmentId}:{workOrderId:D}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
    }
}

internal sealed class MaintenanceAssetRecoveryConfiguration : IEntityTypeConfiguration<MaintenanceAssetRecovery>
{
    public void Configure(EntityTypeBuilder<MaintenanceAssetRecovery> builder)
    {
        builder.ToTable("maintenance_asset_recoveries", table => table.HasComment("维保设备恢复事实，用于关闭已登记或乱序到达的停机窗口。"));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.MaintenanceWorkOrderId });
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100).HasComment("组织标识。");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100).HasComment("环境标识。");
        builder.Property(x => x.MaintenanceWorkOrderId).HasColumnName("maintenance_work_order_id").HasComment("维修工单标识。");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").HasMaxLength(150).HasComment("设备标识。");
        builder.Property(x => x.RestoredAtUtc).HasColumnName("restored_at_utc").HasComment("首次设备恢复时间。");
    }
}

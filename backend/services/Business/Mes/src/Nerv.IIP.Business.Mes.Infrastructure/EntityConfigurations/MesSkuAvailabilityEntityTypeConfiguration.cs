using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure.MasterData;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class MesSkuAvailabilityEntityTypeConfiguration : IEntityTypeConfiguration<MesSkuAvailability>
{
    public void Configure(EntityTypeBuilder<MesSkuAvailability> builder)
    {
        builder.ToTable("mes_sku_availabilities", table =>
            table.HasComment("Latest MasterData SKU availability consumed by BusinessMES new-work-order gates."));
        builder.HasKey(x => x.Id).HasName("pk_mes_sku_availabilities");
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("MES SKU availability projection identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(64).HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(64).HasComment("Owning environment identifier.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code used by MES work orders.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(32).HasComment("Latest consumed SKU status; this slice records disabled facts.");
        builder.Property(x => x.ChangedAtUtc)
            .HasColumnName("changed_at_utc")
            .IsConcurrencyToken()
            .HasComment("UTC time of the latest applied MasterData SKU availability change.");
        builder.Property(x => x.DisabledReason).HasColumnName("disabled_reason").IsRequired().HasMaxLength(500).HasComment("MasterData reason for disabling the SKU.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").IsRequired().HasMaxLength(256).HasComment("Latest applied MasterData integration event identifier.");
        builder.Ignore(x => x.IsDisabled);

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode })
            .IsUnique()
            .HasDatabaseName("ux_mes_sku_availabilities_scope_code");
    }
}

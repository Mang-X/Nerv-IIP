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
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("MES SKU availability projection identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(64).HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(64).HasComment("Owning environment identifier.");
        builder.Property(x => x.SkuCode).IsRequired().HasMaxLength(100).HasComment("MasterData SKU code used by MES work orders.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Latest consumed SKU status; this slice records disabled facts.");
        builder.Property(x => x.ChangedAtUtc)
            .IsConcurrencyToken()
            .HasComment("UTC time of the latest applied MasterData SKU availability change.");
        builder.Property(x => x.DisabledReason).IsRequired().HasMaxLength(500).HasComment("MasterData reason for disabling the SKU.");
        builder.Property(x => x.SourceEventId).IsRequired().HasMaxLength(256).HasComment("Latest applied MasterData integration event identifier.");
        builder.Ignore(x => x.IsDisabled);

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode })
            .IsUnique()
            .HasDatabaseName("ux_mes_sku_availabilities_scope_code");
    }
}

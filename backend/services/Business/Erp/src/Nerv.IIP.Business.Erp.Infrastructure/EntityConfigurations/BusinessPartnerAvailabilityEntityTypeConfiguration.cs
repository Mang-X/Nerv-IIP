using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Erp.Infrastructure.MasterData;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class BusinessPartnerAvailabilityEntityTypeConfiguration : IEntityTypeConfiguration<BusinessPartnerAvailability>
{
    public void Configure(EntityTypeBuilder<BusinessPartnerAvailability> builder)
    {
        builder.ToTable("business_partner_availabilities", table => table.HasComment("Latest MasterData business-partner availability projected for ERP order gates."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("ERP business-partner availability projection identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(64).HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(64).HasComment("Owning environment identifier.");
        builder.Property(x => x.PartnerCode).IsRequired().HasMaxLength(100).HasComment("MasterData business-partner code used by ERP orders.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Latest partner status: active or disabled.");
        builder.Property(x => x.ChangedAtUtc)
            .IsConcurrencyToken()
            .HasComment("UTC time of the latest applied MasterData partner change and optimistic concurrency token.");
        builder.Property(x => x.SourceEventId).IsRequired().HasMaxLength(256).HasComment("Latest applied MasterData integration event identifier.");
        builder.Ignore(x => x.IsDisabled);

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PartnerCode })
            .IsUnique()
            .HasDatabaseName("ux_business_partner_availabilities_scope_code");
    }
}

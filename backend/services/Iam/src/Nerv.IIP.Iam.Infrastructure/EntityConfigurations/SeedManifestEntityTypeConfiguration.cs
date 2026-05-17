using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class SeedManifestEntityTypeConfiguration : IEntityTypeConfiguration<SeedManifest>
{
    public void Configure(EntityTypeBuilder<SeedManifest> builder)
    {
        builder.ToTable("seed_manifests", table => table.HasComment("IAM seed manifests recording applied seed data versions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new SeedManifestId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Seed manifest identifier.");
        builder.Property(x => x.SeedName).IsRequired().HasMaxLength(160).HasComment("Seed data name.");
        builder.Property(x => x.SeedVersion).IsRequired().HasMaxLength(64).HasComment("Seed data version.");
        builder.Property(x => x.OwnerService).IsRequired().HasMaxLength(64).HasComment("Service that owns the seed data.");
        builder.Property(x => x.AppliedAtUtc).HasComment("Seed application time in UTC.");

        builder.HasIndex(x => new { x.SeedName, x.SeedVersion }).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class OrganizationEntityTypeConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations", table => table.HasComment("IAM organizations that scope tenants and their environments."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new OrganizationId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Organization identifier.");
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200).HasComment("Organization display name.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Organization lifecycle status.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");
    }
}

public sealed class IamEnvironmentEntityTypeConfiguration : IEntityTypeConfiguration<IamEnvironment>
{
    public void Configure(EntityTypeBuilder<IamEnvironment> builder)
    {
        builder.ToTable("environments", table => table.HasComment("IAM environments owned by organizations for access scoping."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new IamEnvironmentId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Environment identifier.");
        builder.Property(x => x.OrganizationId)
            .HasConversion(x => x.Id, x => new OrganizationId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Owning organization identifier.");
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200).HasComment("Environment display name.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Environment lifecycle status.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => new { x.OrganizationId, x.Id }).IsUnique();
    }
}

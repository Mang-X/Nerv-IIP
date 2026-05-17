using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class ConnectorHostCredentialEntityTypeConfiguration : IEntityTypeConfiguration<ConnectorHostCredential>
{
    public void Configure(EntityTypeBuilder<ConnectorHostCredential> builder)
    {
        builder.ToTable("connector_host_credentials", table => table.HasComment("IAM credentials that authenticate Connector Hosts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new ConnectorHostCredentialId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Connector Host credential identifier.");
        builder.Property(x => x.ConnectorHostId).IsRequired().HasMaxLength(128).HasComment("Unique Connector Host identifier.");
        builder.Property(x => x.OrganizationId)
            .HasConversion(x => x.Id, x => new OrganizationId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Credential organization identifier.");
        builder.Property(x => x.EnvironmentId)
            .HasConversion(x => x.Id, x => new IamEnvironmentId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Credential environment identifier.");
        builder.Property(x => x.SecretHash).IsRequired().HasMaxLength(512).HasComment("Connector Host secret hash.");
        builder.Property(x => x.ValidFromUtc).HasComment("Credential validity start time in UTC.");
        builder.Property(x => x.ValidToUtc).HasComment("Credential validity end time in UTC.");

        builder.HasIndex(x => x.ConnectorHostId).IsUnique();
        builder.HasMany(x => x.Capabilities)
            .WithOne()
            .HasForeignKey(x => x.ConnectorHostCredentialId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Capabilities).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ConnectorHostCredentialCapabilityEntityTypeConfiguration : IEntityTypeConfiguration<ConnectorHostCredentialCapability>
{
    public void Configure(EntityTypeBuilder<ConnectorHostCredentialCapability> builder)
    {
        builder.ToTable("connector_host_credential_capabilities", table => table.HasComment("IAM capability codes owned by Connector Host credentials."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new ConnectorHostCredentialCapabilityId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Connector Host credential capability identifier.");
        builder.Property(x => x.ConnectorHostCredentialId)
            .HasConversion(x => x.Id, x => new ConnectorHostCredentialId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Owning Connector Host credential identifier.");
        builder.Property(x => x.CapabilityCode).IsRequired().HasMaxLength(160).HasComment("Capability code granted to the Connector Host credential.");

        builder.HasIndex(x => new { x.ConnectorHostCredentialId, x.CapabilityCode }).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.ExternalClientAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class ExternalClientEntityTypeConfiguration : IEntityTypeConfiguration<ExternalClient>
{
    public void Configure(EntityTypeBuilder<ExternalClient> builder)
    {
        builder.ToTable("external_clients", table => table.HasComment("IAM external clients that can use client_credentials tokens."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new ExternalClientId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("External client record identifier.");
        builder.Property(x => x.ClientId).IsRequired().HasMaxLength(128).HasComment("Public client identifier used for client_credentials.");
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(160).HasComment("External client display name.");
        builder.Property(x => x.OrganizationId)
            .HasConversion(x => x.Id, x => new OrganizationId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("External client organization scope.");
        builder.Property(x => x.EnvironmentId)
            .HasConversion(x => x.Id, x => new IamEnvironmentId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("External client environment scope.");
        builder.Property(x => x.SecretHash).IsRequired().HasMaxLength(512).HasComment("External client secret hash.");
        builder.Property(x => x.Enabled).HasComment("Whether the external client can authenticate.");
        builder.Property(x => x.PermissionVersion).HasComment("External client permission version for token invalidation.");
        builder.Property(x => x.ValidFromUtc).HasComment("External client credential validity start time in UTC.");
        builder.Property(x => x.ValidToUtc).HasComment("External client credential validity end time in UTC.");
        builder.HasIndex(x => x.ClientId).IsUnique();
    }
}

public sealed class AuthorizationGrantEntityTypeConfiguration : IEntityTypeConfiguration<AuthorizationGrant>
{
    public void Configure(EntityTypeBuilder<AuthorizationGrant> builder)
    {
        builder.ToTable("authorization_grants", table => table.HasComment("IAM authorization grants for non-user principals and scoped access."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new AuthorizationGrantId(x))
            .ValueGeneratedNever()
            .HasMaxLength(128)
            .HasComment("Authorization grant identifier.");
        builder.Property(x => x.PrincipalType).IsRequired().HasMaxLength(64).HasComment("Principal type, for example external-client.");
        builder.Property(x => x.PrincipalId).IsRequired().HasMaxLength(128).HasComment("Principal identifier.");
        builder.Property(x => x.OrganizationId)
            .HasConversion(x => x.Id, x => new OrganizationId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Grant organization scope.");
        builder.Property(x => x.EnvironmentId)
            .HasConversion(x => x.Id, x => new IamEnvironmentId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Grant environment scope.");
        builder.Property(x => x.PermissionCode).IsRequired().HasMaxLength(160).HasComment("Granted permission code.");
        builder.Property(x => x.ResourceType).IsRequired().HasMaxLength(128).HasDefaultValue("*").HasComment("ABAC resource type scope. '*' grants every resource type.");
        builder.Property(x => x.ResourceId).IsRequired().HasMaxLength(160).HasDefaultValue("*").HasComment("ABAC resource identifier scope. '*' grants every resource id under the resource type.");
        builder.Property(x => x.ValidFromUtc).HasComment("Grant validity start time in UTC.");
        builder.Property(x => x.ValidToUtc).HasComment("Grant validity end time in UTC.");
        builder.Property(x => x.RevokedAtUtc).HasComment("Grant revocation time in UTC.");
        builder.HasIndex(x => new { x.PrincipalType, x.PrincipalId, x.OrganizationId, x.EnvironmentId, x.PermissionCode, x.ResourceType, x.ResourceId }).IsUnique();
    }
}

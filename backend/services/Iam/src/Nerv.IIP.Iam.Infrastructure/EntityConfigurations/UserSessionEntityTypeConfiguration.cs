using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserSessionAggregate;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class UserSessionEntityTypeConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions", table => table.HasComment("IAM refresh sessions issued to authenticated users."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new UserSessionId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("User session identifier.");
        builder.Property(x => x.UserId)
            .HasConversion(x => x.Id, x => new UserId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Authenticated user identifier.");
        builder.Property(x => x.RefreshTokenHash).IsRequired().HasMaxLength(512).HasComment("Refresh token hash used to rotate sessions.");
        builder.Property(x => x.TokenFamilyId).IsRequired().HasMaxLength(64).HasComment("Refresh token family identifier used to detect replay and revoke the full lineage.");
        builder.Property(x => x.PreviousSessionId).HasMaxLength(64).HasComment("Previous session identifier in the refresh token rotation lineage.");
        builder.Property(x => x.IssuedAtUtc).HasComment("Session issue time in UTC.");
        builder.Property(x => x.ExpiresAtUtc).HasComment("Session expiration time in UTC.");
        builder.Property(x => x.RevokedAtUtc).HasComment("Session revocation time in UTC.");
        builder.Property(x => x.RevokedReason).HasMaxLength(256).HasComment("Reason the session was revoked.");
        builder.Property(x => x.PermissionVersion).HasComment("Permission version captured when the session was issued.");
        builder.Property(x => x.ClientInfo).HasMaxLength(512).HasComment("Client information supplied during session creation.");
        builder.Property(x => x.IpAddress).HasMaxLength(64).HasComment("Client IP address supplied during session creation.");
        builder.Property(x => x.AuthenticationMethod).IsRequired().HasMaxLength(32).HasDefaultValue("password").HasComment("Authentication method used to issue the session, for example password or oidc.");
        builder.Property(x => x.ExternalProvider).HasMaxLength(64).HasComment("External identity provider name for SSO sessions.");
        builder.Property(x => x.ExternalSubject).HasMaxLength(256).HasComment("External provider subject bound to the SSO session.");
        builder.Property(x => x.MfaVerifiedAtUtc).HasComment("UTC time when MFA was verified for the session.");

        builder.HasIndex(x => x.RefreshTokenHash).IsUnique();
        builder.HasIndex(x => x.TokenFamilyId);
        builder.HasIndex(x => x.PreviousSessionId);
        builder.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        builder.HasIndex(x => new { x.ExternalProvider, x.ExternalSubject });
    }
}

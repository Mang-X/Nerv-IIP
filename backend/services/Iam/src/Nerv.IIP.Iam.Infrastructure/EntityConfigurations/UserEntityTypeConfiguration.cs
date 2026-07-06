using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table => table.HasComment("IAM users that authenticate and receive scoped permissions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new UserId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("User identifier.");
        builder.Property(x => x.LoginName).IsRequired().HasMaxLength(128).HasComment("Unique login name used for authentication.");
        builder.Property(x => x.Email).IsRequired().HasMaxLength(256).HasComment("Unique user email address.");
        builder.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512).HasComment("Password hash used for credential verification.");
        builder.Property(x => x.Enabled).HasComment("Whether the user can authenticate.");
        builder.Property(x => x.SecurityStamp).IsRequired().HasMaxLength(128).HasComment("Security stamp invalidating stale access tokens.");
        builder.Property(x => x.PermissionVersion).HasComment("Permission version embedded into issued credentials.");
        builder.Property(x => x.LastLoginAtUtc).HasComment("Last successful login time in UTC.");
        builder.Property(x => x.LastFailedLoginAtUtc).HasComment("Last failed login time in UTC.");
        builder.Property(x => x.FailedLoginCount).HasComment("Consecutive failed login count.");
        builder.Property(x => x.LockoutUntilUtc).HasComment("UTC time until which password login is locked after consecutive failures.");
        builder.Property(x => x.AccountExpiresAtUtc).HasComment("Optional UTC time after which the account can no longer authenticate.");
        builder.Property(x => x.PasswordChangedAtUtc).HasComment("UTC time when the current password hash was set.");
        builder.Property(x => x.PasswordExpiresAtUtc).HasComment("Optional UTC time after which login must force password change.");
        builder.Property(x => x.PasswordChangeRequired).HasComment("Whether the user must change password after login before normal use.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => x.LoginName).IsUnique();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.AccountExpiresAtUtc);

        builder.HasMany(x => x.PasswordHistory)
            .WithOne()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.PasswordHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class UserPasswordHistoryEntityTypeConfiguration : IEntityTypeConfiguration<UserPasswordHistory>
{
    public void Configure(EntityTypeBuilder<UserPasswordHistory> builder)
    {
        builder.ToTable("user_password_history", table => table.HasComment("Historical IAM user password hashes used to prevent recent password reuse."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .UseGuidVersion7ValueGenerator()
            .HasComment("Password history row identifier.");
        builder.Property(x => x.UserId)
            .HasConversion(x => x.Id, x => new UserId(x))
            .HasMaxLength(64)
            .IsRequired()
            .HasComment("User identifier that owns this historical password hash.");
        builder.Property(x => x.PasswordHash)
            .IsRequired()
            .HasMaxLength(512)
            .HasComment("Historical password hash retained for password history policy checks.");
        builder.Property(x => x.CreatedAtUtc)
            .HasComment("UTC time when this historical password hash was superseded.");

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
    }
}

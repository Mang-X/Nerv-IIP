using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using NetCorePal.Extensions.Domain;

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
        builder.Property(x => x.FailedLoginCount).HasComment("Consecutive failed login count.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => x.LoginName).IsUnique();
        builder.HasIndex(x => x.Email).IsUnique();
    }
}

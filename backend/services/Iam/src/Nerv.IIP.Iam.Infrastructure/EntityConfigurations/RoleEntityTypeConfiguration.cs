using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class RoleEntityTypeConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", table => table.HasComment("IAM roles that group permission codes for assignment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new RoleId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Role identifier.");
        builder.Property(x => x.RoleName).IsRequired().HasMaxLength(128).HasComment("Unique role name.");
        builder.Property(x => x.NormalizedRoleName)
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Case-insensitive normalized role name.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => x.NormalizedRoleName).IsUnique();
        builder.HasMany(x => x.Permissions)
            .WithOne()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RolePermissionEntityTypeConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", table => table.HasComment("IAM permission codes owned by roles."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new RolePermissionId(x))
            .ValueGeneratedNever()
            .HasMaxLength(256)
            .HasComment("Role permission identifier.");
        builder.Property(x => x.RoleId)
            .HasConversion(x => x.Id, x => new RoleId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Owning role identifier.");
        builder.Property(x => x.PermissionCode).IsRequired().HasMaxLength(160).HasComment("Permission code granted by the role.");

        builder.HasIndex(x => new { x.RoleId, x.PermissionCode }).IsUnique();
    }
}

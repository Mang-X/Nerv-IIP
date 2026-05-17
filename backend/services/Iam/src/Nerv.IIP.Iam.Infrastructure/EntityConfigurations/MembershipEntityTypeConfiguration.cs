using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class MembershipEntityTypeConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships", table => table.HasComment("IAM user memberships scoped to organization and environment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new MembershipId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Membership identifier.");
        builder.Property(x => x.UserId)
            .HasConversion(x => x.Id, x => new UserId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Member user identifier.");
        builder.Property(x => x.OrganizationId)
            .HasConversion(x => x.Id, x => new OrganizationId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Membership organization identifier.");
        builder.Property(x => x.EnvironmentId)
            .HasConversion(x => x.Id, x => new IamEnvironmentId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Membership environment identifier.");

        builder.HasIndex(x => new { x.UserId, x.OrganizationId, x.EnvironmentId }).IsUnique();
        builder.HasMany(x => x.Roles)
            .WithOne()
            .HasForeignKey(x => x.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class MembershipRoleEntityTypeConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> builder)
    {
        builder.ToTable("membership_roles", table => table.HasComment("IAM role assignments owned by memberships."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new MembershipRoleId(x))
            .ValueGeneratedNever()
            .HasMaxLength(160)
            .HasComment("Membership role assignment identifier.");
        builder.Property(x => x.MembershipId)
            .HasConversion(x => x.Id, x => new MembershipId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Owning membership identifier.");
        builder.Property(x => x.RoleId)
            .HasConversion(x => x.Id, x => new RoleId(x))
            .IsRequired()
            .HasMaxLength(64)
            .HasComment("Assigned role identifier.");

        builder.HasIndex(x => new { x.MembershipId, x.RoleId }).IsUnique();
    }
}

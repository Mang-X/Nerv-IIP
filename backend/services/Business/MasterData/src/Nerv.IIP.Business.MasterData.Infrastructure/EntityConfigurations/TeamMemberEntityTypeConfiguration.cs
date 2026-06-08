using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class TeamMemberEntityTypeConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members", tableBuilder =>
            tableBuilder.HasComment("Business master data team membership facts that relate teams to IAM users."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Team member aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the team membership.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the team membership is valid.");
        builder.Property(x => x.TeamCode).HasColumnName("team_code").IsRequired().HasMaxLength(100).HasComment("Team code that the IAM user belongs to.");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(100).HasComment("IAM user id assigned to the team.");
        builder.Property(x => x.IsLeader).HasColumnName("is_leader").IsRequired().HasComment("Flag indicating whether this member leads the team.");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired().HasComment("Local business date when the membership starts.");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasComment("Optional local business date when the membership ends.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Soft delete flag for removed team memberships.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the team membership was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the team membership was last updated.");
        builder.Ignore(x => x.Code);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TeamCode, x.UserId, x.EffectiveFrom }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TeamCode, x.Disabled });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.UserId, x.Disabled });
    }
}

using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class PersonnelSkillEntityTypeConfiguration : IEntityTypeConfiguration<PersonnelSkill>
{
    public void Configure(EntityTypeBuilder<PersonnelSkill> builder)
    {
        builder.ToTable("personnel_skills", tableBuilder =>
            tableBuilder.HasComment("Business master data personnel skill assignments with validity dates."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Personnel skill aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the skill assignment.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the skill assignment is valid.");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(100).HasComment("IAM user id assigned to the skill.");
        builder.Property(x => x.SkillCode).HasColumnName("skill_code").IsRequired().HasMaxLength(100).HasComment("Skill code assigned to the user.");
        builder.Property(x => x.Level).HasColumnName("level").IsRequired().HasMaxLength(80).HasComment("Skill proficiency level.");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired().HasComment("First calendar date when the skill assignment is effective.");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to").IsRequired().HasComment("Last calendar date when the skill assignment is effective.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the skill assignment from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the skill assignment was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the skill assignment was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.UserId, x.SkillCode, x.EffectiveFrom }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Disabled });
        builder.HasIndex(x => new { x.SkillCode, x.Disabled });
    }
}

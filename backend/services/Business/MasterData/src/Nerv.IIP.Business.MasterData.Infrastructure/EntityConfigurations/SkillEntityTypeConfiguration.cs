using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class SkillEntityTypeConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("skills", tableBuilder =>
            tableBuilder.HasComment("Business master data skill catalog definitions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Skill catalog aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the skill.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the skill is valid.");
        builder.Property(x => x.SkillCode).HasColumnName("skill_code").IsRequired().HasMaxLength(100).HasComment("Business unique skill code.");
        builder.Property(x => x.SkillName).HasColumnName("skill_name").IsRequired().HasMaxLength(200).HasComment("Skill display name.");
        builder.Property(x => x.GroupName).HasColumnName("group_name").IsRequired().HasMaxLength(100).HasComment("Skill group name for catalog organization.");
        builder.Property(x => x.RequiresCertification).HasColumnName("requires_certification").IsRequired().HasComment("Whether this skill requires certification evidence.");
        builder.Property(x => x.ValidityMonths).HasColumnName("validity_months").HasComment("Optional certification validity period in months.");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).HasComment("Optional skill description.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the skill from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the skill was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the skill was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkillCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.GroupName, x.Disabled });
        builder.HasIndex(x => x.Disabled);
    }
}

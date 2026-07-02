using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class CorrectiveActionEntityTypeConfiguration : IEntityTypeConfiguration<CorrectiveAction>
{
    public void Configure(EntityTypeBuilder<CorrectiveAction> builder)
    {
        builder.ToTable("corrective_actions", tableBuilder =>
            tableBuilder.HasComment("Quality CAPA corrective and preventive action lifecycle facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("CAPA aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the CAPA.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the CAPA is managed.");
        builder.Property(x => x.CapaCode).HasColumnName("capa_code").IsRequired().HasMaxLength(100).HasComment("Human-readable CAPA code.");
        builder.Property(x => x.SourceNcrId).HasColumnName("source_ncr_id").HasMaxLength(150).HasComment("Optional source NCR id that triggered this CAPA.");
        builder.Property(x => x.RootCause).HasColumnName("root_cause").IsRequired().HasMaxLength(1000).HasComment("Root cause analysis summary such as 5Why or fishbone result.");
        builder.Property(x => x.ContainmentAction).HasColumnName("containment_action").IsRequired().HasMaxLength(1000).HasComment("Immediate containment action taken to control nonconforming output.");
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired().HasMaxLength(150).HasComment("CAPA owner user public id.");
        builder.Property(x => x.DueAtUtc).HasColumnName("due_at_utc").IsRequired().HasComment("UTC due time for CAPA completion.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("CAPA lifecycle status.");
        builder.Property(x => x.EffectivenessVerifiedByUserId).HasColumnName("effectiveness_verified_by_user_id").HasMaxLength(150).HasComment("User id that verified CAPA effectiveness.");
        builder.Property(x => x.EffectivenessResult).HasColumnName("effectiveness_result").HasMaxLength(1000).HasComment("Effectiveness verification result.");
        builder.Property(x => x.EffectivenessVerifiedAtUtc).HasColumnName("effectiveness_verified_at_utc").HasComment("UTC time when effectiveness was verified.");
        builder.Property(x => x.ClosedByUserId).HasColumnName("closed_by_user_id").HasMaxLength(150).HasComment("User id that closed the CAPA.");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc").HasComment("UTC time when CAPA was closed.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when CAPA was opened.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when CAPA was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CapaCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceNcrId });
        builder.HasMany(x => x.Actions)
            .WithOne()
            .HasForeignKey(x => x.CorrectiveActionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CorrectiveActionItemEntityTypeConfiguration : IEntityTypeConfiguration<CorrectiveActionItem>
{
    public void Configure(EntityTypeBuilder<CorrectiveActionItem> builder)
    {
        builder.ToTable("corrective_action_items", tableBuilder =>
            tableBuilder.HasComment("Quality CAPA containment, corrective and preventive action items."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("CAPA action item id.");
        builder.Property(x => x.CorrectiveActionId).HasColumnName("corrective_action_id").IsRequired().HasComment("Owning CAPA id.");
        builder.Property(x => x.ActionType).HasColumnName("action_type").IsRequired().HasMaxLength(50).HasComment("CAPA action type: containment, corrective or preventive.");
        builder.Property(x => x.Description).HasColumnName("description").IsRequired().HasMaxLength(1000).HasComment("CAPA action description.");
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired().HasMaxLength(150).HasComment("Action owner user public id.");
        builder.Property(x => x.DueAtUtc).HasColumnName("due_at_utc").IsRequired().HasComment("UTC due time for this CAPA action.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("CAPA action item status.");
        builder.Property(x => x.CompletedByUserId).HasColumnName("completed_by_user_id").HasMaxLength(150).HasComment("User id that completed this CAPA action item.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC time when this CAPA action item was completed.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when this action item was created.");
        builder.HasIndex(x => new { x.CorrectiveActionId, x.ActionType });
    }
}

using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ShiftHandoverEntityTypeConfiguration : IEntityTypeConfiguration<ShiftHandover>
{
    public void Configure(EntityTypeBuilder<ShiftHandover> builder)
    {
        builder.ToTable("shift_handovers", tableBuilder =>
            tableBuilder.HasComment("MES shift handover facts carrying open production, quality, material and equipment issues between teams."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Shift handover aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the shift handover.");
        builder.Property(x => x.HandoverNo).HasColumnName("handover_no").IsRequired().HasMaxLength(100).HasComment("MES shift handover number allocated by the service numbering counter.");
        builder.Property(x => x.ShiftId).HasColumnName("shift_id").IsRequired().HasMaxLength(100).HasComment("MasterData shift public id.");
        builder.Property(x => x.TeamId).HasColumnName("team_id").IsRequired().HasMaxLength(100).HasComment("MasterData team public id handing over the shift.");
        builder.Property(x => x.HandoverStatus).HasColumnName("handover_status").IsRequired().HasMaxLength(30).HasComment("Shift handover lifecycle status.");
        builder.Property(x => x.OpenIssueCount).HasColumnName("open_issue_count").IsRequired().HasComment("Number of open issues captured when the handover was created.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the handover was created.");
        builder.Property(x => x.AcceptedAtUtc).HasColumnName("accepted_at_utc").HasComment("UTC time when the receiving team accepted the handover.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.HandoverNo })
            .IsUnique()
            .HasDatabaseName("ux_shift_handovers_scope_handover_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ShiftId, x.CreatedAtUtc })
            .HasDatabaseName("ix_shift_handovers_scope_shift_time");
    }
}

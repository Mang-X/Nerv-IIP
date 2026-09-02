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
        builder.Property(x => x.ShiftId).HasColumnName("shift_id").IsRequired().HasMaxLength(100).HasComment("MasterData shift public id (e.g. EARLY / MIDDLE); the shift dimension only, never a team code.");
        builder.Property(x => x.TeamId).HasColumnName("team_id").IsRequired().HasMaxLength(100).HasComment("MasterData team public id (e.g. TEAM-WB-MC-A) handing over the shift; a code, never a display name.");
        builder.Property(x => x.TeamName).HasColumnName("team_name").HasMaxLength(200).HasComment("Display name of the handing-over team captured at handover time; snapshot so the read face needs no MasterData call.");
        builder.Property(x => x.OutgoingUserId).HasColumnName("outgoing_user_id").HasMaxLength(200).HasComment("Identity of the worker handing the shift over.");
        builder.Property(x => x.OutgoingUserName).HasColumnName("outgoing_user_name").HasMaxLength(200).HasComment("Display name of the outgoing worker captured at handover time; snapshot so the read face needs no directory call.");
        builder.Property(x => x.IncomingUserId).HasColumnName("incoming_user_id").HasMaxLength(200).HasComment("Identity of the worker taking the shift over; written when the handover is accepted.");
        builder.Property(x => x.IncomingUserName).HasColumnName("incoming_user_name").HasMaxLength(200).HasComment("Display name of the incoming worker captured at acceptance time.");
        builder.Property(x => x.HandoverStatus).HasColumnName("handover_status").IsRequired().HasMaxLength(30).HasComment("Shift handover lifecycle status.");
        builder.Property(x => x.OpenIssueCount).HasColumnName("open_issue_count").IsRequired().HasComment("Environment-level count of still-open shop-floor facts derived when the handover was created; not the number of shift_handover_open_issues rows.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the handover was created.");
        builder.Property(x => x.AcceptedAtUtc).HasColumnName("accepted_at_utc").HasComment("UTC time when the receiving team accepted the handover.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.HandoverNo })
            .IsUnique()
            .HasDatabaseName("ux_shift_handovers_scope_handover_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ShiftId, x.CreatedAtUtc })
            .HasDatabaseName("ix_shift_handovers_scope_shift_time");
        builder.HasMany(x => x.WipItems)
            .WithOne()
            .HasForeignKey("ShiftHandoverId")
            .HasConstraintName("fk_shift_handover_wip_items_handovers")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.WipItems).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.UnfinishedWorkOrders)
            .WithOne()
            .HasForeignKey("ShiftHandoverId")
            .HasConstraintName("fk_shift_handover_unfinished_work_orders_handovers")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.UnfinishedWorkOrders).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.OpenIssues)
            .WithOne()
            .HasForeignKey("ShiftHandoverId")
            .HasConstraintName("fk_shift_handover_open_issues_handovers")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.OpenIssues).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ShiftHandoverWipItemEntityTypeConfiguration : IEntityTypeConfiguration<ShiftHandoverWipItem>
{
    public void Configure(EntityTypeBuilder<ShiftHandoverWipItem> builder)
    {
        builder.ToTable("shift_handover_wip_items", tableBuilder =>
        {
            tableBuilder.HasComment("MES WIP count lines frozen at shift handover time; never recomputed from work orders.");
            tableBuilder.HasCheckConstraint("ck_shift_handover_wip_items_quantity", "quantity >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Shift handover WIP count line id.");
        builder.Property<ShiftHandoverId>("ShiftHandoverId")
            .HasColumnName("shift_handover_id")
            .IsRequired()
            .HasComment("Owning shift handover aggregate id.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order business id the WIP quantity belongs to.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").HasMaxLength(100).HasComment("Operation task the WIP sits on; null when counted at work-order granularity.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("WIP quantity counted at handover time.");
        builder.HasIndex("ShiftHandoverId")
            .HasDatabaseName("ix_shift_handover_wip_items_handover");
    }
}

public sealed class ShiftHandoverUnfinishedWorkOrderEntityTypeConfiguration : IEntityTypeConfiguration<ShiftHandoverUnfinishedWorkOrder>
{
    public void Configure(EntityTypeBuilder<ShiftHandoverUnfinishedWorkOrder> builder)
    {
        builder.ToTable("shift_handover_unfinished_work_orders", tableBuilder =>
        {
            tableBuilder.HasComment("MES unfinished work orders carried into the next shift, with progress frozen at handover time.");
            tableBuilder.HasCheckConstraint(
                "ck_shift_handover_unfinished_work_orders_progress",
                "planned_quantity > 0 AND completed_quantity >= 0 AND completed_quantity < planned_quantity");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Shift handover unfinished work-order line id.");
        builder.Property<ShiftHandoverId>("ShiftHandoverId")
            .HasColumnName("shift_handover_id")
            .IsRequired()
            .HasComment("Owning shift handover aggregate id.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order business id carried over to the incoming team.");
        builder.Property(x => x.PlannedQuantity).HasColumnName("planned_quantity").HasPrecision(18, 6).IsRequired().HasComment("Work-order planned quantity captured at handover time.");
        builder.Property(x => x.CompletedQuantity).HasColumnName("completed_quantity").HasPrecision(18, 6).IsRequired().HasComment("Work-order completed quantity captured at handover time.");
        builder.Property(x => x.WorkOrderStatus).HasColumnName("work_order_status").IsRequired().HasMaxLength(30).HasComment("Work-order status captured at handover time.");
        builder.HasIndex("ShiftHandoverId")
            .HasDatabaseName("ix_shift_handover_unfinished_work_orders_handover");
    }
}

public sealed class ShiftHandoverOpenIssueEntityTypeConfiguration : IEntityTypeConfiguration<ShiftHandoverOpenIssue>
{
    public void Configure(EntityTypeBuilder<ShiftHandoverOpenIssue> builder)
    {
        builder.ToTable("shift_handover_open_issues", tableBuilder =>
        {
            tableBuilder.HasComment("MES equipment and quality problems handed over unresolved to the incoming team.");
            tableBuilder.HasCheckConstraint("ck_shift_handover_open_issues_category", "category IN ('Equipment', 'Quality')");
            tableBuilder.HasCheckConstraint("ck_shift_handover_open_issues_severity", "severity IN ('Low', 'Medium', 'High')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Shift handover open issue id.");
        builder.Property<ShiftHandoverId>("ShiftHandoverId")
            .HasColumnName("shift_handover_id")
            .IsRequired()
            .HasComment("Owning shift handover aggregate id.");
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<string>().IsRequired().HasMaxLength(20).HasComment("Open issue category: Equipment or Quality.");
        builder.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>().IsRequired().HasMaxLength(20).HasComment("Severity judged by the outgoing team: Low, Medium or High.");
        builder.Property(x => x.Description).HasColumnName("description").IsRequired().HasMaxLength(1000).HasComment("What the incoming team has to deal with, in the outgoing team's own words.");
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id").HasMaxLength(100).HasComment("Optional business id of the originating fact such as a downtime event or defect record.");
        builder.HasIndex("ShiftHandoverId")
            .HasDatabaseName("ix_shift_handover_open_issues_handover");
    }
}

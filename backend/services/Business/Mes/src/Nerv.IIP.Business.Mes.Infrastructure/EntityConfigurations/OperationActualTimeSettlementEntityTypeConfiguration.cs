using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class OperationActualTimeSettlementEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationActualTimeSettlement>
{
    public void Configure(EntityTypeBuilder<OperationActualTimeSettlement> builder)
    {
        builder.ToTable("operation_actual_time_settlements", tableBuilder =>
        {
            tableBuilder.HasComment("Immutable MES operation actual-time settlement revisions and their void lifecycle.");
            tableBuilder.HasCheckConstraint(
                "ck_operation_actual_time_settlements_revision_positive",
                "revision > 0");
            tableBuilder.HasCheckConstraint(
                "ck_operation_actual_time_settlements_ticks_nonnegative",
                "actual_labor_ticks >= 0 AND actual_machine_ticks >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_operation_actual_time_settlements_void_order",
                "voided_at_utc IS NULL OR voided_at_utc >= completed_at_utc");
            tableBuilder.HasCheckConstraint(
                "ck_operation_actual_time_settlements_machine_fact",
                "(machine_time_status = 'Available' AND device_asset_id IS NOT NULL AND billable_machine_ticks IS NOT NULL AND billable_machine_ticks >= 0 AND machine_time_basis_code = 'single-device-active-minus-explicit-pause-v1') OR " +
                "(machine_time_status IN ('NotApplicable', 'Unavailable') AND device_asset_id IS NULL AND billable_machine_ticks IS NULL AND machine_time_basis_code IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator()
            .HasComment("Actual-time settlement revision identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100)
            .HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100)
            .HasComment("Environment id for the settlement.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100)
            .HasComment("MES work order id frozen by the settlement.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100)
            .HasComment("MES operation task id frozen by the settlement.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100)
            .HasComment("MES work center id frozen by the settlement.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsRequired()
            .HasComment("Positive monotonic actual-time settlement business revision.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").IsRequired()
            .HasComment("Operation completion time in UTC frozen by this settlement.");
        builder.Property(x => x.ActualLaborTicks).HasColumnName("actual_labor_ticks").IsRequired()
            .HasComment("Nonnegative actual labor duration in .NET ticks frozen by this settlement.");
        builder.Property(x => x.ActualMachineTicks).HasColumnName("actual_machine_ticks").IsRequired()
            .HasComment("Nonnegative actual machine duration in .NET ticks frozen by this settlement.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").HasMaxLength(100)
            .HasComment("Single MasterData device asset frozen for available billable machine time; null otherwise.");
        builder.Property(x => x.MachineTimeStatus).HasColumnName("machine_time_status")
            .HasConversion<string>().IsRequired().HasMaxLength(32)
            .HasComment("Billable machine-time fact state: Available, NotApplicable, or Unavailable.");
        builder.Property(x => x.BillableMachineTicks).HasColumnName("billable_machine_ticks")
            .HasComment("Nullable nonnegative billable machine duration; zero is authoritative only while status is Available.");
        builder.Property(x => x.MachineTimeBasisCode).HasColumnName("machine_time_basis_code").HasMaxLength(100)
            .HasComment("Governed calculation basis for available billable machine time; null for non-available facts.");
        builder.Property(x => x.VoidedAtUtc).HasColumnName("voided_at_utc")
            .HasComment("UTC time when the settlement was voided by completion-report reversal; null while active.");
        builder.HasAlternateKey(x => new
        {
            x.Id,
            x.OrganizationId,
            x.EnvironmentId,
            x.WorkOrderId,
            x.OperationTaskId,
        })
            .HasName("ak_operation_actual_time_settlements_id_scope_task");
        builder.HasOne<OperationTask>()
            .WithMany()
            .HasPrincipalKey(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.OperationTaskIdValue,
                x.WorkOrderId,
            })
            .HasForeignKey(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.OperationTaskId,
                x.WorkOrderId,
            })
            .HasConstraintName("fk_operation_actual_time_settlements_operation_tasks")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.CoveredReports)
            .WithOne()
            .HasPrincipalKey(x => new
            {
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkOrderId,
                x.OperationTaskId,
            })
            .HasForeignKey(x => new
            {
                x.SettlementId,
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkOrderId,
                x.OperationTaskId,
            })
            .HasConstraintName("fk_operation_actual_time_settlement_reports_settlement")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.CoveredReports).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new
        {
            x.OrganizationId,
            x.EnvironmentId,
            x.OperationTaskId,
            x.Revision,
        })
            .IsUnique()
            .HasDatabaseName("ux_operation_actual_time_settlements_scope_task_revision");
        builder.HasIndex(x => new
        {
            x.OrganizationId,
            x.EnvironmentId,
            x.OperationTaskId,
            x.WorkOrderId,
        })
            .HasDatabaseName("ix_operation_actual_time_settlements_scope_task");
    }
}

public sealed class OperationActualTimeSettlementReportEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationActualTimeSettlementReport>
{
    public void Configure(EntityTypeBuilder<OperationActualTimeSettlementReport> builder)
    {
        builder.ToTable("operation_actual_time_settlement_reports", tableBuilder =>
            tableBuilder.HasComment("Relational production-report lineage covered by one MES actual-time settlement revision."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator()
            .HasComment("Settlement-to-report lineage identifier.");
        builder.Property(x => x.SettlementId).HasColumnName("settlement_id").IsRequired()
            .HasComment("Owning actual-time settlement revision identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100)
            .HasComment("Organization tenant id copied for report foreign-key isolation.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100)
            .HasComment("Environment id copied for report foreign-key isolation.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100)
            .HasComment("MES work order id copied to enforce report ownership.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100)
            .HasComment("MES operation task id copied to enforce report ownership.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100)
            .HasComment("Covered MES production report number.");
        builder.HasOne<ProductionReport>()
            .WithMany()
            .HasPrincipalKey(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.ReportNo,
                x.WorkOrderId,
                x.OperationTaskId,
            })
            .HasForeignKey(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.ReportNo,
                x.WorkOrderId,
                x.OperationTaskId,
            })
            .HasConstraintName("fk_operation_actual_time_settlement_reports_production_reports")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.SettlementId, x.ReportNo })
            .IsUnique()
            .HasDatabaseName("ux_operation_actual_time_settlement_reports_settlement_report");
        builder.HasIndex(x => new
        {
            x.SettlementId,
            x.OrganizationId,
            x.EnvironmentId,
            x.WorkOrderId,
            x.OperationTaskId,
        })
            .HasDatabaseName("ix_operation_actual_time_settlement_reports_settlement_owner");
        builder.HasIndex(x => new
        {
            x.OrganizationId,
            x.EnvironmentId,
            x.ReportNo,
            x.WorkOrderId,
            x.OperationTaskId,
        })
            .HasDatabaseName("ix_operation_actual_time_settlement_reports_report_owner");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class OperationMachineOverheadSettlementEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationMachineOverheadSettlement>
{
    public void Configure(EntityTypeBuilder<OperationMachineOverheadSettlement> builder)
    {
        builder.ToTable("operation_machine_overhead_settlements", table =>
        {
            table.HasComment("Immutable ERP machine-overhead snapshot priced from authoritative MES machine time and a frozen monthly rate.");
            table.HasCheckConstraint(
                "ck_operation_machine_overhead_settlements_fact",
                "(applicability = 'Applicable' AND device_asset_id IS NOT NULL AND actual_machine_ticks >= 0 AND actual_machine_hours IS NOT NULL AND machine_time_basis_code IS NOT NULL) OR (applicability = 'NotApplicable' AND device_asset_id IS NULL AND actual_machine_ticks IS NULL AND actual_machine_hours IS NULL AND machine_time_basis_code IS NULL AND fixed_hourly_rate = 0 AND variable_hourly_rate = 0 AND fixed_amount = 0 AND variable_amount = 0 AND amount = 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Machine-overhead settlement snapshot id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        ConfigureSnapshot(builder);
        builder.HasOne<WorkCenterMachineOverheadRate>().WithMany()
            .HasForeignKey(x => x.WorkCenterMachineOverheadRateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.SettlementRevision })
            .IsUnique().HasDatabaseName("ux_op_machine_overhead_settlements_identity");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_operation_machine_overhead_settlements_work_order");
    }

    internal static void ConfigureSnapshot<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<string>("WorkOrderId").HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order public identifier.");
        builder.Property<string>("OperationTaskId").HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property<string>("WorkCenterId").HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MES work-center public identifier used for rate selection.");
        builder.Property<long>("SettlementRevision").HasColumnName("settlement_revision").HasComment("Monotonic MES actual-time settlement revision.");
        builder.Property<DateTimeOffset>("CompletedAtUtc").HasColumnName("completed_at_utc").HasComment("UTC completion instant used to select the accounting period.");
        builder.Property<MachineOverheadApplicability>("Applicability").HasColumnName("applicability").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Frozen applicable or explicitly not-applicable ERP rate status.");
        builder.Property<string?>("DeviceAssetId").HasColumnName("device_asset_id").HasMaxLength(100).HasComment("Authoritative MES execution-device snapshot when machine time is available.");
        builder.Property<long?>("ActualMachineTicks").HasColumnName("actual_machine_ticks").HasComment("Lossless billable machine duration in TimeSpan ticks.");
        builder.Property<decimal?>("ActualMachineHours").HasColumnName("actual_machine_hours").HasPrecision(24, 12).HasComment("Display hours derived from ticks; pricing uses ticks directly.");
        builder.Property<string?>("MachineTimeBasisCode").HasColumnName("machine_time_basis_code").HasMaxLength(100).HasComment("MES authoritative machine-time calculation basis.");
        builder.Property<WorkCenterMachineOverheadRateId>("WorkCenterMachineOverheadRateId").HasColumnName("work_center_machine_overhead_rate_id").HasComment("Frozen monthly rate revision id.");
        builder.Property<string>("AccountingPeriodCode").HasColumnName("accounting_period_code").IsRequired().HasMaxLength(50).HasComment("Frozen accounting period selected by completion instant.");
        builder.Property<int>("RateRevision").HasColumnName("rate_revision").HasComment("Frozen monthly machine-overhead rate revision number.");
        builder.Property<string>("CurrencyCode").HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength().HasComment("Frozen three-letter currency code.");
        builder.Property<decimal>("FixedHourlyRate").HasColumnName("fixed_hourly_rate").HasPrecision(18, 6).HasComment("Frozen fixed machine-overhead hourly rate.");
        builder.Property<decimal>("VariableHourlyRate").HasColumnName("variable_hourly_rate").HasPrecision(18, 6).HasComment("Frozen variable machine-overhead hourly rate.");
        builder.Property<decimal>("FixedAmount").HasColumnName("fixed_amount").HasPrecision(18, 6).HasComment("Fixed machine overhead rounded to six decimals with ToEven.");
        builder.Property<decimal>("VariableAmount").HasColumnName("variable_amount").HasPrecision(18, 6).HasComment("Variable machine overhead rounded to six decimals with ToEven.");
        builder.Property<decimal>("Amount").HasColumnName("amount").HasPrecision(18, 6).HasComment("Total machine overhead independently rounded to six decimals with ToEven.");
        builder.Property<string>("SourceEventId").HasColumnName("source_event_id").IsRequired().HasMaxLength(256).HasComment("First MES V2 event id that established the snapshot.");
        builder.Property<string>("PayloadHash").HasColumnName("payload_hash").IsRequired().HasMaxLength(64).IsFixedLength().HasComment("SHA-256 of canonical MES machine business fields.");
    }
}

public sealed class OperationMachineOverheadSettlementVoidEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationMachineOverheadSettlementVoid>
{
    public void Configure(EntityTypeBuilder<OperationMachineOverheadSettlementVoid> builder)
    {
        builder.ToTable("operation_machine_overhead_settlement_voids", table =>
            table.HasComment("Append-only exact reversal of an immutable operation machine-overhead snapshot."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Machine-overhead settlement void id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        OperationMachineOverheadSettlementEntityTypeConfiguration.ConfigureSnapshot(builder);
        builder.Property(x => x.VoidedAtUtc).HasColumnName("voided_at_utc").HasComment("UTC MES void occurrence instant.");
        builder.HasOne<WorkCenterMachineOverheadRate>().WithMany()
            .HasForeignKey(x => x.WorkCenterMachineOverheadRateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.SettlementRevision })
            .IsUnique().HasDatabaseName("ux_op_machine_overhead_settlement_voids_identity");
    }
}

public sealed class OperationMachineOverheadSettlementStateEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationMachineOverheadSettlementState>
{
    public void Configure(EntityTypeBuilder<OperationMachineOverheadSettlementState> builder)
    {
        builder.ToTable("operation_machine_overhead_settlement_states", table =>
            table.HasComment("Monotonic ERP machine-overhead processing watermark and active revision for one MES operation."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Machine-overhead settlement state id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property(x => x.HighestRevision).HasColumnName("highest_revision").HasComment("Highest settlement or void revision observed.");
        builder.Property(x => x.ActiveRevision).HasColumnName("active_revision").HasComment("Currently active revision, or null after a void.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .IsUnique().HasDatabaseName("ux_operation_machine_overhead_settlement_states_operation");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class WorkCenterMachineOverheadReconciliationEntityTypeConfiguration
    : IEntityTypeConfiguration<WorkCenterMachineOverheadReconciliation>
{
    public void Configure(EntityTypeBuilder<WorkCenterMachineOverheadReconciliation> builder)
    {
        builder.ToTable("work_center_machine_overhead_reconciliations", table =>
        {
            table.HasComment("Append-only monthly work-center machine-overhead pool reconciliation and close readiness fact.");
            table.HasCheckConstraint("ck_wc_machine_overhead_reconciliations_amounts",
                "actual_fixed_overhead_amount >= 0 AND actual_variable_overhead_amount >= 0 AND actual_total_overhead_amount = actual_fixed_overhead_amount + actual_variable_overhead_amount AND applied_machine_ticks >= 0 AND applied_machine_hours = round(applied_machine_ticks / 36000000000.0, 12) AND applied_fixed_amount >= 0 AND applied_variable_amount >= 0 AND applied_total_amount >= 0 AND applied_rounding_difference_amount = applied_total_amount - applied_fixed_amount - applied_variable_amount AND under_over_applied_fixed_amount = actual_fixed_overhead_amount - applied_fixed_amount AND under_over_applied_variable_amount = actual_variable_overhead_amount - applied_variable_amount AND under_over_applied_total_amount = actual_total_overhead_amount - applied_total_amount AND unallocated_fixed_overhead_amount = greatest(under_over_applied_fixed_amount, 0) AND over_applied_fixed_overhead_amount = greatest(-under_over_applied_fixed_amount, 0)");
            table.HasCheckConstraint("ck_wc_machine_overhead_reconciliations_downtime",
                "abnormal_downtime_hours = round(abnormal_downtime_ticks / 36000000000.0, 12) AND ((abnormal_downtime_ticks = 0 AND abnormal_downtime_disposition = 'None') OR (abnormal_downtime_ticks > 0 AND abnormal_downtime_disposition IN ('Pending', 'PeriodExpense')))");
            table.HasCheckConstraint("ck_wc_machine_overhead_reconciliations_audit",
                "revision > 0 AND rate_revision > 0 AND currency_code ~ '^[A-Z]{3}$'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Machine-overhead reconciliation revision id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("Work center whose actual pool is reconciled.");
        builder.Property(x => x.AccountingPeriodCode).HasColumnName("accounting_period_code").IsRequired().HasMaxLength(50).HasComment("Accounting period owning this reconciliation.");
        builder.Property(x => x.WorkCenterMachineOverheadRateId).HasColumnName("work_center_machine_overhead_rate_id").HasComment("Predetermined monthly rate revision used by this reconciliation.");
        builder.Property(x => x.RateRevision).HasColumnName("rate_revision").HasComment("Frozen predetermined rate revision number.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength().HasComment("Currency shared by actual pool, rate, and applied settlements.");
        Amount(builder, x => x.ActualFixedOverheadAmount, "actual_fixed_overhead_amount", "Actual fixed manufacturing-overhead pool.");
        Amount(builder, x => x.ActualVariableOverheadAmount, "actual_variable_overhead_amount", "Actual variable manufacturing-overhead pool.");
        Amount(builder, x => x.ActualTotalOverheadAmount, "actual_total_overhead_amount", "Actual fixed plus variable manufacturing-overhead pool.");
        builder.Property(x => x.AppliedMachineTicks).HasColumnName("applied_machine_ticks").HasComment("Lossless active billable machine ticks allocated to products; abnormal downtime is excluded.");
        builder.Property(x => x.AppliedMachineHours).HasColumnName("applied_machine_hours").HasPrecision(24, 12).HasComment("Display hours derived from active applied ticks.");
        Amount(builder, x => x.AppliedFixedAmount, "applied_fixed_amount", "Fixed overhead already allocated by active settlements.");
        Amount(builder, x => x.AppliedVariableAmount, "applied_variable_amount", "Variable overhead already allocated by active settlements.");
        Amount(builder, x => x.AppliedTotalAmount, "applied_total_amount", "Total overhead already allocated by active settlements.");
        Amount(builder, x => x.AppliedRoundingDifferenceAmount, "applied_rounding_difference_amount", "Difference caused by independently rounded total versus fixed and variable applied amounts.");
        Amount(builder, x => x.UnderOverAppliedFixedAmount, "under_over_applied_fixed_amount", "Signed actual-minus-applied fixed overhead variance.");
        Amount(builder, x => x.UnderOverAppliedVariableAmount, "under_over_applied_variable_amount", "Signed actual-minus-applied variable overhead variance.");
        Amount(builder, x => x.UnderOverAppliedTotalAmount, "under_over_applied_total_amount", "Signed actual-minus-applied total overhead variance.");
        Amount(builder, x => x.UnallocatedFixedOverheadAmount, "unallocated_fixed_overhead_amount", "Positive fixed overhead left unallocated at low utilization.");
        Amount(builder, x => x.OverAppliedFixedOverheadAmount, "over_applied_fixed_overhead_amount", "Positive reverse fixed variance when applied overhead exceeds the actual pool.");
        builder.Property(x => x.AbnormalDowntimeTicks).HasColumnName("abnormal_downtime_ticks").HasComment("Abnormal downtime ticks excluded from product allocation.");
        builder.Property(x => x.AbnormalDowntimeHours).HasColumnName("abnormal_downtime_hours").HasPrecision(24, 12).HasComment("Display hours derived from abnormal downtime ticks.");
        builder.Property(x => x.AbnormalDowntimeDisposition).HasColumnName("abnormal_downtime_disposition").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("None, Pending, or PeriodExpense close disposition.");
        builder.Property(x => x.Revision).HasColumnName("revision").HasComment("Monotonic append-only reconciliation revision within work center and period.");
        builder.Property(x => x.RecordedBy).HasColumnName("recorded_by").IsRequired().HasMaxLength(200).HasComment("Canonical authenticated actor recording the actual pool.");
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").IsRequired().HasMaxLength(300).HasComment("Auditable source ledger, import, or worksheet reference.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500).HasComment("Business reason for this immutable revision.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC instant when this revision was recorded.");
        builder.Ignore(x => x.IsReadyForClose);
        builder.HasOne<WorkCenterMachineOverheadRate>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.WorkCenterMachineOverheadRateId,
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkCenterId,
                x.AccountingPeriodCode,
                x.RateRevision,
            })
            .HasPrincipalKey(x => new
            {
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkCenterId,
                x.AccountingPeriodCode,
                x.Revision,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterId, x.AccountingPeriodCode, x.Revision })
            .IsUnique().IsDescending(false, false, false, false, true)
            .HasDatabaseName("ux_wc_machine_overhead_reconciliations_scope_revision");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.AccountingPeriodCode, x.WorkCenterId })
            .HasDatabaseName("ix_wc_machine_overhead_reconciliations_period");
    }

    private static void Amount(
        EntityTypeBuilder<WorkCenterMachineOverheadReconciliation> builder,
        System.Linq.Expressions.Expression<Func<WorkCenterMachineOverheadReconciliation, decimal>> property,
        string columnName,
        string comment)
        => builder.Property(property).HasColumnName(columnName).HasPrecision(18, 6).HasComment(comment);
}

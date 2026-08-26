using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class WorkCenterMachineOverheadRateEntityTypeConfiguration
    : IEntityTypeConfiguration<WorkCenterMachineOverheadRate>
{
    public void Configure(EntityTypeBuilder<WorkCenterMachineOverheadRate> builder)
    {
        builder.ToTable(
            "work_center_machine_overhead_rates",
            table =>
            {
                table.HasComment("ERP append-only monthly predetermined machine-overhead rate revisions by work center.");
                table.HasCheckConstraint(
                    "ck_wc_machine_overhead_rates_cost_basis",
                    """
                    (applicability = 'Applicable'
                     AND fixed_overhead_budget >= 0
                     AND variable_overhead_budget >= 0
                     AND fixed_overhead_budget + variable_overhead_budget > 0
                     AND normal_capacity_machine_hours > 0
                     AND fixed_hourly_rate = round(fixed_overhead_budget / normal_capacity_machine_hours, 6)
                     AND variable_hourly_rate = round(variable_overhead_budget / normal_capacity_machine_hours, 6)
                     AND total_hourly_rate = fixed_hourly_rate + variable_hourly_rate)
                    OR
                    (applicability = 'NotApplicable'
                     AND fixed_overhead_budget = 0
                     AND variable_overhead_budget = 0
                     AND normal_capacity_machine_hours = 0
                     AND fixed_hourly_rate = 0
                     AND variable_hourly_rate = 0
                     AND total_hourly_rate = 0)
                    """);
                table.HasCheckConstraint(
                    "ck_wc_machine_overhead_rates_currency_revision",
                    "currency_code ~ '^[A-Z]{3}$' AND revision > 0");
            });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseGuidVersion7ValueGenerator()
            .HasComment("Work-center machine-overhead rate revision id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkCenterId)
            .HasColumnName("work_center_id")
            .IsRequired()
            .HasMaxLength(100)
            .HasComment("Work-center public identifier that owns the monthly machine-overhead pool.");
        builder.Property(x => x.AccountingPeriodCode)
            .HasColumnName("accounting_period_code")
            .IsRequired()
            .HasMaxLength(50)
            .HasComment("ERP accounting period code for this monthly rate revision.");
        builder.Property(x => x.Applicability)
            .HasColumnName("applicability")
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasComment("Explicit Applicable or NotApplicable status for machine-overhead allocation.");
        builder.Property(x => x.FixedOverheadBudget)
            .HasColumnName("fixed_overhead_budget")
            .HasPrecision(18, 6)
            .HasComment("Monthly fixed manufacturing-overhead budget for the work center.");
        builder.Property(x => x.VariableOverheadBudget)
            .HasColumnName("variable_overhead_budget")
            .HasPrecision(18, 6)
            .HasComment("Monthly variable manufacturing-overhead budget for the work center.");
        builder.Property(x => x.NormalCapacityMachineHours)
            .HasColumnName("normal_capacity_machine_hours")
            .HasPrecision(18, 6)
            .HasComment("Normal-capacity machine hours excluding planned maintenance; never actual low-load hours.");
        builder.Property(x => x.FixedHourlyRate)
            .HasColumnName("fixed_hourly_rate")
            .HasPrecision(18, 6)
            .HasComment("System-derived fixed overhead budget divided by normal-capacity machine hours.");
        builder.Property(x => x.VariableHourlyRate)
            .HasColumnName("variable_hourly_rate")
            .HasPrecision(18, 6)
            .HasComment("System-derived variable overhead budget divided by normal-capacity machine hours.");
        builder.Property(x => x.TotalHourlyRate)
            .HasColumnName("total_hourly_rate")
            .HasPrecision(18, 6)
            .HasComment("System-derived sum of fixed and variable machine-overhead hourly rates.");
        builder.Property(x => x.CurrencyCode)
            .HasColumnName("currency_code")
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength()
            .HasComment("Normalized three-letter uppercase currency code fixed within the work-center scope.");
        builder.Property(x => x.Revision)
            .HasColumnName("revision")
            .HasComment("Monotonically increasing append-only revision within scope, work center, and accounting period.");
        builder.Property(x => x.ChangedBy)
            .HasColumnName("changed_by")
            .IsRequired()
            .HasMaxLength(200)
            .HasComment("Canonical authenticated actor that configured this immutable revision.");
        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .IsRequired()
            .HasMaxLength(500)
            .HasComment("Auditable business reason for this immutable revision.");
        builder.Property(x => x.ChangedAtUtc)
            .HasColumnName("changed_at_utc")
            .HasComment("UTC audit instant at which this revision was configured.");
        builder.HasIndex(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.WorkCenterId,
                x.AccountingPeriodCode,
                x.Revision,
            })
            .IsUnique()
            .IsDescending(false, false, false, false, true)
            .HasDatabaseName("ux_wc_machine_overhead_rates_scope_period_revision");
    }
}

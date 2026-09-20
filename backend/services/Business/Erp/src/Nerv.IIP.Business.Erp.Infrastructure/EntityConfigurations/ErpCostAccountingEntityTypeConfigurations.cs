using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure.EntityConfigurations;

public sealed class GLAccountEntityTypeConfiguration : IEntityTypeConfiguration<GLAccount>
{
    public void Configure(EntityTypeBuilder<GLAccount> builder)
    {
        builder.ToTable("gl_accounts", table => table.HasComment("ERP general-ledger account hierarchy."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("GL account aggregate id.");
        AddTenant(builder);
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Tenant-unique GL account code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("GL account display name.");
        builder.Property(x => x.Type).HasColumnName("account_type").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Asset, liability, equity, revenue, or expense classification.");
        builder.Property(x => x.ParentCode).HasColumnName("parent_code").HasMaxLength(100).HasComment("Optional parent GL account code in the same tenant.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasAlternateKey(x => new { x.OrganizationId, x.EnvironmentId, x.Code });
    }
    internal static void AddTenant<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<string>("OrganizationId").HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization boundary.");
        builder.Property<string>("EnvironmentId").HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment boundary.");
    }
}

public sealed class WorkOrderCostEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrderCost>
{
    public void Configure(EntityTypeBuilder<WorkOrderCost> builder)
    {
        builder.ToTable("work_order_costs", table => table.HasComment("ERP actual work-order cost accumulation and capitalization fact."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work-order cost aggregate id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order public identifier.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Finished-good SKU code.");
        builder.Property(x => x.SourceNcrId).HasColumnName("source_ncr_id").HasMaxLength(100).HasComment("Quality NCR public id for a rework work-order cost; null for ordinary work orders.");
        builder.Property(x => x.SourceNcrCode).HasColumnName("source_ncr_code").HasMaxLength(100).HasComment("Quality NCR business code retained for rework cost readback.");
        builder.Property(x => x.SourceWorkOrderId).HasColumnName("source_work_order_id").HasMaxLength(100).HasComment("MES source work-order public id for a rework work-order cost.");
        builder.Property(x => x.LaborCurrencyCode).HasColumnName("labor_currency_code").HasMaxLength(3).IsFixedLength().HasComment("Frozen three-letter currency code shared by all priced labor on this work order; no implicit conversion is allowed.");
        builder.Property(x => x.MachineOverheadCurrencyCode).HasColumnName("machine_overhead_currency_code").HasMaxLength(3).IsFixedLength().HasComment("Frozen machine-overhead currency; it must match priced labor when both exist.");
        builder.Property(x => x.CompletedQuantity).HasColumnName("completed_quantity").HasPrecision(18, 6).HasComment("MES good quantity at completion.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("MES completion timestamp.");
        builder.Property(x => x.CapitalizedCost).HasColumnName("capitalized_cost").HasPrecision(18, 6).HasComment("Finished-goods inventory value posted for this work order.");
        builder.Property(x => x.CapitalizedQuantity).HasColumnName("capitalized_quantity").HasPrecision(18, 6).HasComment("Finished-goods quantity posted for this work order.");
        builder.Property(x => x.WipClearedCost).HasColumnName("wip_cleared_cost").HasPrecision(18, 6).HasComment("Cumulative WIP amount cleared by capitalization vouchers.");
        builder.Property(x => x.ExpectedReportCount).HasColumnName("expected_report_count").HasComment("MES completion count of cost-bearing reports.");
        builder.Property(x => x.ReceivedReportCount).HasColumnName("received_report_count").HasComment("Cost-bearing reports received by ERP.");
        builder.Property(x => x.ExpectedMaterialMovementCount).HasColumnName("expected_material_movement_count").HasComment("MES completion count of expected material postings.");
        builder.Property(x => x.ReceivedMaterialMovementCount).HasColumnName("received_material_movement_count").HasComment("Actual Inventory material postings received by ERP.");
        builder.Property(x => x.CapitalizationPublished).HasColumnName("capitalization_published").HasComment("Whether the cost-ready capitalization event has been published.");
        builder.Ignore(x => x.IsRework); builder.Ignore(x => x.LaborCost); builder.Ignore(x => x.MaterialCost); builder.Ignore(x => x.MachineOverheadCost); builder.Ignore(x => x.TotalAccumulatedCost); builder.Ignore(x => x.VarianceCost);
        builder.HasMany(x => x.Details).WithOne().HasForeignKey("WorkOrderCostId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Details).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceNcrId });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceWorkOrderId });
    }
}

public sealed class PendingMaterialCostEntityTypeConfiguration : IEntityTypeConfiguration<PendingMaterialCost>
{
    public void Configure(EntityTypeBuilder<PendingMaterialCost> builder)
    {
        builder.ToTable("pending_material_costs", table => table.HasComment("Order-independent Inventory material cost awaiting its MES report projection.")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Pending material cost id."); GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.MovementId).HasColumnName("movement_id").IsRequired().HasMaxLength(100).HasComment("Inventory movement public id.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100).HasComment("MES report number used for later correlation.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Consumed material SKU.");
        builder.Property(x => x.SignedQuantity).HasColumnName("signed_quantity").HasPrecision(18, 6).HasComment("Positive actual consumption or negative reversal quantity.");
        builder.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 6).HasComment("Inventory moving-average unit cost.");
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at_utc").HasComment("Inventory posting timestamp.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.MovementId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo });
    }
}

public sealed class WorkOrderCostDetailEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrderCostDetail>
{
    public void Configure(EntityTypeBuilder<WorkOrderCostDetail> builder)
    {
        builder.ToTable("work_order_cost_details", table => table.HasComment("ERP auditable labor, material, or machine-overhead cost detail.")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Cost detail id.");
        builder.Property<WorkOrderCostId>("WorkOrderCostId").HasColumnName("work_order_cost_id").IsRequired().HasComment("Owning work-order cost id.");
        builder.Property(x => x.Type).HasColumnName("cost_type").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Labor, material, or machine-overhead cost type.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Public source event document id.");
        builder.Property(x => x.DimensionCode).HasColumnName("dimension_code").IsRequired().HasMaxLength(100).HasComment("Work center or material SKU dimension.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").HasMaxLength(100).HasComment("MES report number for material-to-work-order correlation.");
        builder.Property(x => x.LaborBasis).HasColumnName("labor_basis").HasConversion<string>().HasMaxLength(50).HasComment("Labor costing basis: theoretical report, actual operation, or append-only reversal/replacement.");
        builder.Property(x => x.LaborLineageId).HasColumnName("labor_lineage_id").HasMaxLength(200).HasComment("Stable MES report or operation-settlement lineage for auditable labor replacement and reversal.");
        builder.Property(x => x.MachineOverheadBasis).HasColumnName("machine_overhead_basis").HasConversion<string>().HasMaxLength(50).HasComment("Machine-overhead basis: actual operation, explicit not-applicable, or append-only reversal/supersession.");
        builder.Property(x => x.MachineOverheadLineageId).HasColumnName("machine_overhead_lineage_id").HasMaxLength(200).HasComment("Stable MES operation-settlement lineage for machine-overhead audit.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Labor or machine hours, or material quantity.");
        builder.Property(x => x.Rate).HasColumnName("rate").HasPrecision(18, 6).HasComment("Labor or machine-overhead hourly rate, or moving-average material unit cost.");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6).HasComment("Signed actual cost amount.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasComment("Source fact occurrence timestamp.");
        builder.HasIndex("WorkOrderCostId", nameof(WorkOrderCostDetail.SourceDocumentId)).IsUnique();
    }
}

public sealed class OperationLaborSettlementEntityTypeConfiguration : IEntityTypeConfiguration<OperationLaborSettlement>
{
    public void Configure(EntityTypeBuilder<OperationLaborSettlement> builder)
    {
        builder.ToTable("operation_labor_settlements", table => table.HasComment("Immutable ERP actual-operation labor snapshot priced at a frozen standard work-center rate."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Operation labor settlement snapshot id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order public identifier.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MES work-center public identifier used for rate selection.");
        builder.Property(x => x.SettlementRevision).HasColumnName("settlement_revision").HasComment("Monotonic MES actual-time settlement revision.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("MES operation completion instant used as the rate basis.");
        builder.Property(x => x.ActualLaborTicks).HasColumnName("actual_labor_ticks").HasComment("Lossless MES actual labor duration in TimeSpan ticks.");
        builder.Property(x => x.ActualLaborHours).HasColumnName("actual_labor_hours").HasPrecision(24, 12).HasComment("Actual labor hours derived from the frozen tick value.");
        builder.Property(x => x.WorkCenterCostRateId).HasColumnName("work_center_cost_rate_id").HasComment("Frozen work-center standard labor-rate revision id.");
        builder.Property(x => x.RateRevision).HasColumnName("rate_revision").HasComment("Frozen work-center standard labor-rate revision number.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength().HasComment("Frozen three-letter currency code; no implicit conversion is allowed.");
        builder.Property(x => x.HourlyRate).HasColumnName("hourly_rate").HasPrecision(18, 6).HasComment("Frozen standard labor hourly rate.");
        builder.Property(x => x.RateBasisAtUtc).HasColumnName("rate_basis_at_utc").HasComment("UTC rate basis equal to the MES completion instant.");
        builder.Property(x => x.RateBasis).HasColumnName("rate_basis").IsRequired().HasMaxLength(30).HasComment("Frozen rate basis; currently standard.");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6).HasComment("Actual labor hours multiplied by the frozen standard rate.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").IsRequired().HasMaxLength(256).HasComment("First MES event id that established the immutable snapshot.");
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").IsRequired().HasMaxLength(64).IsFixedLength().HasComment("SHA-256 of canonical business payload fields for conflict detection.");
        builder.HasOne<WorkCenterCostRate>().WithMany().HasForeignKey(x => x.WorkCenterCostRateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.SettlementRevision })
            .IsUnique()
            .HasDatabaseName("ux_operation_labor_settlements_business_identity");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_operation_labor_settlements_work_order");
    }
}

public sealed class OperationLaborReportSnapshotEntityTypeConfiguration : IEntityTypeConfiguration<OperationLaborReportSnapshot>
{
    public void Configure(EntityTypeBuilder<OperationLaborReportSnapshot> builder)
    {
        builder.ToTable("operation_labor_report_snapshots", table => table.HasComment("ERP immutable MES production-report basis used for standard labor and efficiency variance."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Operation labor report snapshot id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order public identifier.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("Frozen MES work-center identifier.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100).HasComment("MES production-report business identifier.");
        builder.Property(x => x.GoodQuantity).HasColumnName("good_quantity").HasPrecision(18, 6).HasComment("Frozen reported good quantity before reversal sign normalization.");
        builder.Property(x => x.ScrapQuantity).HasColumnName("scrap_quantity").HasPrecision(18, 6).HasComment("Frozen reported scrap quantity; excluded from standard labor hours.");
        builder.Property(x => x.ReworkQuantity).HasColumnName("rework_quantity").HasPrecision(18, 6).HasComment("Frozen reported rework quantity; excluded from standard labor hours.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(30).HasComment("Frozen MES output unit of measure.");
        builder.Property(x => x.TheoreticalRatePerHour).HasColumnName("theoretical_rate_per_hour").HasPrecision(18, 6).HasComment("Frozen theoretical good-output rate per labor hour.");
        builder.Property(x => x.HasValidNumericScale).HasColumnName("has_valid_numeric_scale").HasComment("Whether all source decimal facts fit the governed six-digit scale without PostgreSQL coercion.");
        builder.Property(x => x.ReportedAtUtc).HasColumnName("reported_at_utc").HasComment("Original MES production-report UTC timestamp.");
        builder.Property(x => x.IsReversal).HasColumnName("is_reversal").HasComment("Whether this report reverses a prior production report.");
        builder.Property(x => x.ReversedReportNo).HasColumnName("reversed_report_no").HasMaxLength(100).HasComment("Original MES report number for a reversal snapshot.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").IsRequired().HasMaxLength(256).HasComment("MES event id that established this immutable snapshot.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .IsUnique()
            .HasDatabaseName("ux_operation_labor_report_snapshots_scope_report");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.OperationTaskId })
            .HasDatabaseName("ix_operation_labor_report_snapshots_work_order_operation");
    }
}

public sealed class OperationLaborSettlementVoidEntityTypeConfiguration : IEntityTypeConfiguration<OperationLaborSettlementVoid>
{
    public void Configure(EntityTypeBuilder<OperationLaborSettlementVoid> builder)
    {
        builder.ToTable("operation_labor_settlement_voids", table => table.HasComment("Append-only exact reversal of an immutable operation labor settlement snapshot."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Operation labor settlement void id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order public identifier.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("Frozen MES work-center public identifier.");
        builder.Property(x => x.SettlementRevision).HasColumnName("settlement_revision").HasComment("Voided MES settlement revision.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("Original MES completion instant.");
        builder.Property(x => x.VoidedAtUtc).HasColumnName("voided_at_utc").HasComment("MES void occurrence instant.");
        builder.Property(x => x.ActualLaborTicks).HasColumnName("actual_labor_ticks").HasComment("Exact copy of original actual labor ticks.");
        builder.Property(x => x.ActualLaborHours).HasColumnName("actual_labor_hours").HasPrecision(24, 12).HasComment("Exact copy of original actual labor hours.");
        builder.Property(x => x.WorkCenterCostRateId).HasColumnName("work_center_cost_rate_id").HasComment("Exact copy of original frozen rate id.");
        builder.Property(x => x.RateRevision).HasColumnName("rate_revision").HasComment("Exact copy of original frozen rate revision.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength().HasComment("Exact copy of original frozen currency.");
        builder.Property(x => x.HourlyRate).HasColumnName("hourly_rate").HasPrecision(18, 6).HasComment("Exact copy of original frozen hourly rate.");
        builder.Property(x => x.RateBasisAtUtc).HasColumnName("rate_basis_at_utc").HasComment("Exact copy of original rate basis instant.");
        builder.Property(x => x.RateBasis).HasColumnName("rate_basis").IsRequired().HasMaxLength(30).HasComment("Exact copy of original rate basis.");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6).HasComment("Strictly opposite amount of the original settlement.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").IsRequired().HasMaxLength(256).HasComment("MES settlement-void event id.");
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").IsRequired().HasMaxLength(64).IsFixedLength().HasComment("SHA-256 of canonical void business payload fields.");
        builder.HasOne<WorkCenterCostRate>().WithMany().HasForeignKey(x => x.WorkCenterCostRateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.SettlementRevision })
            .IsUnique()
            .HasDatabaseName("ux_operation_labor_settlement_voids_business_identity");
    }
}

public sealed class OperationLaborSettlementStateEntityTypeConfiguration : IEntityTypeConfiguration<OperationLaborSettlementState>
{
    public void Configure(EntityTypeBuilder<OperationLaborSettlementState> builder)
    {
        builder.ToTable("operation_labor_settlement_states", table => table.HasComment("Monotonic ERP processing watermark and active revision for one MES operation task."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Operation labor settlement state id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property(x => x.HighestRevision).HasColumnName("highest_revision").HasComment("Highest settlement or void revision observed for this operation.");
        builder.Property(x => x.ActiveRevision).HasColumnName("active_revision").HasComment("Currently active settlement revision, or null after a void.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .IsUnique()
            .HasDatabaseName("ux_operation_labor_settlement_states_operation");
    }
}

public sealed class OperationLaborCoveredReportEntityTypeConfiguration : IEntityTypeConfiguration<OperationLaborCoveredReport>
{
    public void Configure(EntityTypeBuilder<OperationLaborCoveredReport> builder)
    {
        builder.ToTable("operation_labor_covered_reports", table => table.HasComment("Permanent MES report lineage covered by one operation-level actual labor settlement."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Covered production-report lineage id.");
        GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work-order public identifier.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation-task public identifier.");
        builder.Property(x => x.SettlementRevision).HasColumnName("settlement_revision").HasComment("MES settlement revision that covered the report.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100).HasComment("MES production report number permanently suppressed from theoretical labor costing.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .IsUnique()
            .HasDatabaseName("ux_operation_labor_covered_reports_report");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.SettlementRevision })
            .HasDatabaseName("ix_operation_labor_covered_reports_settlement");
    }
}

public sealed class WorkCenterCostRateEntityTypeConfiguration : IEntityTypeConfiguration<WorkCenterCostRate>
{
    public void Configure(EntityTypeBuilder<WorkCenterCostRate> builder)
    {
        builder.ToTable("work_center_cost_rates", table => table.HasComment("ERP append-only, effective-dated standard labor hourly-rate revision history by work center.")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work-center standard labor-rate id."); GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MES work-center public identifier.");
        builder.Property(x => x.HourlyRate).HasColumnName("hourly_rate").HasPrecision(18, 6).HasComment("Positive standard labor hourly rate.");
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3).IsFixedLength().HasComment("Normalized ISO-style three-letter uppercase currency code.");
        builder.Property(x => x.EffectiveFromUtc).HasColumnName("effective_from_utc").HasComment("Inclusive UTC instant from which this revision may apply.");
        builder.Property(x => x.EffectiveToUtc).HasColumnName("effective_to_utc").HasComment("Optional exclusive UTC instant after which this revision no longer applies.");
        builder.Property(x => x.Revision).HasColumnName("revision").HasComment("Monotonically increasing revision within organization, environment, and work center.");
        builder.Property(x => x.ChangedBy).HasColumnName("changed_by").IsRequired().HasMaxLength(200).HasComment("Canonical authenticated actor that configured this revision.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500).HasComment("Non-empty business reason for this immutable revision.");
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").HasComment("UTC audit instant at which this revision was configured.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterId, x.Revision })
            .IsUnique()
            .HasDatabaseName("ux_work_center_cost_rates_scope_revision");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterId, x.EffectiveFromUtc, x.EffectiveToUtc, x.Revision })
            .HasDatabaseName("ix_work_center_cost_rates_effective_lookup");
    }
}

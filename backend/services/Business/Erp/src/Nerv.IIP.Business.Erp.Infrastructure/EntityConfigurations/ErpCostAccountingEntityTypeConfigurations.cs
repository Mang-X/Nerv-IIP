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
        builder.Ignore(x => x.LaborCost); builder.Ignore(x => x.MaterialCost); builder.Ignore(x => x.TotalAccumulatedCost); builder.Ignore(x => x.VarianceCost);
        builder.HasMany(x => x.Details).WithOne().HasForeignKey("WorkOrderCostId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Details).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId }).IsUnique();
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
        builder.ToTable("work_order_cost_details", table => table.HasComment("ERP auditable labor or material cost detail.")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Cost detail id.");
        builder.Property<WorkOrderCostId>("WorkOrderCostId").HasColumnName("work_order_cost_id").IsRequired().HasComment("Owning work-order cost id.");
        builder.Property(x => x.Type).HasColumnName("cost_type").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Labor or material cost type.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Public source event document id.");
        builder.Property(x => x.DimensionCode).HasColumnName("dimension_code").IsRequired().HasMaxLength(100).HasComment("Work center or material SKU dimension.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").HasMaxLength(100).HasComment("MES report number for material-to-work-order correlation.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Labor hours or material quantity.");
        builder.Property(x => x.Rate).HasColumnName("rate").HasPrecision(18, 6).HasComment("Hourly rate or moving-average unit cost.");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 6).HasComment("Signed actual cost amount.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasComment("Source fact occurrence timestamp.");
        builder.HasIndex("WorkOrderCostId", nameof(WorkOrderCostDetail.SourceDocumentId)).IsUnique();
    }
}

public sealed class WorkCenterCostRateEntityTypeConfiguration : IEntityTypeConfiguration<WorkCenterCostRate>
{
    public void Configure(EntityTypeBuilder<WorkCenterCostRate> builder)
    {
        builder.ToTable("work_center_cost_rates", table => table.HasComment("ERP phase-one actual labor hourly rates by work center.")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work-center cost-rate id."); GLAccountEntityTypeConfiguration.AddTenant(builder);
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MES work-center public identifier.");
        builder.Property(x => x.HourlyRate).HasColumnName("hourly_rate").HasPrecision(18, 6).HasComment("Actual labor rate per hour in local currency.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterId }).IsUnique();
    }
}

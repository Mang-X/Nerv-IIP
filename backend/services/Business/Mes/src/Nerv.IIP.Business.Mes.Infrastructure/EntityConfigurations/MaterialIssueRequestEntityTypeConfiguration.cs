using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class MaterialIssueRequestEntityTypeConfiguration : IEntityTypeConfiguration<MaterialIssueRequest>
{
    public void Configure(EntityTypeBuilder<MaterialIssueRequest> builder)
    {
        builder.ToTable("material_issue_requests", tableBuilder =>
            tableBuilder.HasComment("MES material issue and line-side receipt facts tracking requested, received and consumed material quantities for work orders."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Material issue request aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the material issue request.");
        builder.Property(x => x.RequestNo).HasColumnName("request_no").IsRequired().HasMaxLength(100).HasComment("MES material issue request number allocated by the service numbering counter.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order id requesting materials.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").HasMaxLength(100).HasComment("Optional MES operation task id requesting materials.");
        builder.Property(x => x.MaterialId).HasColumnName("material_id").IsRequired().HasMaxLength(100).HasComment("Material SKU id requested for staging or line-side receipt.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasDefaultValue(MaterialIssueRequest.UnspecifiedUomCode).HasComment("Unit of measure code captured for the material issue quantity.");
        builder.Property(x => x.MaterialLotId).HasColumnName("material_lot_id").HasMaxLength(100).HasComment("Actual material lot id received line-side, when known.");
        builder.Property(x => x.RequestedQuantity).HasColumnName("requested_quantity").HasPrecision(18, 6).IsRequired().HasComment("Requested material issue quantity.");
        builder.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasPrecision(18, 6).IsRequired().HasComment("Confirmed line-side received quantity.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Material issue lifecycle status within MES.");
        builder.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired().HasComment("UTC time when the material issue request was created.");
        builder.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").HasComment("UTC time when line-side receipt was confirmed.");
        builder.Property(x => x.InventoryPostingFailureCode).HasColumnName("inventory_posting_failure_code").HasMaxLength(100).HasComment("Last Inventory posting failure code returned for this MES material issue request.");
        builder.Property(x => x.InventoryPostingFailureMessage).HasColumnName("inventory_posting_failure_message").HasMaxLength(500).HasComment("Last Inventory posting failure message returned for this MES material issue request.");
        builder.Property(x => x.InventoryPostingFailedAtUtc).HasColumnName("inventory_posting_failed_at_utc").HasComment("UTC time when Inventory rejected the latest MES material issue or line-side receipt posting.");
        builder.Property(x => x.InventoryPostingRollbackKey).HasColumnName("inventory_posting_rollback_key").HasMaxLength(300).HasComment("MES normalized receipt-step key already rolled back for Inventory posting failure, used to avoid double rollback when both transfer legs fail.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_material_issue_requests_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RequestNo })
            .IsUnique()
            .HasDatabaseName("ux_material_issue_requests_scope_request_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.MaterialId })
            .HasDatabaseName("ix_material_issue_requests_scope_work_order_material");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasDatabaseName("ix_material_issue_requests_scope_operation");
    }
}

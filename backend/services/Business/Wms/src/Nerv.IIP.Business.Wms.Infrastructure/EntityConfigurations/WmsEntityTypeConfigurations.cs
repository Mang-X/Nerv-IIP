using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

namespace Nerv.IIP.Business.Wms.Infrastructure.EntityConfigurations;

public sealed class InboundOrderEntityTypeConfiguration : IEntityTypeConfiguration<InboundOrder>
{
    public void Configure(EntityTypeBuilder<InboundOrder> builder)
    {
        builder.ToTable("inbound_orders", table => table.HasComment("WMS inbound execution order header and source document reference."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inbound order aggregate id.");
        AddTenantColumns(builder);
        builder.Property(x => x.InboundOrderNo).HasColumnName("inbound_order_no").IsRequired().HasMaxLength(100).HasComment("WMS inbound order number.");
        builder.Property(x => x.SourceDocumentType).HasColumnName("source_document_type").IsRequired().HasMaxLength(100).HasComment("Producer document type.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Producer document id.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Inbound execution status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC completion time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("InboundOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.InboundOrderNo }).IsUnique();
    }

    internal static void AddTenantColumns<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<string>("OrganizationId").HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property<string>("EnvironmentId").HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id.");
    }
}

public sealed class InboundOrderLineEntityTypeConfiguration : IEntityTypeConfiguration<InboundOrderLine>
{
    public void Configure(EntityTypeBuilder<InboundOrderLine> builder)
    {
        builder.ToTable("inbound_order_lines", table => table.HasComment("WMS inbound order execution lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inbound order line id.");
        builder.Property<InboundOrderId>("InboundOrderId").HasColumnName("inbound_order_id").IsRequired().HasComment("Owning inbound order id.");
        AddLineColumns(builder, "received_quantity", "Inbound received quantity.");
        builder.Property(x => x.StagingLocationCode).HasColumnName("staging_location_code").IsRequired().HasMaxLength(100).HasComment("Staging location for received stock.");
    }

    internal static void AddLineColumns<T>(EntityTypeBuilder<T> builder, string quantityColumn, string quantityComment)
        where T : class
    {
        builder.Property<string>("LineNo").HasColumnName("line_no").IsRequired().HasMaxLength(100).HasComment("Source line number.");
        builder.Property<string>("SkuCode").HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property<string>("UomCode").HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property<decimal>(quantityColumn == "received_quantity" ? "ReceivedQuantity" : "RequestedQuantity").HasColumnName(quantityColumn).IsRequired().HasPrecision(18, 6).HasComment(quantityComment);
        builder.Property<string?>("LotNo").HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional lot number.");
        builder.Property<string?>("SerialNo").HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number.");
        builder.Property<string>("QualityStatus").HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Quality status dimension.");
        builder.Property<string>("OwnerType").HasColumnName("owner_type").IsRequired().HasMaxLength(50).HasComment("Owner type dimension.");
        builder.Property<string?>("OwnerId").HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional owner id.");
    }
}

public sealed class OutboundOrderEntityTypeConfiguration : IEntityTypeConfiguration<OutboundOrder>
{
    public void Configure(EntityTypeBuilder<OutboundOrder> builder)
    {
        builder.ToTable("outbound_orders", table => table.HasComment("WMS outbound execution order header and pack review facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Outbound order aggregate id.");
        InboundOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.OutboundOrderNo).HasColumnName("outbound_order_no").IsRequired().HasMaxLength(100).HasComment("WMS outbound order number.");
        builder.Property(x => x.SourceDocumentType).HasColumnName("source_document_type").IsRequired().HasMaxLength(100).HasComment("Producer document type.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Producer document id.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Outbound execution status.");
        builder.Property(x => x.PackReviewNo).HasColumnName("pack_review_no").HasMaxLength(100).HasComment("Pack review reference.");
        builder.Property(x => x.PackReviewPassed).HasColumnName("pack_review_passed").HasComment("Pack review pass flag.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC completion time.");
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("OutboundOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OutboundOrderNo }).IsUnique();
    }
}

public sealed class OutboundOrderLineEntityTypeConfiguration : IEntityTypeConfiguration<OutboundOrderLine>
{
    public void Configure(EntityTypeBuilder<OutboundOrderLine> builder)
    {
        builder.ToTable("outbound_order_lines", table => table.HasComment("WMS outbound order execution lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Outbound order line id.");
        builder.Property<OutboundOrderId>("OutboundOrderId").HasColumnName("outbound_order_id").IsRequired().HasComment("Owning outbound order id.");
        InboundOrderLineEntityTypeConfiguration.AddLineColumns(builder, "requested_quantity", "Outbound requested quantity.");
        builder.Property(x => x.PickLocationCode).HasColumnName("pick_location_code").IsRequired().HasMaxLength(100).HasComment("Pick location for outbound stock.");
        builder.Property(x => x.InventoryReservationId).HasColumnName("inventory_reservation_id").HasMaxLength(150).HasComment("Public Inventory reservation id allocated for this outbound line.");
    }
}

public sealed class WarehouseTaskEntityTypeConfiguration : IEntityTypeConfiguration<WarehouseTask>
{
    public void Configure(EntityTypeBuilder<WarehouseTask> builder)
    {
        builder.ToTable("warehouse_tasks", table => table.HasComment("WMS putaway and picking warehouse tasks."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Warehouse task id.");
        InboundOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.TaskType).HasColumnName("task_type").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Task type: putaway or picking.");
        builder.Property(x => x.TaskNo).HasColumnName("task_no").IsRequired().HasMaxLength(100).HasComment("Warehouse task number.");
        builder.Property(x => x.SourceOrderNo).HasColumnName("source_order_no").IsRequired().HasMaxLength(100).HasComment("WMS source order number.");
        builder.Property(x => x.SourceOrderLineNo).HasColumnName("source_order_line_no").IsRequired().HasMaxLength(100).HasComment("WMS source order line number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.FromLocationCode).HasColumnName("from_location_code").IsRequired().HasMaxLength(100).HasComment("Task source location.");
        builder.Property(x => x.ToLocationCode).HasColumnName("to_location_code").IsRequired().HasMaxLength(100).HasComment("Task target location.");
        builder.Property(x => x.PlannedQuantity).HasColumnName("planned_quantity").IsRequired().HasPrecision(18, 6).HasComment("Planned execution quantity.");
        builder.Property(x => x.ExecutedQuantity).HasColumnName("executed_quantity").IsRequired().HasPrecision(18, 6).HasComment("Executed task quantity.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Warehouse task status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC completion time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TaskNo }).IsUnique();
    }
}

public sealed class CountExecutionEntityTypeConfiguration : IEntityTypeConfiguration<CountExecution>
{
    public void Configure(EntityTypeBuilder<CountExecution> builder)
    {
        builder.ToTable("count_executions", table => table.HasComment("WMS count execution and variance output facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Count execution id.");
        InboundOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.CountNo).HasColumnName("count_no").IsRequired().HasMaxLength(100).HasComment("Count execution number.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Counted warehouse location.");
        builder.Property(x => x.ExpectedQuantity).HasColumnName("expected_quantity").IsRequired().HasPrecision(18, 6).HasComment("Expected count quantity provided by upstream boundary.");
        builder.Property(x => x.CountedQuantity).HasColumnName("counted_quantity").HasPrecision(18, 6).HasComment("Actual counted quantity.");
        builder.Property(x => x.VarianceQuantity).HasColumnName("variance_quantity").HasPrecision(18, 6).HasComment("Counted quantity minus expected quantity.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Count execution status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC completion time.");
    }
}

public sealed class WcsTaskEntityTypeConfiguration : IEntityTypeConfiguration<WcsTask>
{
    public void Configure(EntityTypeBuilder<WcsTask> builder)
    {
        builder.ToTable("wcs_tasks", table => table.HasComment("WCS adapter task mapping, lifecycle and diagnostics."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("WCS task id.");
        InboundOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.WarehouseTaskId).HasColumnName("warehouse_task_id").IsRequired().HasComment("WMS warehouse task id.");
        builder.Property(x => x.AdapterType).HasColumnName("adapter_type").IsRequired().HasMaxLength(100).HasComment("WCS adapter type.");
        builder.Property(x => x.ExternalTaskId).HasColumnName("external_task_id").IsRequired().HasMaxLength(150).HasComment("External WCS task id.");
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired().HasComment("Outbound adapter payload JSON.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("WCS task status.");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired().HasComment("Dispatch attempt count.");
        builder.Property(x => x.CompletionPayloadJson).HasColumnName("completion_payload_json").HasComment("Completion callback payload JSON.");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100).HasComment("WCS failure diagnostic code.");
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").HasMaxLength(1000).HasComment("WCS failure diagnostic message.");
        builder.Property(x => x.DispatchedAtUtc).HasColumnName("dispatched_at_utc").IsRequired().HasComment("UTC dispatch time.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC completion time.");
        builder.Property(x => x.FailedAtUtc).HasColumnName("failed_at_utc").HasComment("UTC failure time.");
        builder.HasIndex(x => new { x.WarehouseTaskId, x.AdapterType }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ExternalTaskId }).IsUnique();
    }
}

public sealed class InventoryMovementRequestEntityTypeConfiguration : IEntityTypeConfiguration<InventoryMovementRequest>
{
    public void Configure(EntityTypeBuilder<InventoryMovementRequest> builder)
    {
        builder.ToTable("inventory_movement_requests", table => table.HasComment("WMS-owned metadata for Inventory movement posting requests."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inventory movement request id.");
        InboundOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.MovementType).HasColumnName("movement_type").IsRequired().HasMaxLength(50).HasComment("Inventory movement type requested by WMS.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("WMS source document id.");
        builder.Property(x => x.SourceDocumentLineId).HasColumnName("source_document_line_id").HasMaxLength(150).HasComment("WMS source document line id.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Producer-stable idempotency key.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("MasterData SKU code.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MasterData unit of measure code.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Inventory public stock location code.");
        builder.Property(x => x.LotNo).HasColumnName("lot_no").HasMaxLength(100).HasComment("Optional lot number.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number.");
        builder.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired().HasMaxLength(50).HasComment("Quality status dimension.");
        builder.Property(x => x.OwnerType).HasColumnName("owner_type").IsRequired().HasMaxLength(50).HasComment("Owner type dimension.");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(100).HasComment("Optional owner id.");
        builder.Property(x => x.InventoryReservationId).HasColumnName("inventory_reservation_id").HasMaxLength(150).HasComment("Optional Inventory reservation id used to allocate outbound stock.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Movement quantity requested from Inventory.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Posting status for the Inventory request.");
        builder.Property(x => x.InventoryMovementId).HasColumnName("inventory_movement_id").HasMaxLength(150).HasComment("Public Inventory movement id returned after posting.");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100).HasComment("Inventory posting failure code.");
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").HasMaxLength(1000).HasComment("Inventory posting failure message.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at_utc").HasComment("UTC posted time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceDocumentId, x.IdempotencyKey }).IsUnique();
    }
}

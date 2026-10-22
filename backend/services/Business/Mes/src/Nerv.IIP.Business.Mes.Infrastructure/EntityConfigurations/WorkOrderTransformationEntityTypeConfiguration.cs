using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class WorkOrderTransformationEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrderTransformation>
{
    public void Configure(EntityTypeBuilder<WorkOrderTransformation> builder)
    {
        builder.ToTable("work_order_transformations", tableBuilder =>
        {
            tableBuilder.HasComment("MES immutable split or merge audit facts and their scoped idempotency identity.");
            tableBuilder.HasCheckConstraint("ck_work_order_transformations_type", "transformation_type IN ('Split', 'Merge')");
            tableBuilder.HasCheckConstraint("ck_work_order_transformations_status", "status = 'Applied'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work-order transformation aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the MES transformation.");
        builder.Property(x => x.Type).HasColumnName("transformation_type").HasConversion<string>().IsRequired().HasMaxLength(20).HasComment("Transformation type: Split or Merge.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired().HasMaxLength(20).HasComment("Transformation audit status; Applied is committed in the same transaction as work-order changes.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(150).HasComment("Client supplied idempotency identity scoped by organization and environment.");
        builder.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").IsRequired().HasMaxLength(128).HasComment("Canonical request payload fingerprint used to reject a different replay under the same idempotency key.");
        builder.Property(x => x.ActorId).HasColumnName("actor_id").IsRequired().HasMaxLength(200).HasComment("Authenticated actor recorded for the transformation audit.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500).HasComment("Audited business reason for the split or merge.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired().HasComment("UTC time when the transformation was applied.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_work_order_transformations_scope_idempotency");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Type, x.OccurredAtUtc })
            .HasDatabaseName("ix_work_order_transformations_scope_type_occurred");
        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("WorkOrderTransformationId")
            .HasConstraintName("fk_work_order_transformation_lines_transformations")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class WorkOrderTransformationLineEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrderTransformationLine>
{
    public void Configure(EntityTypeBuilder<WorkOrderTransformationLine> builder)
    {
        builder.ToTable("work_order_transformation_lines", tableBuilder =>
        {
            tableBuilder.HasComment("MES immutable source-to-target lineage edges for a split or merge audit.");
            tableBuilder.HasCheckConstraint(
                "ck_work_order_transformation_lines_positive_quantity",
                "quantity > 0");
            tableBuilder.HasCheckConstraint(
                "ck_work_order_transformation_lines_distinct_work_orders",
                "source_work_order_id <> target_work_order_id");
            tableBuilder.HasCheckConstraint(
                "ck_work_order_transformation_lines_positive_versions",
                "source_version > 0 AND target_version > 0");
            tableBuilder.HasCheckConstraint(
                "ck_work_order_transformation_lines_positive_snapshot_quantities",
                "source_quantity > 0 AND target_quantity > 0");
            tableBuilder.HasCheckConstraint(
                "ck_work_order_transformation_lines_lineage_type",
                "lineage_type IN ('Split', 'Merge')");
            tableBuilder.HasCheckConstraint(
                "ck_work_order_transformation_lines_uom_present",
                "trim(uom_code) <> ''");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work-order transformation lineage edge id.");
        builder.Property<WorkOrderTransformationId>("WorkOrderTransformationId")
            .HasColumnName("work_order_transformation_id")
            .IsRequired()
            .HasComment("Owning transformation audit aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id copied onto the lineage edge.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id copied onto the lineage edge.");
        builder.Property(x => x.LineageType).HasColumnName("lineage_type").HasConversion<string>().IsRequired().HasMaxLength(20).HasComment("Lineage relation type: Split or Merge.");
        builder.Property(x => x.SourceWorkOrderId).HasColumnName("source_work_order_id").IsRequired().HasMaxLength(100).HasComment("Source or parent MES work-order business id.");
        builder.Property(x => x.TargetWorkOrderId).HasColumnName("target_work_order_id").IsRequired().HasMaxLength(100).HasComment("Target or child MES work-order business id.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("Quantity represented by this source-to-target lineage edge.");
        builder.Property(x => x.SourceQuantity).HasColumnName("source_quantity").HasPrecision(18, 6).IsRequired().HasComment("Full source work-order planned quantity captured at transformation time.");
        builder.Property(x => x.TargetQuantity).HasColumnName("target_quantity").HasPrecision(18, 6).IsRequired().HasComment("Full target work-order planned quantity captured at transformation time.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("UOM shared by the source and target work orders.");
        builder.Property(x => x.SourceStatus).HasColumnName("source_status").IsRequired().HasMaxLength(30).HasComment("Source work-order status captured before the transformation.");
        builder.Property(x => x.TargetStatus).HasColumnName("target_status").IsRequired().HasMaxLength(30).HasComment("Target work-order status captured at the transformation boundary.");
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").IsRequired().HasComment("Expected source work-order version used for optimistic concurrency.");
        builder.Property(x => x.TargetVersion).HasColumnName("target_version").IsRequired().HasComment("Target work-order version captured for lineage audit.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.SourceWorkOrderId })
            .HasConstraintName("fk_work_order_transformation_lines_source_work_order")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.TargetWorkOrderId })
            .HasConstraintName("fk_work_order_transformation_lines_target_work_order")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceWorkOrderId, x.TargetWorkOrderId, x.LineageType })
            .IsUnique()
            .HasDatabaseName("ux_work_order_transformation_lines_scope_edge");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceWorkOrderId })
            .HasDatabaseName("ix_work_order_transformation_lines_scope_source");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TargetWorkOrderId })
            .HasDatabaseName("ix_work_order_transformation_lines_scope_target");
        builder.HasIndex("WorkOrderTransformationId")
            .HasDatabaseName("ix_work_order_transformation_lines_transformation");
    }
}

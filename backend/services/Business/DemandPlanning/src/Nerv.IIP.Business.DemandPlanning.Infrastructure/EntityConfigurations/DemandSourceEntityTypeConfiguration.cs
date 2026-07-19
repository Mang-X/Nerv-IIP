using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class DemandSourceEntityTypeConfiguration : IEntityTypeConfiguration<DemandSource>
{
    public void Configure(EntityTypeBuilder<DemandSource> builder)
    {
        builder.ToTable("demand_sources", table => table.HasComment("DemandPlanning owned demand source facts for MPS and MRP input."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Demand source aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id that owns the demand.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment id, such as dev, test, or production planning space.");
        builder.Property(x => x.DemandType).HasColumnName("demand_type").HasMaxLength(32).IsRequired().HasComment("Demand type: forecast, sales-order, safety-stock, or manual.");
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(128).IsRequired().HasComment("External or manual source reference unique in the planning scope.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").HasMaxLength(128).IsRequired().HasDefaultValue(string.Empty).HasComment("Stable upstream source document identifier; sales order public id for ERP demand.");
        builder.Property(x => x.SourceLineReference).HasColumnName("source_line_reference").HasMaxLength(64).IsRequired().HasDefaultValue(string.Empty).HasComment("Stable upstream source line reference; empty for manually managed demand.");
        builder.Property(x => x.CustomerCode).HasColumnName("customer_code").HasMaxLength(100).IsRequired().HasDefaultValue(string.Empty).HasComment("Customer code snapshot supplied by the upstream sales order.");
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasDefaultValue(0).IsConcurrencyToken().HasComment("Latest accepted upstream business version and optimistic concurrency token; zero for manually managed demand.");
        builder.Property(x => x.SourceStatus).HasColumnName("source_status").HasMaxLength(32).IsRequired().HasDefaultValue("active").HasComment("Explainable upstream lifecycle status: active or cancelled.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(64).IsRequired().HasComment("Demanded finished-good SKU code snapshot.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(32).IsRequired().HasComment("Demand quantity unit of measure snapshot.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(64).IsRequired().HasComment("Demand site code snapshot.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Positive demand quantity.");
        builder.Property(x => x.DueDate).HasColumnName("due_date").HasComment("Demand due date bucket.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the demand source was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC timestamp when the demand source was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DemandType, x.SourceReference, x.SourceLineReference }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DemandType, x.SourceDocumentId })
            .HasDatabaseName("ix_demand_sources_scope_type_source_document");
    }
}

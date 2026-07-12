using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class PlanningSuggestionEntityTypeConfiguration : IEntityTypeConfiguration<PlanningSuggestion>
{
    public void Configure(EntityTypeBuilder<PlanningSuggestion> builder)
    {
        builder.ToTable("planning_suggestions", table => table.HasComment("DemandPlanning generated planned purchase and planned work-order suggestions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Planning suggestion aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id that owns the suggestion.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment id.");
        builder.Property(x => x.MrpRunId).HasColumnName("mrp_run_id").HasComment("Owning MRP run id; no cross-service foreign key.");
        builder.Property(x => x.SuggestionType).HasColumnName("suggestion_type").HasMaxLength(32).IsRequired().HasComment("Suggestion type such as planned-purchase or planned-work-order.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(64).IsRequired().HasComment("Suggested SKU code snapshot.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(32).IsRequired().HasComment("Suggested quantity unit of measure snapshot.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(64).IsRequired().HasComment("Suggested site code snapshot.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Positive suggested quantity.");
        builder.Property(x => x.RequiredDate).HasColumnName("required_date").HasComment("Required date for downstream procurement or production.");
        builder.Property(x => x.ReleaseDate).HasColumnName("release_date").HasComment("MRP-calculated release date after lead-time offset.");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(128).IsRequired().HasComment("MRP reason code explaining why the suggestion exists.");
        builder.Property(x => x.GrossDemandQuantity).HasColumnName("gross_demand_quantity").HasPrecision(18, 6).HasComment("Gross requirement quantity before MRP netting.");
        builder.Property(x => x.OnHandQuantity).HasColumnName("on_hand_quantity").HasPrecision(18, 6).HasComment("On-hand inventory quantity snapshot used by MRP netting.");
        builder.Property(x => x.ReservedQuantity).HasColumnName("reserved_quantity").HasPrecision(18, 6).HasComment("Reserved inventory quantity snapshot used by MRP netting.");
        builder.Property(x => x.AvailableToNetQuantity).HasColumnName("available_to_net_quantity").HasPrecision(18, 6).HasComment("Inventory quantity actually available for netting after reserved and safety stock protection.");
        builder.Property(x => x.ScheduledReceiptQuantity).HasColumnName("scheduled_receipt_quantity").HasPrecision(18, 6).HasComment("Scheduled receipt quantity consumed by MRP netting.");
        builder.Property(x => x.SafetyStockQuantity).HasColumnName("safety_stock_quantity").HasPrecision(18, 6).HasComment("Safety stock quantity protected during MRP netting.");
        builder.Property(x => x.NetRequirementQuantity).HasColumnName("net_requirement_quantity").HasPrecision(18, 6).HasComment("Calculated net requirement quantity before lot sizing.");
        builder.Property(x => x.PlannedQuantity).HasColumnName("planned_quantity").HasPrecision(18, 6).HasComment("Planned suggestion quantity after lot sizing.");
        builder.Property(x => x.ScrapRate).HasColumnName("scrap_rate").HasPrecision(18, 6).HasComment("BOM scrap rate applied to this requirement explanation.");
        builder.Property(x => x.YieldRate).HasColumnName("yield_rate").HasPrecision(18, 6).HasDefaultValue(1m).HasComment("Yield rate applied to this requirement explanation.");
        builder.Property(x => x.PrimarySourceType).HasColumnName("primary_source_type").HasMaxLength(32).IsRequired().HasComment("Primary requirement source type such as sales, forecast, safety-stock, mps, or component.");
        builder.Property(x => x.Formula).HasColumnName("formula").HasMaxLength(512).IsRequired().HasComment("Human-readable net requirement formula from persisted MRP inputs.");
        builder.Property(x => x.UomConversionSummary).HasColumnName("uom_conversion_summary").HasMaxLength(512).IsRequired().HasComment("UOM conversion summary used while calculating this suggestion.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).HasComment("Suggestion lifecycle status.");
        builder.Property(x => x.AcceptedDownstreamService).HasColumnName("accepted_downstream_service").HasMaxLength(64).HasComment("Downstream service that accepted the suggestion.");
        builder.Property(x => x.AcceptedDownstreamDocumentType).HasColumnName("accepted_downstream_document_type").HasMaxLength(64).HasComment("Downstream document type that accepted the suggestion.");
        builder.Property(x => x.AcceptedDownstreamDocumentId).HasColumnName("accepted_downstream_document_id").HasMaxLength(128).HasComment("Downstream document id that accepted the suggestion.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the suggestion was created.");
        builder.Property(x => x.AcceptedAtUtc).HasColumnName("accepted_at_utc").HasComment("UTC timestamp when the suggestion was accepted.");
        builder.HasMany(x => x.PeggingLinks).WithOne().HasForeignKey(x => x.PlanningSuggestionId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(PlanningSuggestion.PeggingLinks))?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.MrpRunId, x.SuggestionType, x.SkuCode });
    }
}

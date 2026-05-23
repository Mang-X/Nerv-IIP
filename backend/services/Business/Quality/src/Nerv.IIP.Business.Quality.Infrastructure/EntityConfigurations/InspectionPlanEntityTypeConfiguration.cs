using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class InspectionPlanEntityTypeConfiguration : IEntityTypeConfiguration<InspectionPlan>
{
    public void Configure(EntityTypeBuilder<InspectionPlan> builder)
    {
        builder.ToTable("inspection_plans", tableBuilder =>
            tableBuilder.HasComment("Quality inspection plan version and applicability facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inspection plan aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the plan.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the plan applies.");
        builder.Property(x => x.PlanCode).HasColumnName("plan_code").IsRequired().HasMaxLength(100).HasComment("Human-readable inspection plan code.");
        builder.Property(x => x.Category).HasColumnName("category").IsRequired().HasMaxLength(50).HasComment("Inspection category: receiving, operation, final, maintenance or customer-return.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(100).HasComment("Optional MasterData SKU code applicability reference.");
        builder.Property(x => x.PartnerId).HasColumnName("partner_id").HasMaxLength(150).HasComment("Optional supplier or customer public reference id.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").HasMaxLength(150).HasComment("Optional work center public reference id.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").HasMaxLength(150).HasComment("Optional device asset public reference id.");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(100).HasComment("Optional source document type covered by the plan.");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired().HasComment("Plan version number.");
        builder.Property(x => x.SupersedesPlanId).HasColumnName("supersedes_plan_id").HasComment("Previous inspection plan version id superseded by this version.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Inspection plan lifecycle status.");
        builder.Property(x => x.ActivatedAtUtc).HasColumnName("activated_at_utc").HasComment("UTC time when the plan was activated.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the plan was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the plan was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PlanCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Category, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status });
        builder.HasMany(x => x.Characteristics)
            .WithOne()
            .HasForeignKey(x => x.InspectionPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InspectionPlanCharacteristicEntityTypeConfiguration : IEntityTypeConfiguration<InspectionPlanCharacteristic>
{
    public void Configure(EntityTypeBuilder<InspectionPlanCharacteristic> builder)
    {
        builder.ToTable("inspection_plan_characteristics", tableBuilder =>
            tableBuilder.HasComment("Quality inspection plan characteristics and sampling rules."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inspection plan characteristic id.");
        builder.Property(x => x.InspectionPlanId).HasColumnName("inspection_plan_id").IsRequired().HasComment("Owning inspection plan id.");
        builder.Property(x => x.CharacteristicCode).HasColumnName("characteristic_code").IsRequired().HasMaxLength(100).HasComment("Stable characteristic code within the plan.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Characteristic display name.");
        builder.Property(x => x.Method).HasColumnName("method").IsRequired().HasMaxLength(100).HasComment("Inspection method or measurement procedure.");
        builder.Property(x => x.Severity).HasColumnName("severity").IsRequired().HasMaxLength(50).HasComment("Quality severity classification.");
        builder.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired().HasComment("Whether this characteristic is required for plan execution.");
        builder.Property(x => x.SamplingRule).HasColumnName("sampling_rule").IsRequired().HasMaxLength(200).HasComment("Sampling rule or sample size expression.");
        builder.HasIndex(x => new { x.InspectionPlanId, x.CharacteristicCode }).IsUnique();
    }
}

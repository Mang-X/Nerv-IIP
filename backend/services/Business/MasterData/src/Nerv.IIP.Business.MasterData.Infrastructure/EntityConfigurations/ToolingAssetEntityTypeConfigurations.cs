using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class ToolingAssetEntityTypeConfiguration : IEntityTypeConfiguration<ToolingAsset>
{
    public void Configure(EntityTypeBuilder<ToolingAsset> builder)
    {
        builder.ToTable("tooling_assets", t => t.HasComment("Tooling and mould master assets governed by MasterData."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Tooling asset identifier.");
        builder.Property(x => x.OrganizationId).HasMaxLength(64).IsRequired().HasComment("Organization scope.");
        builder.Property(x => x.EnvironmentId).HasMaxLength(64).IsRequired().HasComment("Environment scope.");
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired().HasComment("Coding-engine allocated tooling code.");
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired().HasComment("Tooling display name.");
        builder.Property(x => x.ToolingType).HasMaxLength(64).IsRequired().HasComment("Tooling type code.");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired().HasComment("Lifecycle status: Available, Maintenance, or Retired.");
        builder.Property(x => x.MaintenanceLifeCount).HasComment("Usage count at which maintenance becomes due.");
        builder.Property(x => x.UsageCount).HasComment("Accumulated governed usage count.");
        builder.Property(x => x.CreatedAtUtc).HasComment("Creation timestamp in UTC.");
        builder.Property(x => x.UpdatedAtUtc).HasComment("Last update timestamp in UTC.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasMany(x => x.Applicability).WithOne().HasForeignKey("ToolingAssetId").IsRequired().OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ToolingApplicabilityEntityTypeConfiguration : IEntityTypeConfiguration<ToolingApplicability>
{
    public void Configure(EntityTypeBuilder<ToolingApplicability> builder)
    {
        builder.ToTable("tooling_applicability", t => t.HasComment("Work-center and SKU applicability of a tooling asset."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Applicability row identifier.");
        builder.Property(x => x.WorkCenterCode).HasMaxLength(64).IsRequired().HasComment("Applicable work-center code.");
        builder.Property(x => x.SkuCode).HasMaxLength(64).IsRequired().HasComment("Applicable SKU code.");
        builder.HasIndex("ToolingAssetId", nameof(ToolingApplicability.WorkCenterCode), nameof(ToolingApplicability.SkuCode)).IsUnique();
    }
}

public sealed class ChangeoverMatrixEntryEntityTypeConfiguration : IEntityTypeConfiguration<ChangeoverMatrixEntry>
{
    public void Configure(EntityTypeBuilder<ChangeoverMatrixEntry> builder)
    {
        builder.ToTable("changeover_matrix_entries", t => t.HasComment("Authoritative setup duration and tooling requirements by work center and SKU transition."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Changeover matrix entry identifier.");
        builder.Property(x => x.OrganizationId).HasMaxLength(64).IsRequired().HasComment("Organization scope.");
        builder.Property(x => x.EnvironmentId).HasMaxLength(64).IsRequired().HasComment("Environment scope.");
        builder.Property(x => x.WorkCenterCode).HasMaxLength(64).IsRequired().HasComment("Work-center code.");
        builder.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(32).IsRequired().HasComment("Source dimension: SKU or ProductFamily.");
        builder.Property(x => x.SourceCode).HasMaxLength(64).IsRequired().HasComment("Source SKU or product-family code.");
        builder.Property(x => x.ToSkuCode).HasMaxLength(64).IsRequired().HasComment("Target SKU code.");
        builder.Property(x => x.SetupMinutes).HasComment("Setup duration in minutes.");
        builder.Property(x => x.Active).HasComment("Whether this matrix entry is active.");
        builder.Property(x => x.CreatedAtUtc).HasComment("Creation timestamp in UTC.");
        builder.Property(x => x.UpdatedAtUtc).HasComment("Last update timestamp in UTC.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterCode, x.SourceType, x.SourceCode, x.ToSkuCode }).IsUnique();
        builder.HasMany(x => x.RequiredTooling).WithOne().HasForeignKey("ChangeoverMatrixEntryId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.Specificity);
        builder.Ignore(x => x.FromSkuCode);
        builder.Ignore(x => x.FromProductFamilyCode);
    }
}

public sealed class ChangeoverRequiredToolingEntityTypeConfiguration : IEntityTypeConfiguration<ChangeoverRequiredTooling>
{
    public void Configure(EntityTypeBuilder<ChangeoverRequiredTooling> builder)
    {
        builder.ToTable("changeover_required_tooling", t => t.HasComment("Tooling assets required by a changeover matrix entry."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Required-tooling row identifier.");
        builder.Property(x => x.ToolingCode).HasMaxLength(64).IsRequired().HasComment("Required tooling code.");
        builder.HasIndex("ChangeoverMatrixEntryId", nameof(ChangeoverRequiredTooling.ToolingCode)).IsUnique();
    }
}

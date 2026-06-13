using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class RoutingEntityTypeConfiguration : IEntityTypeConfiguration<Routing>
{
    public void Configure(EntityTypeBuilder<Routing> builder)
    {
        builder.ToTable("routings", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering routing versions with ordered work center operation steps."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Routing aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the routing is valid.");
        builder.Property(x => x.RoutingCode).HasColumnName("routing_code").IsRequired().HasMaxLength(100).HasComment("Routing business code.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsRequired().HasMaxLength(50).HasComment("Routing revision.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Produced SKU code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(30).HasComment("Routing lifecycle status.");
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").HasComment("First effective date after release.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the routing was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the routing was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RoutingCode, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.Status });
        builder.OwnsMany(x => x.Operations, ConfigureOperations);
        builder.Navigation(x => x.Operations).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureOperations(OwnedNavigationBuilder<Routing, RoutingOperation> builder)
    {
        builder.ToTable("routing_operations", tableBuilder =>
            tableBuilder.HasComment("Routing ordered operation steps and work center references."));
        builder.WithOwner().HasForeignKey("routing_id");
        builder.Property<int>("id").ValueGeneratedOnAdd();
        builder.HasKey("id");
        builder.Property("routing_id").HasColumnName("routing_id").HasComment("Owning routing id.");
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired().HasComment("Positive operation sequence number.");
        builder.Property(x => x.WorkCenterCode).HasColumnName("work_center_code").IsRequired().HasMaxLength(100).HasComment("MasterData work center code reference.");
        builder.Property(x => x.OperationCode).HasColumnName("operation_code").IsRequired().HasMaxLength(100).HasComment("Standard operation code snapshot captured when the routing version was released.");
        builder.Property(x => x.OperationName).HasColumnName("operation_name").IsRequired().HasMaxLength(200).HasComment("Operation display name submitted with routing release.");
        builder.Property(x => x.StandardMinutes).HasColumnName("standard_minutes").IsRequired().HasComment("Standard operation duration in minutes.");
        builder.HasIndex("routing_id", nameof(RoutingOperation.Sequence)).IsUnique();
    }
}

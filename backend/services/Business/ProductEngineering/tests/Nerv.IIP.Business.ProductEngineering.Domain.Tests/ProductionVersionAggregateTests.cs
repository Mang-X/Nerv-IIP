using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

namespace Nerv.IIP.Business.ProductEngineering.Domain.Tests;

public sealed class ProductionVersionAggregateTests
{
    [Fact]
    public void Create_active_default_version_binds_published_mbom_and_routing()
    {
        var version = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-A",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 12, 31),
            1m,
            100m,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);

        Assert.Equal("SKU-FG-1000", version.SkuCode);
        Assert.Equal("mbom-A", version.MbomVersionId);
        Assert.Equal("routing-A", version.RoutingVersionId);
        Assert.True(version.IsDefault);
        Assert.Equal(ProductionVersionStatus.Active, version.Status);
        Assert.True(version.IsResolvableFor(new DateOnly(2026, 7, 1), 50m));
        Assert.IsType<ProductionVersionCreatedDomainEvent>(version.GetDomainEvents().Single());
    }

    [Fact]
    public void Create_rejects_unpublished_bound_versions_and_invalid_ranges()
    {
        Assert.Throws<InvalidOperationException>(() => ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-draft",
            "routing-A",
            new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Draft,
            EngineeringVersionStatus.Published));

        Assert.Throws<ArgumentException>(() => ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-A",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 5, 31),
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));

        Assert.Throws<ArgumentException>(() => ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-A",
            new DateOnly(2026, 6, 1),
            null,
            100m,
            10m,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
    }

    [Fact]
    public void Archived_version_cannot_be_resolved_for_new_work_orders()
    {
        var version = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-A",
            new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        version.ClearDomainEvents();

        version.Archive("superseded by ECO-2026-001");

        Assert.Equal(ProductionVersionStatus.Archived, version.Status);
        Assert.False(version.IsResolvableFor(new DateOnly(2026, 7, 1), 50m));
        Assert.IsType<ProductionVersionArchivedDomainEvent>(version.GetDomainEvents().Single());
    }
}

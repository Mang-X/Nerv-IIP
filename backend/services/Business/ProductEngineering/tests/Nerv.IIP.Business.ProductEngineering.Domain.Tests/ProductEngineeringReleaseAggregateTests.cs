using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.DomainEvents;

namespace Nerv.IIP.Business.ProductEngineering.Domain.Tests;

public sealed class ProductEngineeringReleaseAggregateTests
{
    [Fact]
    public void EngineeringDocument_registers_filestorage_reference_and_rejects_blank_file_id()
    {
        var document = EngineeringDocument.Register(
            "org-001",
            "env-dev",
            "DOC-1000",
            "A",
            "file-001",
            "pump.dwg",
            "application/dwg",
            "cad-drawing");

        Assert.Equal("file-001", document.FileId);
        Assert.Equal("DOC-1000", document.DocumentNumber);
        Assert.IsType<EngineeringDocumentRegisteredDomainEvent>(document.GetDomainEvents().Single());
        Assert.Throws<ArgumentException>(() => EngineeringDocument.Register(
            "org-001",
            "env-dev",
            "DOC-1001",
            "A",
            " ",
            "pump.dwg",
            "application/dwg",
            "cad-drawing"));
    }

    [Fact]
    public void EngineeringItem_creates_released_item_revision()
    {
        var item = EngineeringItem.CreateRevision(
            "org-001",
            "env-dev",
            "ENG-1000",
            "A",
            "Pump Assembly",
            true);

        Assert.Equal("ENG-1000", item.ItemCode);
        Assert.Equal("A", item.Revision);
        Assert.Equal(EngineeringVersionStatus.Published, item.Status);
        Assert.IsType<EngineeringItemRevisionCreatedDomainEvent>(item.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => item.Rename("Renamed Pump"));
    }

    [Fact]
    public void EngineeringBom_release_makes_component_lines_immutable()
    {
        var bom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-1000", "A", "ENG-1000")
            .AddLine("ENG-1001", 2m, "EA");

        bom.Release(new DateOnly(2026, 6, 1));

        Assert.Equal(EngineeringVersionStatus.Published, bom.Status);
        Assert.Single(bom.Lines);
        Assert.IsType<EngineeringBomReleasedDomainEvent>(bom.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => bom.AddLine("ENG-1002", 1m, "EA"));
    }

    [Fact]
    public void ManufacturingBom_release_references_released_ebom_and_recipe_lines()
    {
        var mbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 1.5m, "KG", 0.03m)
            .AddRecipeLine("mix-temperature", "65", "C");

        mbom.ReleaseFromEngineeringBom("ebom-001", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));

        Assert.Equal("ebom-001", mbom.EngineeringBomVersionId);
        Assert.Equal(EngineeringVersionStatus.Published, mbom.Status);
        Assert.Single(mbom.MaterialLines);
        Assert.Single(mbom.RecipeLines);
        Assert.IsType<ManufacturingBomReleasedDomainEvent>(mbom.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => mbom.AddMaterialLine("SKU-RM-2000", 1m, "KG", 0m));
    }

    [Fact]
    public void Routing_release_creates_ordered_work_center_operations()
    {
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(20, "WC-PACK-01", "packaging", "包装", 15)
            .AddOperation(10, "WC-MIX-01", "mixing", "混合", 30);

        routing.Release(new DateOnly(2026, 6, 1));

        Assert.Equal([10, 20], routing.Operations.Select(x => x.Sequence).ToArray());
        Assert.Equal(["mixing", "packaging"], routing.Operations.Select(x => x.OperationCode).ToArray());
        Assert.Equal(EngineeringVersionStatus.Published, routing.Status);
        Assert.IsType<RoutingReleasedDomainEvent>(routing.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => routing.AddOperation(30, "WC-QA-01", "inspection", "检验", 10));
        Assert.Throws<ArgumentException>(() => Routing.CreateDraft("org-001", "env-dev", "ROUTE-1001", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-QA-01", " ", "检验", 10));
    }

    [Fact]
    public void EngineeringChange_release_requires_approval_and_affected_versions()
    {
        var change = EngineeringChange.Open("org-001", "env-dev", "ECO-0001", "Release MBOM A")
            .Affect("engineering-bom", "ebom-001")
            .Affect("manufacturing-bom", "mbom-001")
            .Approve("approval-chain-001");

        change.Release(new DateOnly(2026, 6, 1));

        Assert.Equal(EngineeringVersionStatus.Published, change.Status);
        Assert.Equal(2, change.AffectedVersions.Count);
        Assert.IsType<EngineeringChangeReleasedDomainEvent>(change.GetDomainEvents().Single());

        var unapproved = EngineeringChange.Open("org-001", "env-dev", "ECO-0002", "Missing approval")
            .Affect("routing", "routing-001");
        Assert.Throws<InvalidOperationException>(() => unapproved.Release(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void ProductionVersion_cannot_bind_unpublished_mbom_or_routing()
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

        Assert.Throws<InvalidOperationException>(() => ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "mbom-A",
            "routing-draft",
            new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Draft));
    }
}

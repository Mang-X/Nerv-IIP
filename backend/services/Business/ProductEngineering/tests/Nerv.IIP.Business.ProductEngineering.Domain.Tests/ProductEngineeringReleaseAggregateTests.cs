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
            .AddLine(
                "ENG-1001",
                2m,
                "EA",
                isPhantom: true,
                alternateGroup: "ALT-A",
                alternatePriority: 1,
                referenceDesignators: "R1,R2",
                scrapRate: 0.02m,
                yieldRate: 0.98m,
                backflush: true);

        bom.Release(new DateOnly(2026, 6, 1));

        Assert.Equal(EngineeringVersionStatus.Published, bom.Status);
        var line = Assert.Single(bom.Lines);
        Assert.True(line.IsPhantom);
        Assert.Equal("ALT-A", line.AlternateGroup);
        Assert.Equal(1, line.AlternatePriority);
        Assert.Equal("R1,R2", line.ReferenceDesignators);
        Assert.Equal(0.02m, line.ScrapRate);
        Assert.Equal(0.98m, line.YieldRate);
        Assert.True(line.Backflush);
        Assert.IsType<EngineeringBomReleasedDomainEvent>(bom.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => bom.AddLine("ENG-1002", 1m, "EA"));
    }

    [Fact]
    public void ManufacturingBom_release_references_released_ebom_and_recipe_lines()
    {
        var mbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine(
                "SKU-RM-1000",
                1.5m,
                "KG",
                0.03m,
                isPhantom: false,
                alternateGroup: "ALT-RM",
                alternatePriority: 1,
                substituteSkuCodes: "SKU-RM-1001;SKU-RM-1002",
                referenceDesignators: "BATCH-A",
                yieldRate: 0.97m,
                backflush: true)
            .AddRecipeLine("mix-temperature", "65", "C");

        mbom.ReleaseFromEngineeringBom("ebom-001", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));

        Assert.Equal("ebom-001", mbom.EngineeringBomVersionId);
        Assert.Equal(EngineeringVersionStatus.Published, mbom.Status);
        var materialLine = Assert.Single(mbom.MaterialLines);
        Assert.Equal("ALT-RM", materialLine.AlternateGroup);
        Assert.Equal("SKU-RM-1001;SKU-RM-1002", materialLine.SubstituteSkuCodes);
        Assert.Equal("BATCH-A", materialLine.ReferenceDesignators);
        Assert.Equal(0.97m, materialLine.YieldRate);
        Assert.True(materialLine.Backflush);
        Assert.Single(mbom.RecipeLines);
        Assert.IsType<ManufacturingBomReleasedDomainEvent>(mbom.GetDomainEvents().Single());
        Assert.Throws<InvalidOperationException>(() => mbom.AddMaterialLine("SKU-RM-2000", 1m, "KG", 0m));
    }

    [Fact]
    public void Routing_release_creates_ordered_work_center_operations()
    {
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(20, "WC-PACK-01", "packaging", "包装", setupMinutes: 3, runMinutes: 12, teardownMinutes: 0, controlKey: "PACK", requiresReporting: true, requiresQualityInspection: false, isOutsourced: false)
            .AddOperation(10, "WC-MIX-01", "mixing", "混合", setupMinutes: 5, runMinutes: 30, teardownMinutes: 2, controlKey: "MIX", requiresReporting: true, requiresQualityInspection: true, isOutsourced: false);

        routing.Release(new DateOnly(2026, 6, 1));

        Assert.Equal([10, 20], routing.Operations.Select(x => x.Sequence).ToArray());
        Assert.Equal(["mixing", "packaging"], routing.Operations.Select(x => x.OperationCode).ToArray());
        var mixing = routing.Operations.First();
        Assert.Equal(5, mixing.SetupMinutes);
        Assert.Equal(30, mixing.RunMinutes);
        Assert.Equal(2, mixing.TeardownMinutes);
        Assert.Equal(37, mixing.StandardMinutes);
        Assert.Equal("MIX", mixing.ControlKey);
        Assert.True(mixing.RequiresQualityInspection);
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
    public void EngineeringChange_rejects_conflicting_duplicate_successor()
    {
        var change = EngineeringChange.Open("org-001", "env-dev", "ECO-0003", "Supersede EBOM")
            .Affect("Engineering-Bom", "EBOM-001:A", "EBOM-001:B")
            .Affect("engineering-bom", "ebom-001:a", "ebom-001:b");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            change.Affect("engineering-bom", "ebom-001:a", "EBOM-001:C"));

        Assert.Contains("can only declare one successor", exception.Message, StringComparison.OrdinalIgnoreCase);
        var affectedVersion = Assert.Single(change.AffectedVersions);
        Assert.Equal("EBOM-001:B", affectedVersion.SupersededByVersionId);
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

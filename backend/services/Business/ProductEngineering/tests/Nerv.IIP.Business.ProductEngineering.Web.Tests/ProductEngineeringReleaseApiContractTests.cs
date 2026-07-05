using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Auth;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductEngineering;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.ProductionVersions;
using Nerv.IIP.Business.ProductEngineering.Web.Endpoints.StandardOperations;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringReleaseApiContractTests
{
    [Fact]
    public void Product_engineering_release_endpoint_contracts_cover_issue_127_surface()
    {
        var contracts = ProductEngineeringEndpointContracts.All;

        Assert.Equal(26, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/documents" && x.PermissionCode == EngineeringPermissionCodes.DocumentsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/documents" && x.PermissionCode == EngineeringPermissionCodes.DocumentsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/documents/{documentNumber}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.DocumentsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/items" && x.PermissionCode == EngineeringPermissionCodes.ItemsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/items" && x.PermissionCode == EngineeringPermissionCodes.ItemsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/items/{itemCode}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.ItemsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/manufacturing-boms/release" && x.PermissionCode == EngineeringPermissionCodes.BomsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/routings/release" && x.PermissionCode == EngineeringPermissionCodes.RoutingsManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/release" && x.PermissionCode == EngineeringPermissionCodes.ChangesManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/cancel-scheduled" && x.PermissionCode == EngineeringPermissionCodes.ChangesManage);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/reschedule" && x.PermissionCode == EngineeringPermissionCodes.ChangesManage);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms/{bomCode}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms/explosion" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms/where-used" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/boms/diff" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "POST" && x.Route == "/api/business/v1/engineering/engineering-changes/impact-preview" && x.PermissionCode == EngineeringPermissionCodes.ChangesRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms/{bomCode}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms/explosion" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms/where-used" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/routings" && x.PermissionCode == EngineeringPermissionCodes.RoutingsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/routings/{routingCode}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.RoutingsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-changes" && x.PermissionCode == EngineeringPermissionCodes.ChangesRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-changes/{changeNumber}" && x.PermissionCode == EngineeringPermissionCodes.ChangesRead);
        Assert.All(contracts, contract =>
        {
            Assert.StartsWith("/api/business/v1/engineering", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.PermissionCode, EngineeringPermissionCodes.All);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("Business", contract.OperationId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Product_engineering_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var contracts = ProductEngineeringEndpointContracts.All
            .Select(contract => (contract.EndpointType, contract.Route))
            .Concat(ProductionVersionEndpointContracts.All.Select(contract => (contract.EndpointType, contract.Route)))
            .Concat(StandardOperationEndpointContracts.All.Select(contract => (contract.EndpointType, contract.Route)))
            .Append((EndpointType: typeof(GetMasterDataWorkCenterUsageEndpoint), Route: "/api/business/v1/engineering/internal/master-data/work-centers/{workCenterCode}/usage"))
            .ToArray();

        var failures = contracts
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(typeof(RegisterEngineeringDocumentEndpoint))]
    [InlineData(typeof(ListEngineeringDocumentsEndpoint))]
    [InlineData(typeof(GetEngineeringDocumentEndpoint))]
    [InlineData(typeof(CreateEngineeringItemRevisionEndpoint))]
    [InlineData(typeof(ListEngineeringItemsEndpoint))]
    [InlineData(typeof(GetEngineeringItemEndpoint))]
    [InlineData(typeof(ReleaseEngineeringBomEndpoint))]
    [InlineData(typeof(GetEngineeringBomEndpoint))]
    [InlineData(typeof(GetEngineeringBomExplosionEndpoint))]
    [InlineData(typeof(GetEngineeringBomWhereUsedEndpoint))]
    [InlineData(typeof(GetBomDiffEndpoint))]
    [InlineData(typeof(GetEngineeringChangeImpactPreviewEndpoint))]
    [InlineData(typeof(ReleaseManufacturingBomEndpoint))]
    [InlineData(typeof(GetManufacturingBomEndpoint))]
    [InlineData(typeof(GetManufacturingBomExplosionEndpoint))]
    [InlineData(typeof(GetManufacturingBomWhereUsedEndpoint))]
    [InlineData(typeof(ReleaseRoutingEndpoint))]
    [InlineData(typeof(GetRoutingEndpoint))]
    [InlineData(typeof(ReleaseEngineeringChangeEndpoint))]
    [InlineData(typeof(CancelScheduledEngineeringChangeEndpoint))]
    [InlineData(typeof(RescheduleEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringChangesEndpoint))]
    [InlineData(typeof(GetEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringBomsEndpoint))]
    [InlineData(typeof(ListManufacturingBomsEndpoint))]
    [InlineData(typeof(ListRoutingsEndpoint))]
    [InlineData(typeof(GetMasterDataWorkCenterUsageEndpoint))]
    public void Product_engineering_release_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public async Task List_engineering_boms_returns_component_lines_for_version_details()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-1000", "A", "ENG-1000")
            .AddLine("ENG-1001", 2m, "EA")
            .AddLine("ENG-1002", 1m, "EA");
        bom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(bom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListEngineeringBomsQueryHandler(dbContext).Handle(
            new ListEngineeringBomsQuery("org-001", "env-dev", " ENG-1000 ", "Published"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("Published", item.Status);
        Assert.Collection(
            item.Lines,
            line =>
            {
                Assert.Equal("ENG-1001", line.ChildItemCode);
                Assert.Equal(2m, line.Quantity);
                Assert.Equal("EA", line.UnitOfMeasureCode);
            },
            line => Assert.Equal("ENG-1002", line.ChildItemCode));
    }

    [Fact]
    public async Task Release_engineering_bom_rejects_unknown_master_data_sku_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var masterData = new RecordingMasterDataReferenceValidator(("sku", "ENG-1000"));
        var handler = new ReleaseEngineeringBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            masterData);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringBomCommand(
                "org-001",
                "env-dev",
                "EBOM-MD",
                "A",
                "ENG-1000",
                new DateOnly(2026, 6, 1),
                [new BomLineCommand("ENG-MISSING", 1m, "EA")]),
            CancellationToken.None));

        Assert.Contains("MasterData", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ENG-MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            [("sku", "ENG-1000"), ("sku", "ENG-MISSING")],
            masterData.Requests);
        Assert.Empty(dbContext.EngineeringBoms);
    }

    [Fact]
    public async Task Release_manufacturing_bom_rejects_unknown_master_data_material_sku_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-MD", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-MISSING", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var masterData = new RecordingMasterDataReferenceValidator(("sku", "SKU-FG-1000"));
        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            masterData);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-MD",
                "A",
                "SKU-FG-1000",
                "EBOM-MD",
                "A",
                new DateOnly(2026, 6, 1),
                [new ManufacturingBomMaterialLineCommand("SKU-RM-MISSING", 1m, "EA", 0m)],
                []),
            CancellationToken.None));

        Assert.Contains("MasterData", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SKU-RM-MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            [("sku", "SKU-FG-1000"), ("sku", "SKU-RM-MISSING")],
            masterData.Requests);
        Assert.Empty(dbContext.ManufacturingBoms);
    }

    [Fact]
    public async Task Release_routing_rejects_unknown_master_data_work_center_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(NewStandardOperation("mixing", "WC-MISSING"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var masterData = new RecordingMasterDataReferenceValidator(("sku", "SKU-FG-1000"));
        var handler = new ReleaseRoutingCommandHandler(
            new RoutingRepository(dbContext),
            new StandardOperationRepository(dbContext),
            masterData);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseRoutingCommand(
                "org-001",
                "env-dev",
                "ROUTE-MD",
                "A",
                "SKU-FG-1000",
                new DateOnly(2026, 6, 1),
                [new RoutingOperationCommand(10, null, "mixing", null)]),
            CancellationToken.None));

        Assert.Contains("MasterData", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WC-MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            [("sku", "SKU-FG-1000"), ("work-center", "WC-MISSING")],
            masterData.Requests);
        Assert.Empty(dbContext.Routings);
    }

    [Fact]
    public async Task Get_engineering_bom_returns_complete_component_lines_by_code_and_revision()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-1000", "A", "ENG-1000")
            .AddLine("ENG-1001", 2m, "EA");
        bom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(bom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetEngineeringBomQueryHandler(dbContext).Handle(
            new GetEngineeringBomQuery("org-001", "env-dev", "EBOM-1000", "A"),
            CancellationToken.None);

        Assert.Equal("EBOM-1000", detail.BomCode);
        Assert.Equal("A", detail.Revision);
        Assert.Equal("Published", detail.Status);
        Assert.Equal("ENG-1001", Assert.Single(detail.Lines).ChildItemCode);
    }

    [Fact]
    public async Task List_manufacturing_boms_returns_released_material_lines_for_mrp_snapshots()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 3m, "pcs", 0m);
        bom.ReleaseFromEngineeringBom("EBOM-1000:A", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));
        dbContext.ManufacturingBoms.Add(bom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", " SKU-FG-1000 ", "Published"),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("MBOM-1000", item.BomCode);
        var line = Assert.Single(item.MaterialLines);
        Assert.Equal("SKU-RM-1000", line.SkuCode);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal("pcs", line.UnitOfMeasureCode);
    }

    [Fact]
    public async Task List_manufacturing_boms_returns_recipe_lines_for_version_details()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 3m, "pcs", 0m)
            .AddRecipeLine("mix-temperature", "65", "C");
        bom.ReleaseFromEngineeringBom("EBOM-1000:A", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));
        dbContext.ManufacturingBoms.Add(bom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", "SKU-FG-1000", "Published"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        var recipe = Assert.Single(item.RecipeLines);
        Assert.Equal("mix-temperature", recipe.ParameterCode);
        Assert.Equal("65", recipe.TargetValue);
        Assert.Equal("C", recipe.UnitOfMeasureCode);
    }

    [Fact]
    public async Task Get_manufacturing_bom_returns_material_and_recipe_lines_by_code_and_revision()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 3m, "pcs", 0m)
            .AddRecipeLine("mix-temperature", "65", "C");
        bom.ReleaseFromEngineeringBom("EBOM-1000:A", EngineeringVersionStatus.Published, new DateOnly(2026, 6, 1));
        dbContext.ManufacturingBoms.Add(bom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetManufacturingBomQueryHandler(dbContext).Handle(
            new GetManufacturingBomQuery("org-001", "env-dev", "MBOM-1000", "A"),
            CancellationToken.None);

        Assert.Equal("MBOM-1000", detail.BomCode);
        Assert.Equal("SKU-RM-1000", Assert.Single(detail.MaterialLines).SkuCode);
        Assert.Equal("mix-temperature", Assert.Single(detail.RecipeLines).ParameterCode);
    }

    [Fact]
    public async Task Bom_diff_returns_structured_added_removed_replaced_and_field_changes()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DIFF", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-KEEP", 2m, "EA", scrapRate: 0.01m, yieldRate: 0.98m)
            .AddLine("SKU-RM-REPLACE-OLD", 1m, "EA", alternateGroup: "main")
            .AddLine("SKU-RM-REMOVE", 1m, "EA");
        source.Release(new DateOnly(2026, 1, 1));
        var target = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DIFF", "B", "SKU-FG-1000")
            .AddLine("SKU-RM-KEEP", 2.5m, "KG", scrapRate: 0.03m, yieldRate: 0.95m)
            .AddLine("SKU-RM-REPLACE-NEW", 1m, "EA", alternateGroup: "main")
            .AddLine("SKU-RM-ADD", 4m, "EA");
        target.Release(new DateOnly(2026, 4, 1));
        dbContext.EngineeringBoms.AddRange(source, target);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetBomDiffQueryHandler(dbContext).Handle(
            new GetBomDiffQuery("org-001", "env-dev", "EngineeringBom", "EBOM-DIFF", "A", "EBOM-DIFF", "B"),
            CancellationToken.None);

        Assert.Equal("EngineeringBom", response.BomKind);
        Assert.Contains(response.Lines, line => line.ChangeType == "added" && line.NewItemCode == "SKU-RM-ADD");
        Assert.Contains(response.Lines, line => line.ChangeType == "removed" && line.OldItemCode == "SKU-RM-REMOVE");
        Assert.Contains(response.Lines, line =>
            line.ChangeType == "replaced" &&
            line.OldItemCode == "SKU-RM-REPLACE-OLD" &&
            line.NewItemCode == "SKU-RM-REPLACE-NEW");
        var changed = Assert.Single(response.Lines, line => line.ChangeType == "changed");
        Assert.Equal("SKU-RM-KEEP", changed.OldItemCode);
        Assert.Equal("SKU-RM-KEEP", changed.NewItemCode);
        Assert.Contains(changed.FieldChanges, change => change.FieldName == "quantity" && change.OldValue == "2" && change.NewValue == "2.5");
        Assert.Contains(changed.FieldChanges, change => change.FieldName == "unitOfMeasureCode" && change.OldValue == "EA" && change.NewValue == "KG");
        Assert.Contains(changed.FieldChanges, change => change.FieldName == "scrapRate" && change.OldValue == "0.01" && change.NewValue == "0.03");
        Assert.Contains(changed.FieldChanges, change => change.FieldName == "yieldRate" && change.OldValue == "0.98" && change.NewValue == "0.95");
    }

    [Fact]
    public async Task Engineering_change_impact_preview_expands_affected_versions_to_downstream_candidates()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-IMPACT", "A", "SKU-FG-3000")
            .AddLine("SKU-RM-3000", 1m, "EA");
        ebom.Release(new DateOnly(2026, 1, 1));
        var mbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-IMPACT", "A", "SKU-FG-3000")
            .AddMaterialLine("SKU-RM-3000", 1m, "EA", 0m);
        mbom.ReleaseFromEngineeringBom("EBOM-IMPACT:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-IMPACT", "A", "SKU-FG-3000")
            .AddOperation(10, "WC-FINAL", "assembly", "Assembly", 30);
        routing.Release(new DateOnly(2026, 1, 1));
        var productionVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-IMPACT:A",
            "ROUTE-IMPACT:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.EngineeringBoms.Add(ebom);
        dbContext.ManufacturingBoms.Add(mbom);
        dbContext.Routings.Add(routing);
        dbContext.ProductionVersions.Add(productionVersion);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetEngineeringChangeImpactPreviewQueryHandler(dbContext).Handle(
            new GetEngineeringChangeImpactPreviewQuery(
                "org-001",
                "env-dev",
                new DateOnly(2026, 6, 1),
                [new EngineeringChangeImpactAffectedVersionInput("engineering-bom", "EBOM-IMPACT:A")]),
            CancellationToken.None);

        var productionVersionId = productionVersion.Id.Id.ToString("D");
        Assert.Contains(response.Nodes, node => node.NodeType == "engineering-bom" && node.VersionId == "EBOM-IMPACT:A" && node.ImpactLevel == "direct");
        Assert.Contains(response.Nodes, node => node.NodeType == "manufacturing-bom" && node.VersionId == "MBOM-IMPACT:A" && node.ImpactLevel == "derived");
        Assert.Contains(response.Nodes, node => node.NodeType == "routing" && node.VersionId == "ROUTE-IMPACT:A" && node.ImpactLevel == "derived");
        Assert.Contains(response.Nodes, node => node.NodeType == "production-version" && node.VersionId == productionVersionId && node.ImpactLevel == "downstream");
        Assert.Contains(response.Nodes, node => node.NodeType == "mrp-candidate" && node.RelatedVersionId == productionVersionId);
        Assert.Contains(response.Nodes, node => node.NodeType == "mes-work-order-candidate" && node.RelatedVersionId == productionVersionId);
        Assert.Contains(response.Nodes, node => node.NodeType == "aps-plan-candidate" && node.RelatedVersionId == productionVersionId && node.ConsoleRoute == "/scheduling");
        Assert.Contains(response.Risks, risk => risk.Code == "downstream-execution-impact" && risk.Severity == "warning");
    }

    [Fact]
    public async Task Engineering_change_impact_preview_normalizes_production_version_guid_before_adding_candidates()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var productionVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-4000",
            "MBOM-PV:A",
            "ROUTE-PV:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(productionVersion);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var productionVersionId = productionVersion.Id.Id.ToString("D");
        var response = await new GetEngineeringChangeImpactPreviewQueryHandler(dbContext).Handle(
            new GetEngineeringChangeImpactPreviewQuery(
                "org-001",
                "env-dev",
                new DateOnly(2026, 6, 1),
                [new EngineeringChangeImpactAffectedVersionInput("production-version", productionVersion.Id.Id.ToString("B").ToUpperInvariant())]),
            CancellationToken.None);

        var productionVersionNodes = response.Nodes
            .Where(node => node.NodeType == "production-version")
            .ToArray();
        var node = Assert.Single(productionVersionNodes);
        Assert.Equal(productionVersionId, node.VersionId);
        Assert.Equal("direct", node.ImpactLevel);
        Assert.Contains(response.Nodes, candidate => candidate.NodeType == "mrp-candidate" && candidate.RelatedVersionId == productionVersionId);
    }

    [Fact]
    public async Task Engineering_bom_explosion_selects_effective_versions_and_rolls_quantities_with_line_factors()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fgBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FG", "A", "SKU-FG")
            .AddLine("SKU-SUB", 2m, "EA", isPhantom: true, alternateGroup: "ALT-A", alternatePriority: 1, referenceDesignators: "R1,R2", scrapRate: 0.10m, yieldRate: 0.95m);
        fgBom.Release(new DateOnly(2026, 1, 1));
        var subBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-SUB", "A", "SKU-SUB")
            .AddLine("SKU-RM", 3m, "EA");
        subBom.Release(new DateOnly(2026, 1, 1));
        dbContext.EngineeringBoms.AddRange(fgBom, subBom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetEngineeringBomExplosionQueryHandler(dbContext).Handle(
            new GetEngineeringBomExplosionQuery("org-001", "env-dev", "SKU-FG", new DateOnly(2026, 6, 1), 10m),
            CancellationToken.None);

        Assert.Equal("EngineeringBom", response.BomKind);
        Assert.Equal("EBOM-FG", response.Root.BomCode);
        Assert.Equal("A", response.Root.Revision);
        var sub = Assert.Single(response.Root.Children!);
        Assert.Equal("SKU-SUB", sub.ItemCode);
        Assert.True(sub.IsPhantom);
        Assert.Equal("ALT-A", sub.AlternateGroup);
        Assert.Equal("R1,R2", sub.ReferenceDesignators);
        Assert.Equal(2m, sub.LineQuantity);
        Assert.Equal(23.1579m, Math.Round(sub.RequiredQuantity, 4));
        var raw = Assert.Single(sub.Children!);
        Assert.Equal("SKU-RM", raw.ItemCode);
        Assert.Equal(69.4737m, Math.Round(raw.RequiredQuantity, 4));
        Assert.Contains(response.Diagnostics, x => x.Code == "missing-child-bom" && x.ItemCode == "SKU-RM");
    }

    [Fact]
    public async Task Manufacturing_bom_explosion_prefers_production_version_selection_for_lot_size()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldMbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-FG", "A", "SKU-FG")
            .AddMaterialLine("SKU-OLD", 1m, "EA", 0m);
        oldMbom.ReleaseFromEngineeringBom("EBOM-FG:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        var selectedMbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-FG", "B", "SKU-FG")
            .AddMaterialLine("SKU-RM", 5m, "EA", 0.05m, substituteSkuCodes: "SKU-RM-ALT");
        selectedMbom.ReleaseFromEngineeringBom("EBOM-FG:B", EngineeringVersionStatus.Published, new DateOnly(2026, 2, 1));
        dbContext.ManufacturingBoms.AddRange(oldMbom, selectedMbom);
        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG",
            "MBOM-FG:B",
            "ROUTE-FG:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            100m,
            1,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetManufacturingBomExplosionQueryHandler(dbContext).Handle(
            new GetManufacturingBomExplosionQuery("org-001", "env-dev", "SKU-FG", new DateOnly(2026, 6, 1), 50m),
            CancellationToken.None);

        Assert.Equal("RootProductionVersion", response.SelectionMode);
        Assert.Equal("MBOM-FG", response.Root.BomCode);
        Assert.Equal("B", response.Root.Revision);
        var material = Assert.Single(response.Root.Children!);
        Assert.Equal("SKU-RM", material.ItemCode);
        Assert.Equal("SKU-RM-ALT", material.SubstituteSkuCodes);
        Assert.Equal(262.5m, material.RequiredQuantity);
    }

    [Fact]
    public async Task Engineering_bom_explosion_reports_cycle_diagnostics_without_recursing_forever()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bomA = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-A", "A", "SKU-A")
            .AddLine("SKU-B", 1m, "EA");
        bomA.Release(new DateOnly(2026, 1, 1));
        var bomB = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-B", "A", "SKU-B")
            .AddLine("SKU-A", 1m, "EA");
        bomB.Release(new DateOnly(2026, 1, 1));
        dbContext.EngineeringBoms.AddRange(bomA, bomB);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetEngineeringBomExplosionQueryHandler(dbContext).Handle(
            new GetEngineeringBomExplosionQuery("org-001", "env-dev", "SKU-A", new DateOnly(2026, 6, 1), 1m),
            CancellationToken.None);

        Assert.Contains(response.Diagnostics, x => x.Code == "cycle-detected" && x.ItemCode == "SKU-A");
    }

    [Fact]
    public async Task Engineering_bom_explosion_detects_root_cycle_case_insensitively()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bomA = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-A", "A", "SKU-A")
            .AddLine("sku-a", 1m, "EA");
        bomA.Release(new DateOnly(2026, 1, 1));
        dbContext.EngineeringBoms.Add(bomA);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetEngineeringBomExplosionQueryHandler(dbContext).Handle(
            new GetEngineeringBomExplosionQuery("org-001", "env-dev", "SKU-A", new DateOnly(2026, 6, 1), 1m),
            CancellationToken.None);

        Assert.Contains(response.Diagnostics, x => x.Code == "cycle-detected" && x.ItemCode == "sku-a");
    }

    [Fact]
    public async Task Engineering_bom_explosion_uses_created_time_tiebreaker_for_same_effective_date()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rev9 = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FG", "9", "SKU-FG")
            .AddLine("SKU-OLD", 1m, "EA");
        rev9.Release(new DateOnly(2026, 1, 1));
        SetCreatedAtUtc(rev9, createdAt);
        var rev10 = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FG", "10", "SKU-FG")
            .AddLine("SKU-NEW", 1m, "EA");
        rev10.Release(new DateOnly(2026, 1, 1));
        SetCreatedAtUtc(rev10, createdAt.AddMinutes(1));
        dbContext.EngineeringBoms.AddRange(rev9, rev10);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetEngineeringBomExplosionQueryHandler(dbContext).Handle(
            new GetEngineeringBomExplosionQuery("org-001", "env-dev", "SKU-FG", new DateOnly(2026, 6, 1), 1m),
            CancellationToken.None);

        Assert.Equal("10", response.Root.Revision);
        Assert.Equal("SKU-NEW", Assert.Single(response.Root.Children!).ItemCode);
    }

    [Fact]
    public async Task Manufacturing_bom_explosion_reports_unresolved_production_version_before_effective_fallback()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var effectiveMbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-FG", "A", "SKU-FG")
            .AddMaterialLine("SKU-RM", 1m, "EA", 0m);
        effectiveMbom.ReleaseFromEngineeringBom("EBOM-FG:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        dbContext.ManufacturingBoms.Add(effectiveMbom);
        dbContext.ProductionVersions.Add(ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG",
            "legacy-version-id",
            "ROUTE-FG:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            1,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetManufacturingBomExplosionQueryHandler(dbContext).Handle(
            new GetManufacturingBomExplosionQuery("org-001", "env-dev", "SKU-FG", new DateOnly(2026, 6, 1), 1m),
            CancellationToken.None);

        Assert.Equal("EffectiveBom", response.SelectionMode);
        Assert.Contains(response.Diagnostics, x => x.Code == "production-version-unresolved" && x.ItemCode == "SKU-FG");
        Assert.Equal("MBOM-FG", response.Root.BomCode);
    }

    [Fact]
    public async Task Engineering_bom_where_used_returns_only_current_effective_parent_versions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FG", "A", "SKU-FG")
            .AddLine("SKU-RM", 1m, "EA");
        oldBom.Release(new DateOnly(2026, 1, 1));
        SetCreatedAtUtc(oldBom, createdAt);
        var currentBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FG", "B", "SKU-FG")
            .AddLine("SKU-RM", 2m, "EA");
        currentBom.Release(new DateOnly(2026, 3, 1));
        SetCreatedAtUtc(currentBom, createdAt.AddMinutes(1));
        var otherParent = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-OTHER", "A", "SKU-OTHER")
            .AddLine("SKU-RM", 4m, "EA");
        otherParent.Release(new DateOnly(2026, 2, 1));
        dbContext.EngineeringBoms.AddRange(oldBom, currentBom, otherParent);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetEngineeringBomWhereUsedQueryHandler(dbContext).Handle(
            new GetEngineeringBomWhereUsedQuery("org-001", "env-dev", "SKU-RM", new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.Collection(
            response.Items.OrderBy(x => x.ParentItemCode),
            usage =>
            {
                Assert.Equal("SKU-FG", usage.ParentItemCode);
                Assert.Equal("B", usage.Revision);
                Assert.Equal(2m, usage.LineQuantity);
            },
            usage =>
            {
                Assert.Equal("SKU-OTHER", usage.ParentItemCode);
                Assert.Equal("A", usage.Revision);
            });
    }

    [Fact]
    public async Task Manufacturing_bom_where_used_returns_direct_released_parent_context()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldMbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-FG", "A", "SKU-FG")
            .AddMaterialLine("SKU-RM", 1m, "EA", 0m);
        oldMbom.ReleaseFromEngineeringBom("EBOM-FG:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        SetCreatedAtUtc(oldMbom, createdAt);
        var mbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-FG", "B", "SKU-FG")
            .AddMaterialLine("SKU-RM", 2m, "EA", 0.02m, isPhantom: false, referenceDesignators: "P1");
        mbom.ReleaseFromEngineeringBom("EBOM-FG:B", EngineeringVersionStatus.Published, new DateOnly(2026, 3, 1));
        SetCreatedAtUtc(mbom, createdAt.AddMinutes(1));
        dbContext.ManufacturingBoms.AddRange(oldMbom, mbom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetManufacturingBomWhereUsedQueryHandler(dbContext).Handle(
            new GetManufacturingBomWhereUsedQuery("org-001", "env-dev", "SKU-RM", new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        var usage = Assert.Single(response.Items);
        Assert.Equal("ManufacturingBom", usage.BomKind);
        Assert.Equal("MBOM-FG", usage.BomCode);
        Assert.Equal("SKU-FG", usage.ParentItemCode);
        Assert.Equal(new DateOnly(2026, 3, 1), usage.EffectiveDate);
        Assert.Equal(2m, usage.LineQuantity);
        Assert.Equal(0.02m, usage.ScrapRate);
        Assert.Equal("P1", usage.ReferenceDesignators);
    }

    [Fact]
    public async Task List_routings_returns_operation_details_for_version_details()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(20, "WC-PACK-01", "packing", "Pack", 15)
            .AddOperation(10, "WC-MIX-01", "mixing", "Mix", 30);
        routing.Release(new DateOnly(2026, 6, 1));
        dbContext.Routings.Add(routing);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListRoutingsQueryHandler(dbContext).Handle(
            new ListRoutingsQuery("org-001", "env-dev", " SKU-FG-1000 ", "Published"),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Collection(
            item.Operations,
            operation =>
            {
                Assert.Equal(10, operation.Sequence);
                Assert.Equal("WC-MIX-01", operation.WorkCenterCode);
                Assert.Equal("mixing", operation.OperationCode);
            },
            operation => Assert.Equal(20, operation.Sequence));
    }

    [Fact]
    public async Task Get_routing_returns_operations_by_code_and_revision()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-MIX-01", "mixing", "Mix", 30);
        routing.Release(new DateOnly(2026, 6, 1));
        dbContext.Routings.Add(routing);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetRoutingQueryHandler(dbContext).Handle(
            new GetRoutingQuery("org-001", "env-dev", "ROUTE-1000", "A"),
            CancellationToken.None);

        Assert.Equal("ROUTE-1000", detail.RoutingCode);
        Assert.Equal("mixing", Assert.Single(detail.Operations).OperationCode);
    }

    [Fact]
    public async Task List_documents_supports_item_and_type_filters()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.EngineeringDocuments.AddRange(
            EngineeringDocument.Register("org-001", "env-dev", "DOC-1000", "A", "ENG-1000", "file-001", "shock.dwg", "application/dwg", "cad-drawing"),
            EngineeringDocument.Register("org-001", "env-dev", "DOC-1001", "A", "ENG-2000", "file-002", "manual.pdf", "application/pdf", "manual"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListEngineeringDocumentsQueryHandler(dbContext).Handle(
            new ListEngineeringDocumentsQuery("org-001", "env-dev", " ENG-1000 ", " cad-drawing ", Skip: 0, Take: 10),
            CancellationToken.None);

        var document = Assert.Single(response.Items);
        Assert.Equal("DOC-1000", document.DocumentNumber);
        Assert.Equal("ENG-1000", document.ItemCode);
        Assert.Equal("cad-drawing", document.DocumentType);
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task List_items_supports_status_and_revision_details()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.EngineeringItems.AddRange(
            EngineeringItem.CreateRevision("org-001", "env-dev", "ENG-1000", "A", "Shock absorber", release: true),
            EngineeringItem.CreateRevision("org-001", "env-dev", "ENG-1000", "B", "Shock absorber draft", release: false));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListEngineeringItemsQueryHandler(dbContext).Handle(
            new ListEngineeringItemsQuery("org-001", "env-dev", " ENG-1000 ", "Published", Skip: 0, Take: 10),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("ENG-1000", item.ItemCode);
        Assert.Equal("A", item.Revision);
        Assert.Equal("Published", item.Status);
    }

    [Fact]
    public async Task List_engineering_changes_returns_affected_versions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var change = EngineeringChange.Open("org-001", "env-dev", "ECO-1000", "Initial release")
            .Approve("approval-001")
            .Affect("engineering-bom", "EBOM-1000:A");
        change.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringChanges.Add(change);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListEngineeringChangesQueryHandler(dbContext).Handle(
            new ListEngineeringChangesQuery("org-001", "env-dev", "Published", Skip: 0, Take: 10),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("ECO-1000", item.ChangeNumber);
        Assert.Equal("Published", item.Status);
        Assert.Equal("EBOM-1000:A", Assert.Single(item.AffectedVersions).VersionId);
    }

    [Fact]
    public async Task List_queries_reject_unknown_status_filters()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.ThrowsAsync<KnownException>(() => new ListEngineeringBomsQueryHandler(dbContext).Handle(
            new ListEngineeringBomsQuery("org-001", "env-dev", null, "Released"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", null, "Released"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new ListRoutingsQueryHandler(dbContext).Handle(
            new ListRoutingsQuery("org-001", "env-dev", null, "Released"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new ListEngineeringItemsQueryHandler(dbContext).Handle(
            new ListEngineeringItemsQuery("org-001", "env-dev", null, "Released"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new ListEngineeringChangesQueryHandler(dbContext).Handle(
            new ListEngineeringChangesQuery("org-001", "env-dev", "Released"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Get_document_item_and_change_return_known_exception_when_missing()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringDocumentQueryHandler(dbContext).Handle(
            new GetEngineeringDocumentQuery("org-001", "env-dev", "DOC-MISSING", "A"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringItemQueryHandler(dbContext).Handle(
            new GetEngineeringItemQuery("org-001", "env-dev", "ITEM-MISSING", "A"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringChangeQueryHandler(dbContext).Handle(
            new GetEngineeringChangeQuery("org-001", "env-dev", "ECO-MISSING"),
            CancellationToken.None));
    }

    [Fact]
    public async Task List_manufacturing_boms_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ManufacturingBoms.AddRange(
            ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-001", "A", "SKU-FG-1000"),
            ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-002", "A", "SKU-FG-1000"),
            ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-003", "A", "SKU-FG-1000"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", "SKU-FG-1000", null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("MBOM-002", item.BomCode);

        var firstPage = await new ListManufacturingBomsQueryHandler(dbContext).Handle(
            new ListManufacturingBomsQuery("org-001", "env-dev", "SKU-FG-1000", null, Skip: -10, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, firstPage.Total);
        Assert.Equal("MBOM-001", Assert.Single(firstPage.Items).BomCode);
    }

    [Fact]
    public void Release_command_validators_reject_blank_scope_and_file_references()
    {
        var documentValidator = new RegisterEngineeringDocumentCommandValidator();
        var documentResult = documentValidator.Validate(new RegisterEngineeringDocumentCommand(
            "",
            "env-dev",
            "DOC-1000",
            "A",
            "",
            "pump.dwg",
            "application/dwg",
            "cad-drawing"));

        Assert.False(documentResult.IsValid);
        Assert.Contains(documentResult.Errors, x => IsValidationFailureFor(x, nameof(RegisterEngineeringDocumentCommand.OrganizationId)));
        Assert.Contains(documentResult.Errors, x => IsValidationFailureFor(x, nameof(RegisterEngineeringDocumentCommand.FileId)));

        var bomValidator = new ReleaseEngineeringBomCommandValidator();
        var bomResult = bomValidator.Validate(new ReleaseEngineeringBomCommand(
            "org-001",
            "",
            "EBOM-1000",
            "A",
            "ENG-1000",
            new DateOnly(2026, 6, 1),
            []));

        Assert.False(bomResult.IsValid);
        Assert.Contains(bomResult.Errors, x => IsValidationFailureFor(x, nameof(ReleaseEngineeringBomCommand.EnvironmentId)));
        Assert.Contains(bomResult.Errors, x => IsValidationFailureFor(x, nameof(ReleaseEngineeringBomCommand.Lines)));

        var routingCommandResult = new ReleaseRoutingCommandValidator().Validate(new ReleaseRoutingCommand(
            "org-001",
            "env-dev",
            "ROUTE-1000",
            "A",
            "SKU-FG-1000",
            new DateOnly(2026, 6, 1),
            [new RoutingOperationCommand(10, null, "mixing", null)]));
        var routingRequestResult = new ReleaseRoutingRequestValidator().Validate(new ReleaseRoutingRequest(
            "org-001",
            "env-dev",
            "ROUTE-1000",
            "A",
            "SKU-FG-1000",
            new DateOnly(2026, 6, 1),
            [new RoutingOperationCommand(10, null, "mixing", null)]));

        Assert.True(routingCommandResult.IsValid);
        Assert.True(routingRequestResult.IsValid);
    }

    [Fact]
    public void Release_manufacturing_bom_validator_rejects_invalid_material_lines()
    {
        var result = new ReleaseManufacturingBomCommandValidator().Validate(new ReleaseManufacturingBomCommand(
            "org-001",
            "env-dev",
            "MBOM-1000",
            "A",
            "SKU-FG-1000",
            "EBOM-1000",
            "A",
            new DateOnly(2026, 6, 1),
            [new ManufacturingBomMaterialLineCommand(null!, 0m, "", -0.01m)],
            []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => IsValidationFailureMessageFor(x, nameof(ManufacturingBomMaterialLineCommand.SkuCode)));
        Assert.Contains(result.Errors, x => IsValidationFailureMessageFor(x, nameof(ManufacturingBomMaterialLineCommand.Quantity)));
        Assert.Contains(result.Errors, x => IsValidationFailureMessageFor(x, nameof(ManufacturingBomMaterialLineCommand.UnitOfMeasureCode)));
        Assert.Contains(result.Errors, x => IsValidationFailureMessageFor(x, nameof(ManufacturingBomMaterialLineCommand.ScrapRate)));
    }

    [Fact]
    public async Task Release_manufacturing_bom_rejects_duplicate_business_key_and_stores_unique_ebom_version_reference()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-1000", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-1000", 2m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        dbContext.ManufacturingBoms.Add(ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-1000", "A", "SKU-FG-1000"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        var command = new ReleaseManufacturingBomCommand(
            "org-001",
            "env-dev",
            "MBOM-1000",
            "A",
            "SKU-FG-1000",
            "EBOM-1000",
            "A",
            new DateOnly(2026, 6, 1),
            [new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1.5m, "KG", 0m)],
            []);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);

        var createdCommand = command with { Revision = "B" };
        await handler.Handle(createdCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await dbContext.ManufacturingBoms.SingleAsync(x => x.BomCode == "MBOM-1000" && x.Revision == "B", CancellationToken.None);
        Assert.Equal("EBOM-1000:A", created.EngineeringBomVersionId);
    }

    [Fact]
    public async Task Release_manufacturing_bom_rejects_missing_ebom_child_sku_material_line()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-CONTINUITY", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-1000", 1m, "EA")
            .AddLine("SKU-RM-2000", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-CONTINUITY",
                "A",
                "SKU-FG-1000",
                "EBOM-CONTINUITY",
                "A",
                new DateOnly(2026, 6, 1),
                [new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1m, "EA", 0m)],
                []),
            CancellationToken.None));

        Assert.Contains("SKU-RM-2000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Release_manufacturing_bom_trims_parent_sku_for_continuity_validation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-CONTINUITY", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-1000", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        await handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-CONTINUITY",
                "A",
                " SKU-FG-1000 ",
                "EBOM-CONTINUITY",
                "A",
                new DateOnly(2026, 6, 1),
                [new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1m, "EA", 0m)],
                []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await dbContext.ManufacturingBoms.SingleAsync(x => x.BomCode == "MBOM-CONTINUITY", CancellationToken.None);
        Assert.Equal("SKU-FG-1000", created.SkuCode);
    }

    [Fact]
    public async Task Release_manufacturing_bom_rejects_ebom_parent_sku_mismatch()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-CONTINUITY", "A", "SKU-FG-OTHER")
            .AddLine("SKU-RM-1000", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-CONTINUITY",
                "A",
                "SKU-FG-1000",
                "EBOM-CONTINUITY",
                "A",
                new DateOnly(2026, 6, 1),
                [new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1m, "EA", 0m)],
                []),
            CancellationToken.None));

        Assert.Contains("SKU-FG-OTHER", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SKU-FG-1000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_manufacturing_bom_allows_manufacturing_only_material_line_not_in_ebom()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-CONTINUITY", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-1000", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        await handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-CONTINUITY",
                "A",
                "SKU-FG-1000",
                "EBOM-CONTINUITY",
                "A",
                new DateOnly(2026, 6, 1),
                [
                    new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1m, "EA", 0m),
                    new ManufacturingBomMaterialLineCommand("SKU-RM-9999", 1m, "EA", 0m)
                ],
                []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await dbContext.ManufacturingBoms
            .Include(x => x.MaterialLines)
            .SingleAsync(x => x.BomCode == "MBOM-CONTINUITY", CancellationToken.None);
        Assert.Contains(created.MaterialLines, x => x.SkuCode == "SKU-RM-9999");
    }

    [Fact]
    public async Task Release_manufacturing_bom_allows_omitting_phantom_ebom_child_sku()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-CONTINUITY", "A", "SKU-FG-1000")
            .AddLine("SKU-RM-1000", 1m, "EA")
            .AddLine("SKU-PHANTOM-1000", 1m, "EA", isPhantom: true);
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        await handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-CONTINUITY",
                "A",
                "SKU-FG-1000",
                "EBOM-CONTINUITY",
                "A",
                new DateOnly(2026, 6, 1),
                [new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1m, "EA", 0m)],
                []),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await dbContext.ManufacturingBoms
            .Include(x => x.MaterialLines)
            .SingleAsync(x => x.BomCode == "MBOM-CONTINUITY", CancellationToken.None);
        Assert.DoesNotContain(created.MaterialLines, x => x.SkuCode == "SKU-PHANTOM-1000");
    }

    [Fact]
    public async Task Release_engineering_bom_rejects_overlapping_published_revision()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var existing = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-OVERLAP", "A", "ENG-1000")
            .AddLine("ENG-1001", 1m, "EA");
        existing.Release(new DateOnly(2026, 1, 1));
        dbContext.EngineeringBoms.Add(existing);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringBomCommandHandler(new EngineeringBomRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringBomCommand(
                "org-001",
                "env-dev",
                "EBOM-OVERLAP",
                "B",
                "ENG-1000",
                new DateOnly(2026, 6, 1),
                [new BomLineCommand("ENG-1002", 1m, "EA")]),
            CancellationToken.None));

        Assert.Contains("published", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EBOM-OVERLAP", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_manufacturing_bom_rejects_overlapping_published_revision()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-OVERLAP", "A", "ENG-1000")
            .AddLine("ENG-1001", 1m, "EA");
        ebom.Release(new DateOnly(2026, 1, 1));
        var existing = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-OVERLAP", "A", "SKU-FG-1000")
            .AddMaterialLine("SKU-RM-1000", 1m, "EA", 0m);
        existing.ReleaseFromEngineeringBom("EBOM-OVERLAP:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        dbContext.AddRange(ebom, existing);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-OVERLAP",
                "B",
                "SKU-FG-1000",
                "EBOM-OVERLAP",
                "A",
                new DateOnly(2026, 6, 1),
                [new ManufacturingBomMaterialLineCommand("SKU-RM-1001", 1m, "EA", 0m)],
                []),
            CancellationToken.None));

        Assert.Contains("published", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MBOM-OVERLAP", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_routing_rejects_overlapping_published_revision()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(NewStandardOperation("mixing", "WC-MIX-01"));
        var existing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-OVERLAP", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-MIX-01", "mixing", "Mix", 30);
        existing.Release(new DateOnly(2026, 1, 1));
        dbContext.Routings.Add(existing);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseRoutingCommandHandler(
            new RoutingRepository(dbContext),
            new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseRoutingCommand(
                "org-001",
                "env-dev",
                "ROUTE-OVERLAP",
                "B",
                "SKU-FG-1000",
                new DateOnly(2026, 6, 1),
                [new RoutingOperationCommand(10, null, "mixing", null)]),
            CancellationToken.None));

        Assert.Contains("published", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROUTE-OVERLAP", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MasterData_work_center_usage_query_reports_active_standard_operation_and_routing_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(StandardOperation.Create(
            "org-001",
            "env-dev",
            "mixing",
            "Mixing",
            "WC-MIX-01",
            5,
            30,
            "INHOUSE",
            true,
            false,
            false,
            null));
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-MIX", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-MIX-01", "mixing", "Mixing", 30);
        routing.Release(new DateOnly(2026, 6, 1));
        var draftRouting = Routing.CreateDraft("org-001", "env-dev", "ROUTE-DRAFT", "A", "SKU-FG-1001")
            .AddOperation(10, "WC-MIX-01", "mixing", "Mixing", 30);
        dbContext.Routings.AddRange(routing, draftRouting);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var usage = await new GetMasterDataWorkCenterUsageQueryHandler(dbContext).Handle(
            new GetMasterDataWorkCenterUsageQuery("org-001", "env-dev", "WC-MIX-01"),
            CancellationToken.None);

        Assert.True(usage.HasActiveReference);
        Assert.Contains("standard-operation:mixing", usage.References);
        Assert.Contains("routing:ROUTE-MIX:A", usage.References);
        Assert.DoesNotContain("routing:ROUTE-DRAFT:A", usage.References);
    }

    [Fact]
    public async Task Release_engineering_bom_wraps_duplicate_component_as_known_exception()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new ReleaseEngineeringBomCommandHandler(new EngineeringBomRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringBomCommand(
                "org-001",
                "env-dev",
                "EBOM-2000",
                "A",
                "ENG-2000",
                new DateOnly(2026, 6, 1),
                [
                    new BomLineCommand("ENG-2001", 1m, "EA"),
                    new BomLineCommand("ENG-2001", 2m, "EA")
                ]),
            CancellationToken.None));

        Assert.Contains("already contains child item", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Release_manufacturing_bom_wraps_duplicate_material_as_known_exception()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-2000", "A", "SKU-FG-2000")
            .AddLine("SKU-RM-2000", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseManufacturingBomCommandHandler(
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseManufacturingBomCommand(
                "org-001",
                "env-dev",
                "MBOM-2000",
                "A",
                "SKU-FG-2000",
                "EBOM-2000",
                "A",
                new DateOnly(2026, 6, 1),
                [
                    new ManufacturingBomMaterialLineCommand("SKU-RM-2000", 1m, "KG", 0m),
                    new ManufacturingBomMaterialLineCommand("SKU-RM-2000", 2m, "KG", 0m)
                ],
                []),
            CancellationToken.None));

        Assert.Contains("already contains SKU", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Release_routing_wraps_duplicate_sequence_as_known_exception()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.AddRange(
            NewStandardOperation("mixing", "WC-MIX-01"),
            NewStandardOperation("packing", "WC-PACK-01"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseRoutingCommandHandler(
            new RoutingRepository(dbContext),
            new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseRoutingCommand(
                "org-001",
                "env-dev",
                "ROUTE-2000",
                "A",
                "SKU-FG-2000",
                new DateOnly(2026, 6, 1),
                [
                    new RoutingOperationCommand(10, "WC-MIX-01", "mixing", "Mix", 30),
                    new RoutingOperationCommand(10, "WC-PACK-01", "packing", "Pack", 15)
                ]),
            CancellationToken.None));

        Assert.Contains("already contains operation sequence", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Release_routing_rejects_duplicate_business_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(NewStandardOperation("mixing", "WC-MIX-01"));
        var existing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-1000", "A", "SKU-FG-1000")
            .AddOperation(10, "WC-MIX-01", "mixing", "混合", 30);
        existing.Release(new DateOnly(2026, 6, 1));
        dbContext.Routings.Add(existing);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseRoutingCommandHandler(
            new RoutingRepository(dbContext),
            new StandardOperationRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseRoutingCommand(
                "org-001",
                "env-dev",
                "ROUTE-1000",
                "A",
                "SKU-FG-1000",
                new DateOnly(2026, 6, 1),
                [new RoutingOperationCommand(10, "WC-MIX-01", "mixing", "混合", 30)]),
            CancellationToken.None));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Release_routing_snapshots_enabled_standard_operation_defaults()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.StandardOperations.Add(StandardOperation.Create(
            "org-001",
            "env-dev",
            "mixing",
            "Standard mixing",
            "WC-MIX-DEFAULT",
            7,
            31,
            "MIX-QA",
            requiresReporting: true,
            requiresQualityInspection: true,
            isOutsourced: false,
            description: null));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseRoutingCommandHandler(
            new RoutingRepository(dbContext),
            new StandardOperationRepository(dbContext));

        await handler.Handle(
            new ReleaseRoutingCommand(
                "org-001",
                "env-dev",
                "ROUTE-STD",
                "A",
                "SKU-FG-1000",
                new DateOnly(2026, 6, 1),
                [new RoutingOperationCommand(10, "WC-IGNORED", "mixing", "Ignored name", 1)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var routing = await dbContext.Routings.SingleAsync(x => x.RoutingCode == "ROUTE-STD", CancellationToken.None);
        var operation = Assert.Single(routing.Operations);
        Assert.Equal("WC-MIX-DEFAULT", operation.WorkCenterCode);
        Assert.Equal("Standard mixing", operation.OperationName);
        Assert.Equal(7, operation.SetupMinutes);
        Assert.Equal(31, operation.RunMinutes);
        Assert.Equal(38, operation.StandardMinutes);
        Assert.Equal("MIX-QA", operation.ControlKey);
        Assert.True(operation.RequiresQualityInspection);
    }

    [Fact]
    public async Task Release_engineering_change_archives_affected_product_engineering_versions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-3000", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        ebom.Release(new DateOnly(2026, 1, 1));
        var mbom = ManufacturingBom.CreateDraft("org-001", "env-dev", "MBOM-3000", "A", "SKU-FG-3000")
            .AddMaterialLine("SKU-RM-3000", 1m, "EA", 0m);
        mbom.ReleaseFromEngineeringBom("EBOM-3000:A", EngineeringVersionStatus.Published, new DateOnly(2026, 1, 1));
        var routing = Routing.CreateDraft("org-001", "env-dev", "ROUTE-3000", "A", "SKU-FG-3000")
            .AddOperation(10, "WC-MIX-01", "mixing", "Mix", 30);
        routing.Release(new DateOnly(2026, 1, 1));
        var productionVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-3000:A",
            "ROUTE-3000:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.AddRange(ebom, mbom, routing, productionVersion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalReferenceId = Guid.NewGuid().ToString("D");
        var approvalVerifier = new RecordingApprovalVerifier();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier);

        await handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-3000",
                "Supersede first release",
                approvalReferenceId,
                new DateOnly(2026, 6, 1),
                [
                    new AffectedVersionCommand("engineering-bom", "EBOM-3000:A"),
                    new AffectedVersionCommand("manufacturing-bom", "MBOM-3000:A"),
                    new AffectedVersionCommand("routing", "ROUTE-3000:A"),
                    new AffectedVersionCommand("production-version", productionVersion.Id.Id.ToString("D"))
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(EngineeringVersionStatus.Archived, ebom.Status);
        Assert.Equal(EngineeringVersionStatus.Archived, mbom.Status);
        Assert.Equal(EngineeringVersionStatus.Archived, routing.Status);
        Assert.Equal(ProductionVersionStatus.Archived, productionVersion.Status);
        Assert.Equal(EngineeringVersionStatus.Published, Assert.Single(dbContext.EngineeringChanges).Status);
        Assert.Equal((approvalReferenceId, "ECO-3000"), approvalVerifier.Calls.Single());
    }

    [Fact]
    public async Task Future_release_engineering_change_schedules_without_archiving_or_raising_release_event()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FUTURE", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        ebom.ClearDomainEvents();
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalVerifier = new RecordingApprovalVerifier();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier,
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        await handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-FUTURE",
                "Future EBOM switch",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 3),
                [new AffectedVersionCommand("engineering-bom", "EBOM-FUTURE:A")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var change = Assert.Single(dbContext.EngineeringChanges);
        Assert.Equal(EngineeringVersionStatus.Scheduled, change.Status);
        Assert.Equal(new DateOnly(2026, 6, 3), change.EffectiveDate);
        Assert.Empty(change.GetDomainEvents());
        Assert.Equal(EngineeringVersionStatus.Published, ebom.Status);
        Assert.Equal("ECO-FUTURE", approvalVerifier.Calls.Single().ChangeNumber);
    }

    [Fact]
    public async Task Scheduled_engineering_change_promotes_due_release_and_archives_affected_versions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DUE", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));
        await handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-DUE",
                "Due EBOM switch",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 3),
                [new AffectedVersionCommand("engineering-bom", "EBOM-DUE:A")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var promoted = await new EngineeringChangeScheduledReleaseService(dbContext)
            .PromoteDueReleasesAsync(new DateOnly(2026, 6, 3), CancellationToken.None);

        Assert.Equal(1, promoted);
        Assert.Equal(EngineeringVersionStatus.Archived, ebom.Status);
        Assert.Equal(EngineeringVersionStatus.Published, Assert.Single(dbContext.EngineeringChanges).Status);
    }

    [Fact]
    public async Task Cancel_scheduled_engineering_change_leaves_affected_versions_visible_to_downstream()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-CANCEL", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await ScheduleEngineeringChangeAsync(dbContext, "ECO-CANCEL", "EBOM-CANCEL:A", new DateOnly(2026, 6, 3));

        await new CancelScheduledEngineeringChangeCommandHandler(dbContext).Handle(
            new CancelScheduledEngineeringChangeCommand("org-001", "env-dev", "ECO-CANCEL", "operator cancelled"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var promoted = await new EngineeringChangeScheduledReleaseService(dbContext)
            .PromoteDueReleasesAsync(new DateOnly(2026, 6, 3), CancellationToken.None);

        Assert.Equal(0, promoted);
        Assert.Equal(EngineeringVersionStatus.Published, ebom.Status);
        Assert.Equal(EngineeringVersionStatus.Cancelled, Assert.Single(dbContext.EngineeringChanges).Status);
    }

    [Fact]
    public async Task Reschedule_engineering_change_uses_new_effective_date_for_promotion()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-RESCHEDULE", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        ebom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.Add(ebom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await ScheduleEngineeringChangeAsync(dbContext, "ECO-RESCHEDULE", "EBOM-RESCHEDULE:A", new DateOnly(2026, 6, 3));

        await new RescheduleEngineeringChangeCommandHandler(dbContext).Handle(
            new RescheduleEngineeringChangeCommand("org-001", "env-dev", "ECO-RESCHEDULE", new DateOnly(2026, 6, 10), "supplier delay"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var earlyPromoted = await new EngineeringChangeScheduledReleaseService(dbContext)
            .PromoteDueReleasesAsync(new DateOnly(2026, 6, 3), CancellationToken.None);
        var duePromoted = await new EngineeringChangeScheduledReleaseService(dbContext)
            .PromoteDueReleasesAsync(new DateOnly(2026, 6, 10), CancellationToken.None);

        Assert.Equal(0, earlyPromoted);
        Assert.Equal(1, duePromoted);
        Assert.Equal(EngineeringVersionStatus.Archived, ebom.Status);
        Assert.Equal(new DateOnly(2026, 6, 10), Assert.Single(dbContext.EngineeringChanges).EffectiveDate);
    }

    [Fact]
    public async Task Scheduled_release_promotion_isolates_failed_changes_for_next_retry()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var validBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-VALID", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        validBom.Release(new DateOnly(2026, 6, 1));
        var draftBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DRAFT-RETRY", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        var validChange = EngineeringChange.Open("org-001", "env-dev", "ECO-VALID", "Due valid change")
            .Affect("engineering-bom", "EBOM-VALID:A")
            .Approve(Guid.NewGuid().ToString("D"));
        validChange.Schedule(new DateOnly(2026, 6, 3));
        var retryChange = EngineeringChange.Open("org-001", "env-dev", "ECO-RETRY", "Due invalid change")
            .Affect("engineering-bom", "EBOM-DRAFT-RETRY:A")
            .Approve(Guid.NewGuid().ToString("D"));
        retryChange.Schedule(new DateOnly(2026, 6, 3));
        dbContext.AddRange(validBom, draftBom, validChange, retryChange);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var promoted = await new EngineeringChangeScheduledReleaseService(dbContext)
            .PromoteDueReleasesAsync(new DateOnly(2026, 6, 3), CancellationToken.None);

        var persistedValidBom = await dbContext.EngineeringBoms.SingleAsync(x => x.BomCode == "EBOM-VALID", CancellationToken.None);
        var persistedValidChange = await dbContext.EngineeringChanges.SingleAsync(x => x.ChangeNumber == "ECO-VALID", CancellationToken.None);
        var persistedRetryChange = await dbContext.EngineeringChanges.SingleAsync(x => x.ChangeNumber == "ECO-RETRY", CancellationToken.None);
        Assert.Equal(1, promoted);
        Assert.Equal(EngineeringVersionStatus.Archived, persistedValidBom.Status);
        Assert.Equal(EngineeringVersionStatus.Published, persistedValidChange.Status);
        Assert.Equal(EngineeringVersionStatus.Scheduled, persistedRetryChange.Status);
    }

    [Fact]
    public async Task Release_engineering_change_records_supersede_successor_version()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-SUP", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        oldBom.Release(new DateOnly(2026, 1, 1));
        var successorBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-SUP", "B", "ENG-3000")
            .AddLine("ENG-3001", 2m, "EA");
        successorBom.Release(new DateOnly(2026, 6, 1));
        dbContext.EngineeringBoms.AddRange(oldBom, successorBom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier());

        await handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-SUPERSEDE",
                "Supersede EBOM A with EBOM B",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-bom", "EBOM-SUP:A", "EBOM-SUP:B")]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var affectedVersion = Assert.Single(dbContext.EngineeringChanges.Include(x => x.AffectedVersions).Single().AffectedVersions);
        Assert.Equal("EBOM-SUP:A", affectedVersion.VersionId);
        Assert.Equal("EBOM-SUP:B", affectedVersion.SupersededByVersionId);
        Assert.Equal(EngineeringVersionStatus.Archived, oldBom.Status);
        Assert.Equal(EngineeringVersionStatus.Published, successorBom.Status);
    }

    [Fact]
    public async Task Release_engineering_change_rebinds_successor_production_version_window_to_effective_date()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-3000:A",
            "ROUTE-3000:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        var successor = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-3000:B",
            "ROUTE-3000:B",
            new DateOnly(2026, 7, 1),
            null,
            null,
            null,
            20,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.AddRange(oldVersion, successor);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier());

        await handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-PV-SUPERSEDE",
                "Supersede production version window",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", oldVersion.Id.Id.ToString("D"), successor.Id.Id.ToString("D"))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(ProductionVersionStatus.Archived, oldVersion.Status);
        Assert.Equal(new DateOnly(2026, 5, 31), oldVersion.ValidTo);
        Assert.Equal(ProductionVersionStatus.Active, successor.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), successor.ValidFrom);
        Assert.True(successor.IsResolvableFor(new DateOnly(2026, 6, 1), 1m));
    }

    [Fact]
    public async Task Release_engineering_change_rejects_successor_production_version_when_effective_date_exceeds_successor_valid_to()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-3000:A",
            "ROUTE-3000:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        var successor = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-3000:B",
            "ROUTE-3000:B",
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 5, 31),
            null,
            null,
            20,
            false,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.AddRange(oldVersion, successor);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-PV-DEAD-WINDOW",
                "Reject successor dead window",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", oldVersion.Id.Id.ToString("D"), successor.Id.Id.ToString("D"))]),
            CancellationToken.None));

        Assert.Contains("successor production version effective window", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProductionVersionStatus.Active, oldVersion.Status);
        Assert.Null(oldVersion.ValidTo);
        Assert.Equal(new DateOnly(2026, 3, 1), successor.ValidFrom);
        Assert.Equal(new DateOnly(2026, 5, 31), successor.ValidTo);
    }

    [Fact]
    public async Task Release_engineering_change_rejects_self_supersede_version()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approvalVerifier = new RecordingApprovalVerifier();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-SELF-SUPERSEDE",
                "Reject self supersede",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-bom", "EBOM-SELF:A", " EBOM-SELF:A ")]),
            CancellationToken.None));

        Assert.Contains("cannot supersede itself", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(approvalVerifier.Calls);
        Assert.Empty(dbContext.EngineeringChanges);
    }

    [Fact]
    public async Task Release_engineering_change_rejects_same_change_supersede_cycle()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approvalVerifier = new RecordingApprovalVerifier();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-CYCLE",
                "Reject supersede cycle",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [
                    new AffectedVersionCommand("engineering-bom", "EBOM-CYCLE:A", "EBOM-CYCLE:B"),
                    new AffectedVersionCommand("engineering-bom", "EBOM-CYCLE:B", "EBOM-CYCLE:A")
                ]),
            CancellationToken.None));

        Assert.Contains("supersede cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(approvalVerifier.Calls);
        Assert.Empty(dbContext.EngineeringChanges);
    }

    [Fact]
    public async Task Release_engineering_change_rejects_conflicting_duplicate_successor()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approvalVerifier = new RecordingApprovalVerifier();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-DUPLICATE-SUCCESSOR",
                "Reject conflicting successors",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [
                    new AffectedVersionCommand("engineering-bom", "EBOM-DUP:A", "EBOM-DUP:B"),
                    new AffectedVersionCommand(" Engineering-Bom ", " ebom-dup:a ", "EBOM-DUP:C")
                ]),
            CancellationToken.None));

        Assert.Contains("can only declare one successor", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(approvalVerifier.Calls);
        Assert.Empty(dbContext.EngineeringChanges);
    }

    [Fact]
    public async Task Release_engineering_change_normalizes_blank_successor_for_idempotency_fingerprint()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-FP", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        oldBom.Release(new DateOnly(2026, 1, 1));
        dbContext.EngineeringBoms.Add(oldBom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier());
        var firstCommand = new ReleaseEngineeringChangeCommand(
            "org-001",
            "env-dev",
            null,
            "Supersede with no successor",
            Guid.NewGuid().ToString("D"),
            new DateOnly(2026, 6, 1),
            [new AffectedVersionCommand("engineering-bom", "EBOM-FP:A")],
            "eco-fingerprint-successor-blank");

        var first = await handler.Handle(firstCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var replay = await handler.Handle(firstCommand with
        {
            AffectedVersions = [new AffectedVersionCommand(" engineering-bom ", " EBOM-FP:A ", "   ")]
        }, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Single(dbContext.EngineeringChanges);
    }

    [Fact]
    public async Task Release_engineering_change_wraps_archive_invalid_state_as_known_exception()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var draftBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DRAFT", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        dbContext.EngineeringBoms.Add(draftBom);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-DRAFT-ARCHIVE",
                "Reject draft archive as business error",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-bom", "EBOM-DRAFT:A")]),
            CancellationToken.None));

        Assert.Contains("Only released engineering BOM versions can be archived", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.EngineeringChanges);
    }

    [Fact]
    public async Task Release_engineering_change_requires_matching_approved_business_approval_chain()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var approvalVerifier = new RecordingApprovalVerifier(shouldApprove: false);
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-APPROVAL-GATE",
                "Cannot release without approved chain",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-bom", "EBOM-404:A")]),
            CancellationToken.None));

        Assert.Contains("approved BusinessApproval chain", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.EngineeringChanges);
    }

    [Fact]
    public async Task Release_engineering_change_rejects_cross_tenant_production_version_archive()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var foreignProductionVersion = ProductionVersion.Create(
            "org-002",
            "env-dev",
            "SKU-FG-3000",
            "MBOM-3000:A",
            "ROUTE-3000:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.Add(foreignProductionVersion);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var approvalVerifier = new RecordingApprovalVerifier();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            approvalVerifier);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-CROSS-TENANT",
                "Attempt cross-tenant supersede",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", foreignProductionVersion.Id.Id.ToString("D"))]),
            CancellationToken.None));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProductionVersionStatus.Active, foreignProductionVersion.Status);
        Assert.Empty(dbContext.EngineeringChanges);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Release_routing_endpoint_rejects_missing_operation_code(string operationCode)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/engineering/routings/release", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            routingCode = "ROUTE-1000",
            revision = "A",
            skuCode = "SKU-FG-1000",
            effectiveDate = "2026-06-01",
            operations = new[]
            {
                new
                {
                    sequence = 10,
                    workCenterCode = "WC-MIX-01",
                    operationCode,
                    operationName = "混合",
                    standardMinutes = 30,
                },
            },
        });

        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("operationCode", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_engineering_document_generates_number_and_replays_idempotent_create()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new ProductEngineeringCodingService();
        var handler = new RegisterEngineeringDocumentCommandHandler(new EngineeringDocumentRepository(dbContext), numbering);
        var command = new RegisterEngineeringDocumentCommand(
            "org-001",
            "env-dev",
            null,
            "A",
            "file-001",
            "shock-absorber.dwg",
            "application/dwg",
            "cad-drawing",
            "engineering-document-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Matches("^EDOC-[0-9]{8}-[0-9]{6}$", first.Id);
        Assert.Single(dbContext.EngineeringDocuments);
    }

    [Fact]
    public async Task Register_engineering_document_preserves_legacy_idempotency_fingerprint_without_item_code()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new ProductEngineeringCodingService();
        var handler = new RegisterEngineeringDocumentCommandHandler(new EngineeringDocumentRepository(dbContext), numbering);
        var command = new RegisterEngineeringDocumentCommand(
            "org-001",
            "env-dev",
            null,
            "A",
            "file-001",
            "shock-absorber.dwg",
            "application/dwg",
            "cad-drawing",
            "engineering-document-legacy");

        await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var legacyReplay = new RegisterEngineeringDocumentCommand(
            "org-001",
            "env-dev",
            null,
            "A",
            "file-001",
            "shock-absorber.dwg",
            "application/dwg",
            "cad-drawing",
            "engineering-document-legacy",
            ItemCode: "   ");

        var replay = await handler.Handle(legacyReplay, CancellationToken.None);

        Assert.Matches("^EDOC-[0-9]{8}-[0-9]{6}$", replay.Id);
        Assert.Single(dbContext.EngineeringDocuments);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"product-engineering-release-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static async Task ScheduleEngineeringChangeAsync(ApplicationDbContext dbContext, string changeNumber, string versionId, DateOnly effectiveDate)
    {
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new RecordingApprovalVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(effectiveDate.AddDays(-1)));
        await handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                changeNumber,
                "Schedule engineering change",
                Guid.NewGuid().ToString("D"),
                effectiveDate,
                [new AffectedVersionCommand("engineering-bom", versionId)]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static bool IsValidationFailureFor(FluentValidation.Results.ValidationFailure failure, string propertyName)
    {
        return NormalizeValidationPropertyName(failure.PropertyName) == NormalizeValidationPropertyName(propertyName);
    }

    private static bool IsValidationFailureMessageFor(FluentValidation.Results.ValidationFailure failure, string propertyName)
    {
        return NormalizeValidationPropertyName(failure.ErrorMessage).Contains(NormalizeValidationPropertyName(propertyName), StringComparison.Ordinal);
    }

    private static bool HasInternalServicePolicy(IEnumerable<RouteEndpoint> endpoints, string route)
    {
        return endpoints
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Any(authorizeData => string.Equals(authorizeData.Policy, InternalServiceAuthorizationPolicy.Name, StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return ProductEngineeringWebTestFactory.Create("product-engineering-release-http");
    }

    private static string NormalizeValidationPropertyName(string propertyName)
    {
        return propertyName.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private static void SetCreatedAtUtc<TAggregate>(TAggregate aggregate, DateTime createdAtUtc)
    {
        var field = typeof(TAggregate).GetField(
            "<CreatedAtUtc>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field is null)
        {
            throw new InvalidOperationException($"Aggregate {typeof(TAggregate).Name} does not expose a CreatedAtUtc backing field.");
        }

        field.SetValue(aggregate, createdAtUtc);
    }

    private static StandardOperation NewStandardOperation(string operationCode, string workCenterCode)
    {
        return StandardOperation.Create(
            "org-001",
            "env-dev",
            operationCode,
            operationCode,
            workCenterCode,
            1,
            10,
            "STD",
            requiresReporting: true,
            requiresQualityInspection: false,
            isOutsourced: false,
            description: null);
    }


    private sealed class RecordingApprovalVerifier(bool shouldApprove = true) : IEngineeringApprovalVerifier
    {
        public List<(string ApprovalReferenceId, string ChangeNumber)> Calls { get; } = [];

        public Task EnsureApprovedAsync(
            string organizationId,
            string environmentId,
            string approvalReferenceId,
            string changeNumber,
            CancellationToken cancellationToken)
        {
            Calls.Add((approvalReferenceId, changeNumber));
            if (!shouldApprove)
            {
                throw new KnownException("Engineering change release requires an approved BusinessApproval chain for the same ECO document.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedBusinessDateProvider(DateOnly businessDate) : IProductEngineeringBusinessDateProvider
    {
        public DateOnly GetBusinessDate() => businessDate;
    }

    private sealed class RecordingMasterDataReferenceValidator(params (string ResourceType, string Code)[] activeReferences)
        : IProductEngineeringMasterDataReferenceValidator
    {
        private readonly HashSet<string> activeReferenceKeys = activeReferences
            .Select(reference => $"{reference.ResourceType}:{reference.Code}")
            .ToHashSet(StringComparer.Ordinal);

        public List<(string ResourceType, string Code)> Requests { get; } = [];

        public Task ValidateActiveReferencesAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<ProductEngineeringMasterDataReference> references,
            CancellationToken cancellationToken)
        {
            Requests.AddRange(references.Select(reference => (reference.ResourceType, reference.Code)));
            var missing = references
                .Where(reference => !activeReferenceKeys.Contains($"{reference.ResourceType}:{reference.Code}"))
                .Select(reference => reference.Code)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new KnownException($"MasterData reference(s) are missing or inactive: {string.Join(", ", missing)}.");
            }

            return Task.CompletedTask;
        }
    }
}

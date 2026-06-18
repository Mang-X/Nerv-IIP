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

        Assert.Equal(18, contracts.Count);
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
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/engineering-boms/{bomCode}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
        Assert.Contains(contracts, x => x.HttpMethod == "GET" && x.Route == "/api/business/v1/engineering/manufacturing-boms/{bomCode}/{revision}" && x.PermissionCode == EngineeringPermissionCodes.BomsRead);
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
    [InlineData(typeof(ReleaseManufacturingBomEndpoint))]
    [InlineData(typeof(GetManufacturingBomEndpoint))]
    [InlineData(typeof(ReleaseRoutingEndpoint))]
    [InlineData(typeof(GetRoutingEndpoint))]
    [InlineData(typeof(ReleaseEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringChangesEndpoint))]
    [InlineData(typeof(GetEngineeringChangeEndpoint))]
    [InlineData(typeof(ListEngineeringBomsEndpoint))]
    [InlineData(typeof(ListManufacturingBomsEndpoint))]
    [InlineData(typeof(ListRoutingsEndpoint))]
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
    public async Task Release_manufacturing_bom_rejects_orphan_material_line_not_in_ebom()
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
                [
                    new ManufacturingBomMaterialLineCommand("SKU-RM-1000", 1m, "EA", 0m),
                    new ManufacturingBomMaterialLineCommand("SKU-RM-9999", 1m, "EA", 0m)
                ],
                []),
            CancellationToken.None));

        Assert.Contains("SKU-RM-9999", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not present", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private static bool IsValidationFailureFor(FluentValidation.Results.ValidationFailure failure, string propertyName)
    {
        return NormalizeValidationPropertyName(failure.PropertyName) == NormalizeValidationPropertyName(propertyName);
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
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_product_engineering_policy;Username=nerv;Password=nerv",
            ["InternalService:BearerToken"] = "test-internal-service-token",
        };

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(settings));
            });
    }

    private static string NormalizeValidationPropertyName(string propertyName)
    {
        return propertyName.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
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
}

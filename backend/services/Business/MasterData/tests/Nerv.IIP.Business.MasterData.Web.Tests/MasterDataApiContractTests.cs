using System.Net;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Web.Application.Auth;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Endpoints.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataApiContractTests
{
    [Theory]
    [InlineData(typeof(CreateSkuEndpoint))]
    [InlineData(typeof(CreateUnitOfMeasureEndpoint))]
    [InlineData(typeof(CreateUomConversionEndpoint))]
    [InlineData(typeof(CreateBusinessPartnerEndpoint))]
    [InlineData(typeof(CreateDepartmentEndpoint))]
    [InlineData(typeof(CreateTeamEndpoint))]
    [InlineData(typeof(CreateWorkshopEndpoint))]
    [InlineData(typeof(AddTeamMemberEndpoint))]
    [InlineData(typeof(RemoveTeamMemberEndpoint))]
    [InlineData(typeof(AssignPersonnelSkillEndpoint))]
    [InlineData(typeof(ListPersonnelSkillMatrixEndpoint))]
    [InlineData(typeof(CreateSiteEndpoint))]
    [InlineData(typeof(CreateProductionLineEndpoint))]
    [InlineData(typeof(CreateShiftEndpoint))]
    [InlineData(typeof(CreateWorkCalendarEndpoint))]
    [InlineData(typeof(CreateWorkCenterEndpoint))]
    [InlineData(typeof(RegisterDeviceAssetEndpoint))]
    [InlineData(typeof(CreateReferenceDataCodeEndpoint))]
    [InlineData(typeof(CreateProductCategoryEndpoint))]
    [InlineData(typeof(UpdateProductCategoryEndpoint))]
    [InlineData(typeof(ArchiveProductCategoryEndpoint))]
    [InlineData(typeof(CreateSkillEndpoint))]
    [InlineData(typeof(UpdateSkillEndpoint))]
    [InlineData(typeof(ArchiveSkillEndpoint))]
    [InlineData(typeof(UpdateMasterDataResourceEndpoint))]
    [InlineData(typeof(DisableMasterDataResourceEndpoint))]
    [InlineData(typeof(EnableMasterDataResourceEndpoint))]
    public void MasterData_mutation_endpoints_route_through_mediator(Type endpointType)
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
    public void MasterData_endpoint_sources_do_not_commit_transactions_directly()
    {
        var source = File.ReadAllText(Path.Combine(MasterDataServiceRoot(), "src", "Nerv.IIP.Business.MasterData.Web", "Endpoints", "MasterData", "MasterDataEndpoints.cs"));

        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterData_list_endpoint_routes_through_mediator()
    {
        var parameterTypes = typeof(ListMasterDataResourcesEndpoint)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
        Assert.DoesNotContain(typeof(ApplicationDbContext), parameterTypes);
    }

    [Fact]
    public void MasterData_endpoint_contracts_have_stable_openapi_operation_ids()
    {
        var contracts = MasterDataEndpointContracts.All;

        Assert.Equal(41, contracts.Count);
        Assert.Equal(contracts.Count, contracts.Select(x => x.EndpointType).Distinct().Count());
        Assert.Equal(contracts.Count, contracts.Select(x => x.OperationId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(contracts, contract =>
        {
            Assert.Matches("^[a-z][A-Za-z0-9]*$", contract.OperationId);
            Assert.Contains("BusinessMasterData", contract.OperationId, StringComparison.Ordinal);
            Assert.StartsWith("/api/business/v1/master-data/", contract.Route, StringComparison.Ordinal);
            Assert.Contains(contract.HttpMethod, new[] { "GET", "POST", "PUT", "PATCH", "DELETE" });
            Assert.Contains(contract.PermissionCode, NervIipBusinessMasterDataPermissionSet);
        });
    }

    [Fact]
    public void MasterData_catalog_endpoints_expose_product_category_and_skill_routes()
    {
        var contracts = MasterDataEndpointContracts.All.ToArray();

        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/master-data/product-categories"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataProductsRead
            && x.OperationId == "listBusinessMasterDataProductCategories");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/master-data/product-categories/{categoryCode}"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataProductsRead
            && x.OperationId == "getBusinessMasterDataProductCategory");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/master-data/product-categories"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataProductsManage
            && x.OperationId == "createBusinessMasterDataProductCategory");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT"
            && x.Route == "/api/business/v1/master-data/product-categories/{categoryCode}"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataProductsManage
            && x.OperationId == "updateBusinessMasterDataProductCategory");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/master-data/product-categories/{categoryCode}/archive"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataProductsManage
            && x.OperationId == "archiveBusinessMasterDataProductCategory");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/master-data/skills"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataResourcesRead
            && x.OperationId == "listBusinessMasterDataSkills");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/master-data/skills/{skillCode}"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataResourcesRead
            && x.OperationId == "getBusinessMasterDataSkill");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/master-data/skills"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataResourcesManage
            && x.OperationId == "createBusinessMasterDataSkill");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT"
            && x.Route == "/api/business/v1/master-data/skills/{skillCode}"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataResourcesManage
            && x.OperationId == "updateBusinessMasterDataSkill");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/master-data/skills/{skillCode}/archive"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataResourcesManage
            && x.OperationId == "archiveBusinessMasterDataSkill");
    }

    [Fact]
    public void MasterData_exposes_customer_credit_profile_route()
    {
        var contracts = MasterDataEndpointContracts.All.ToArray();

        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/master-data/partners/{customerCode}/credit"
            && x.PermissionCode == BusinessPermissionCodes.MasterDataPartnersRead
            && x.OperationId == "getBusinessMasterDataPartnerCredit");
    }

    [Fact]
    public void MasterData_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var failures = MasterDataEndpointContracts.All
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void MasterData_endpoint_contracts_are_the_route_registry()
    {
        var source = File.ReadAllText(Path.Combine(MasterDataServiceRoot(), "src", "Nerv.IIP.Business.MasterData.Web", "Endpoints", "MasterData", "MasterDataEndpoints.cs"));

        Assert.DoesNotContain("[Http", source, StringComparison.Ordinal);
        Assert.Contains("Get(contract.Route);", source, StringComparison.Ordinal);
        Assert.Contains("Post(contract.Route);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MasterData_endpoints_define_permissions_from_contracts()
    {
        var source = File.ReadAllText(Path.Combine(MasterDataServiceRoot(), "src", "Nerv.IIP.Business.MasterData.Web", "Endpoints", "MasterData", "MasterDataEndpoints.cs"));
        var failures = MasterDataEndpointContracts.All
            .Where(contract =>
                !source.Contains($"MasterDataEndpointContracts.Get<{contract.EndpointType.Name}>()", StringComparison.Ordinal) ||
                !source.Contains("ConfigureMasterDataContract(contract);", StringComparison.Ordinal))
            .Select(contract => $"{contract.EndpointType.Name} does not configure permission contract")
            .ToArray();

        Assert.Empty(failures);
        Assert.DoesNotContain("Description(builder => builder.WithName(contract.OperationId));", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_references_supports_reference_data_codes_for_process_master_data()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "quality-reason", "scratch", "Scratch"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ResolveMasterDataReferencesQueryHandler(dbContext);
        var response = await handler.Handle(
            new ResolveMasterDataReferencesQuery(
                "org-001",
                "env-dev",
                [
                    new MasterDataReferenceRequest("reference-data", "scratch", "quality-reason"),
                    new MasterDataReferenceRequest("reference-data:quality-reason", "scratch")
                ]),
            CancellationToken.None);

        Assert.All(response.References, reference =>
        {
            Assert.True(reference.Exists);
            Assert.True(reference.Active);
            Assert.Equal("Scratch", reference.DisplayName);
        });
    }

    [Fact]
    public async Task Product_category_catalog_commands_list_detail_update_and_archive_tree_nodes()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = new ProductCategoryRepository(dbContext);
        var create = new CreateProductCategoryCommandHandler(dbContext, repository, new MasterDataCodingService());

        var root = await create.Handle(
            new CreateProductCategoryCommand("org-001", "env-dev", "CAT-FG", "Finished Goods", null, "Sellable output"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await create.Handle(
            new CreateProductCategoryCommand("org-001", "env-dev", "CAT-FG-PUMP", "Pump Products", "CAT-FG", "Pump family"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var list = await new ListProductCategoriesQueryHandler(dbContext).Handle(
            new ListProductCategoriesQuery("org-001", "env-dev", Enabled: true, Search: "pump", ParentCode: "CAT-FG", Skip: 0, Take: 10),
            CancellationToken.None);
        var child = Assert.Single(list.Items);

        Assert.Equal("product-category", root.ResourceType);
        Assert.Equal("CAT-FG-PUMP", child.CategoryCode);
        Assert.Equal("CAT-FG", child.ParentCode);
        Assert.Equal("CAT-FG/CAT-FG-PUMP", child.Path);

        var updated = await new UpdateProductCategoryCommandHandler(dbContext).Handle(
            new UpdateProductCategoryCommand("org-001", "env-dev", "CAT-FG-PUMP", "Pump Products Updated", null, "Top level family"),
            CancellationToken.None);

        Assert.Null(updated.ParentCode);
        Assert.Equal("Pump Products Updated", updated.CategoryName);

        var archived = await new ArchiveProductCategoryCommandHandler(dbContext).Handle(
            new ArchiveProductCategoryCommand("org-001", "env-dev", "CAT-FG-PUMP", "retired"),
            CancellationToken.None);

        Assert.False(archived.Enabled);
    }

    [Fact]
    public async Task Product_category_rejects_cycle_when_parent_is_descendant()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductCategories.AddRange(
            ProductCategory.Create("org-001", "env-dev", "CAT-ROOT", "Root", null, null),
            ProductCategory.Create("org-001", "env-dev", "CAT-CHILD", "Child", "CAT-ROOT", null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<KnownException>(() => new UpdateProductCategoryCommandHandler(dbContext).Handle(
            new UpdateProductCategoryCommand("org-001", "env-dev", "CAT-ROOT", "Root", "CAT-CHILD", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Product_category_create_rejects_missing_parent()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var create = new CreateProductCategoryCommandHandler(dbContext, new ProductCategoryRepository(dbContext), new MasterDataCodingService());

        var exception = await Assert.ThrowsAsync<KnownException>(() => create.Handle(
            new CreateProductCategoryCommand("org-001", "env-dev", "CAT-ORPHAN", "Orphan", "CAT-MISSING", null),
            CancellationToken.None));

        Assert.Contains("Parent product category 'CAT-MISSING' was not found.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Product_category_archive_uses_default_reason_when_request_reason_is_blank()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductCategories.Add(ProductCategory.Create("org-001", "env-dev", "CAT-FG", "Finished Goods", null, null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var archived = await new ArchiveProductCategoryCommandHandler(dbContext).Handle(
            new ArchiveProductCategoryCommand("org-001", "env-dev", "CAT-FG", " "),
            CancellationToken.None);

        Assert.False(archived.Enabled);
    }

    [Fact]
    public async Task Product_category_archive_rejects_active_child_or_sku_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductCategories.AddRange(
            ProductCategory.Create("org-001", "env-dev", "CAT-FG", "Finished Goods", null, null),
            ProductCategory.Create("org-001", "env-dev", "CAT-PUMP", "Pump", "CAT-FG", null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ArchiveProductCategoryCommandHandler(dbContext);
        var childReference = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ArchiveProductCategoryCommand("org-001", "env-dev", "CAT-FG", "retired"),
            CancellationToken.None));
        Assert.Contains("active child product category", childReference.Message, StringComparison.OrdinalIgnoreCase);

        var child = await dbContext.ProductCategories.SingleAsync(x => x.CategoryCode == "CAT-PUMP", CancellationToken.None);
        child.Disable("retired");
        dbContext.Skus.Add(Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "FG-PUMP-001",
            "Pump",
            "ea",
            "CAT-FG",
            "finished-goods",
            "none",
            "none",
            "none",
            "ambient",
            "ean13",
            true,
            []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var skuReference = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ArchiveProductCategoryCommand("org-001", "env-dev", "CAT-FG", "retired"),
            CancellationToken.None));
        Assert.Contains("active SKU", skuReference.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Skill_catalog_commands_list_detail_update_and_archive_certification_rules()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var create = new CreateSkillCommandHandler(new SkillRepository(dbContext), new MasterDataCodingService());

        var created = await create.Handle(
            new CreateSkillCommand("org-001", "env-dev", "SK-WELD", "Welding", "Manufacturing", true, 24, "Welding certificate"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var list = await new ListSkillsQueryHandler(dbContext).Handle(
            new ListSkillsQuery("org-001", "env-dev", Enabled: true, Search: "weld", GroupName: "Manufacturing", Skip: 0, Take: 10),
            CancellationToken.None);
        var item = Assert.Single(list.Items);

        Assert.Equal("skill", created.ResourceType);
        Assert.Equal("SK-WELD", item.SkillCode);
        Assert.True(item.RequiresCertification);
        Assert.Equal(24, item.ValidityMonths);

        var updated = await new UpdateSkillCommandHandler(dbContext).Handle(
            new UpdateSkillCommand("org-001", "env-dev", "SK-WELD", "Advanced Welding", "Manufacturing", true, 36, "Advanced certificate"),
            CancellationToken.None);

        Assert.Equal("Advanced Welding", updated.SkillName);
        Assert.Equal(36, updated.ValidityMonths);

        var archived = await new ArchiveSkillCommandHandler(dbContext).Handle(
            new ArchiveSkillCommand("org-001", "env-dev", "SK-WELD", "retired"),
            CancellationToken.None);

        Assert.False(archived.Enabled);
    }

    [Fact]
    public async Task Skill_archive_uses_default_reason_when_request_reason_is_blank()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skills.Add(Skill.Create("org-001", "env-dev", "SK-WELD", "Welding", "Manufacturing", true, 24, null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var archived = await new ArchiveSkillCommandHandler(dbContext).Handle(
            new ArchiveSkillCommand("org-001", "env-dev", "SK-WELD", ""),
            CancellationToken.None);

        Assert.False(archived.Enabled);
    }

    [Fact]
    public async Task Business_partner_credit_query_returns_credit_master_data()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.BusinessPartners.Add(BusinessPartner.Create(
            "org-001",
            "env-dev",
            "CUST-001",
            "customer",
            "Credit Customer",
            ["customer"],
            null,
            defaultCurrencyCode: "CNY",
            creditLimit: 1000m,
            creditCurrencyCode: "CNY"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetBusinessPartnerCreditQueryHandler(dbContext).Handle(
            new GetBusinessPartnerCreditQuery("org-001", "env-dev", "CUST-001"),
            CancellationToken.None);

        Assert.Equal("CUST-001", response.CustomerCode);
        Assert.Equal(1000m, response.CreditLimit);
        Assert.Equal("CNY", response.CurrencyCode);
    }

    [Fact]
    public async Task Business_partner_credit_query_blocks_missing_credit_limit()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.BusinessPartners.Add(BusinessPartner.Create("org-001", "env-dev", "CUST-001", "customer", "Credit Customer"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new GetBusinessPartnerCreditQueryHandler(dbContext).Handle(
            new GetBusinessPartnerCreditQuery("org-001", "env-dev", "CUST-001"),
            CancellationToken.None));

        Assert.Contains("does not have a credit limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_resources_supports_reference_data_codes()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "quality-reason", "scratch", "Scratch"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ListMasterDataResourcesQueryHandler(dbContext);
        var response = await handler.Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "reference-data"),
            CancellationToken.None);

        var resource = Assert.Single(response.Resources);
        Assert.Equal("reference-data", resource.ResourceType);
        Assert.Equal("quality-reason:scratch", resource.Code);
        Assert.Equal("Scratch", resource.DisplayName);
        Assert.True(resource.Active);
    }

    [Fact]
    public async Task List_resources_returns_typed_fields_and_filters_reference_data_code_set()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skus.Add(Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "RM-001",
            "Raw Material",
            "kg",
            "chemical",
            "raw-material",
            "none",
            "none",
            "none",
            "ambient",
            "ean13",
            true,
            []));
        dbContext.BusinessPartners.Add(Domain.AggregatesModel.BusinessPartnerAggregate.BusinessPartner.Create("org-001", "env-dev", "SUP-001", "supplier", "Supplier A", ["supplier", "customer"], "TAX-001", creditLimit: 250000m, creditCurrencyCode: "CNY"));
        dbContext.Workshops.Add(Workshop.Create("org-001", "env-dev", "WS-001", "Mixing Workshop", "SITE-001", "manager-001", "Wet process"));
        dbContext.ProductionLines.Add(Domain.AggregatesModel.ProductionLineAggregate.ProductionLine.Create("org-001", "env-dev", "LINE-001", "Line 1", "SITE-001", "WS-001"));
        dbContext.WorkCenters.Add(Domain.AggregatesModel.WorkCenterAggregate.WorkCenter.CreateResource("org-001", "env-dev", "WC-001", "Mixing", 960, "work-center", "PLANT-001", "LINE-001", "WS-001", "CAL-001", "minute", true));
        dbContext.DeviceAssets.Add(
            Domain.AggregatesModel.DeviceAssetAggregate.DeviceAsset.RegisterCapability("org-001", "env-dev", "DEV-001", "Mixer", "LINE-001", "WC-001", "mixer", "ACME", "SN-001", 10m, 500m, "kg", "critical", true, true, new Dictionary<string, string>())
                .WithLedger(new DateOnly(2024, 1, 15), 125000m, "CNY", new DateOnly(2027, 1, 14), "SUP-001", "SITE-001", "WS-001", "LINE-001", "ST-001", "DEV-PARENT-01", null)
                .ReplaceComponents([new Domain.AggregatesModel.DeviceAssetAggregate.DeviceAssetComponentDraft("MOTOR", "Drive motor", 1m, true)]));
        dbContext.Departments.Add(Domain.AggregatesModel.DepartmentAggregate.Department.Create("org-001", "env-dev", "DEPT-ROOT", "Manufacturing", null));
        dbContext.Departments.Add(Domain.AggregatesModel.DepartmentAggregate.Department.Create("org-001", "env-dev", "DEPT-ALT", "Quality", null));
        dbContext.Departments.Add(Domain.AggregatesModel.DepartmentAggregate.Department.Create("org-001", "env-dev", "DEPT-SUB", "Line Ops", "DEPT-ROOT"));
        dbContext.Teams.Add(Domain.AggregatesModel.TeamAggregate.Team.Create("org-001", "env-dev", "TEAM-001", "Line A Day Team", "DEPT-SUB", "SHIFT-DAY"));
        dbContext.Teams.Add(Domain.AggregatesModel.TeamAggregate.Team.Create("org-001", "env-dev", "TEAM-002", "Quality Team", "DEPT-ROOT", "SHIFT-NIGHT"));
        dbContext.PersonnelSkills.Add(Domain.AggregatesModel.PersonnelSkillAggregate.PersonnelSkill.Assign("org-001", "env-dev", "worker-001", "WELD", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "material-type", "raw-material", "Raw Material"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "storage-condition", "ambient", "Ambient"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ListMasterDataResourcesQueryHandler(dbContext);

        var sku = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku"), CancellationToken.None)).Resources);
        Assert.Equal("chemical", sku.Category);
        Assert.Equal("raw-material", sku.MaterialType);

        var partner = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "business-partner"), CancellationToken.None)).Resources);
        Assert.Equal("supplier", partner.PartnerType);
        Assert.Equal(["supplier", "customer"], partner.PartnerRoles);
        Assert.Equal("TAX-001", partner.TaxId);
        Assert.Equal(250000m, partner.CreditLimit);
        Assert.Equal("CNY", partner.CreditCurrencyCode);

        var line = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "production-line"), CancellationToken.None)).Resources);
        Assert.Equal("SITE-001", line.SiteCode);
        Assert.Equal("WS-001", line.WorkshopCode);

        var workshop = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "workshop"), CancellationToken.None)).Resources);
        Assert.Equal("SITE-001", workshop.SiteCode);
        Assert.Equal("active", workshop.Status);

        var workCenter = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "work-center"), CancellationToken.None)).Resources);
        Assert.Equal("PLANT-001", workCenter.PlantCode);
        Assert.Equal("LINE-001", workCenter.LineCode);
        Assert.Equal("WS-001", workCenter.WorkshopCode);
        Assert.Equal(960, workCenter.CapacityMinutesPerDay);

        var device = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "device-asset"), CancellationToken.None)).Resources);
        Assert.Equal("LINE-001", device.LineCode);
        Assert.Equal("WC-001", device.WorkCenterCode);
        Assert.False(string.IsNullOrWhiteSpace(device.DeviceAssetId));
        Assert.Equal("SITE-001", device.SiteCode);
        Assert.Equal("WS-001", device.WorkshopCode);
        Assert.Equal(new DateOnly(2027, 1, 14), device.WarrantyExpiresOn);
        Assert.Equal("SUP-001", device.SupplierPartnerCode);
        Assert.Equal("active", device.Status);

        var detail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "device-asset", "DEV-001"),
            CancellationToken.None);
        Assert.Equal(new DateOnly(2024, 1, 15), detail.PurchaseDate);
        Assert.Equal(125000m, detail.PurchaseCost);
        Assert.Equal("CNY", detail.PurchaseCurrencyCode);
        Assert.Equal("ST-001", detail.StationCode);
        Assert.Equal("DEV-PARENT-01", detail.ParentDeviceId);
        Assert.Single(detail.Components!);
        Assert.Equal("MOTOR", detail.Components!.Single().ComponentCode);

        var detailByPublicId = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "device-asset", device.DeviceAssetId!),
            CancellationToken.None);
        Assert.Equal("DEV-001", detailByPublicId.Code);
        Assert.Equal(new DateOnly(2027, 1, 14), detailByPublicId.WarrantyExpiresOn);

        var childDepartment = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "department", ParentCode: "DEPT-ROOT"), CancellationToken.None)).Resources);
        Assert.Equal("DEPT-SUB", childDepartment.Code);
        Assert.Equal("DEPT-ROOT", childDepartment.ParentDepartmentCode);

        var team = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "team", DepartmentCode: "DEPT-SUB"), CancellationToken.None)).Resources);
        Assert.Equal("TEAM-001", team.Code);
        Assert.Equal("DEPT-SUB", team.DepartmentCode);
        Assert.Equal("SHIFT-DAY", team.ShiftCode);

        var skill = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "personnel-skill", UserId: "worker-001", SkillCode: "WELD"), CancellationToken.None)).Resources);
        Assert.Equal("worker-001", skill.UserId);
        Assert.Equal("WELD", skill.SkillCode);
        Assert.Equal("senior", skill.SkillLevel);
        Assert.Equal(new DateOnly(2026, 1, 1), skill.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), skill.EffectiveTo);

        var filteredSku = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Category: "chemical", Keyword: "raw"), CancellationToken.None)).Resources);
        Assert.Equal("RM-001", filteredSku.Code);

        var allDepartments = await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "department", Take: 1, All: true), CancellationToken.None);
        Assert.Equal(3, allDepartments.Resources.Count);
        Assert.Equal(3, allDepartments.Total);
        Assert.False(allDepartments.Truncated);
        Assert.Equal(5000, allDepartments.Limit);

        var referenceData = Assert.Single((await handler.Handle(new ListMasterDataResourcesQuery("org-001", "env-dev", "reference-data", CodeSet: "material-type"), CancellationToken.None)).Resources);
        Assert.Equal("material-type", referenceData.CodeSet);
        Assert.Equal("raw-material", referenceData.Code);
    }

    [Fact]
    public async Task List_resources_all_mode_reports_truncation_when_limit_is_reached()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var i = 0; i < 5001; i++)
        {
            dbContext.Departments.Add(Domain.AggregatesModel.DepartmentAggregate.Department.Create("org-001", "env-dev", $"DEPT-{i:0000}", $"Department {i:0000}", null));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListMasterDataResourcesQueryHandler(dbContext).Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "department", All: true),
            CancellationToken.None);

        Assert.Equal(5001, response.Total);
        Assert.Equal(5000, response.Resources.Count);
        Assert.True(response.Truncated);
        Assert.Equal(5000, response.Limit);
    }

    [Fact]
    public async Task Business_partner_supports_multiple_roles_and_unique_tax_id()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext));

        await handler.Handle(
            new CreateBusinessPartnerCommand(
                "org-001",
                "env-dev",
                "BP-001",
                "supplier",
                "Partner A",
                ["supplier", "customer", "carrier"],
                "TAX-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "business-partner", "BP-001"),
            CancellationToken.None);

        Assert.Equal("supplier", detail.PartnerType);
        Assert.Equal(["supplier", "customer", "carrier"], detail.PartnerRoles);
        Assert.Equal("TAX-001", detail.TaxId);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateBusinessPartnerCommand("org-001", "env-dev", "BP-002", "customer", "Partner B", ["customer"], "TAX-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Business_partner_detail_exposes_commercial_contact_defaults_and_role_updates()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext));

        await createHandler.Handle(
            new CreateBusinessPartnerCommand(
                "org-001",
                "env-dev",
                "BP-407",
                "supplier",
                "Partner 407",
                ["supplier"],
                "TAX-407",
                TaxRegionCode: "CN-SH",
                DefaultCurrencyCode: "CNY",
                PaymentTermsCode: "NET30",
                PrimaryAddress: "Shanghai",
                PrimaryContactName: "Li Wei",
                PrimaryContactEmail: "li.wei@example.com",
                PrimaryContactPhone: "+86-21-0000"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var updateHandler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));
        var updated = await updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "business-partner",
                "BP-407",
                Name: "Partner 407 Updated",
                PartnerRoles: ["customer", "supplier"],
                TaxId: "TAX-408",
                TaxRegionCode: "CN-BJ",
                DefaultCurrencyCode: "USD",
                PaymentTermsCode: "NET45",
                PrimaryAddress: "Beijing",
                PrimaryContactName: "Wang Min",
                PrimaryContactEmail: "wang.min@example.com",
                PrimaryContactPhone: "+86-10-0000"),
            CancellationToken.None);

        Assert.Equal("customer", updated.PartnerType);
        Assert.Equal(["customer", "supplier"], updated.PartnerRoles);
        Assert.Equal("CN-BJ", updated.TaxRegionCode);
        Assert.Equal("USD", updated.DefaultCurrencyCode);
        Assert.Equal("NET45", updated.PaymentTermsCode);
        Assert.Equal("Beijing", updated.PrimaryAddress);
        Assert.Equal("Wang Min", updated.PrimaryContactName);
        Assert.Equal("wang.min@example.com", updated.PrimaryContactEmail);
        Assert.Equal("+86-10-0000", updated.PrimaryContactPhone);
    }

    [Fact]
    public async Task Business_partner_update_can_clear_credit_limit()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext));

        await createHandler.Handle(
            new CreateBusinessPartnerCommand(
                "org-001",
                "env-dev",
                "CUST-CREDIT",
                "customer",
                "Credit Customer",
                ["customer"],
                null,
                CreditLimit: 1000m,
                CreditCurrencyCode: "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var updated = await new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext)).Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "business-partner",
                "CUST-CREDIT",
                ClearCreditLimit: true),
            CancellationToken.None);

        Assert.Null(updated.CreditLimit);
        Assert.Null(updated.CreditCurrencyCode);
    }

    [Fact]
    public async Task Business_partner_partial_update_partner_type_preserves_secondary_roles()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext));

        await createHandler.Handle(
            new CreateBusinessPartnerCommand(
                "org-001",
                "env-dev",
                "BP-MULTI",
                "supplier",
                "Multi-role Partner",
                ["supplier", "carrier"],
                "TAX-MULTI"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var updateHandler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));
        var updated = await updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "business-partner",
                "BP-MULTI",
                PartnerType: "customer"),
            CancellationToken.None);

        Assert.Equal("customer", updated.PartnerType);
        Assert.Equal(["customer", "carrier"], updated.PartnerRoles);
    }

    [Fact]
    public async Task Business_partner_tax_id_uniqueness_ignores_disabled_partners()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createHandler = new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext));

        await createHandler.Handle(
            new CreateBusinessPartnerCommand("org-001", "env-dev", "BP-OLD", "supplier", "Old Partner", ["supplier"], "TAX-REUSE"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var oldPartner = await dbContext.BusinessPartners.SingleAsync(x => x.Code == "BP-OLD", CancellationToken.None);
        oldPartner.Disable("retired");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await createHandler.Handle(
            new CreateBusinessPartnerCommand("org-001", "env-dev", "BP-NEW", "customer", "New Partner", ["customer"], "TAX-REUSE"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("BP-NEW", created.Code);
    }

    [Fact]
    public async Task MasterData_lifecycle_commands_update_disable_enable_and_detail_by_code()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skus.Add(Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "RM-001",
            "Raw Material",
            "kg",
            "chemical",
            "raw-material",
            "none",
            "none",
            "none",
            "ambient",
            "ean13",
            true,
            []));
        dbContext.UomConversions.Add(Domain.AggregatesModel.UomConversionAggregate.UomConversion.Create(
            "org-001",
            "env-dev",
            "kg",
            "g",
            1000m,
            0m,
            3,
            "half-up",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        SeedSkuControlledReferenceData(dbContext);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var update = await new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext)).Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "sku",
                "RM-001",
                Name: "Updated Raw Material",
                BaseUomCode: "g",
                Category: "chemical",
                MaterialType: "semi-finished"),
            CancellationToken.None);

        Assert.Equal("RM-001", update.Code);
        Assert.Equal("Updated Raw Material", update.DisplayName);
        Assert.Equal("semi-finished", update.MaterialType);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var disabled = await new SetMasterDataResourceEnabledCommandHandler(dbContext).Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "RM-001", false, Reason: "duplicate"),
            CancellationToken.None);
        Assert.False(disabled.Active);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var activeList = await new ListMasterDataResourcesQueryHandler(dbContext).Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "sku"),
            CancellationToken.None);
        Assert.Empty(activeList.Resources);

        var detail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "sku", "RM-001"),
            CancellationToken.None);
        Assert.False(detail.Active);
        Assert.Equal("g", detail.BaseUomCode);

        var enabled = await new SetMasterDataResourceEnabledCommandHandler(dbContext).Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "RM-001", true, Reason: "reactivated"),
            CancellationToken.None);
        Assert.True(enabled.Active);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MasterData_disable_rejects_active_uom_and_work_center_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "ea", "Each", "quantity", 0, "half-up"));
        dbContext.Skus.Add(Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "FG-EA-001",
            "Each Finished Good",
            "ea",
            "finished-good",
            "finished-goods",
            "none",
            "none",
            "none",
            "ambient",
            "ean13",
            true,
            []));
        dbContext.WorkCenters.Add(Domain.AggregatesModel.WorkCenterAggregate.WorkCenter.Create("org-001", "env-dev", "WC-ASSY", "Assembly", 480));
        dbContext.DeviceAssets.Add(Domain.AggregatesModel.DeviceAssetAggregate.DeviceAsset.Register("org-001", "env-dev", "DEV-ASSY", "Assembly Device", "LINE-1", "WC-ASSY"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new SetMasterDataResourceEnabledCommandHandler(dbContext);
        var uomReference = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "unit-of-measure", "ea", false, Reason: "retired"),
            CancellationToken.None));
        Assert.Contains("active SKU", uomReference.Message, StringComparison.OrdinalIgnoreCase);

        var workCenterReference = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "work-center", "WC-ASSY", false, Reason: "retired"),
            CancellationToken.None));
        Assert.Contains("active device asset", workCenterReference.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MasterData_disable_rejects_active_uom_conversion_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UnitsOfMeasure.AddRange(
            Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "kg", "Kilogram", "weight", 3, "half-up"),
            Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "g", "Gram", "weight", 0, "half-up"));
        dbContext.UomConversions.Add(Domain.AggregatesModel.UomConversionAggregate.UomConversion.Create(
            "org-001",
            "env-dev",
            "kg",
            "g",
            1000m,
            0m,
            3,
            "half-up",
            new DateOnly(2026, 1, 1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new SetMasterDataResourceEnabledCommandHandler(dbContext);
        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "unit-of-measure", "kg", false, Reason: "retired"),
            CancellationToken.None));

        Assert.Contains("UOM conversion", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MasterData_disable_rejects_device_asset_supplier_and_parent_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.BusinessPartners.Add(BusinessPartner.Create("org-001", "env-dev", "SUP-ACME", "supplier", "ACME Supplier"));
        var supplierDevice = DeviceAsset.Register("org-001", "env-dev", "DEV-SUP", "Supplier Device", "LINE-1", "WC-1")
            .WithLedger(null, null, string.Empty, null, "SUP-ACME", string.Empty, string.Empty, "LINE-1", string.Empty, null, null);
        var parentDevice = DeviceAsset.Register("org-001", "env-dev", "DEV-PARENT", "Parent Device", "LINE-1", "WC-1");
        dbContext.DeviceAssets.AddRange(supplierDevice, parentDevice);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var childDevice = DeviceAsset.Register("org-001", "env-dev", "DEV-CHILD", "Child Device", "LINE-1", "WC-1")
            .WithLedger(null, null, string.Empty, null, string.Empty, string.Empty, string.Empty, "LINE-1", string.Empty, parentDevice.Id.ToString(), null);
        dbContext.DeviceAssets.Add(childDevice);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new SetMasterDataResourceEnabledCommandHandler(dbContext);
        var supplierReference = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "business-partner", "SUP-ACME", false, Reason: "retired"),
            CancellationToken.None));
        Assert.Contains("active device asset", supplierReference.Message, StringComparison.OrdinalIgnoreCase);

        var parentReference = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "device-asset", "DEV-PARENT", false, Reason: "retired"),
            CancellationToken.None));
        Assert.Contains("active child device asset", parentReference.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductEngineering_reference_checker_converts_downstream_http_failure_to_known_exception()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("https://product-engineering.local")
        };
        var checker = new HttpProductEngineeringReferenceUsageChecker(httpClient, new FixedInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => checker.GetWorkCenterUsageAsync(
            "org-001",
            "env-dev",
            "WC-MIX",
            CancellationToken.None));

        Assert.Contains("ProductEngineering work center usage check", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductEngineering_reference_checker_treats_missing_references_as_empty()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"success":true,"message":"ok","code":0,"data":{"hasActiveReference":true}}""",
                Encoding.UTF8,
                "application/json")
        }))
        {
            BaseAddress = new Uri("https://product-engineering.local")
        };
        var checker = new HttpProductEngineeringReferenceUsageChecker(httpClient, new FixedInternalServiceTokenProvider());

        var usage = await checker.GetWorkCenterUsageAsync(
            "org-001",
            "env-dev",
            "WC-MIX",
            CancellationToken.None);

        Assert.True(usage.HasActiveReference);
        Assert.Empty(usage.References);
    }

    [Fact]
    public async Task MasterData_disable_rejects_product_engineering_work_center_references()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkCenters.Add(Domain.AggregatesModel.WorkCenterAggregate.WorkCenter.Create("org-001", "env-dev", "WC-MIX", "Mixing", 480));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new SetMasterDataResourceEnabledCommandHandler(
            dbContext,
            new FixedDownstreamReferenceChecker(new MasterDataDownstreamReferenceUsage(true, ["routing:ROUTE-MIX:A"])));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "work-center", "WC-MIX", false, Reason: "retired"),
            CancellationToken.None));

        Assert.Contains("ProductEngineering", exception.Message, StringComparison.Ordinal);
        Assert.Contains("routing:ROUTE-MIX:A", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MasterData_lifecycle_commands_update_organization_and_shift_structure_fields()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Departments.Add(Domain.AggregatesModel.DepartmentAggregate.Department.Create("org-001", "env-dev", "DEPT-ROOT", "Manufacturing", null));
        dbContext.Departments.Add(Domain.AggregatesModel.DepartmentAggregate.Department.Create("org-001", "env-dev", "DEPT-SUB", "Line Ops", "DEPT-ROOT"));
        dbContext.Teams.Add(Domain.AggregatesModel.TeamAggregate.Team.Create("org-001", "env-dev", "TEAM-001", "Line A Day Team", "DEPT-SUB", "SHIFT-DAY"));
        dbContext.Shifts.Add(Domain.AggregatesModel.ShiftAggregate.Shift.Create("org-001", "env-dev", "SHIFT-DAY", "Day", new TimeOnly(8, 0), new TimeOnly(20, 0), 720));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));

        var department = await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "department",
                "DEPT-SUB",
                Name: "Assembly Ops",
                ParentDepartmentCode: "DEPT-ALT"),
            CancellationToken.None);
        Assert.Equal("Assembly Ops", department.DisplayName);
        Assert.Equal("DEPT-ALT", department.ParentDepartmentCode);

        var team = await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "team",
                "TEAM-001",
                DepartmentCode: "DEPT-ROOT",
                ShiftCode: "SHIFT-NIGHT"),
            CancellationToken.None);
        Assert.Equal("DEPT-ROOT", team.DepartmentCode);
        Assert.Equal("SHIFT-NIGHT", team.ShiftCode);

        var shift = await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "shift",
                "SHIFT-DAY",
                Name: "Day 8h",
                StartsAt: new TimeOnly(8, 30),
                EndsAt: new TimeOnly(17, 30),
                PaidMinutes: 480),
            CancellationToken.None);
        Assert.Equal("Day 8h", shift.DisplayName);
        Assert.Equal(new TimeOnly(8, 30), shift.StartsAt);
        Assert.Equal(new TimeOnly(17, 30), shift.EndsAt);
        Assert.Equal(480, shift.PaidMinutes);

        var enabledHandler = new SetMasterDataResourceEnabledCommandHandler(dbContext);
        var disabled = await enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "shift", "SHIFT-DAY", false, Reason: "retired"),
            CancellationToken.None);
        Assert.False(disabled.Active);
        var disabledAgain = await enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "shift", "SHIFT-DAY", false, Reason: "duplicate click"),
            CancellationToken.None);
        Assert.False(disabledAgain.Active);
    }

    [Fact]
    public async Task MasterData_lifecycle_commands_manage_work_calendar_details_and_uom_conversion_crud()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "kg", "Kilogram", "weight", 3, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "g", "Gram", "weight", 3, "half-up"));
        dbContext.UomConversions.Add(Domain.AggregatesModel.UomConversionAggregate.UomConversion.Create("org-001", "env-dev", "kg", "g", 1000m, 0m, 3, "half-up", new DateOnly(2026, 1, 1)));
        var calendar = Domain.AggregatesModel.WorkCalendarAggregate.WorkCalendar.Create("org-001", "env-dev", "CAL-001", "Standard Calendar");
        calendar.AddWorkingDay(DayOfWeek.Monday);
        dbContext.WorkCalendars.Add(calendar);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var updateHandler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));

        var calendarDetail = await updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "work-calendar",
                "CAL-001",
                Name: "Factory Calendar",
                WorkingTimes:
                [
                    new WorkCalendarWorkingTimeDetail(DayOfWeek.Monday),
                    new WorkCalendarWorkingTimeDetail(DayOfWeek.Tuesday)
                ],
                Holidays: [new WorkCalendarHolidayDetail(new DateOnly(2026, 5, 1), "Labor Day")],
                Exceptions: [new WorkCalendarExceptionDetail(new DateOnly(2026, 5, 2), true, new TimeOnly(9, 0), new TimeOnly(15, 0), "Make-up shift")]),
            CancellationToken.None);

        Assert.Equal("Factory Calendar", calendarDetail.DisplayName);
        Assert.Equal(2, calendarDetail.WorkingTimes?.Count);
        Assert.Single(calendarDetail.Holidays!);
        Assert.Single(calendarDetail.Exceptions!);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var persistedCalendar = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "work-calendar", "CAL-001"),
            CancellationToken.None);
        Assert.Equal(2, persistedCalendar.WorkingTimes?.Count);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday],
            persistedCalendar.WorkingTimes!.Select(x => x.DayOfWeek).OrderBy(x => x).ToArray());
        Assert.Equal(new DateOnly(2026, 5, 1), Assert.Single(persistedCalendar.Holidays!).Date);

        var conversionDetail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "uom-conversion", "kg->g", EffectiveFrom: new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        Assert.Equal(1000m, conversionDetail.Factor);
        Assert.Equal(new DateOnly(2026, 1, 1), conversionDetail.EffectiveFrom);

        var conversionUpdate = await updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "uom-conversion",
                "kg->g",
                Factor: 1000.5m,
                Offset: 0.1m,
                Precision: 4,
                RoundingMode: "bankers",
                EffectiveFrom: new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        Assert.Equal(1000.5m, conversionUpdate.Factor);
        Assert.Equal(0.1m, conversionUpdate.Offset);
        Assert.Equal(4, conversionUpdate.Precision);
        Assert.Equal("bankers", conversionUpdate.RoundingMode);

        var kg = await dbContext.UnitsOfMeasure.SingleAsync(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev" && x.Code == "kg", CancellationToken.None);
        kg.Disable("legacy unit");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var conversionUpdateWithDisabledUnit = await updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "uom-conversion",
                "kg->g",
                Factor: 1000.75m,
                EffectiveFrom: new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        Assert.Equal(1000.75m, conversionUpdateWithDisabledUnit.Factor);

        var enabledHandler = new SetMasterDataResourceEnabledCommandHandler(dbContext);
        var disabled = await enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "uom-conversion", "kg->g", false, Reason: "superseded", EffectiveFrom: new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        Assert.False(disabled.Active);
        var disabledAgain = await enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "uom-conversion", "kg->g", false, Reason: "duplicate click", EffectiveFrom: new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        Assert.False(disabledAgain.Active);

        var disabledCalendar = await enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "work-calendar", "CAL-001", false, Reason: "retired"),
            CancellationToken.None);
        Assert.False(disabledCalendar.Active);
        var disabledCalendarAgain = await enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "work-calendar", "CAL-001", false, Reason: "duplicate click"),
            CancellationToken.None);
        Assert.False(disabledCalendarAgain.Active);
    }

    [Fact]
    public async Task Personnel_skill_matrix_query_groups_skills_by_worker_and_filters_by_skill()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.PersonnelSkills.Add(Domain.AggregatesModel.PersonnelSkillAggregate.PersonnelSkill.Assign("org-001", "env-dev", "worker-001", "WELD", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        dbContext.PersonnelSkills.Add(Domain.AggregatesModel.PersonnelSkillAggregate.PersonnelSkill.Assign("org-001", "env-dev", "worker-001", "QA", "junior", new DateOnly(2026, 2, 1), new DateOnly(2026, 12, 31)));
        dbContext.PersonnelSkills.Add(Domain.AggregatesModel.PersonnelSkillAggregate.PersonnelSkill.Assign("org-001", "env-dev", "worker-002", "WELD", "junior", new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 31)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ListPersonnelSkillMatrixQueryHandler(dbContext);

        var matrix = await handler.Handle(new ListPersonnelSkillMatrixQuery("org-001", "env-dev"), CancellationToken.None);
        Assert.Equal(2, matrix.Rows.Count);
        Assert.Equal(2, matrix.SkillCodes.Count);
        Assert.Equal(2, matrix.Rows.Single(x => x.UserId == "worker-001").Skills.Count);

        var weldOnly = await handler.Handle(new ListPersonnelSkillMatrixQuery("org-001", "env-dev", SkillCode: "WELD"), CancellationToken.None);
        Assert.Equal(["WELD"], weldOnly.SkillCodes);
        Assert.All(weldOnly.Rows, row => Assert.Single(row.Skills));
        Assert.Equal(["worker-001", "worker-002"], weldOnly.Rows.Select(x => x.UserId).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Create_uom_conversion_requires_same_dimension_and_allows_reverse_conversion()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "kg", "Kilogram", "weight", 3, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "g", "Gram", "weight", 3, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "l", "Liter", "volume", 3, "half-up"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateUomConversionCommandHandler(new UomConversionRepository(dbContext), dbContext);

        var dimensionMismatch = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateUomConversionCommand("org-001", "env-dev", "kg", "l", 1m, 0m, 3, "half-up", new DateOnly(2026, 1, 1)),
            CancellationToken.None));
        Assert.Contains("same dimension", dimensionMismatch.Message, StringComparison.OrdinalIgnoreCase);

        await handler.Handle(
            new CreateUomConversionCommand("org-001", "env-dev", "kg", "g", 1000m, 0m, 3, "half-up", new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reverse = await handler.Handle(
            new CreateUomConversionCommand("org-001", "env-dev", "g", "kg", 0.001m, 0m, 3, "half-up", new DateOnly(2026, 1, 1)),
            CancellationToken.None);
        Assert.Equal("g->kg", reverse.Code);
    }

    [Fact]
    public async Task Uom_conversion_detail_exposes_effective_to()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "box", "Box", "quantity", 0, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "ea", "Each", "quantity", 0, "half-up"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new CreateUomConversionCommandHandler(new UomConversionRepository(dbContext), dbContext);

        await handler.Handle(
            new CreateUomConversionCommand("org-001", "env-dev", "box", "ea", 24m, 0m, 0, "half-up", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "uom-conversion", "box->ea"),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 12, 31), detail.EffectiveTo);

        var list = await new ListMasterDataResourcesQueryHandler(dbContext).Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "uom-conversion"),
            CancellationToken.None);
        var item = Assert.Single(list.Resources);
        Assert.Equal(new DateOnly(2026, 12, 31), item.EffectiveTo);
    }

    [Fact]
    public async Task Reference_data_detail_update_and_enable_require_code_set()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "quality-level", "none", "None"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "shelf-life-policy", "none", "No Shelf Life"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detailHandler = new GetMasterDataResourceDetailQueryHandler(dbContext);
        var updateHandler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));
        var enabledHandler = new SetMasterDataResourceEnabledCommandHandler(dbContext);

        await Assert.ThrowsAsync<KnownException>(() => detailHandler.Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "reference-data", "none"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => updateHandler.Handle(
            new UpdateMasterDataResourceCommand("org-001", "env-dev", "reference-data", "none", Name: "Updated"),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => enabledHandler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "reference-data", "none", false, Reason: "disabled"),
            CancellationToken.None));

        var detail = await detailHandler.Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "reference-data", "none", "shelf-life-policy"),
            CancellationToken.None);

        Assert.Equal("shelf-life-policy", detail.CodeSet);
        Assert.Equal("No Shelf Life", detail.DisplayName);
    }

    [Fact]
    public async Task List_resources_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-001", "Sku 1", "pcs", "electronic"));
        dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-002", "Sku 2", "pcs", "electronic"));
        dbContext.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-003", "Sku 3", "pcs", "electronic"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListMasterDataResourcesQueryHandler(dbContext).Handle(
            new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Equal("SKU-002", Assert.Single(response.Resources).Code);
    }

    [Fact]
    public async Task MasterData_create_commands_create_core_resources_without_direct_save()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "kg", "Kilogram", "mass", 3, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "g", "Gram", "mass", 3, "half-up"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = new[]
        {
            await new CreateSkuCommandHandler(new SkuRepository(dbContext)).Handle(
                new CreateSkuCommand("org-001", "env-dev", "SKU-001", "Finished Good", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, ["rohs"]),
                CancellationToken.None),
            await new CreateUnitOfMeasureCommandHandler(new UnitOfMeasureRepository(dbContext)).Handle(
                new CreateUnitOfMeasureCommand("org-001", "env-dev", "lb", "Pound", "mass", 3, "half-up"),
                CancellationToken.None),
            await new CreateUomConversionCommandHandler(new UomConversionRepository(dbContext), dbContext).Handle(
                new CreateUomConversionCommand("org-001", "env-dev", "kg", "g", 1000m, 0m, 3, "half-up", new DateOnly(2026, 1, 1)),
                CancellationToken.None),
            await new CreateBusinessPartnerCommandHandler(new BusinessPartnerRepository(dbContext)).Handle(
                new CreateBusinessPartnerCommand("org-001", "env-dev", "SUP-001", "supplier", "Supplier A"),
                CancellationToken.None),
            await new CreateDepartmentCommandHandler(new DepartmentRepository(dbContext)).Handle(
                new CreateDepartmentCommand("org-001", "env-dev", "D-001", "Production", null),
                CancellationToken.None),
            await new CreateTeamCommandHandler(new TeamRepository(dbContext)).Handle(
                new CreateTeamCommand("org-001", "env-dev", "T-001", "Team A", "D-001", "S-001"),
                CancellationToken.None),
            await new CreateWorkshopCommandHandler(new WorkshopRepository(dbContext)).Handle(
                new CreateWorkshopCommand("org-001", "env-dev", "WS-001", "Workshop A", "SITE-001", "user-manager", "Process workshop"),
                CancellationToken.None),
            await new AddTeamMemberCommandHandler(new TeamMemberRepository(dbContext)).Handle(
                new AddTeamMemberCommand("org-001", "env-dev", "T-001", "user-001", true, new DateOnly(2026, 1, 1), null),
                CancellationToken.None),
            await new AssignPersonnelSkillCommandHandler(new PersonnelSkillRepository(dbContext)).Handle(
                new AssignPersonnelSkillCommand("org-001", "env-dev", "user-001", "weighing", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
                CancellationToken.None),
            await new CreateSiteCommandHandler(new SiteRepository(dbContext)).Handle(
                new CreateSiteCommand("org-001", "env-dev", "SITE-001", "Main Plant", "Asia/Shanghai"),
                CancellationToken.None),
            await new CreateProductionLineCommandHandler(new ProductionLineRepository(dbContext)).Handle(
                new CreateProductionLineCommand("org-001", "env-dev", "LINE-001", "Line 1", "SITE-001", "WS-001"),
                CancellationToken.None),
            await new CreateShiftCommandHandler(new ShiftRepository(dbContext)).Handle(
                new CreateShiftCommand("org-001", "env-dev", "S-001", "Night Shift", new TimeOnly(20, 0), new TimeOnly(8, 0), 720),
                CancellationToken.None),
            await new CreateWorkCalendarCommandHandler(new WorkCalendarRepository(dbContext)).Handle(
                new CreateWorkCalendarCommand("org-001", "env-dev", "CAL-001", "Standard Calendar"),
                CancellationToken.None),
            await new CreateWorkCenterCommandHandler(new WorkCenterRepository(dbContext)).Handle(
                new CreateWorkCenterCommand("org-001", "env-dev", "WC-001", "Mixing", 960, "work-center", "SITE-001", "LINE-001", "CAL-001", "minute", true, "WS-001"),
                CancellationToken.None),
            await new RegisterDeviceAssetCommandHandler(new DeviceAssetRepository(dbContext)).Handle(
                new RegisterDeviceAssetCommand("org-001", "env-dev", "EQ-001", "Mixer 500", "LINE-001", "WC-001", "mixer", "ACME", "SN-001", 10m, 500m, "kg", "critical", true, true, new Dictionary<string, string>()),
                CancellationToken.None),
            await new CreateReferenceDataCodeCommandHandler(new ReferenceDataCodeRepository(dbContext)).Handle(
                new CreateReferenceDataCodeCommand("org-001", "env-dev", "quality-reason", "scratch", "Scratch"),
                CancellationToken.None),
        };

        Assert.Equal(16, created.Length);
        Assert.Contains(created, x => x.ResourceType == "sku" && x.Code == "SKU-001");
        Assert.Contains(created, x => x.ResourceType == "uom-conversion" && x.Code == "kg->g");
        Assert.Contains(created, x => x.ResourceType == "workshop" && x.Code == "WS-001");
        Assert.Contains(created, x => x.ResourceType == "team-member" && x.Code == "T-001:user-001");
        Assert.Contains(created, x => x.ResourceType == "reference-data-code" && x.Code == "scratch");
        Assert.Equal(16, dbContext.ChangeTracker.Entries().Count(entry => entry.State == EntityState.Added));
    }

    [Fact]
    public async Task Device_asset_commands_reject_invalid_component_quantity_and_currency()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var registerHandler = new RegisterDeviceAssetCommandHandler(new DeviceAssetRepository(dbContext));

        var invalidQuantity = await Assert.ThrowsAsync<KnownException>(() => registerHandler.Handle(
            new RegisterDeviceAssetCommand(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Code: "EQ-BAD-QTY",
                Model: "Mixer 500",
                LineCode: "LINE-001",
                WorkCenterCode: "WC-001",
                AssetClassCode: "mixer",
                Manufacturer: "ACME",
                SerialNo: "SN-001",
                MinimumCapacity: 10m,
                MaximumCapacity: 500m,
                CapacityUomCode: "kg",
                Criticality: "critical",
                Maintainable: true,
                TelemetryEnabled: true,
                ExternalReferences: new Dictionary<string, string>(),
                Components: [new DeviceAssetComponentDraft("MOTOR", "Drive motor", 0m, true)]),
            CancellationToken.None));
        Assert.Contains("quantity must be greater than zero", invalidQuantity.Message, StringComparison.OrdinalIgnoreCase);

        var invalidCurrency = await Assert.ThrowsAsync<KnownException>(() => registerHandler.Handle(
            new RegisterDeviceAssetCommand(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Code: "EQ-BAD-CURRENCY",
                Model: "Mixer 500",
                LineCode: "LINE-001",
                WorkCenterCode: "WC-001",
                AssetClassCode: "mixer",
                Manufacturer: "ACME",
                SerialNo: "SN-002",
                MinimumCapacity: 10m,
                MaximumCapacity: 500m,
                CapacityUomCode: "kg",
                Criticality: "critical",
                Maintainable: true,
                TelemetryEnabled: true,
                ExternalReferences: new Dictionary<string, string>(),
                PurchaseCurrencyCode: "USDT"),
            CancellationToken.None));
        Assert.Contains("3-letter ISO 4217", invalidCurrency.Message, StringComparison.OrdinalIgnoreCase);

        dbContext.DeviceAssets.Add(DeviceAsset.Register("org-001", "env-dev", "EQ-OK", "Mixer 500", "LINE-001", "WC-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var updateHandler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));

        var invalidUpdate = await Assert.ThrowsAsync<KnownException>(() => updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "device-asset",
                "EQ-OK",
                Components: [new DeviceAssetComponentDetail("MOTOR", "Drive motor", -1m, true)]),
            CancellationToken.None));
        Assert.Contains("quantity must be greater than zero", invalidUpdate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Team_member_queries_list_and_remove_active_members()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = new TeamMemberRepository(dbContext);
        await new AddTeamMemberCommandHandler(repository).Handle(
            new AddTeamMemberCommand("org-001", "env-dev", "T-001", "user-001", true, new DateOnly(2026, 1, 1), null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var list = await new ListTeamMembersQueryHandler(dbContext).Handle(
            new ListTeamMembersQuery("org-001", "env-dev", "T-001"),
            CancellationToken.None);

        var member = Assert.Single(list.Members);
        Assert.Equal("user-001", member.UserId);
        Assert.True(member.IsLeader);

        await new RemoveTeamMemberCommandHandler(dbContext).Handle(
            new RemoveTeamMemberCommand("org-001", "env-dev", "T-001", "user-001", "transferred"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var activeList = await new ListTeamMembersQueryHandler(dbContext).Handle(
            new ListTeamMembersQuery("org-001", "env-dev", "T-001"),
            CancellationToken.None);
        Assert.Empty(activeList.Members);
    }

    [Fact]
    public async Task Create_sku_command_rejects_duplicate_business_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Skus.Add(Domain.AggregatesModel.SkuAggregate.Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "SKU-001",
            "Finished Good",
            "kg",
            "electronic",
            "finished-goods",
            "none",
            "none",
            "none",
            "ambient",
            "ean13",
            true,
            ["rohs"]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", "SKU-001", "Duplicate", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, []),
            CancellationToken.None));
        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_sku_command_validates_controlled_reference_data_when_repository_is_available()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(
            new SkuRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext));

        var missing = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", "SKU-001", "Finished Good", "kg", "missing-category", "finished-goods", "none", "none", "none", "ambient", "ean13", true, []),
            CancellationToken.None));
        Assert.Contains("product-category:missing-category", missing.Message, StringComparison.Ordinal);

        SeedSkuControlledReferenceData(dbContext);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", "SKU-001", "Finished Good", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, []),
            CancellationToken.None);

        Assert.Equal("sku", created.ResourceType);
        Assert.Equal("SKU-001", created.Code);
    }

    [Fact]
    public async Task Create_sku_command_preserves_channel_uoms_planning_profile_and_lifecycle_flags()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSkuControlledReferenceData(dbContext);
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "ea", "Each", "quantity", 0, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "box", "Box", "quantity", 0, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "case", "Case", "quantity", 0, "half-up"));
        dbContext.UomConversions.Add(Domain.AggregatesModel.UomConversionAggregate.UomConversion.Create("org-001", "env-dev", "box", "ea", 24m, 0m, 0, "half-up", new DateOnly(2026, 1, 1)));
        dbContext.UomConversions.Add(Domain.AggregatesModel.UomConversionAggregate.UomConversion.Create("org-001", "env-dev", "case", "ea", 12m, 0m, 0, "half-up", new DateOnly(2026, 1, 1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateSkuCommandHandler(
            new SkuRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext),
            dbContext);

        await handler.Handle(
            new CreateSkuCommand(
                "org-001",
                "env-dev",
                "SKU-407",
                "Issue 407 SKU",
                "ea",
                "electronic",
                "finished-goods",
                "none",
                "none",
                "none",
                "ambient",
                "ean13",
                true,
                [],
                InventoryUomCode: "ea",
                PurchaseUomCode: "box",
                SalesUomCode: "case",
                ManufacturingUomCode: "ea",
                ProcurementType: "make",
                MrpType: "pd",
                LotSizingPolicy: "fixed-lot",
                MinimumLotSize: 10m,
                MaximumLotSize: 1000m,
                LotSizeMultiple: 5m,
                SafetyStockQuantity: 25m,
                ReorderPointQuantity: 50m,
                PlannedDeliveryTimeDays: 7,
                InHouseProductionTimeDays: 2,
                GoodsReceiptProcessingTimeDays: 1,
                AbcClass: "A",
                LifecycleStatus: "blocked",
                PurchasingEnabled: false,
                ManufacturingEnabled: true,
                SalesEnabled: false),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery("org-001", "env-dev", "sku", "SKU-407"),
            CancellationToken.None);

        Assert.Equal("box", detail.PurchaseUomCode);
        Assert.Equal("case", detail.SalesUomCode);
        Assert.Equal("make", detail.ProcurementType);
        Assert.Equal("pd", detail.MrpType);
        Assert.Equal("fixed-lot", detail.LotSizingPolicy);
        Assert.Equal(25m, detail.SafetyStockQuantity);
        Assert.Equal(50m, detail.ReorderPointQuantity);
        Assert.Equal("blocked", detail.LifecycleStatus);
        Assert.False(detail.PurchasingEnabled);
        Assert.True(detail.ManufacturingEnabled);
        Assert.False(detail.SalesEnabled);
    }

    [Fact]
    public async Task Create_sku_command_rejects_missing_or_expired_channel_uom_conversion()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSkuControlledReferenceData(dbContext);
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "ea", "Each", "quantity", 0, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "box", "Box", "quantity", 0, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "case", "Case", "quantity", 0, "half-up"));
        dbContext.UomConversions.Add(Domain.AggregatesModel.UomConversionAggregate.UomConversion.Create(
            "org-001",
            "env-dev",
            "box",
            "ea",
            24m,
            0m,
            0,
            "half-up",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateSkuCommandHandler(
            new SkuRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext),
            dbContext);

        var expired = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", "SKU-EXPIRED-UOM", "Expired UOM", "ea", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], PurchaseUomCode: "box"),
            CancellationToken.None));
        Assert.Contains("requires an active direct conversion", expired.Message, StringComparison.Ordinal);

        var missing = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", "SKU-MISSING-UOM", "Missing UOM", "ea", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], SalesUomCode: "case"),
            CancellationToken.None));
        Assert.Contains("requires an active direct conversion", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_sku_command_rejects_missing_channel_uom_conversion()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedSkuControlledReferenceData(dbContext);
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "ea", "Each", "quantity", 0, "half-up"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "box", "Box", "quantity", 0, "half-up"));
        dbContext.Skus.Add(Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "SKU-UPDATE-UOM",
            "Update UOM",
            "ea",
            "electronic",
            "finished-goods",
            "none",
            "none",
            "none",
            "ambient",
            "ean13",
            true,
            []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new UpdateMasterDataResourceCommand("org-001", "env-dev", "sku", "SKU-UPDATE-UOM", PurchaseUomCode: "box"),
            CancellationToken.None));

        Assert.Contains("requires an active direct conversion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assign_personnel_skill_command_validates_skill_and_level_reference_data_when_repository_is_available()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new AssignPersonnelSkillCommandHandler(
            new PersonnelSkillRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext));

        var missingSkill = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new AssignPersonnelSkillCommand("org-001", "env-dev", "worker-001", "welding", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None));
        Assert.Contains("skill:welding", missingSkill.Message, StringComparison.Ordinal);

        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "skill", "welding", "焊接"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var missingLevel = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new AssignPersonnelSkillCommand("org-001", "env-dev", "worker-001", "welding", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None));
        Assert.Contains("skill-level:senior", missingLevel.Message, StringComparison.Ordinal);

        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "skill-level", "senior", "高级"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await handler.Handle(
            new AssignPersonnelSkillCommand("org-001", "env-dev", "worker-001", "welding", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None);

        Assert.Equal("personnel-skill", created.ResourceType);
        Assert.Equal("worker-001:welding", created.Code);
    }

    [Theory]
    [InlineData("", "senior", "SkillCode", "skill")]
    [InlineData("welding", "", "Level", "skill-level")]
    public async Task Assign_personnel_skill_command_rejects_blank_controlled_reference_fields(
        string skillCode,
        string level,
        string expectedField,
        string expectedCodeSet)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new AssignPersonnelSkillCommandHandler(
            new PersonnelSkillRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext));
        if (!string.IsNullOrWhiteSpace(skillCode))
        {
            dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "skill", skillCode, skillCode));
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "skill-level", level, level));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new AssignPersonnelSkillCommand("org-001", "env-dev", "worker-001", skillCode, level, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None));

        Assert.Contains($"Personnel skill field '{expectedField}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{expectedCodeSet}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_data_seed_is_idempotent_and_creates_controlled_reference_data()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new MasterDataSeedService(dbContext);

        await seed.SeedAsync("org-001", "env-dev", CancellationToken.None);
        await seed.SeedAsync("org-001", "env-dev", CancellationToken.None);

        Assert.True(await dbContext.ReferenceDataCodes.AnyAsync(x =>
            x.OrganizationId == "org-001" &&
            x.EnvironmentId == "env-dev" &&
            x.CodeSet == "product-category" &&
            x.Code == "electronic" &&
            !x.Disabled));
        Assert.True(await dbContext.ReferenceDataCodes.AnyAsync(x =>
            x.CodeSet == "barcode-rule" &&
            x.Code == "gs1-128" &&
            !x.Disabled));
        Assert.Single(dbContext.UnitsOfMeasure.Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev" && x.Code == "kg"));
        Assert.Single(dbContext.UomConversions.Where(x => x.FromUomCode == "kg" && x.ToUomCode == "g"));
        Assert.Single(dbContext.Shifts.Where(x => x.Code == "DAY"));
        Assert.Single(dbContext.WorkCalendars.Where(x => x.Code == "STANDARD"));
    }

    [Fact]
    public async Task Create_sku_command_generates_unique_server_codes_for_parallel_requests()
    {
        await using var provider = CreateInMemoryProvider();
        var numbering = new MasterDataCodingService();

        var tasks = Enumerable.Range(1, 20)
            .Select(async index =>
            {
                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), numbering);
                var result = await handler.Handle(
                    new CreateSkuCommand("org-001", "env-dev", null, $"Finished Good {index}", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], $"sku-create-{index}"),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return result.Code;
            });

        var codes = await Task.WhenAll(tasks);

        Assert.Equal(20, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^SKU-[0-9]{8}-[0-9]{6}$", code));
    }

    [Fact]
    public async Task Create_sku_command_reuses_existing_result_for_same_idempotency_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var numbering = new MasterDataCodingService();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), numbering);
        var command = new CreateSkuCommand("org-001", "env-dev", null, "Finished Good", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-idempotent-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.Code, second.Code);
        Assert.Single(dbContext.Skus);
    }

    [Fact]
    public async Task Create_sku_command_replay_returns_persisted_display_name()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateCodingService(scope));
        var command = new CreateSkuCommand("org-001", "env-dev", null, "Original Name", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-idempotent-display-name");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var persisted = await dbContext.Skus.SingleAsync(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev" && x.Code == first.Code, CancellationToken.None);
        persisted.Rename("Persisted Name");
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var replay = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.Code, replay.Code);
        Assert.Equal("Persisted Name", replay.DisplayName);
    }

    [Fact]
    public async Task Create_sku_command_rejects_same_idempotency_key_with_different_name()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = CreateCodingService(scope);
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), coding);

        await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Original Name", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-idempotent-name"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Changed Name", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-idempotent-name"),
            CancellationToken.None));

        Assert.Contains("conflicts with a different", exception.Message, StringComparison.Ordinal);
        Assert.Contains("create payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_unit_of_measure_command_reuses_existing_result_for_same_idempotency_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateUnitOfMeasureCommandHandler(new UnitOfMeasureRepository(dbContext), CreateCodingService(scope));
        var command = new CreateUnitOfMeasureCommand("org-001", "env-dev", null, "Kilogram", "mass", 3, "half-up", "uom-idempotent-001");

        var first = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.Code, second.Code);
        Assert.Single(dbContext.UnitsOfMeasure);
    }

    [Fact]
    public async Task Create_unit_of_measure_command_rejects_same_idempotency_key_with_different_payload()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateUnitOfMeasureCommandHandler(new UnitOfMeasureRepository(dbContext), CreateCodingService(scope));

        await handler.Handle(
            new CreateUnitOfMeasureCommand("org-001", "env-dev", null, "Kilogram", "mass", 3, "half-up", "uom-idempotent-conflict"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateUnitOfMeasureCommand("org-001", "env-dev", null, "Gram", "mass", 3, "half-up", "uom-idempotent-conflict"),
            CancellationToken.None));

        Assert.Contains("conflicts with a different", exception.Message, StringComparison.Ordinal);
        Assert.Contains("create payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_sku_command_db_coding_generates_unique_codes_for_parallel_requests_after_counter_exists()
    {
        await using var provider = CreateInMemoryProvider("master-data-api-contract-db-coding-parallel");
        using (var seedScope = provider.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seedHandler = new CreateSkuCommandHandler(new SkuRepository(seedContext), CreateCodingService(seedScope));
            await seedHandler.Handle(
                new CreateSkuCommand("org-001", "env-dev", null, "Seed SKU", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-db-seed"),
                CancellationToken.None);
            await seedContext.SaveChangesAsync(CancellationToken.None);
        }

        var tasks = Enumerable.Range(1, 8)
            .Select(async index =>
            {
                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateCodingService(scope));
                var result = await handler.Handle(
                    new CreateSkuCommand("org-001", "env-dev", null, $"Parallel SKU {index}", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], $"sku-db-parallel-{index}"),
                    CancellationToken.None);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                return result.Code;
            });

        var codes = await Task.WhenAll(tasks);

        Assert.Equal(8, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^SKU-[0-9]{8}-[0-9]{6}$", code));
    }

    [Fact]
    public async Task Create_sku_command_db_coding_reserves_counter_before_unit_of_work_save()
    {
        const string databaseName = "master-data-api-contract-db-coding-uow";
        await using var provider = CreateInMemoryProvider(databaseName);
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateCodingService(scope));

        await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Deferred Coding", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-deferred-coding"),
            CancellationToken.None);

        using var observerScope = provider.CreateScope();
        var observerContext = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(observerContext.CodeCounters);
        Assert.Empty(observerContext.CodeIdempotencyKeys);
        Assert.Empty(observerContext.Skus);
    }

    [Fact]
    public async Task Create_sku_command_persists_coding_counter_and_idempotency_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateSkuCommandHandler(new SkuRepository(dbContext), CreateCodingService(scope));

        var result = await handler.Handle(
            new CreateSkuCommand("org-001", "env-dev", null, "Persisted Coding", "kg", "electronic", "finished-goods", "none", "none", "none", "ambient", "ean13", true, [], "sku-persisted-coding"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Matches("^SKU-[0-9]{8}-[0-9]{6}$", result.Code);
        Assert.Single(dbContext.CodeCounters);
        var idempotency = Assert.Single(dbContext.CodeIdempotencyKeys);
        Assert.Equal(result.Code, idempotency.Code);
    }

    private static ServiceProvider CreateInMemoryProvider(string? databaseName = null)
    {
        databaseName ??= $"master-data-api-contract-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static MasterDataCodingService CreateCodingService(IServiceScope scope)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var serviceScopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
        return new MasterDataCodingService(dbContext, serviceScopeFactory);
    }

    private static void SeedSkuControlledReferenceData(ApplicationDbContext dbContext)
    {
        dbContext.ReferenceDataCodes.AddRange(
            ReferenceDataCode.Create("org-001", "env-dev", "product-category", "chemical", "Chemical"),
            ReferenceDataCode.Create("org-001", "env-dev", "product-category", "electronic", "Electronic"),
            ReferenceDataCode.Create("org-001", "env-dev", "material-type", "raw-material", "Raw Material"),
            ReferenceDataCode.Create("org-001", "env-dev", "material-type", "semi-finished", "Semi-Finished"),
            ReferenceDataCode.Create("org-001", "env-dev", "material-type", "finished-goods", "Finished Goods"),
            ReferenceDataCode.Create("org-001", "env-dev", "batch-tracking-policy", "none", "No Batch Tracking"),
            ReferenceDataCode.Create("org-001", "env-dev", "batch-tracking-policy", "mandatory", "Mandatory Batch"),
            ReferenceDataCode.Create("org-001", "env-dev", "serial-tracking-policy", "none", "No Serial Tracking"),
            ReferenceDataCode.Create("org-001", "env-dev", "shelf-life-policy", "none", "No Shelf Life"),
            ReferenceDataCode.Create("org-001", "env-dev", "storage-condition", "ambient", "Ambient"),
            ReferenceDataCode.Create("org-001", "env-dev", "barcode-rule", "ean13", "EAN-13"),
            ReferenceDataCode.Create("org-001", "env-dev", "compliance-tag", "rohs", "RoHS"));
    }

    private sealed class FixedDownstreamReferenceChecker(MasterDataDownstreamReferenceUsage usage) : IMasterDataDownstreamReferenceChecker
    {
        public Task<MasterDataDownstreamReferenceUsage> GetWorkCenterUsageAsync(
            string organizationId,
            string environmentId,
            string workCenterCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(usage);
        }
    }

    private sealed class FixedInternalServiceTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-internal-token";
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
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
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_masterdata_policy;Username=nerv;Password=nerv",
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

    private static readonly string[] NervIipBusinessMasterDataPermissionSet =
    [
        BusinessPermissionCodes.MasterDataProductsRead,
        BusinessPermissionCodes.MasterDataProductsManage,
        BusinessPermissionCodes.MasterDataPartnersRead,
        BusinessPermissionCodes.MasterDataPartnersManage,
        BusinessPermissionCodes.MasterDataResourcesRead,
        BusinessPermissionCodes.MasterDataResourcesManage
    ];

    private static string MasterDataServiceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "services", "Business", "MasterData");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate backend/services/Business/MasterData from test output directory.");
    }
}

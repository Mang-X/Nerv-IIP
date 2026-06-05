using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionRecords;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityInspectionEndpointContractTests
{
    [Fact]
    public void Inspection_endpoints_expose_issue_132_routes_permissions_and_operation_ids()
    {
        var contracts = QualityInspectionEndpointContracts.All;

        Assert.Equal(6, contracts.Count);
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-plans"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionPlansManage
            && x.OperationId == "createBusinessQualityInspectionPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionPlansManage
            && x.OperationId == "activateBusinessQualityInspectionPlan");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/inspection-plans"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsRead
            && x.OperationId == "listBusinessQualityInspectionPlans");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-records"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsCreate
            && x.OperationId == "createBusinessQualityInspectionRecord");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/inspection-records/{inspectionRecordId}/failures/ncr"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "openBusinessQualityNcrFromInspection");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/inspection-records"
            && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsRead
            && x.OperationId == "listBusinessQualityInspectionRecords");
    }

    [Fact]
    public void Inspection_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var failures = QualityInspectionEndpointContracts.All
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(typeof(CreateInspectionPlanEndpoint))]
    [InlineData(typeof(ActivateInspectionPlanEndpoint))]
    [InlineData(typeof(ListInspectionPlansEndpoint))]
    [InlineData(typeof(CreateInspectionRecordEndpoint))]
    [InlineData(typeof(OpenNcrFromInspectionEndpoint))]
    [InlineData(typeof(ListInspectionRecordsEndpoint))]
    public void Inspection_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
    }

    [Fact]
    public void Create_inspection_plan_validator_rejects_duplicate_characteristic_codes()
    {
        var validator = new CreateInspectionPlanCommandValidator();

        var result = validator.Validate(new CreateInspectionPlanCommand(
            "org-001",
            "env-dev",
            "IQP-RECEIVING-001",
            "receiving",
            "SKU-RM-1000",
            null,
            null,
            null,
            "purchase-receipt",
            [
                new InspectionPlanCharacteristicInput("appearance", "Appearance", "visual", "critical", true, "zero-defect"),
                new InspectionPlanCharacteristicInput(" APPEARANCE ", "Appearance duplicate", "visual", "critical", true, "zero-defect"),
            ]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_inspection_plan_validator_rejects_blank_characteristic_codes()
    {
        var validator = new CreateInspectionPlanCommandValidator();

        var result = validator.Validate(new CreateInspectionPlanCommand(
            "org-001",
            "env-dev",
            "IQP-RECEIVING-001",
            "receiving",
            "SKU-RM-1000",
            null,
            null,
            null,
            "purchase-receipt",
            [
                new InspectionPlanCharacteristicInput(null!, "Appearance", "visual", "critical", true, "zero-defect"),
                new InspectionPlanCharacteristicInput(" ", "COA", "document", "major", true, "certificate-match"),
            ]));

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Create_inspection_record_validator_requires_disposition_reason_for_non_passed_lines()
    {
        var validator = new CreateInspectionRecordCommandValidator();

        var result = validator.Validate(new CreateInspectionRecordCommand(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            10m,
            "BATCH-001",
            null,
            [new InspectionResultLineCommandInput("coa", "mismatch", null, InspectionLineResults.Failed, "wrong-spec", 10m, [])],
            null,
            []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage.Contains("Disposition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_inspection_plans_returns_offset_page_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.InspectionPlans.AddRange(
            NewInspectionPlan("IQP-001"),
            NewInspectionPlan("IQP-002"),
            NewInspectionPlan("IQP-003"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListInspectionPlansQueryHandler(dbContext).Handle(
            new ListInspectionPlansQuery("org-001", "env-dev", null, null, null, null, null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task List_inspection_plans_filters_keyword_before_paging_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.InspectionPlans.AddRange(
            NewInspectionPlan("IQP-TARGET-001"),
            NewInspectionPlan("IQP-OTHER-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListInspectionPlansQueryHandler(dbContext).Handle(
            new ListInspectionPlansQuery("org-001", "env-dev", null, null, null, null, null, Keyword: "target", Skip: 0, Take: 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("IQP-TARGET-001", item.PlanCode);
    }

    [Fact]
    public async Task List_ncrs_filters_keyword_by_id_or_code_before_paging_and_total_count()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var target = NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-TARGET-001",
            "receiving",
            "RCV-001",
            "SKU-RM-1000",
            1m,
            "dimension-out-of-spec",
            null,
            null,
            []);
        dbContext.NonconformanceReports.AddRange(
            target,
            NonconformanceReport.Open("org-001", "env-dev", "NCR-OTHER-001", "receiving", "RCV-002", "SKU-RM-1000", 1m, "scratch", null, null, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListNonconformanceReportsQueryHandler(dbContext).Handle(
            new ListNonconformanceReportsQuery("org-001", "env-dev", null, null, null, Keyword: target.Id.ToString(), Skip: 0, Take: 1),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal(target.Id, item.NcrId);
        Assert.Equal("NCR-TARGET-001", item.NcrCode);
    }

    [Fact]
    public async Task Open_ncr_from_inspection_links_record_and_preserves_source_document_reference()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-001",
            "SKU-RM-1000",
            10m,
            "BATCH-001",
            null,
            [InspectionResultLineInput.Fail("coa", "mismatch", "wrong-spec", 10m, ["file-photo-001"])],
            "Supplier certificate mismatch",
            ["file-mrb-001"]);
        dbContext.InspectionRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var handler = new OpenNcrFromInspectionCommandHandler(
            new InspectionRecordRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new FixedNonconformanceReportCodeGenerator());

        var ncrId = await handler.Handle(
            new OpenNcrFromInspectionCommand(record.Id, "Supplier certificate mismatch", ["file-photo-001"]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var ncr = Assert.Single(dbContext.NonconformanceReports);
        Assert.Equal(ncr.Id, ncrId);
        Assert.Equal(record.Id, ncr.SourceInspectionRecordId);
        Assert.Equal("RCV-001", ncr.SourceDocumentId);
        Assert.Equal(ncr.Id.ToString(), record.NonconformanceReportId);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"quality-inspection-api-contract-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static InspectionPlan NewInspectionPlan(string planCode)
    {
        return InspectionPlan.Create(
            "org-001",
            "env-dev",
            planCode,
            "receiving",
            "SKU-RM-1000",
            "supplier-001",
            null,
            null,
            "purchase-receipt");
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_inspection_policy;Username=nerv;Password=nerv",
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

    private static bool HasInternalServicePolicy(IEnumerable<RouteEndpoint> endpoints, string route)
    {
        return endpoints
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, route, StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Any(authorizeData => string.Equals(authorizeData.Policy, InternalServiceAuthorizationPolicy.Name, StringComparison.Ordinal));
    }

    private sealed class FixedNonconformanceReportCodeGenerator : INonconformanceReportCodeGenerator
    {
        public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("NCR-INS-001");
        }
    }
}

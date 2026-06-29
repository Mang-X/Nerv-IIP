using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands;
using Nerv.IIP.Business.Quality.Web.Application.Commands.QualityReasons;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Auth;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.Queries.QualityReasons;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Endpoints.QualityReasons;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.ServiceAuth;
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityEndpointContractTests
{
    [Fact]
    public void Ncr_endpoints_expose_issue_97_routes_permissions_and_operation_ids()
    {
        var contracts = QualityEndpointContracts.All.ToArray();

        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/ncrs"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "createBusinessQualityNcr");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/ncrs"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrRead
            && x.OperationId == "listBusinessQualityNcrs");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/ncrs/{ncrId}"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrRead
            && x.OperationId == "getBusinessQualityNcr");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/ncrs/{ncrId}/disposition"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "submitBusinessQualityNcrDisposition");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/ncrs/{ncrId}/close"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "closeBusinessQualityNcr");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/capas/{correctiveActionId}/actions/{correctiveActionItemId}/complete"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "completeBusinessQualityCapaAction");
    }

    [Fact]
    public void Quality_reason_endpoints_expose_routes_permissions_and_operation_ids()
    {
        var contracts = QualityReasonEndpointContracts.All.ToArray();

        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/reason-codes"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrRead
            && x.OperationId == "listBusinessQualityReasonCodes");
        Assert.Contains(contracts, x => x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/quality/reason-codes/{reasonCode}"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrRead
            && x.OperationId == "getBusinessQualityReasonCode");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/reason-codes"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "createBusinessQualityReasonCode");
        Assert.Contains(contracts, x => x.HttpMethod == "PUT"
            && x.Route == "/api/business/v1/quality/reason-codes/{reasonCode}"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "updateBusinessQualityReasonCode");
        Assert.Contains(contracts, x => x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/quality/reason-codes/{reasonCode}/archive"
            && x.PermissionCode == BusinessPermissionCodes.QualityNcrManage
            && x.OperationId == "archiveBusinessQualityReasonCode");
    }

    [Fact]
    public void Ncr_business_endpoints_require_internal_service_authorization_policy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var failures = QualityEndpointContracts.All.Concat(QualityReasonEndpointContracts.All)
            .Where(contract => !HasInternalServicePolicy(endpoints, contract.Route))
            .Select(contract => $"{contract.EndpointType.Name} is missing {InternalServiceAuthorizationPolicy.Name}.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Quality_reason_catalog_commands_list_detail_update_and_archive()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var create = new CreateQualityReasonCommandHandler(new QualityReasonRepository(dbContext));

        var created = await create.Handle(
            new CreateQualityReasonCommand("org-001", "env-dev", "QR-SCRATCH", "Scratch", "Appearance", "minor", "rework"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var list = await new ListQualityReasonsQueryHandler(dbContext).Handle(
            new ListQualityReasonsQuery("org-001", "env-dev", Enabled: true, Search: "scr", GroupName: "Appearance", Skip: 0, Take: 10),
            CancellationToken.None);
        var item = Assert.Single(list.Items);

        Assert.Equal("QR-SCRATCH", created.ReasonCode);
        Assert.Equal("QR-SCRATCH", item.ReasonCode);
        Assert.Equal("minor", item.Severity);

        var detail = await new GetQualityReasonQueryHandler(dbContext).Handle(
            new GetQualityReasonQuery("org-001", "env-dev", "QR-SCRATCH"),
            CancellationToken.None);

        Assert.Equal("rework", detail.DefaultDisposition);

        var updated = await new UpdateQualityReasonCommandHandler(dbContext).Handle(
            new UpdateQualityReasonCommand("org-001", "env-dev", "QR-SCRATCH", "Deep scratch", "Appearance", "major", "scrap"),
            CancellationToken.None);

        Assert.Equal("Deep scratch", updated.ReasonName);
        Assert.Equal("scrap", updated.DefaultDisposition);

        var archived = await new ArchiveQualityReasonCommandHandler(dbContext).Handle(
            new ArchiveQualityReasonCommand("org-001", "env-dev", "QR-SCRATCH"),
            CancellationToken.None);

        Assert.False(archived.Enabled);
    }

    [Fact]
    public async Task Quality_reason_create_allocates_code_when_omitted()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var create = new CreateQualityReasonCommandHandler(
            new QualityReasonRepository(dbContext),
            new QualityCodingService());

        var created = await create.Handle(
            new CreateQualityReasonCommand("org-001", "env-dev", null, "Scratch", "Appearance", "minor", "rework"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.StartsWith("QR-", created.ReasonCode, StringComparison.Ordinal);
        Assert.True(await dbContext.QualityReasons.AnyAsync(x => x.ReasonCode == created.ReasonCode));
    }

    [Fact]
    public async Task Quality_reason_create_replays_same_reason_for_same_idempotency_key()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var create = new CreateQualityReasonCommandHandler(
            new QualityReasonRepository(dbContext),
            new QualityCodingService());

        var first = await create.Handle(
            new CreateQualityReasonCommand("org-001", "env-dev", null, "Scratch", "Appearance", "minor", "rework", "quality-reason-create-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var replay = await create.Handle(
            new CreateQualityReasonCommand("org-001", "env-dev", null, "Scratch", "Appearance", "minor", "rework", "quality-reason-create-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(first.ReasonCode, replay.ReasonCode);
        Assert.Equal(first.ReasonName, replay.ReasonName);
        Assert.Equal(1, await dbContext.QualityReasons.CountAsync());
    }

    [Fact]
    public void Quality_reason_command_validators_reject_unsupported_catalog_values()
    {
        var create = new CreateQualityReasonCommandValidator().Validate(
            new CreateQualityReasonCommand("org-001", "env-dev", "QR-BAD", "Bad reason", "Appearance", "low", "use-as-is"));
        var update = new UpdateQualityReasonCommandValidator().Validate(
            new UpdateQualityReasonCommand("org-001", "env-dev", "QR-BAD", "Bad reason", "Appearance", "high", "accept"));

        const string severityMessage = "Severity must be one of: minor, major, critical.";
        const string defaultDispositionMessage = "DefaultDisposition must be one of: rework, scrap, return-to-supplier, conditional-release, or omitted.";

        Assert.False(create.IsValid);
        Assert.Contains(create.Errors, x => x.ErrorMessage == severityMessage);
        Assert.Contains(create.Errors, x => x.ErrorMessage == defaultDispositionMessage);
        Assert.False(update.IsValid);
        Assert.Contains(update.Errors, x => x.ErrorMessage == severityMessage);
        Assert.Contains(update.Errors, x => x.ErrorMessage == defaultDispositionMessage);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"quality-reasons-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Ncr_code_generator_includes_scope_tokens_and_guid_v7_suffix()
    {
        var generator = new NonconformanceReportCodeGenerator();

        var code = await generator.NextAsync("org-001", "env-dev", CancellationToken.None);

        Assert.StartsWith("NCR-org001-envdev-", code, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(code["NCR-org001-envdev-".Length..], "N", out var id));
        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void Quality_facts_expose_service_name_for_multienv_configuration()
    {
        Assert.Equal("BusinessQuality", QualityFacts.ServiceName);
    }

    [Fact]
    public void Integration_event_context_accessor_falls_back_to_system_context_without_http_context()
    {
        var accessor = new HttpQualityIntegrationEventContextAccessor(new HttpContextAccessor());

        var context = accessor.GetContext();

        Assert.False(string.IsNullOrWhiteSpace(context.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(context.CausationId));
        Assert.Equal($"system:{QualityIntegrationEventSources.BusinessQuality}", context.Actor);
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
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_policy;Username=nerv;Password=nerv",
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
}

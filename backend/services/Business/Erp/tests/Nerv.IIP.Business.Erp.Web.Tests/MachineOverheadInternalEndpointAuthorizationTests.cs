using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Auth;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MachineOverheadInternalEndpointAuthorizationTests
{
    private const string Route = "/api/business/v1/erp/finance/work-center-machine-overhead-reconciliations";

    [Fact]
    public async Task Machine_overhead_scheme_uses_the_common_scoped_caller_handler()
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var scope = factory.Services.CreateScope();
        var schemes = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemes.GetSchemeAsync("ErpMachineOverheadInternalCaller");

        Assert.NotNull(scheme);
        Assert.Equal(typeof(ScopedCallerAuthenticationHandler), scheme.HandlerType);
        Assert.Null(typeof(Program).Assembly.GetType(
            "Nerv.IIP.Business.Erp.Web.Application.Auth.MachineOverheadInternalCallerAuthenticationHandler"));
    }

    [Fact]
    public async Task Registration_rejects_missing_scoped_caller_profiles_when_the_host_starts()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddErpMachineOverheadInternalCallerAuthorization(
                new ConfigurationBuilder().Build()))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("POST", "not-an-authorized-token")]
    [InlineData("GET", "not-an-authorized-token")]
    [InlineData("POST", "test-general-internal-token")]
    [InlineData("GET", "test-general-internal-token")]
    public async Task Endpoints_reject_unknown_and_generic_internal_credentials_with_401(string method, string token)
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        using var request = CreateRequest(method, token, "org-trusted", "env-trusted");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        sender.AssertNoRequests();
    }

    [Fact]
    public async Task Endpoints_enforce_finance_read_and_manage_as_distinct_runtime_actions()
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();

        using (var request = CreateRequest("GET", "finance-reader-token", "org-trusted", "env-trusted"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var request = CreateRequest("POST", "finance-reader-token", "org-trusted", "env-trusted"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using (var request = CreateRequest("POST", "finance-manager-a-token", "org-trusted", "env-trusted"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var request = CreateRequest("GET", "finance-manager-a-token", "org-trusted", "env-trusted"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Assert.Single(sender.Queries);
        Assert.Single(sender.Commands);
    }

    [Theory]
    [InlineData("POST", "org-other", "env-trusted")]
    [InlineData("POST", "org-trusted", "env-other")]
    [InlineData("GET", "org-other", "env-trusted")]
    [InlineData("GET", "org-trusted", "env-other")]
    public async Task Endpoints_reject_each_organization_and_environment_mismatch_with_403(
        string method,
        string organizationId,
        string environmentId)
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        var token = method == "POST" ? "finance-manager-a-token" : "finance-reader-token";
        using var request = CreateRequest(method, token, organizationId, environmentId);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        sender.AssertNoRequests();
    }

    [Theory]
    [InlineData("POST", null, "env-trusted")]
    [InlineData("POST", "org-trusted", null)]
    [InlineData("GET", null, "env-trusted")]
    [InlineData("GET", "org-trusted", null)]
    public async Task Endpoints_reject_each_missing_scope_header_with_400_and_do_not_dispatch(
        string method,
        string? organizationId,
        string? environmentId)
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        var token = method == "POST" ? "finance-manager-a-token" : "finance-reader-token";
        using var request = CreateRequest(method, token, organizationId, environmentId);
        request.Headers.Add("X-Correlation-Id", "corr-missing-machine-overhead-scope");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal(
            "corr-missing-machine-overhead-scope",
            Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(
            "X-Organization-Id and X-Environment-Id headers are required.",
            body.GetProperty("message").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, body.GetProperty("code").GetInt32());
        Assert.Empty(body.GetProperty("errorData").EnumerateArray());
        Assert.False(body.TryGetProperty("type", out _));
        Assert.False(body.TryGetProperty("title", out _));
        Assert.False(body.TryGetProperty("status", out _));
        Assert.False(body.TryGetProperty("detail", out _));
        sender.AssertNoRequests();
    }

    [Fact]
    public async Task Get_dispatches_the_claim_bound_query_scope_and_request_filters()
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();
        using var request = CreateRequest("GET", "finance-reader-token", "org-trusted", "env-trusted");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(sender.Queries);
        Assert.Equal("org-trusted", query.OrganizationId);
        Assert.Equal("env-trusted", query.EnvironmentId);
        Assert.Equal("2026-08", query.AccountingPeriodCode);
        Assert.Equal("WC-01", query.WorkCenterId);
        Assert.Equal(2, query.PageNumber);
        Assert.Equal(25, query.PageSize);
        Assert.Empty(sender.Commands);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("open", data.GetProperty("accountingPeriodStatus").GetString());
        Assert.Equal("unavailable", data.GetProperty("reconciliationStatus").GetString());
        Assert.Equal("reconciliation_not_recorded", data.GetProperty("reconciliationUnavailableReason").GetString());
    }

    [Fact]
    public async Task Post_audits_the_authenticated_scope_bound_subject_and_ignores_forwarded_actor()
    {
        var sender = new CapturingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateClient();

        using (var request = CreateRequest(
            "POST", "finance-manager-a-token", "org-trusted", "env-trusted", "user:forged"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var request = CreateRequest(
            "POST", "finance-manager-b-token", "org-trusted", "env-trusted"))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Collection(sender.Commands,
            first =>
            {
                Assert.Equal("org-trusted", first.OrganizationId);
                Assert.Equal("env-trusted", first.EnvironmentId);
                Assert.Equal("internal-service:finance-manager-a", first.RecordedBy);
                Assert.Equal("ledger:POST", first.SourceReference);
            },
            second =>
            {
                Assert.Equal("internal-service:finance-manager-b", second.RecordedBy);
                Assert.Equal("ledger:POST", second.SourceReference);
            });
        Assert.Empty(sender.Queries);
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingSender sender)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(TestConfiguration()));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            });
        });

    private static Dictionary<string, string?> TestConfiguration()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
            ["InternalService:BearerToken"] = "test-general-internal-token",
            ["Persistence:AutoMigrate"] = "false",
        };
        AddProfile(values, 1, "finance-reader", "finance-reader-token", "business.erp.finance.read");
        AddProfile(values, 2, "finance-manager-a", "finance-manager-a-token", "business.erp.finance.manage");
        AddProfile(values, 3, "finance-manager-b", "finance-manager-b-token", "business.erp.finance.manage");
        return values;
    }

    private static void AddProfile(
        IDictionary<string, string?> values,
        int index,
        string subject,
        string token,
        string permission)
    {
        var prefix = $"Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:{index}";
        values[$"{prefix}:Name"] = subject;
        values[$"{prefix}:BearerToken"] = token;
        values[$"{prefix}:Subject"] = subject;
        values[$"{prefix}:OrganizationId"] = "org-trusted";
        values[$"{prefix}:EnvironmentId"] = "env-trusted";
        values[$"{prefix}:Permissions:0"] = permission;
    }

    private static HttpRequestMessage CreateRequest(
        string method,
        string token,
        string? organizationId,
        string? environmentId,
        string? forwardedActor = null)
    {
        var request = method == "POST"
            ? new HttpRequestMessage(HttpMethod.Post, Route)
            {
                Content = JsonContent.Create(new
                {
                    workCenterId = "WC-01",
                    accountingPeriodCode = "2026-08",
                    actualFixedOverheadAmount = 100m,
                    actualVariableOverheadAmount = 20m,
                    currencyCode = "CNY",
                    abnormalDowntimeTicks = 0,
                    abnormalDowntimeDisposition = 0,
                    sourceReference = $"ledger:{method}",
                    reason = "month end",
                }),
            }
            : new HttpRequestMessage(
                HttpMethod.Get,
                $"{Route}?accountingPeriodCode=2026-08&workCenterId=WC-01&pageNumber=2&pageSize=25");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (organizationId is not null) request.Headers.Add("X-Organization-Id", organizationId);
        if (environmentId is not null) request.Headers.Add("X-Environment-Id", environmentId);
        if (forwardedActor is not null) request.Headers.Add("X-Authenticated-Actor", forwardedActor);
        return request;
    }

    private sealed class CapturingSender : ISender
    {
        public List<ReconcileWorkCenterMachineOverheadCommand> Commands { get; } = [];
        public List<ListWorkCenterMachineOverheadReconciliationsQuery> Queries { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ReconcileWorkCenterMachineOverheadCommand command)
            {
                Commands.Add(command);
                return Task.FromResult((TResponse)(object)new WorkCenterMachineOverheadReconciliationId(Guid.CreateVersion7()));
            }

            var query = Assert.IsType<ListWorkCenterMachineOverheadReconciliationsQuery>(request);
            Queries.Add(query);
            return Task.FromResult((TResponse)(object)new ListWorkCenterMachineOverheadReconciliationsResponse(
                query.OrganizationId,
                query.EnvironmentId,
                query.AccountingPeriodCode,
                query.WorkCenterId,
                query.PageNumber,
                query.PageSize,
                0,
                [],
                "open",
                "unavailable",
                "reconciliation_not_recorded"));
        }

        public void AssertNoRequests()
        {
            Assert.Empty(Commands);
            Assert.Empty(Queries);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualitySpcEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Spc_read_endpoints_bind_subgroup_size_and_take_by_name()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-service-token");

        var chart = await client.GetFromJsonAsync<ResponseDataEnvelope<SpcControlChartResponse>>(
            "/api/business/v1/quality/spc/control-chart?organizationId=org-001&environmentId=env-dev&skuCode=SKU-RM-1000&characteristicCode=length&workCenterId=WC-MIX-01&subgroupSize=2&take=4",
            JsonOptions);
        var capability = await client.GetFromJsonAsync<ResponseDataEnvelope<ProcessCapabilityResponse>>(
            "/api/business/v1/quality/spc/process-capability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-RM-1000&characteristicCode=length&workCenterId=WC-MIX-01&subgroupSize=2&take=4",
            JsonOptions);

        Assert.NotNull(chart?.Data);
        Assert.NotNull(capability?.Data);

        var sender = factory.Services.GetRequiredService<RecordingSender>();
        var chartQuery = Assert.Single(sender.Requests.OfType<QuerySpcControlChartQuery>());
        var capabilityQuery = Assert.Single(sender.Requests.OfType<QueryProcessCapabilityQuery>());
        Assert.Equal(2, chartQuery.SubgroupSize);
        Assert.Equal(4, chartQuery.Take);
        Assert.Equal(2, capabilityQuery.SubgroupSize);
        Assert.Equal(4, capabilityQuery.Take);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_quality_spc_endpoint;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<RecordingSender>();
                    services.AddSingleton<ISender>(serviceProvider => serviceProvider.GetRequiredService<RecordingSender>());
                });
            });
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult((TResponse)CreateResponse(request));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult<object?>(CreateResponse(request));
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("SPC endpoint tests do not use streaming requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("SPC endpoint tests do not use streaming requests.");
        }

        private static object CreateResponse(object request) =>
            request switch
            {
                QuerySpcControlChartQuery query => new SpcControlChartResponse(
                    query.OrganizationId,
                    query.EnvironmentId,
                    query.SkuCode,
                    query.CharacteristicCode,
                    query.WorkCenterId,
                    query.SubgroupSize,
                    [],
                    [],
                    new SpcControlLimitsResponse(10m, 1m, 12m, 8m, 3m, 0m, false, DateTimeOffset.UtcNow),
                    []),
                QueryProcessCapabilityQuery query => new ProcessCapabilityResponse(
                    query.OrganizationId,
                    query.EnvironmentId,
                    query.SkuCode,
                    query.CharacteristicCode,
                    query.WorkCenterId,
                    query.Take,
                    10m,
                    1m,
                    8m,
                    12m,
                    0.66m,
                    0.66m),
                _ => throw new NotSupportedException($"Request type '{request.GetType().Name}' is not supported by this test sender."),
            };
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}

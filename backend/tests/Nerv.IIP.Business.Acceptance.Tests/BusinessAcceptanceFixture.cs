using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerv.IIP.Business.Acceptance.Tests;

[CollectionDefinition(Name)]
public sealed class BusinessAcceptanceCollection : ICollectionFixture<BusinessAcceptanceFixture>
{
    public const string Name = "Business full-chain acceptance";
}

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class BusinessAcceptanceHarnessInfrastructureTests
{
    private readonly BusinessAcceptanceFixture _fixture;

    public BusinessAcceptanceHarnessInfrastructureTests(BusinessAcceptanceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Fixture_creates_authorized_correlated_clients_for_full_chain_acceptance()
    {
        using var client = _fixture.CreateInternalClient("BusinessMasterData");
        var correlation = _fixture.BeginCorrelation("engineering-to-manufacturing");

        _fixture.ApplyAcceptanceHeaders(client, correlation);

        Assert.Equal("org-acceptance", _fixture.OrganizationId);
        Assert.Equal("env-acceptance", _fixture.EnvironmentId);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "local-internal-service-token"), client.DefaultRequestHeaders.Authorization);
        Assert.True(client.DefaultRequestHeaders.TryGetValues("X-Correlation-Id", out var values));
        Assert.Equal(correlation.CorrelationId, Assert.Single(values));
    }

    [Fact]
    public void Event_recorder_keeps_events_grouped_by_correlation()
    {
        var correlation = _fixture.BeginCorrelation("plan-to-produce");

        _fixture.Events.Record("BusinessDemandPlanning", "planning.MrpRunCompleted", correlation, new { RunId = "mrp-001" });
        _fixture.Events.Record("BusinessMes", "mes.WorkOrderCreated", correlation, new { WorkOrderId = "wo-001" });

        var events = _fixture.Events.ForCorrelation(correlation.CorrelationId);

        Assert.Collection(
            events,
            first =>
            {
                Assert.Equal("BusinessDemandPlanning", first.Service);
                Assert.Equal("planning.MrpRunCompleted", first.EventType);
                Assert.Equal(correlation.CorrelationId, first.CorrelationId);
            },
            second =>
            {
                Assert.Equal("BusinessMes", second.Service);
                Assert.Equal("mes.WorkOrderCreated", second.EventType);
                Assert.Equal(correlation.CorrelationId, second.CorrelationId);
            });
    }

    [Fact]
    public async Task Http_helper_reads_standard_response_data_envelope()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new BusinessAcceptanceResponseEnvelope<SampleResponse>(
                new SampleResponse("accepted"),
                Success: true,
                Message: "OK",
                Code: 200))
        };

        var data = await BusinessAcceptanceHttp.ReadDataEnvelopeAsync<SampleResponse>(response, CancellationToken.None);

        Assert.Equal("accepted", data.Status);
    }

    private sealed record SampleResponse(string Status);
}

public sealed class BusinessAcceptanceFixture : IDisposable
{
    private const string InternalBearerToken = "local-internal-service-token";
    private int _correlationSequence;
    private readonly List<HttpClient> _clients = [];

    public string OrganizationId { get; } = "org-acceptance";

    public string EnvironmentId { get; } = "env-acceptance";

    public BusinessAcceptanceFixtureEventRecorder Events { get; } = new();

    public HttpClient CreateInternalClient(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var client = new HttpClient
        {
            BaseAddress = new Uri($"https://acceptance.local/{serviceName}/", UriKind.Absolute)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalBearerToken);
        client.DefaultRequestHeaders.Add("X-Organization-Id", OrganizationId);
        client.DefaultRequestHeaders.Add("X-Environment-Id", EnvironmentId);

        _clients.Add(client);
        return client;
    }

    public BusinessAcceptanceCorrelation BeginCorrelation(string chainName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainName);

        var sequence = Interlocked.Increment(ref _correlationSequence);
        return new BusinessAcceptanceCorrelation(
            chainName,
            $"corr-77-{Normalize(chainName)}-{sequence:000}",
            OrganizationId,
            EnvironmentId);
    }

    public void ApplyAcceptanceHeaders(HttpClient client, BusinessAcceptanceCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(correlation);

        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation.CorrelationId);
        client.DefaultRequestHeaders.Remove("X-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Organization-Id", correlation.OrganizationId);
        client.DefaultRequestHeaders.Remove("X-Environment-Id");
        client.DefaultRequestHeaders.Add("X-Environment-Id", correlation.EnvironmentId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", InternalBearerToken);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _clients.Clear();
    }

    private static string Normalize(string value)
    {
        var normalized = new string(value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }
}

public sealed record BusinessAcceptanceCorrelation(
    string ChainName,
    string CorrelationId,
    string OrganizationId,
    string EnvironmentId);

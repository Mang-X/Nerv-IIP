using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;
using Xunit;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// Behavioural proof that the shared BusinessGateway host keeps per-test state apart.
/// </summary>
/// <remarks>
/// These replace the previous <c>BusinessGatewayTestIsolationTests</c>, which only asserted that the
/// assembly still carried <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c> —
/// a test that could only ever protect the workaround, never the isolation it stood in for. Every
/// test here fails if a lease's fakes, downstream-health recorder, or scope header leak across
/// leases, which is the property the serialization was standing in for.
/// </remarks>
public sealed class BusinessGatewaySharedHostIsolationTests
{
    private const string SkusRoute =
        "/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev";

    [Fact]
    public void Assembly_does_not_disable_test_parallelization()
    {
        var behaviors = typeof(BusinessGatewaySharedHostIsolationTests).Assembly
            .GetCustomAttributes<CollectionBehaviorAttribute>()
            .Where(behavior => behavior.DisableTestParallelization)
            .ToArray();

        Assert.Empty(behaviors);
    }

    [Fact]
    public async Task Concurrent_leases_on_the_shared_host_reuse_it_without_sharing_downstream_fakes()
    {
        await using var allowed = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, new RecordingMasterDataClient(), "token-allowed"));
        await using var denied = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Forbidden(),
            services => AddMasterData(services, new RecordingMasterDataClient(), "token-denied"));

        Assert.True(allowed.IsShared);
        Assert.True(denied.IsShared);
        Assert.NotEqual(allowed.ScopeId, denied.ScopeId);
        Assert.Same(allowed.Services, denied.Services);

        using var allowedClient = Authenticated(allowed.CreateClient());
        using var deniedClient = Authenticated(denied.CreateClient());

        // Interleave the two leases against the one host: a shared slot would let the second call
        // observe the first lease's authorization decision.
        var first = await allowedClient.GetAsync(SkusRoute);
        var second = await deniedClient.GetAsync(SkusRoute);
        var third = await allowedClient.GetAsync(SkusRoute);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
    }

    [Fact]
    public async Task A_lease_never_observes_another_leases_downstream_client()
    {
        var mine = new RecordingMasterDataClient();
        var theirs = new RecordingMasterDataClient();
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, mine, "internal-mine"));
        await using var otherLease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, theirs, "internal-theirs"));

        using var client = Authenticated(lease.CreateClient());
        var response = await client.GetAsync(SkusRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, mine.ListResourcesCallCount);
        Assert.Equal("internal-mine", mine.LastInternalToken);
        Assert.Equal(0, theirs.ListResourcesCallCount);
        Assert.Null(theirs.LastInternalToken);
    }

    [Fact]
    public async Task Parallel_leases_keep_their_own_authorization_and_token_state()
    {
        const int leaseCount = 24;

        var results = await Task.WhenAll(Enumerable.Range(0, leaseCount).Select(async index =>
        {
            var masterData = new RecordingMasterDataClient();
            var token = $"internal-token-{index}";
            var allow = index % 2 == 0;
            await using var lease = BusinessGatewayTestHost.Lease(
                allow
                    ? FakeBusinessGatewayAuthorizationClient.Allowed()
                    : FakeBusinessGatewayAuthorizationClient.Forbidden(),
                services => AddMasterData(services, masterData, token));
            using var client = Authenticated(lease.CreateClient());

            var response = await client.GetAsync(SkusRoute);
            return (Index: index, Allow: allow, response.StatusCode, masterData.LastInternalToken, Expected: token);
        }));

        Assert.All(results, result =>
        {
            if (result.Allow)
            {
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                Assert.Equal(result.Expected, result.LastInternalToken);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
                Assert.Null(result.LastInternalToken);
            }
        });
    }

    [Fact]
    public async Task Downstream_health_degradation_recorded_by_one_lease_is_invisible_to_the_next()
    {
        await using var degrading = BusinessGatewayTestHost.Lease(new UnavailableAuthorizationClient());
        using var degradingClient = Authenticated(degrading.CreateClient());
        var degraded = await degradingClient.GetAsync(
            "/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, degraded.StatusCode);
        Assert.Equal("Degraded: IAM", await degradingClient.GetStringAsync("/health"));

        await using var clean = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed());
        using var cleanClient = Authenticated(clean.CreateClient());

        Assert.Equal("Healthy", await cleanClient.GetStringAsync("/health"));
    }

    [Fact]
    public async Task A_disposed_lease_stops_answering_and_fails_loudly_instead_of_silently_reusing_state()
    {
        var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, new RecordingMasterDataClient(), "internal-disposed"));
        var scopeId = lease.ScopeId!;
        using var client = Authenticated(lease.CreateClient());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SkusRoute)).StatusCode);

        await lease.DisposeAsync();

        Assert.False(BusinessGatewayTestHost.IsScopeRegistered(scopeId));

        // A stale scope header must fail the request outright. Silently falling through to the real
        // registration — or worse, to whichever lease reused the id — is exactly the class of leak
        // this design has to make impossible.
        var afterDispose = await client.GetAsync(SkusRoute);
        Assert.Equal(HttpStatusCode.InternalServerError, afterDispose.StatusCode);
    }

    [Fact]
    public async Task An_unleased_request_falls_back_to_the_real_registration_rather_than_a_neighbours_fake()
    {
        var neighbour = new RecordingMasterDataClient();
        await using var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, neighbour, "internal-neighbour"));

        // No scope header: this is the anonymous contract client used for /swagger and /health.
        using var unleased = BusinessGatewayTestHost.CreateSharedContractClient();
        var health = await unleased.GetStringAsync("/health");

        Assert.Equal("Healthy", health);
        Assert.Equal(0, neighbour.ListResourcesCallCount);
    }

    [Fact]
    public async Task Repeated_leases_on_one_profile_never_build_another_host()
    {
        await using var warmup = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed());
        using var warmupClient = Authenticated(warmup.CreateClient());
        await warmupClient.GetStringAsync("/health");
        var host = warmup.Services;

        for (var i = 0; i < 25; i++)
        {
            await using var lease = BusinessGatewayTestHost.Lease(
                FakeBusinessGatewayAuthorizationClient.Allowed(),
                services => AddMasterData(services, new RecordingMasterDataClient(), $"internal-{i}"));
            using var client = Authenticated(lease.CreateClient());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SkusRoute)).StatusCode);

            // Reference identity, not a global counter: the sibling profile may legitimately be
            // built by another test in parallel, and that must not read as a regression here.
            Assert.Same(host, lease.Services);
        }

        // Two profiles for the whole assembly is the ceiling; anything more means leases stopped
        // sharing and every test is paying host-construction cost again.
        Assert.InRange(BusinessGatewayTestHost.BuiltHostCount, 1, 2);
    }

    [Fact]
    public void Every_service_a_test_may_swap_is_a_per_request_seam()
    {
        // A type entering this set that the gateway resolves once at startup would silently share
        // one test's fake with every other test on the same host.
        var overridable = BusinessGatewayTestHost.OverridableServiceTypes.ToArray();
        Assert.Contains(typeof(IBusinessGatewayAuthorizationClient), overridable);
        Assert.Contains(typeof(IInternalServiceTokenProvider), overridable);
        Assert.Contains(typeof(IBusinessMasterDataClient), overridable);
        Assert.All(
            overridable,
            type => Assert.True(type.IsInterface, $"{type} must be an interface to be routed per request."));
    }

    [Fact]
    public async Task Configuration_that_cannot_be_scoped_to_a_request_falls_back_to_a_dedicated_host()
    {
        await using var shared = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed());
        await using var dedicated = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            configureBuilder: builder => builder.UseSetting("Gateway:AuthorizationCacheTtlSeconds", "0"));
        await using var dedicatedByRegistration = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => services.AddSingleton(TimeProvider.System));

        Assert.True(shared.IsShared);
        Assert.False(dedicated.IsShared);
        Assert.False(dedicatedByRegistration.IsShared);
        Assert.NotSame(shared.Services, dedicated.Services);
    }

    private static void AddMasterData(
        IServiceCollection services,
        IBusinessMasterDataClient masterData,
        string internalToken)
    {
        services.RemoveAll<IBusinessMasterDataClient>();
        services.AddSingleton(masterData);
        services.RemoveAll<IInternalServiceTokenProvider>();
        services.AddSingleton<IInternalServiceTokenProvider>(
            new IsolationInternalServiceTokenProvider(internalToken));
    }

    private static HttpClient Authenticated(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        return client;
    }

    private sealed record IsolationInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

    /// <summary>Stands in for an unreachable IAM, which is what marks a downstream degraded.</summary>
    private sealed class UnavailableAuthorizationClient : IBusinessGatewayAuthorizationClient
    {
        public Task<BusinessGatewayAuthorizationResult> CheckAsync(
            string bearerToken,
            BusinessGatewayPermissionRequirement requirement,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("IAM is unreachable in this lease.");

        public Task<BusinessGatewayAuthorizationResult> CheckAsync(
            string bearerToken,
            BusinessGatewayPermissionRequirement requirement,
            BusinessGatewayAuthorizationContinuityMode continuityMode,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("IAM is unreachable in this lease.");
    }
}

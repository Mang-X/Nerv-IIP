using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;
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

    /// <summary>How long a host build must stay blocked while a response body is still in flight.</summary>
    private static readonly TimeSpan BuildMustStayBlockedFor = TimeSpan.FromSeconds(1);

    /// <summary>Slack for the same build to finish once the body has been drained.</summary>
    private static readonly TimeSpan BuildMustCompleteWithin = TimeSpan.FromSeconds(30);

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

        using var allowedClient = BusinessGatewayTestHost.Authenticated(allowed.CreateClient());
        using var deniedClient = BusinessGatewayTestHost.Authenticated(denied.CreateClient());

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

        using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
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
            using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());

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
        using var degradingClient = BusinessGatewayTestHost.Authenticated(degrading.CreateClient());
        var degraded = await degradingClient.GetAsync(
            "/api/business-console/v1/workbench/summary?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, degraded.StatusCode);
        Assert.Equal("Degraded: IAM", await degradingClient.GetStringAsync("/health"));

        await using var clean = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed());
        using var cleanClient = BusinessGatewayTestHost.Authenticated(clean.CreateClient());

        Assert.Equal("Healthy", await cleanClient.GetStringAsync("/health"));
    }

    [Fact]
    public async Task A_disposed_lease_stops_answering_and_fails_loudly_instead_of_silently_reusing_state()
    {
        var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, new RecordingMasterDataClient(), "internal-disposed"));
        var scopeId = lease.ScopeId!;
        using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());

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

        // The "Healthy" above must be a property of this request, not of nothing having degraded a
        // process-wide fallback earlier in the run: an unleased request gets its own recorder, so
        // no ordering between anonymous requests can decide the outcome.
        using var firstScope = lease.Services.CreateScope();
        using var secondScope = lease.Services.CreateScope();
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<BusinessGatewayDownstreamHealthState>(),
            secondScope.ServiceProvider.GetRequiredService<BusinessGatewayDownstreamHealthState>());
    }

    [Fact]
    public async Task Repeated_leases_never_build_another_host_for_either_profile()
    {
        await using var defaultWarmup = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed());
        await using var pinnedWarmup = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            profile: BusinessGatewayTestHostProfile.ServiceBaseUrls);
        using var warmupClient = BusinessGatewayTestHost.Authenticated(defaultWarmup.CreateClient());
        await warmupClient.GetStringAsync("/health");
        var defaultHost = defaultWarmup.Services;
        var pinnedHost = pinnedWarmup.Services;

        // One host per profile, and the two profiles really are two hosts — anything else means
        // leases stopped sharing and every test is paying host-construction cost again.
        Assert.NotSame(defaultHost, pinnedHost);

        for (var i = 0; i < 25; i++)
        {
            await using var lease = BusinessGatewayTestHost.Lease(
                FakeBusinessGatewayAuthorizationClient.Allowed(),
                services => AddMasterData(services, new RecordingMasterDataClient(), $"internal-{i}"));
            await using var pinnedLease = BusinessGatewayTestHost.Lease(
                FakeBusinessGatewayAuthorizationClient.Allowed(),
                services => AddMasterData(services, new RecordingMasterDataClient(), $"internal-pinned-{i}"),
                BusinessGatewayTestHostProfile.ServiceBaseUrls);
            using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(SkusRoute)).StatusCode);

            // Reference identity, not a global counter: a counter that can only be incremented by
            // the two `Lazy<T>` profile factories cannot exceed two, so asserting that bound would
            // prove nothing.
            Assert.Same(defaultHost, lease.Services);
            Assert.Same(pinnedHost, pinnedLease.Services);
        }
    }

    [Fact]
    public async Task Host_construction_waits_for_a_response_body_that_is_still_being_written()
    {
        // The OpenAPI document is the one anonymous response large enough to still be streaming
        // after its headers are flushed. Generating it uncached has to take the same gate the
        // document cache uses, or this test reintroduces the concurrent NSwag generation the cache
        // exists to remove.
        await using var generation = await BusinessGatewayTestHost.EnterOpenApiGenerationAsync();
        using var client = BusinessGatewayTestHost.CreateSharedContractClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The response headers are flushed, but the document is far larger than TestServer's
        // response pipe threshold, so the gateway pipeline is still writing. Releasing the gate
        // permit here — which is what a client-side DelegatingHandler around `base.SendAsync` does,
        // because HttpClient buffers the content outside the handler chain — would let host
        // construction mutate FastEndpoints' static serializer configuration underneath a live
        // request. The permit is therefore held server side, and this build must not start yet.
        var build = Task.Run(() => BusinessGatewayTestHostGate.Build(() => 0));
        var completedTooEarly = true;
        try
        {
            await build.WaitAsync(BuildMustStayBlockedFor);
        }
        catch (TimeoutException)
        {
            completedTooEarly = false;
        }

        Assert.False(
            completedTooEarly,
            "Host construction completed while the gateway was still writing a response body "
            + $"(gate reported {BusinessGatewayTestHostGate.RequestsInFlight} request(s) in flight).");

        var document = await response.Content.ReadAsStringAsync();

        // Guards the premise: a document that fits in the pipe buffer would complete server side
        // before this test ever looks, and the assertion above would pass vacuously.
        Assert.True(
            document.Length > 64 * 1024,
            $"The OpenAPI document is only {document.Length} bytes, which no longer exceeds TestServer's "
            + "response pipe threshold; this test would no longer observe the window it guards.");

        await build.WaitAsync(BuildMustCompleteWithin);
    }

    [Fact]
    public void A_configuration_block_that_only_removes_a_registration_falls_back_to_a_dedicated_host()
    {
        // Removing without re-registering cannot be expressed per request: the shared host would
        // keep the real registration and the test would silently run against different wiring than
        // a dedicated host gives it. An empty probe collection makes `RemoveAll` a no-op, so this
        // is only caught because the probe is seeded with the shared host's own registrations.
        using var removedOnly = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => services.RemoveAll<IBusinessMasterDataClient>());
        using var removedAndReplaced = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.Allowed(),
            services => AddMasterData(services, new RecordingMasterDataClient(), "internal-replaced"));

        Assert.False(removedOnly.IsShared);
        Assert.True(removedAndReplaced.IsShared);
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
            new TestInternalServiceTokenProvider(internalToken));
    }

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

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.Resilience;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// Shared, reusable <see cref="WebApplicationFactory{TEntryPoint}"/> hosts for the BusinessGateway
/// test assembly.
/// </summary>
/// <remarks>
/// <para>
/// Every gateway HTTP test used to build its own host, which cost roughly 0.4–0.9 s per test and
/// forced the whole assembly to run serially. Instead, this type builds <em>one host per profile</em>
/// and gives each test a <see cref="BusinessGatewayTestHostLease"/>: an isolated, disposable slot
/// holding that test's downstream fakes.
/// </para>
/// <para>
/// Isolation mechanism: the lease client stamps <see cref="ScopeHeader"/> on every request; the
/// shared host resolves the overridable downstream services per request by looking that header up
/// in <see cref="Scopes"/>. Nothing is copied into or reset inside the container — a lease's fakes
/// are only ever reachable from a request carrying that lease's own header, so there is no shared
/// mutable slot to leak and no reset step that can be forgotten.
/// </para>
/// <para>
/// A test whose configuration cannot be expressed as per-request instances (custom
/// <see cref="IWebHostBuilder"/> settings, non-instance registrations, overrides of types outside
/// <see cref="OverridableServiceTypes"/>, or the <em>removal</em> of a registration it does not put
/// back) transparently falls back to a dedicated host, so correctness never depends on the sharing
/// being applicable.
/// </para>
/// </remarks>
internal static class BusinessGatewayTestHost
{
    /// <summary>Per-request header carrying the owning lease id.</summary>
    internal const string ScopeHeader = "X-Nerv-IIP-Test-Scope";

    /// <summary>
    /// The rate limiter in <c>Program.cs</c> partitions by principal name and every test signs in as
    /// the same principal, so a shared host would exhaust the production 300/60s window. Rate
    /// limiting itself keeps dedicated coverage in
    /// <see cref="BusinessGatewayRateLimitTests"/>, which pins its own permit budget.
    /// </summary>
    private const string SharedHostRateLimitPermits = "100000000";

    /// <summary>
    /// Services a lease may swap per request. All of them are stateless-per-request seams the
    /// gateway resolves while handling a request; anything else forces a dedicated host.
    /// </summary>
    internal static readonly FrozenSet<Type> OverridableServiceTypes = BuildOverridableServiceTypes();

    private static readonly ConcurrentDictionary<string, BusinessGatewayTestScope> Scopes =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<BusinessGatewayTestHostProfile, string> OpenApiDocuments = new();

    private static readonly ConcurrentDictionary<BusinessGatewayTestHostProfile, FrozenSet<Type>> SharedHostServiceTypes =
        new();

    private static readonly SemaphoreSlim OpenApiGenerationGate = new(1, 1);

    /// <summary>
    /// Marker registration used to seed the harvest probe. Reference identity of this delegate is
    /// what tells a surviving seed apart from something the test itself registered.
    /// </summary>
    private static readonly Func<IServiceProvider, object> HarvestProbeSeed =
        _ => throw new InvalidOperationException("The harvest probe collection is never built.");

    private static readonly Lazy<WebApplicationFactory<Program>> DefaultProfileFactory =
        new(() => BuildSharedFactory(BusinessGatewayTestHostProfile.Default), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<WebApplicationFactory<Program>> ServiceBaseUrlProfileFactory =
        new(() => BuildSharedFactory(BusinessGatewayTestHostProfile.ServiceBaseUrls), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Leases a slot on the shared host for <paramref name="profile"/>.
    /// </summary>
    /// <param name="authorizationClient">
    /// The authorization seam every gateway test replaces; always routed per lease.
    /// </param>
    /// <param name="configureServices">
    /// The test's usual <c>RemoveAll</c>/<c>AddSingleton(instance)</c> block. It is replayed against a
    /// throwaway collection to harvest the instances rather than mutating the shared container.
    /// </param>
    /// <param name="configureBuilder">
    /// Host-level settings. Any value here forces a dedicated host, because settings are baked in at
    /// build time and cannot be scoped to a request.
    /// </param>
    internal static BusinessGatewayTestHostLease Lease(
        IBusinessGatewayAuthorizationClient authorizationClient,
        Action<IServiceCollection>? configureServices = null,
        BusinessGatewayTestHostProfile profile = BusinessGatewayTestHostProfile.Default,
        Action<IWebHostBuilder>? configureBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(authorizationClient);

        if (configureBuilder is null)
        {
            // Forces the shared host to exist, which is also what publishes the registration
            // snapshot the harvest probe compares against.
            var shared = SharedFactory(profile);
            var overrides = new Dictionary<Type, object>
            {
                [typeof(IBusinessGatewayAuthorizationClient)] = authorizationClient,
            };

            if (TryHarvestOverrides(profile, configureServices, overrides))
            {
                var scope = new BusinessGatewayTestScope(overrides);
                Scopes[scope.Id] = scope;
                return new BusinessGatewayTestHostLease(shared, scope, ownsFactory: false);
            }
        }

        return new BusinessGatewayTestHostLease(
            CreateDedicatedFactory(profile, configureBuilder, services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton(authorizationClient);
                configureServices?.Invoke(services);
            }),
            scope: null,
            ownsFactory: true);
    }

    /// <summary>
    /// Builds a host that is not shared with any other test. Construction still goes through
    /// <see cref="BusinessGatewayTestHostGate"/>, so it cannot race a request on a shared host.
    /// </summary>
    internal static WebApplicationFactory<Program> CreateDedicatedFactory(
        BusinessGatewayTestHostProfile profile = BusinessGatewayTestHostProfile.Default,
        Action<IWebHostBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null) =>
        new BusinessGatewayGatedWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            ApplyProfile(builder, profile);
            configureBuilder?.Invoke(builder);
            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });

    /// <summary>
    /// A dedicated host with <em>no</em> profile settings applied, for the tests that assert on
    /// missing configuration (no JWKS, no downstream base URL). Construction is still gated.
    /// </summary>
    internal static WebApplicationFactory<Program> CreateUnconfiguredFactory(
        Action<IWebHostBuilder> configureBuilder) =>
        new BusinessGatewayGatedWebApplicationFactory().WithWebHostBuilder(configureBuilder);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> onto a gated host, optionally stamped with a lease scope.
    /// </summary>
    /// <remarks>
    /// The request permit is taken server side (see <see cref="BusinessGatewayTestHostGate"/>), so
    /// nothing has to be wrapped around the client handler here.
    /// </remarks>
    internal static HttpClient CreateGatedClient(
        WebApplicationFactory<Program> factory,
        string? scopeId = null,
        Uri? baseAddress = null)
    {
        var client = factory.CreateDefaultClient(baseAddress ?? new Uri("http://localhost"));
        if (scopeId is not null)
        {
            ApplyScopeHeader(client.DefaultRequestHeaders, scopeId);
        }

        return client;
    }

    /// <summary>
    /// Applies the <see cref="ScopeHeader"/> that routes a request's downstream fakes to the owning
    /// lease.
    /// </summary>
    internal static void ApplyScopeHeader(HttpHeaders headers, string scopeId)
    {
        headers.Remove(ScopeHeader);
        headers.Add(ScopeHeader, scopeId);
    }

    /// <summary>
    /// Stamps the standard valid access token on <paramref name="client"/> and returns it, for the
    /// tests that sign in as the ordinary gateway principal.
    /// </summary>
    internal static HttpClient Authenticated(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());
        return client;
    }

    /// <summary>
    /// A client onto the shared host with no lease at all, for anonymous read-only contract surfaces
    /// (the Swagger document, <c>/health</c>) that inject no downstream fake.
    /// </summary>
    internal static HttpClient CreateSharedContractClient(
        BusinessGatewayTestHostProfile profile = BusinessGatewayTestHostProfile.Default) =>
        CreateGatedClient(SharedFactory(profile));

    /// <summary>
    /// The generated OpenAPI document for a profile, produced once and reused.
    /// </summary>
    /// <remarks>
    /// NSwag's document generation is not safe to run concurrently on one host: two overlapping
    /// <c>/swagger/v1/swagger.json</c> requests can each observe a half-populated schema dictionary.
    /// Serial execution used to hide that; caching the document removes the race at its source
    /// instead, and the contract assertions still run against the real generated document.
    /// </remarks>
    internal static async Task<string> GetOpenApiDocumentAsync(
        BusinessGatewayTestHostProfile profile = BusinessGatewayTestHostProfile.Default)
    {
        if (OpenApiDocuments.TryGetValue(profile, out var cached))
        {
            return cached;
        }

        await OpenApiGenerationGate.WaitAsync();
        try
        {
            if (OpenApiDocuments.TryGetValue(profile, out cached))
            {
                return cached;
            }

            using var client = CreateSharedContractClient(profile);
            var document = await client.GetStringAsync("/swagger/v1/swagger.json");
            OpenApiDocuments[profile] = document;
            return document;
        }
        finally
        {
            OpenApiGenerationGate.Release();
        }
    }

    /// <summary>
    /// Serialises a raw, deliberately uncached <c>/swagger/v1/swagger.json</c> request against the
    /// same gate <see cref="GetOpenApiDocumentAsync"/> uses. Any test that regenerates the document
    /// instead of reading the cache has to take this, or it reintroduces the concurrent NSwag
    /// generation that the cache exists to remove.
    /// </summary>
    internal static async ValueTask<IAsyncDisposable> EnterOpenApiGenerationAsync()
    {
        await OpenApiGenerationGate.WaitAsync();
        return new OpenApiGenerationRegistration();
    }

    /// <summary>
    /// Unregisters a lease's scope once no request is still using it. Draining first is what makes
    /// "the lease is gone" and "a request may still resolve its fakes" mutually exclusive rather
    /// than merely unlikely.
    /// </summary>
    internal static async ValueTask ReleaseScopeAsync(string scopeId)
    {
        if (!Scopes.TryGetValue(scopeId, out var scope))
        {
            return;
        }

        await scope.DrainAsync();
        Scopes.TryRemove(scopeId, out _);
    }

    internal static bool IsScopeRegistered(string scopeId) => Scopes.ContainsKey(scopeId);

    /// <summary>
    /// Marks the request as using its lease's scope for the duration of the server pipeline; see
    /// <see cref="ReleaseScopeAsync"/>. Returns a no-op handle for unleased requests and for a
    /// header that no longer resolves (which <see cref="ResolveScope"/> reports as an error).
    /// </summary>
    internal static IDisposable TrackRequest(HttpContext context) =>
        context.Request.Headers.TryGetValue(ScopeHeader, out var header)
        && Scopes.TryGetValue(header.ToString(), out var scope)
            ? scope.Enter()
            : NullRegistration.Instance;

    private static WebApplicationFactory<Program> SharedFactory(BusinessGatewayTestHostProfile profile) =>
        profile switch
        {
            BusinessGatewayTestHostProfile.Default => DefaultProfileFactory.Value,
            BusinessGatewayTestHostProfile.ServiceBaseUrls => ServiceBaseUrlProfileFactory.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown gateway test host profile."),
        };

    /// <summary>
    /// Replays the test's service-configuration block against a probe collection seeded with the
    /// shared host's own registrations, and harvests the singleton instances it registered.
    /// </summary>
    /// <remarks>
    /// The seeding matters: on an empty collection a bare <c>RemoveAll&lt;T&gt;()</c> leaves no trace
    /// at all, so a block that removes a registration without putting one back would be harvested as
    /// "shareable" while the shared host quietly keeps the real registration. Seeding makes that
    /// removal observable as a missing seed, and any removal the lease cannot reproduce per request
    /// falls back to a dedicated host.
    /// </remarks>
    private static bool TryHarvestOverrides(
        BusinessGatewayTestHostProfile profile,
        Action<IServiceCollection>? configureServices,
        Dictionary<Type, object> overrides)
    {
        if (configureServices is null)
        {
            return true;
        }

        var seeds = SharedHostServiceTypes[profile];
        var probe = new ServiceCollection();
        foreach (var seed in seeds)
        {
            probe.Add(new ServiceDescriptor(seed, HarvestProbeSeed, ServiceLifetime.Transient));
        }

        configureServices(probe);

        var surviving = new HashSet<Type>();
        foreach (var descriptor in probe)
        {
            if (ReferenceEquals(descriptor.ImplementationFactory, HarvestProbeSeed))
            {
                surviving.Add(descriptor.ServiceType);
                continue;
            }

            if (descriptor.ImplementationInstance is not { } instance
                || !OverridableServiceTypes.Contains(descriptor.ServiceType))
            {
                return false;
            }

            overrides[descriptor.ServiceType] = instance;
        }

        // A seed the block removed is only reproducible per request if the block also supplied a
        // per-request instance for it.
        return seeds.All(seed => surviving.Contains(seed) || overrides.ContainsKey(seed));
    }

    private static WebApplicationFactory<Program> BuildSharedFactory(BusinessGatewayTestHostProfile profile)
    {
        var factory = new BusinessGatewayGatedWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            ApplyProfile(builder, profile);
            builder.UseSetting("Security:RateLimit:PermitLimit", SharedHostRateLimitPermits);
            builder.ConfigureServices(services =>
            {
                RouteOverridableServicesToScope(services);
                SharedHostServiceTypes[profile] = SnapshotHarvestSeeds(services);
            });
        });

        // WebApplicationFactory builds lazily. Force it here so the (gated) construction happens
        // once, up front, instead of inside whichever test happens to send the first request.
        _ = factory.Services;
        return factory;
    }

    /// <summary>
    /// The service types a harvest probe is seeded with. Open generics are excluded because they
    /// cannot be registered with a factory descriptor and are never swapped by a gateway test.
    /// </summary>
    private static FrozenSet<Type> SnapshotHarvestSeeds(IServiceCollection services) =>
        services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => !type.IsGenericTypeDefinition)
            .ToFrozenSet();

    private static void ApplyProfile(IWebHostBuilder builder, BusinessGatewayTestHostProfile profile)
    {
        builder.UseSetting("Iam:Jwt:JwksJson", BusinessGatewayTestTokens.PublicJwksJson());
        builder.UseSetting("Iam:Jwt:Issuer", BusinessGatewayTestTokens.Issuer);
        builder.UseSetting("Iam:Jwt:Audience", BusinessGatewayTestTokens.Audience);
        if (profile == BusinessGatewayTestHostProfile.ServiceBaseUrls)
        {
            BusinessGatewayTestServiceBaseUrls.Configure(builder);
        }
    }

    private static void RouteOverridableServicesToScope(IServiceCollection services)
    {
        foreach (var serviceType in OverridableServiceTypes)
        {
            RouteServiceToScope(services, serviceType);
        }

        RouteDownstreamHealthStateToScope(services);
    }

    private static void RouteServiceToScope(IServiceCollection services, Type serviceType)
    {
        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == serviceType);
        if (original is null)
        {
            return;
        }

        var fallback = CreateFallbackResolver(original);
        services.RemoveAll(serviceType);
        services.Add(new ServiceDescriptor(
            serviceType,
            serviceProvider =>
                ResolveScope(serviceProvider) is { } scope && scope.Overrides.TryGetValue(serviceType, out var instance)
                    ? instance
                    : fallback(serviceProvider),
            ServiceLifetime.Scoped));
    }

    /// <summary>
    /// The shared host must still give a request that overrides nothing the real registration, so
    /// "no fake supplied" keeps behaving exactly as it did with a per-test host.
    /// </summary>
    private static Func<IServiceProvider, object> CreateFallbackResolver(ServiceDescriptor original)
    {
        Func<IServiceProvider, object> create = original switch
        {
            { ImplementationInstance: { } instance } => _ => instance,
            { ImplementationFactory: { } factory } => factory,
            { ImplementationType: { } type } => serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, type),
            _ => throw new InvalidOperationException(
                $"Service '{original.ServiceType}' has no resolvable implementation to fall back to."),
        };

        if (original.Lifetime != ServiceLifetime.Singleton)
        {
            return create;
        }

        var gate = new Lock();
        // Volatile: the fast path reads this field without taking the lock, and a non-volatile read
        // is not ordered against the writes made while constructing the instance on weaker memory
        // models (arm64).
        object? singleton = null;
        return serviceProvider =>
        {
            if (Volatile.Read(ref singleton) is { } published)
            {
                return published;
            }

            lock (gate)
            {
                if (singleton is not { } existing)
                {
                    existing = create(serviceProvider);
                    Volatile.Write(ref singleton, existing);
                }

                return existing;
            }
        };
    }

    /// <summary>
    /// <see cref="BusinessGatewayDownstreamHealthState"/> is a process-lifetime singleton that
    /// records downstream degradation and is read back by <c>/health</c> and the workbench summary.
    /// On a shared host it is the one genuinely cross-test mutable object, so it is scoped per lease
    /// exactly like the downstream clients — and a request with no lease at all gets a fresh
    /// instance rather than a process-wide one, so an anonymous contract request can never observe
    /// degradation recorded by whichever anonymous request happened to run before it.
    /// </summary>
    private static void RouteDownstreamHealthStateToScope(IServiceCollection services)
    {
        services.RemoveAll<BusinessGatewayDownstreamHealthState>();
        services.AddScoped(serviceProvider =>
            ResolveScope(serviceProvider)?.HealthState ?? new BusinessGatewayDownstreamHealthState());
    }

    private static BusinessGatewayTestScope? ResolveScope(IServiceProvider serviceProvider)
    {
        var httpContext = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext is null
            || !httpContext.Request.Headers.TryGetValue(ScopeHeader, out var header))
        {
            return null;
        }

        var scopeId = header.ToString();
        if (Scopes.TryGetValue(scopeId, out var scope))
        {
            return scope;
        }

        throw new InvalidOperationException(
            $"Gateway test scope '{scopeId}' is not registered. The lease was disposed while a request "
            + "was still in flight, or the scope header leaked onto a client from another lease.");
    }

    private static FrozenSet<Type> BuildOverridableServiceTypes()
    {
        var businessServiceClients = typeof(BusinessGatewayPermissions).Assembly
            .GetTypes()
            .Where(type => type is { IsInterface: true, IsPublic: true }
                && type.Namespace == "Nerv.IIP.BusinessGateway.Web.Application.BusinessServices"
                && type.Name.StartsWith("IBusiness", StringComparison.Ordinal)
                && type.Name.EndsWith("Client", StringComparison.Ordinal));

        return businessServiceClients
            .Append(typeof(IBusinessGatewayAuthorizationClient))
            .Append(typeof(IInternalServiceTokenProvider))
            .ToFrozenSet();
    }

    private sealed class OpenApiGenerationRegistration : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            OpenApiGenerationGate.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullRegistration : IDisposable
    {
        internal static readonly NullRegistration Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A <see cref="WebApplicationFactory{TEntryPoint}"/> whose lazy host construction is serialized
    /// against in-flight gateway requests, and whose pipeline is wrapped by the permit middleware.
    /// </summary>
    private sealed class BusinessGatewayGatedWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.Insert(
                0,
                ServiceDescriptor.Transient<IStartupFilter, BusinessGatewayTestHostGate.RequestPermitStartupFilter>()));

        protected override IHost CreateHost(IHostBuilder builder) =>
            BusinessGatewayTestHostGate.Build(() => base.CreateHost(builder));
    }
}

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
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
/// The named host profiles this assembly runs gateway HTTP tests against.
/// </summary>
internal enum BusinessGatewayTestHostProfile
{
    /// <summary>
    /// JWT validation configured, downstream base URLs left at their Development defaults.
    /// </summary>
    Default,

    /// <summary>
    /// JWT validation configured plus every downstream base URL pinned to a <c>*.local</c> host, so
    /// a facade that forgets to use its injected client fails as an unreachable downstream instead
    /// of silently hitting a developer's localhost port.
    /// </summary>
    ServiceBaseUrls,
}

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
/// <see cref="IWebHostBuilder"/> settings, non-instance registrations, or overrides of types outside
/// <see cref="OverridableServiceTypes"/>) transparently falls back to a dedicated host, so
/// correctness never depends on the sharing being applicable.
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

    private static readonly SemaphoreSlim OpenApiGenerationGate = new(1, 1);

    private static readonly Lazy<WebApplicationFactory<Program>> DefaultProfileFactory =
        new(() => BuildSharedFactory(BusinessGatewayTestHostProfile.Default), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<WebApplicationFactory<Program>> ServiceBaseUrlProfileFactory =
        new(() => BuildSharedFactory(BusinessGatewayTestHostProfile.ServiceBaseUrls), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Number of hosts actually constructed; asserted by the isolation tests.</summary>
    private static int _builtHostCount;

    internal static int BuiltHostCount => Volatile.Read(ref _builtHostCount);

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

        var overrides = new Dictionary<Type, object>
        {
            [typeof(IBusinessGatewayAuthorizationClient)] = authorizationClient,
        };

        if (configureBuilder is null && TryHarvestOverrides(configureServices, overrides))
        {
            var scope = new BusinessGatewayTestScope(overrides);
            Scopes[scope.Id] = scope;
            return new BusinessGatewayTestHostLease(SharedFactory(profile), scope, ownsFactory: false);
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
    /// Creates an <see cref="HttpClient"/> that holds a request permit for the duration of every
    /// exchange, so no host can be built while it is talking to the gateway.
    /// </summary>
    internal static HttpClient CreateGatedClient(
        WebApplicationFactory<Program> factory,
        string? scopeId = null,
        Uri? baseAddress = null)
    {
        var client = factory.CreateDefaultClient(
            baseAddress ?? new Uri("http://localhost"),
            new BusinessGatewayTestHostGate.RequestPermitHandler());
        if (scopeId is not null)
        {
            BusinessGatewayTestHostGate.ApplyScopeHeader(client.DefaultRequestHeaders, scopeId);
        }

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

    internal static void ReleaseScope(string scopeId) => Scopes.TryRemove(scopeId, out _);

    internal static bool IsScopeRegistered(string scopeId) => Scopes.ContainsKey(scopeId);

    private static WebApplicationFactory<Program> SharedFactory(BusinessGatewayTestHostProfile profile) =>
        profile switch
        {
            BusinessGatewayTestHostProfile.Default => DefaultProfileFactory.Value,
            BusinessGatewayTestHostProfile.ServiceBaseUrls => ServiceBaseUrlProfileFactory.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown gateway test host profile."),
        };

    /// <summary>
    /// Replays the test's service-configuration block against a throwaway collection and harvests
    /// the singleton instances it registered. Returns <see langword="false"/> as soon as it sees a
    /// registration that cannot be expressed as a per-request instance.
    /// </summary>
    private static bool TryHarvestOverrides(
        Action<IServiceCollection>? configureServices,
        Dictionary<Type, object> overrides)
    {
        if (configureServices is null)
        {
            return true;
        }

        var probe = new ServiceCollection();
        configureServices(probe);
        foreach (var descriptor in probe)
        {
            if (descriptor.ImplementationInstance is not { } instance
                || !OverridableServiceTypes.Contains(descriptor.ServiceType))
            {
                return false;
            }

            overrides[descriptor.ServiceType] = instance;
        }

        return true;
    }

    private static WebApplicationFactory<Program> BuildSharedFactory(BusinessGatewayTestHostProfile profile)
    {
        var factory = new BusinessGatewayGatedWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            ApplyProfile(builder, profile);
            builder.UseSetting("Security:RateLimit:PermitLimit", SharedHostRateLimitPermits);
            builder.ConfigureServices(RouteOverridableServicesToScope);
        });

        // WebApplicationFactory builds lazily. Force it here so the (gated) construction happens
        // once, up front, instead of inside whichever test happens to send the first request.
        _ = factory.Services;
        Interlocked.Increment(ref _builtHostCount);
        return factory;
    }

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
        object? singleton = null;
        return serviceProvider =>
        {
            if (singleton is not null)
            {
                return singleton;
            }

            lock (gate)
            {
                return singleton ??= create(serviceProvider);
            }
        };
    }

    /// <summary>
    /// <see cref="BusinessGatewayDownstreamHealthState"/> is a process-lifetime singleton that
    /// records downstream degradation and is read back by <c>/health</c> and the workbench summary.
    /// On a shared host it is the one genuinely cross-test mutable object, so it is scoped per lease
    /// exactly like the downstream clients.
    /// </summary>
    private static void RouteDownstreamHealthStateToScope(IServiceCollection services)
    {
        services.RemoveAll<BusinessGatewayDownstreamHealthState>();
        var unscoped = new BusinessGatewayDownstreamHealthState();
        services.AddScoped(serviceProvider => ResolveScope(serviceProvider)?.HealthState ?? unscoped);
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

    /// <summary>
    /// A <see cref="WebApplicationFactory{TEntryPoint}"/> whose lazy host construction is serialized
    /// against in-flight gateway requests.
    /// </summary>
    private sealed class BusinessGatewayGatedWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder) =>
            BusinessGatewayTestHostGate.Build(() => base.CreateHost(builder));
    }
}

/// <summary>
/// One test's isolated slot on a shared host: the downstream fakes it registered plus its own
/// downstream-health recorder.
/// </summary>
internal sealed class BusinessGatewayTestScope(IReadOnlyDictionary<Type, object> overrides)
{
    public string Id { get; } = Guid.CreateVersion7().ToString("n");

    public IReadOnlyDictionary<Type, object> Overrides { get; } = overrides;

    public BusinessGatewayDownstreamHealthState HealthState { get; } = new();
}

/// <summary>
/// Test-facing handle over either a shared-host scope or a dedicated host. Drop-in replacement for
/// the <see cref="WebApplicationFactory{TEntryPoint}"/> the tests used to create directly.
/// </summary>
internal sealed class BusinessGatewayTestHostLease(
    WebApplicationFactory<Program> factory,
    BusinessGatewayTestScope? scope,
    bool ownsFactory) : IAsyncDisposable, IDisposable
{
    /// <summary><see langword="true"/> when this lease runs on a shared host.</summary>
    public bool IsShared => scope is not null;

    public string? ScopeId => scope?.Id;

    public IServiceProvider Services => factory.Services;

    public HttpClient CreateClient() => BusinessGatewayTestHost.CreateGatedClient(factory, scope?.Id);

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (scope is not null)
        {
            BusinessGatewayTestHost.ReleaseScope(scope.Id);
        }

        if (ownsFactory)
        {
            await factory.DisposeAsync();
        }
    }
}

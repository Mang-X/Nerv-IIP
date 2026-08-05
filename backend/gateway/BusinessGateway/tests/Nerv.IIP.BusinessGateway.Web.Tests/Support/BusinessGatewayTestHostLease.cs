using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

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
            await BusinessGatewayTestHost.ReleaseScopeAsync(scope.Id);
        }

        if (ownsFactory)
        {
            await factory.DisposeAsync();
        }
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

/// <summary>
/// Creates PlatformGateway test hosts whose lazy construction cannot overlap a request executing
/// on another host in this assembly.
/// </summary>
internal static class PlatformGatewayTestHost
{
    internal static WebApplicationFactory<Program> CreateFactory() =>
        new GatedWebApplicationFactory();

    private sealed class GatedWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services => services.Insert(
                0,
                ServiceDescriptor.Transient<IStartupFilter, PlatformGatewayTestHostGate.RequestPermitStartupFilter>()));

        protected override IHost CreateHost(IHostBuilder builder) =>
            PlatformGatewayTestHostGate.Build(() => base.CreateHost(builder));
    }
}

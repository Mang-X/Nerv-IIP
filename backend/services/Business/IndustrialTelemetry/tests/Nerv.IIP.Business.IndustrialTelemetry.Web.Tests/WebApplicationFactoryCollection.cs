namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

// FastEndpoints 8.1.0 stores Config, including Serializer.Options, in static
// process-wide state. Concurrent test-host startup can mutate and copy the shared
// converter list at the same time. Remove this collection only when FastEndpoints
// supports per-host configuration or host startup no longer touches shared state.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebApplicationFactoryCollection
{
    private WebApplicationFactoryCollection()
    {
    }

    public const string Name = "web-application-factory";
}

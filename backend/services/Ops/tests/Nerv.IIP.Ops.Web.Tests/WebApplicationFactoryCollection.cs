namespace Nerv.IIP.Ops.Web.Tests;

// FastEndpoints 8.1.0 Config children are static readonly/getter-only process state,
// so same-process test-host starts stay serialized. Deliberate incompatible mutation
// runs only in the sacrificial Nerv.IIP.FastEndpoints.ProcessIsolation.Tests project.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebApplicationFactoryCollection
{
    private WebApplicationFactoryCollection()
    {
    }

    public const string Name = "web-application-factory";
}

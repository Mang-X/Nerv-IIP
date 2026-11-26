var builder = DistributedApplication.CreateBuilder(args);
new AppHostComposition(builder).RegisterResources();
builder.Build().Run();

internal sealed partial class AppHostComposition
{
    public void RegisterResources()
    {
        var infrastructure = RegisterInfrastructure();
        var services = RegisterServices(infrastructure);
        RegisterGatewaysAndFrontend(infrastructure, services);
    }
}

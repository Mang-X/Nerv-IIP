using InventoryInfrastructure = Nerv.IIP.Business.Inventory.Infrastructure;
using MesInfrastructure = Nerv.IIP.Business.Mes.Infrastructure;
using ErpInfrastructure = Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Performance.Tests;

internal static class BusinessPerformanceServiceProvider
{
    public static ServiceProvider CreateInventoryProvider(PerformanceBaselineSettings settings)
    {
        var services = CreateBaseServices();
        services.AddInventoryPostgreSqlPersistence(settings.ConnectionString);
        return services.BuildServiceProvider();
    }

    public static ServiceProvider CreateMesProvider(PerformanceBaselineSettings settings)
    {
        var services = CreateBaseServices();
        services.AddMesPostgreSqlPersistence(settings.ConnectionString);
        return services.BuildServiceProvider();
    }

    public static ServiceProvider CreateErpProvider(PerformanceBaselineSettings settings)
    {
        var services = CreateBaseServices();
        services.AddErpPostgreSqlPersistence(settings.ConnectionString);
        return services.BuildServiceProvider();
    }

    public static Task MigrateInventoryAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        return provider.GetRequiredService<InventoryInfrastructure.ApplicationDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    public static Task MigrateMesAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        return provider.GetRequiredService<MesInfrastructure.ApplicationDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    public static Task MigrateErpAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        return provider.GetRequiredService<ErpInfrastructure.ApplicationDbContext>()
            .Database.MigrateAsync(cancellationToken);
    }

    private static ServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, NoopMediator>();
        return services;
    }
}

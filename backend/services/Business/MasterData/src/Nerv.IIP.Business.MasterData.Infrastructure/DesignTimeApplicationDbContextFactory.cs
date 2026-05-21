using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain;

namespace Nerv.IIP.Business.MasterData.Infrastructure;

public class DesignTimeApplicationDbContextFactory: IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddMediatR(c =>
            c.RegisterServicesFromAssemblies(typeof(DesignTimeApplicationDbContextFactory).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            // change connectionstring if you want to run command “dotnet ef database update”
            options.UseNpgsql("Host=any;Database=any;Username=any;Password=any",
                b =>
                {
                    b.MigrationsAssembly(typeof(DesignTimeApplicationDbContextFactory).Assembly.FullName);
                    b.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataFacts.Schema);
                });
        });
        var provider = services.BuildServiceProvider();
        var dbContext = provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return dbContext;
    }
}

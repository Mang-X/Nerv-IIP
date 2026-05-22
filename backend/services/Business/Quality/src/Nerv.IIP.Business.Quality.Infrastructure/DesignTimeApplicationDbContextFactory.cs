using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;

namespace Nerv.IIP.Business.Quality.Infrastructure;

public sealed class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblies(typeof(DesignTimeApplicationDbContextFactory).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql("Host=any;Database=any;Username=any;Password=any",
                b =>
                {
                    b.MigrationsAssembly(typeof(DesignTimeApplicationDbContextFactory).Assembly.FullName);
                    b.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema);
                });
        });
        var provider = services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }
}

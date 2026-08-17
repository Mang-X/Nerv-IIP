using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.FileStorage.Infrastructure;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"filestorage-web-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Persistence:Provider", "PostgreSQL");
        builder.UseSetting(
            "ConnectionStrings:FileStorageDb",
            "Host=localhost;Database=filestorage_web_tests;Username=nerv;Password=not-used");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        });
    }
}

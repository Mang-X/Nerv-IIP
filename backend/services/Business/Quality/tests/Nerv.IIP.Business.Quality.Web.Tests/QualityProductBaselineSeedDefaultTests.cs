using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #3805：质量基础目录 seed（<c>Quality:Seed:Enabled</c>）默认开启，不再依赖
/// <c>Persistence:AutoMigrate</c> 或部署方显式配置；显式 <c>false</c> 仍可关闭。
/// </summary>
public sealed class QualityProductBaselineSeedDefaultTests
{
    [Fact]
    public async Task Product_baseline_seed_runs_when_the_switch_is_not_set()
    {
        await using var factory = CreateFactory(Guid.NewGuid().ToString("N"), seedEnabled: null);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(
            await dbContext.QualityReasons.AnyAsync(),
            "Quality:Seed:Enabled 未设置时，产品基线 seed 应默认运行。");
    }

    [Fact]
    public async Task Product_baseline_seed_does_not_run_when_explicitly_disabled()
    {
        await using var factory = CreateFactory(Guid.NewGuid().ToString("N"), seedEnabled: false);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.False(
            await dbContext.QualityReasons.AnyAsync(),
            "Quality:Seed:Enabled=false 时，产品基线 seed 不应运行。");
    }

    private static WebApplicationFactory<Program> CreateFactory(string databaseName, bool? seedEnabled)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
                if (seedEnabled.HasValue)
                {
                    builder.UseSetting("Quality:Seed:Enabled", seedEnabled.Value ? "true" : "false");
                }

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ApplicationDbContext>();
                    services.RemoveAll<DbContextOptions>();
                    services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                    services.AddDbContext<ApplicationDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                });
            });
    }
}

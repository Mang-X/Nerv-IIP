using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesStartupGovernanceTests
{
    [Fact]
    public async Task AutoMigrate_true_outside_development_is_rejected()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=nerv_iip_mes_governance;Username=nerv;Password=nerv",
                        ["InternalService:BearerToken"] = "test-internal-service-token",
                        ["Persistence:AutoMigrate"] = "true",
                    }));
            });

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/swagger/v1/swagger.json");
        });

        Assert.Contains(Flatten(exception), x =>
            x is InvalidOperationException
            && x.Message.Contains("Persistence:AutoMigrate=true", StringComparison.Ordinal));
    }

    private static IEnumerable<Exception> Flatten(Exception? exception)
    {
        if (exception is null)
        {
            yield break;
        }

        yield return exception;
        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions.SelectMany(Flatten))
            {
                yield return innerException;
            }
        }

        if (exception.InnerException is not null)
        {
            foreach (var innerException in Flatten(exception.InnerException))
            {
                yield return innerException;
            }
        }
    }
}

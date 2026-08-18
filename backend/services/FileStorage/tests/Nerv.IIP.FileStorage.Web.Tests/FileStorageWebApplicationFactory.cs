using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nerv.IIP.FileStorage.Infrastructure;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"filestorage-web-{Guid.NewGuid():N}";
    private readonly InMemoryDatabaseRoot databaseRoot = new();

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
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName, databaseRoot));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder) =>
        FileStorageTestHostStartupGate.Build(() => base.CreateHost(builder));
}

/// <summary>
/// An otherwise unmodified FileStorage application factory whose lazy host construction still uses
/// the assembly-wide startup gate. Configuration-governance and real-PostgreSQL tests use this type
/// when replacing persistence would invalidate the behavior under test.
/// </summary>
internal sealed class FileStorageUnconfiguredWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder) =>
        FileStorageTestHostStartupGate.Build(() => base.CreateHost(builder));
}

/// <summary>
/// Serializes only FileStorage test host construction. FastEndpoints initializes process-wide
/// endpoint and serializer metadata while a host starts; two factories building that metadata
/// concurrently can leave an endpoint definition partially initialized. The permit is released as
/// soon as host startup returns, so requests remain fully parallel.
/// </summary>
internal static class FileStorageTestHostStartupGate
{
    private static readonly SemaphoreSlim HostStartupGate = new(1, 1);
    private static readonly TimeSpan HostStartupBudget = TimeSpan.FromSeconds(60);

    private static int hostStartupsWaiting;

    internal static T Build<T>(Func<T> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref hostStartupsWaiting);
        try
        {
            if (!HostStartupGate.Wait(HostStartupBudget))
            {
                throw new TimeoutException(
                    "FileStorage test host startup could not acquire the exclusive FastEndpoints "
                    + $"initialization gate within {HostStartupBudget.TotalSeconds:F0}s. "
                    + $"Last observation: {Volatile.Read(ref hostStartupsWaiting)} startup(s) waiting; "
                    + $"elapsed={Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s. "
                    + "A previous host startup is stuck; no request permit is held by this gate.");
            }

            try
            {
                return build();
            }
            finally
            {
                HostStartupGate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref hostStartupsWaiting);
        }
    }
}

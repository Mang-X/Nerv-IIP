using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageTestIsolationTests
{
    [Fact]
    public async Task Concurrent_hosts_keep_configuration_provider_and_metadata_state_isolated_while_requests_stay_parallel()
    {
        var tusRootPath = Path.Combine(
            Path.GetTempPath(),
            "nerv-iip-tests",
            "filestorage-isolation",
            Guid.NewGuid().ToString("N"));
        var requestGate = new ConcurrencyFanOutGate("FileStorage isolation probe");

        try
        {
            await using var serverProxyFactoryRoot = new FileStorageWebApplicationFactory();
            await using var serverProxyFactory = WithRequestConcurrencyProbe(
                serverProxyFactoryRoot,
                requestGate);
            await using var tusFactoryRoot = new FileStorageWebApplicationFactory();
            await using var tusFactory = WithRequestConcurrencyProbe(
                tusFactoryRoot,
                requestGate,
                builder =>
                {
                    builder.UseSetting("FileStorage:UploadProvider", "tus");
                    builder.UseSetting("FileStorage:Tus:RootPath", tusRootPath);
                });

            var clients = await Task.WhenAll(
                Task.Run(() => CreateInternalServiceClient(serverProxyFactory)),
                Task.Run(() => CreateInternalServiceClient(tusFactory)));
            using var serverProxyClient = clients[0];
            using var tusClient = clients[1];

            var created = await Task.WhenAll(
                CreateUploadSessionAsync(serverProxyClient, "server-proxy-owner"),
                CreateUploadSessionAsync(tusClient, "tus-owner"));

            Assert.Equal("server-proxy", created[0].Provider);
            Assert.Equal("server-proxy", created[0].UploadMode);
            Assert.Equal("tus", created[1].Provider);
            Assert.Equal("tus", created[1].UploadMode);

            await AssertProviderIdentityAndOwnedSessionAsync(
                serverProxyFactory,
                ownedUploadSessionId: created[0].UploadSessionId,
                foreignUploadSessionId: created[1].UploadSessionId);
            await AssertProviderIdentityAndOwnedSessionAsync(
                tusFactory,
                ownedUploadSessionId: created[1].UploadSessionId,
                foreignUploadSessionId: created[0].UploadSessionId);

            var serverProxyRequest = serverProxyClient.GetAsync(RequestConcurrencyProbeStartupFilter.Route);
            var tusRequest = tusClient.GetAsync(RequestConcurrencyProbeStartupFilter.Route);
            try
            {
                await requestGate.WaitForInFlightAsync(2, TimeSpan.FromSeconds(10));
                Assert.Equal(2, requestGate.MaxInFlight);
            }
            finally
            {
                requestGate.Release();
            }

            using var serverProxyResponse = await serverProxyRequest;
            using var tusResponse = await tusRequest;
            Assert.Equal(StatusCodes.Status204NoContent, (int)serverProxyResponse.StatusCode);
            Assert.Equal(StatusCodes.Status204NoContent, (int)tusResponse.StatusCode);
        }
        finally
        {
            if (Directory.Exists(tusRootPath))
            {
                Directory.Delete(tusRootPath, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> WithRequestConcurrencyProbe(
        FileStorageWebApplicationFactory factory,
        ConcurrencyFanOutGate requestGate,
        Action<IWebHostBuilder>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            configure?.Invoke(builder);
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(
                    new RequestConcurrencyProbeStartupFilter(requestGate)));
        });

    private static HttpClient CreateInternalServiceClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        return client;
    }

    private static async Task<CreateUploadSessionResponse> CreateUploadSessionAsync(
        HttpClient client,
        string ownerId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/files/v1/upload-sessions",
            new CreateUploadSessionRequest(
                $"org-{ownerId}",
                "test",
                new OwnerReference("FileStorageTests", "IsolationProbe", ownerId),
                "attachment",
                $"{ownerId}.txt",
                "text/plain",
                16,
                null));

        response.EnsureSuccessStatusCode();
        return Assert.IsType<CreateUploadSessionResponse>(
            await response.Content.ReadFromJsonAsync<CreateUploadSessionResponse>());
    }

    private static async Task AssertProviderIdentityAndOwnedSessionAsync(
        WebApplicationFactory<Program> factory,
        string ownedUploadSessionId,
        string foreignUploadSessionId)
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        Assert.Equal("PostgreSQL", configuration["Persistence:Provider"]);
        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", dbContext.Database.ProviderName);
        Assert.IsType<PostgreSqlFileStorageService>(fileStorageService);
        Assert.True(await dbContext.UploadSessions.AnyAsync(x => x.UploadSessionId == ownedUploadSessionId));
        Assert.False(await dbContext.UploadSessions.AnyAsync(x => x.UploadSessionId == foreignUploadSessionId));
    }

    private sealed class RequestConcurrencyProbeStartupFilter(ConcurrencyFanOutGate requestGate) : IStartupFilter
    {
        internal const string Route = "/__tests/filestorage-request-concurrency";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path == Route)
                    {
                        await requestGate.PassAsync(context.RequestAborted);
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                        return;
                    }

                    await nextMiddleware(context);
                });
                next(app);
            };
    }
}

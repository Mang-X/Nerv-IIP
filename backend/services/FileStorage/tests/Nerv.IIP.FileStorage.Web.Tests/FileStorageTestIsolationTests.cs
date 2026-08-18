using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageTestIsolationTests
{
    [Fact]
    public async Task Concurrent_hosts_keep_configuration_provider_and_metadata_state_isolated()
    {
        await using var serverProxyFactory = new FileStorageWebApplicationFactory();
        await using var tusFactory = new FileStorageWebApplicationFactory()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("FileStorage:UploadProvider", "tus"));

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
    }

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
}

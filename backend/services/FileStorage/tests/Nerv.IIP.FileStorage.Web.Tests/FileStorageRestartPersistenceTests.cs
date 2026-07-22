using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageRestartPersistenceTests
{
    [FileStorageRealPostgresFact]
    public async Task Metadata_usage_and_download_grant_survive_web_host_restart()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_filestorage_restart");
        string fileId;
        string uploadSessionId;
        string grantId;

        await using (var firstFactory = CreateFactory(database.ConnectionString, autoMigrate: true))
        {
            using var client = CreateClient(firstFactory);
            var createdResponse = await client.PostAsJsonAsync(
                "/api/files/v1/upload-sessions",
                new CreateUploadSessionRequest(
                    "org-restart",
                    "production",
                    new OwnerReference("AppHub", "ApplicationPackage", "app-restart"),
                    "application-package",
                    "restart.zip",
                    "application/zip",
                    4096,
                    "sha256:restart"));
            createdResponse.EnsureSuccessStatusCode();
            var created = await createdResponse.Content.ReadFromJsonAsync<CreateUploadSessionResponse>();
            Assert.NotNull(created);
            fileId = created.FileId;
            uploadSessionId = created.UploadSessionId;

            var completedResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest(
                    "org-restart",
                    "production",
                    "application-package",
                    "sha256:restart",
                    4096));
            completedResponse.EnsureSuccessStatusCode();

            var grantResponse = await client.PostAsJsonAsync(
                $"/api/files/v1/files/{fileId}/download-grants",
                new CreateDownloadGrantRequest("org-restart", "production"));
            grantResponse.EnsureSuccessStatusCode();
            var grant = await grantResponse.Content.ReadFromJsonAsync<DownloadGrantResponse>();
            Assert.NotNull(grant);
            var grantUrlSegments = grant.Download.Url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            grantId = grantUrlSegments[^2];
        }

        await using (var restartedFactory = CreateFactory(database.ConnectionString, autoMigrate: false))
        {
            using var client = CreateClient(restartedFactory);
            var metadata = await client.GetFromJsonAsync<FileMetadataResponse>($"/api/files/v1/files/{fileId}");
            var usage = await client.GetFromJsonAsync<FileStorageUsageResponse>(
                "/api/files/v1/usage?organizationId=org-restart&environmentId=production&filePurpose=application-package");
            using var scope = restartedFactory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persistedUploadSession = await dbContext.UploadSessions
                .AsNoTracking()
                .SingleAsync(x => x.UploadSessionId == uploadSessionId);
            var persistedGrant = await dbContext.DownloadGrants
                .AsNoTracking()
                .SingleAsync(x => x.DownloadGrantId == grantId);

            Assert.NotNull(metadata);
            Assert.Equal(fileId, metadata.FileId);
            Assert.Equal("restart.zip", metadata.FileName);
            Assert.NotNull(usage);
            Assert.Equal(4096, usage.UsedBytes);
            Assert.True(persistedUploadSession.Completed);
            Assert.Equal(fileId, persistedUploadSession.FileId);
            Assert.Equal(fileId, persistedGrant.FileId);
            Assert.Equal("org-restart", persistedGrant.OrganizationId);
            Assert.Equal("production", persistedGrant.EnvironmentId);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, bool autoMigrate)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Persistence:Provider", "PostgreSQL");
                builder.UseSetting("Persistence:AutoMigrate", autoMigrate.ToString());
                builder.UseSetting("ConnectionStrings:FileStorageDb", connectionString);
                builder.UseSetting("FileStorage:Scanning:Enabled", "false");
                builder.UseSetting("FileStorage:GarbageCollection:IntervalSeconds", "3600");
            });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);
        return client;
    }

}

internal sealed class FileStorageRealPostgresFactAttribute : FactAttribute
{
    public FileStorageRealPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the FileStorage restart persistence smoke.";
        }
    }
}

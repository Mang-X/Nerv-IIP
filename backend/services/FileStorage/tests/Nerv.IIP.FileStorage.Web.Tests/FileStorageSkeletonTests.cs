using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

// Placeholder coverage for the current FileStorage service shape; replace with
// behavior-focused tests once the FileStorage implementation lands.
public sealed class FileStorageSkeletonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Service_exposes_health_and_file_storage_boundaries()
    {
        var client = factory.CreateClient();

        var health = await client.GetStringAsync("/health");
        var boundaries = await client.GetFromJsonAsync<FileStorageBoundaries>("/internal/file-storage/v1/boundaries");

        Assert.Equal("Healthy", health);
        Assert.Contains("FileMetadata", boundaries!.DomainFacts);
        Assert.Contains("UploadProvider", boundaries.ProviderBoundaries);
        Assert.Contains("scanStatus", boundaries.DomainFacts);
    }

    private sealed record FileStorageBoundaries(IReadOnlyList<string> DomainFacts, IReadOnlyList<string> ProviderBoundaries);
}

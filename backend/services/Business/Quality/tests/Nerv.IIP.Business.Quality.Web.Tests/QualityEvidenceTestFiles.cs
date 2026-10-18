using System.Net;
using System.Net.Http.Json;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.Sdk.FileStorage;

namespace Nerv.IIP.Business.Quality.Web.Tests;

// 元数据 HTTP fixture；不代表真实 FileStorage/provider 运行。
internal sealed class QualityEvidenceTestFiles : HttpMessageHandler
{
    public Dictionary<string, FileMetadataResponse> Files { get; } = new(StringComparer.Ordinal);
    public List<string> Reads { get; } = [];
    public HttpStatusCode MissingStatus { get; set; } = HttpStatusCode.NotFound;

    public IFileStorageClient CreateClient() =>
        new HttpFileStorageClient(new HttpClient(this) { BaseAddress = new Uri("http://file-storage.test") });

    public static IFileStorageClient Client(NonconformanceReport? ncr = null, params string[] fileIds)
    {
        var handler = new QualityEvidenceTestFiles();
        foreach (var fileId in fileIds)
        {
            handler.Files.Add(fileId, Metadata(ncr!, fileId));
        }

        return handler.CreateClient();
    }

    public static FileMetadataResponse Metadata(NonconformanceReport ncr, string fileId) =>
        new(fileId, ncr.OrganizationId, ncr.EnvironmentId,
            new OwnerReference("business-quality", "nonconformance-report", ncr.Id.ToString()),
            "quality-evidence", "evidence.png", "image/png", 8, null, "available",
            DateTimeOffset.Parse("2026-10-18T00:00:00Z"), DateTimeOffset.Parse("2026-10-18T00:00:00Z"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(HttpMethod.Get, request.Method);
        var fileId = Uri.UnescapeDataString(request.RequestUri!.Segments.Last());
        Reads.Add(fileId);
        return Task.FromResult(Files.TryGetValue(fileId, out var file)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(file) }
            : new HttpResponseMessage(MissingStatus));
    }
}
